using System.Drawing;
using System.Text;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.RescueWorker;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class RescueWorkerApplicationTests
{
    private const string EngineTimeoutEnvName = "IMM_THUMB_RESCUE_ENGINE_TIMEOUT_SEC";
    private const string OpenCvTimeoutEnvName = "IMM_THUMB_RESCUE_OPENCV_TIMEOUT_SEC";

    [Test]
    public async Task RunWithTimeoutAsync_制限時間超過ならTimeoutExceptionへ変換する()
    {
        TimeoutException ex = Assert.ThrowsAsync<TimeoutException>(
            async () =>
                await RescueWorkerApplication.RunWithTimeoutAsync(
                    async cts =>
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200), cts);
                        return 1;
                    },
                    TimeSpan.FromMilliseconds(30),
                    "engine attempt timeout: failure_id=1 engine=ffmpeg1pass"
                )
        );

        Assert.That(ex?.Message, Does.Contain("timeout_sec=0"));
    }

    [Test]
    public async Task RunWithTimeoutAsync_時間内完了なら結果を返す()
    {
        int value = await RescueWorkerApplication.RunWithTimeoutAsync(
            _ => Task.FromResult(42),
            TimeSpan.FromSeconds(1),
            "unused"
        );

        Assert.That(value, Is.EqualTo(42));
    }

    [Test]
    public void ResolveTimeoutSeconds_環境変数が不正なら既定値へ戻す()
    {
        string? previous = Environment.GetEnvironmentVariable(EngineTimeoutEnvName);

        try
        {
            Environment.SetEnvironmentVariable(EngineTimeoutEnvName, "abc");

            TimeSpan timeout = RescueWorkerApplication.ResolveEngineAttemptTimeout();

            Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(120)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EngineTimeoutEnvName, previous);
        }
    }

    [Test]
    public void ResolveTimeoutSeconds_環境変数が小さすぎても下限へ丸める()
    {
        string? previous = Environment.GetEnvironmentVariable(EngineTimeoutEnvName);

        try
        {
            Environment.SetEnvironmentVariable(EngineTimeoutEnvName, "1");

            TimeSpan timeout = RescueWorkerApplication.ResolveEngineAttemptTimeout();

            Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EngineTimeoutEnvName, previous);
        }
    }

    [Test]
    public void ResolveEngineAttemptTimeout_OpenCvは既定で長めの時間を返す()
    {
        string? previous = Environment.GetEnvironmentVariable(OpenCvTimeoutEnvName);

        try
        {
            Environment.SetEnvironmentVariable(OpenCvTimeoutEnvName, null);

            TimeSpan timeout = RescueWorkerApplication.ResolveEngineAttemptTimeout("opencv");

            Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(300)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenCvTimeoutEnvName, previous);
        }
    }

    [Test]
    public void ResolveEngineAttemptTimeout_OpenCv環境変数が不正なら既定値へ戻す()
    {
        string? previous = Environment.GetEnvironmentVariable(OpenCvTimeoutEnvName);

        try
        {
            Environment.SetEnvironmentVariable(OpenCvTimeoutEnvName, "abc");

            TimeSpan timeout = RescueWorkerApplication.ResolveEngineAttemptTimeout("opencv");

            Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(300)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenCvTimeoutEnvName, previous);
        }
    }

    [Test]
    public void ResolveFailureKind_MoovAtomNotFoundな壊れMp4はUnsupportedCodec()
    {
        string moviePath = CreateFailureKindTempFile(".mp4");

        try
        {
            ThumbnailFailureKind actual = ThumbnailRescueHandoffPolicy.ResolveFailureKind(
                ex: null,
                moviePath: moviePath,
                failureReasonOverride: "moov atom not found"
            );

            Assert.That(actual, Is.EqualTo(ThumbnailFailureKind.UnsupportedCodec));
        }
        finally
        {
            File.Delete(moviePath);
        }
    }

    [Test]
    public void ResolveFailureKind_InvalidDataな壊れMovはUnsupportedCodec()
    {
        string moviePath = CreateFailureKindTempFile(".mov");

        try
        {
            ThumbnailFailureKind actual = ThumbnailRescueHandoffPolicy.ResolveFailureKind(
                ex: null,
                moviePath: moviePath,
                failureReasonOverride: "invalid data found when processing input"
            );

            Assert.That(actual, Is.EqualTo(ThumbnailFailureKind.UnsupportedCodec));
        }
        finally
        {
            File.Delete(moviePath);
        }
    }

    [Test]
    public void ResolveFailureKind_MkvのInvalidDataは従来どおりIndexCorruption()
    {
        string moviePath = CreateFailureKindTempFile(".mkv");

        try
        {
            ThumbnailFailureKind actual = ThumbnailRescueHandoffPolicy.ResolveFailureKind(
                ex: null,
                moviePath: moviePath,
                failureReasonOverride: "invalid data found when processing input"
            );

            Assert.That(actual, Is.EqualTo(ThumbnailFailureKind.IndexCorruption));
        }
        finally
        {
            File.Delete(moviePath);
        }
    }

    [Test]
    public void ShouldRunAutogenVirtualDurationRetry_長尺nearBlackのautogenだけtrue()
    {
        var nearBlackPlan = new RescueWorkerApplication.RescueExecutionPlan(
            "route-near-black-or-old-frame",
            "near-black-or-old-frame",
            ["autogen"],
            false,
            []
        );
        var fixedPlan = new RescueWorkerApplication.RescueExecutionPlan(
            "fixed",
            "unclassified",
            ["autogen"],
            false,
            []
        );

        Assert.That(
            RescueWorkerApplication.ShouldRunAutogenVirtualDurationRetry(
                nearBlackPlan,
                "autogen",
                TimeSpan.FromHours(2).TotalSeconds
            ),
            Is.True
        );
        Assert.That(
            RescueWorkerApplication.ShouldRunAutogenVirtualDurationRetry(
                nearBlackPlan,
                "ffmpeg1pass",
                TimeSpan.FromHours(2).TotalSeconds
            ),
            Is.False
        );
        Assert.That(
            RescueWorkerApplication.ShouldRunAutogenVirtualDurationRetry(
                fixedPlan,
                "autogen",
                TimeSpan.FromHours(2).TotalSeconds
            ),
            Is.False
        );
        Assert.That(
            RescueWorkerApplication.ShouldRunAutogenVirtualDurationRetry(
                nearBlackPlan,
                "autogen",
                TimeSpan.FromHours(1).TotalSeconds
            ),
            Is.False
        );
    }

    [Test]
    public void BuildAutogenVirtualDurationRetryPlans_2時間以上なら1_2_1_3_1_4を返す()
    {
        IReadOnlyList<RescueWorkerApplication.AutogenVirtualDurationRetryPlan> plans =
            RescueWorkerApplication.BuildAutogenVirtualDurationRetryPlans(
                2,
                TimeSpan.FromHours(3).TotalSeconds
            );

        Assert.That(plans.Select(x => x.DurationDivisor), Is.EqualTo(new[] { 2d, 3d, 4d }));
        Assert.That(plans.All(x => x.VirtualDurationSec > 0d), Is.True);
        Assert.That(
            plans.All(x => x.ThumbInfo != null && x.ThumbInfo.ThumbSec != null && x.ThumbInfo.ThumbSec.Count > 0),
            Is.True
        );
    }

    [Test]
    public void BuildAutogenVirtualDurationRetryPlans_2時間未満なら空()
    {
        IReadOnlyList<RescueWorkerApplication.AutogenVirtualDurationRetryPlan> plans =
            RescueWorkerApplication.BuildAutogenVirtualDurationRetryPlans(
                2,
                TimeSpan.FromMinutes(30).TotalSeconds
            );

        Assert.That(plans, Is.Empty);
    }

    [Test]
    public void BuildNearBlackRetryThumbInfos_黒多め背景モードは候補数が増える()
    {
        IReadOnlyList<ThumbInfo> normalPlans = RescueWorkerApplication.BuildNearBlackRetryThumbInfos(
            2,
            "big",
            @"C:\thumb\big",
            600d
        );
        IReadOnlyList<ThumbInfo> darkPlans = RescueWorkerApplication.BuildNearBlackRetryThumbInfos(
            2,
            "big",
            @"C:\thumb\big",
            600d,
            "dark-heavy-background"
        );

        Assert.That(darkPlans.Count, Is.GreaterThan(normalPlans.Count));
        Assert.That(darkPlans.All(x => x.ThumbSec != null && x.ThumbSec.Count > 0), Is.True);
    }

    [Test]
    public void BuildUltraShortNearBlackRetryCaptureSeconds_黒多め背景モードは候補数が増える()
    {
        IReadOnlyList<double> normalSecs =
            RescueWorkerApplication.BuildUltraShortNearBlackRetryCaptureSeconds(0.8d);
        IReadOnlyList<double> darkSecs =
            RescueWorkerApplication.BuildUltraShortNearBlackRetryCaptureSeconds(
                0.8d,
                "dark-heavy-background"
            );

        Assert.That(darkSecs.Count, Is.GreaterThan(normalSecs.Count));
    }

    [Test]
    public void ShouldForceDarkHeavyBackgroundRetry_指定時だけtrue()
    {
        Assert.That(
            RescueWorkerApplication.ShouldForceDarkHeavyBackgroundRetry(
                "dark-heavy-background",
                "ffmpeg1pass"
            ),
            Is.True
        );
        Assert.That(
            RescueWorkerApplication.ShouldForceDarkHeavyBackgroundRetry(
                "dark-heavy-background",
                "opencv"
            ),
            Is.False
        );
        Assert.That(
            RescueWorkerApplication.ShouldForceDarkHeavyBackgroundRetry("", "ffmpeg1pass"),
            Is.False
        );
    }

    [Test]
    public void BuildNearBlackRetryThumbInfos_Liteも候補数が増える()
    {
        IReadOnlyList<ThumbInfo> normalPlans = RescueWorkerApplication.BuildNearBlackRetryThumbInfos(
            2,
            "big",
            @"C:\thumb\big",
            600d
        );
        IReadOnlyList<ThumbInfo> litePlans = RescueWorkerApplication.BuildNearBlackRetryThumbInfos(
            2,
            "big",
            @"C:\thumb\big",
            600d,
            "dark-heavy-background-lite"
        );

        Assert.That(litePlans.Count, Is.GreaterThan(normalPlans.Count));
    }

    [Test]
    public void ShouldAllowDarkHeavyBackgroundLiteSuccess_Lite指定時だけtrue()
    {
        Assert.That(
            RescueWorkerApplication.ShouldAllowDarkHeavyBackgroundLiteSuccess(
                "dark-heavy-background-lite",
                "ffmpeg1pass"
            ),
            Is.True
        );
        Assert.That(
            RescueWorkerApplication.ShouldAllowDarkHeavyBackgroundLiteSuccess(
                "dark-heavy-background",
                "ffmpeg1pass"
            ),
            Is.False
        );
        Assert.That(
            RescueWorkerApplication.ShouldAllowDarkHeavyBackgroundLiteSuccess(
                "dark-heavy-background-lite",
                "opencv"
            ),
            Is.False
        );
    }

    [Test]
    public void BuildExperimentalFinalSeekCaptureSeconds_540秒なら12点を均等に返す()
    {
        IReadOnlyList<double> captureSecs =
            RescueWorkerApplication.BuildExperimentalFinalSeekCaptureSeconds(540d, 12);

        Assert.That(captureSecs.Count, Is.EqualTo(12));
        Assert.That(captureSecs.First(), Is.EqualTo(41.538d).Within(0.001d));
        Assert.That(captureSecs.Last(), Is.EqualTo(498.462d).Within(0.001d));
        Assert.That(captureSecs.Zip(captureSecs.Skip(1), (a, b) => b - a).All(x => x > 0d), Is.True);
    }

    [Test]
    public void BuildExperimentalFinalSeekCaptureSeconds_不正値なら空()
    {
        Assert.That(
            RescueWorkerApplication.BuildExperimentalFinalSeekCaptureSeconds(0d, 12),
            Is.Empty
        );
        Assert.That(
            RescueWorkerApplication.BuildExperimentalFinalSeekCaptureSeconds(10d, 0),
            Is.Empty
        );
    }

    [Test]
    public void ShouldRunExperimentalFinalSeekRescue_BigMovieだけtrue()
    {
        QueueObj bigMovie = new() { MovieSizeBytes = 4L * 1024L * 1024L * 1024L };
        QueueObj normalMovie = new() { MovieSizeBytes = 1L * 1024L * 1024L * 1024L };

        Assert.That(
            RescueWorkerApplication.ShouldRunExperimentalFinalSeekRescue(bigMovie),
            Is.True
        );
        Assert.That(
            RescueWorkerApplication.ShouldRunExperimentalFinalSeekRescue(normalMovie),
            Is.False
        );
    }

    [Test]
    public void ShouldUseIsolatedChildProcess_OpenCvだけtrue()
    {
        Assert.That(RescueWorkerApplication.ShouldUseIsolatedChildProcess("opencv"), Is.True);
        Assert.That(
            RescueWorkerApplication.ShouldUseIsolatedChildProcess("ffmpeg1pass"),
            Is.False
        );
    }

    [Test]
    public void BuildIsolatedAttemptArguments_必要引数をすべて組み立てる()
    {
        var request = new RescueWorkerApplication.IsolatedEngineAttemptRequest(
            "opencv",
            @"E:\movie\sample.wmv",
            @"E:\movie\sample.repaired.wmv",
            "難読",
            @"C:\thumb\anime",
            99,
            12345,
            "15,15,15",
            @"C:\temp\attempt.json",
            @"D:\logs",
            "trace-123"
        );

        var args = RescueWorkerApplication.BuildIsolatedAttemptArguments(request);

        Assert.That(args, Is.EqualTo(new[]
        {
            "--attempt-child",
            "--engine",
            "opencv",
            "--movie",
            @"E:\movie\sample.wmv",
            "--source-movie",
            @"E:\movie\sample.repaired.wmv",
            "--db-name",
            "難読",
            "--thumb-folder",
            @"C:\thumb\anime",
            "--tab-index",
            "99",
            "--movie-size-bytes",
            "12345",
            "--thumb-sec-csv",
            "15,15,15",
            "--log-dir",
            @"D:\logs",
            "--trace-id",
            "trace-123",
            "--result-json",
            @"C:\temp\attempt.json",
        }));
    }

    [Test]
    public void TryParseIsolatedAttemptArguments_必要値を復元できる()
    {
        string[] args =
        [
            "--attempt-child",
            "--engine",
            "opencv",
            "--movie",
            @"E:\movie\sample.wmv",
            "--source-movie",
            @"E:\movie\sample.repaired.wmv",
            "--db-name",
            "難読",
            "--thumb-folder",
            @"C:\thumb\anime",
            "--tab-index",
            "3",
            "--movie-size-bytes",
            "98765",
            "--thumb-sec-csv",
            "15,15,15",
            "--log-dir",
            @"D:\logs",
            "--trace-id",
            "trace-123",
            "--result-json",
            @"C:\temp\attempt.json",
        ];

        bool ok = RescueWorkerApplication.TryParseIsolatedAttemptArguments(args, out var request);

        Assert.That(ok, Is.True);
        Assert.That(request.EngineId, Is.EqualTo("opencv"));
        Assert.That(request.MoviePath, Is.EqualTo(@"E:\movie\sample.wmv"));
        Assert.That(request.SourceMoviePath, Is.EqualTo(@"E:\movie\sample.repaired.wmv"));
        Assert.That(request.DbName, Is.EqualTo("難読"));
        Assert.That(request.ThumbFolder, Is.EqualTo(@"C:\thumb\anime"));
        Assert.That(request.TabIndex, Is.EqualTo(3));
        Assert.That(request.MovieSizeBytes, Is.EqualTo(98765));
        Assert.That(request.ThumbSecCsv, Is.EqualTo("15,15,15"));
        Assert.That(request.LogDirectoryPath, Is.EqualTo(@"D:\logs"));
        Assert.That(request.TraceId, Is.EqualTo("trace-123"));
        Assert.That(request.ResultJsonPath, Is.EqualTo(@"C:\temp\attempt.json"));
    }

    [Test]
    public void TryFindExistingSuccessThumbnailPath_既存jpgがあればTrueを返す()
    {
        string thumbRoot = Path.Combine(Path.GetTempPath(), $"imm-worker-skip-{Guid.NewGuid():N}");
        string moviePath = @"E:\movie\sample.mp4";
        string outPath = ThumbnailLayoutProfileResolver.Resolve(2).BuildOutPath(thumbRoot);
        Directory.CreateDirectory(outPath);

        try
        {
            string successPath = Path.Combine(outPath, "sample.#abc12345.jpg");
            using Bitmap bmp = new(8, 8);
            using Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            bmp.Save(successPath, System.Drawing.Imaging.ImageFormat.Jpeg);

            bool result = RescueWorkerApplication.TryFindExistingSuccessThumbnailPath(
                thumbRoot,
                2,
                moviePath,
                out string resolvedPath
            );

            Assert.That(result, Is.True);
            Assert.That(resolvedPath, Is.EqualTo(successPath));
        }
        finally
        {
            if (Directory.Exists(thumbRoot))
            {
                Directory.Delete(thumbRoot, recursive: true);
            }
        }
    }

    [Test]
    public void ShouldReplaceExistingSuccessThumbnailWhenMetadataMissing_フラグありかつメタ無しならTrue()
    {
        string tempRoot = CreateTempRoot();

        try
        {
            string imagePath = Path.Combine(tempRoot, "missing-meta.jpg");
            WriteSolidJpeg(imagePath, Color.White);

            bool shouldReplace =
                RescueWorkerApplication.ShouldReplaceExistingSuccessThumbnailWhenMetadataMissing(
                    "{\"replace_if_metadata_missing\":true}",
                    imagePath
                );

            Assert.That(shouldReplace, Is.True);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void ShouldReplaceExistingSuccessThumbnailWhenMetadataMissing_WB互換メタありならFalse()
    {
        string tempRoot = CreateTempRoot();

        try
        {
            string imagePath = Path.Combine(tempRoot, "with-meta.jpg");
            WriteSolidJpeg(imagePath, Color.White);
            WhiteBrowserThumbInfoSerializer.AppendToJpeg(
                imagePath,
                new ThumbnailSheetSpec
                {
                    ThumbCount = 1,
                    ThumbWidth = 160,
                    ThumbHeight = 120,
                    ThumbColumns = 1,
                    ThumbRows = 1,
                    CaptureSeconds = [15],
                }
            );

            bool shouldReplace =
                RescueWorkerApplication.ShouldReplaceExistingSuccessThumbnailWhenMetadataMissing(
                    "{\"replace_if_metadata_missing\":true}",
                    imagePath
                );

            Assert.That(shouldReplace, Is.False);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void TryParseArguments_LogDirとFailureDbDirを復元できる()
    {
        string[] args =
        [
            "--main-db",
            @"C:\db\anime.wb",
            "--thumb-folder",
            @"D:\thumbs\anime",
            "--log-dir",
            @"E:\logs",
            "--failure-db-dir",
            @"F:\failuredb",
        ];

        bool ok = RescueWorkerApplication.TryParseArguments(
            args,
            out string mainDbFullPath,
            out string thumbFolder,
            out string logDirectoryPath,
            out string failureDbDirectoryPath,
            out long requestedFailureId
        );

        Assert.That(ok, Is.True);
        Assert.That(mainDbFullPath, Is.EqualTo(@"C:\db\anime.wb"));
        Assert.That(thumbFolder, Is.EqualTo(@"D:\thumbs\anime"));
        Assert.That(logDirectoryPath, Is.EqualTo(@"E:\logs"));
        Assert.That(failureDbDirectoryPath, Is.EqualTo(@"F:\failuredb"));
        Assert.That(requestedFailureId, Is.EqualTo(0L));
    }

    [Test]
    public void TryParseJobJsonArguments_rescue_subcommandを復元できる()
    {
        string[] args =
        [
            "rescue",
            "--job-json",
            @"C:\temp\job.json",
            "--result-json",
            @"C:\temp\result.json",
        ];

        bool ok = RescueWorkerApplication.TryParseJobJsonArguments(
            args,
            out string jobJsonPath,
            out string resultJsonPath
        );

        Assert.That(ok, Is.True);
        Assert.That(jobJsonPath, Is.EqualTo(@"C:\temp\job.json"));
        Assert.That(resultJsonPath, Is.EqualTo(@"C:\temp\result.json"));
    }

    [Test]
    public void TryReadMainJobContract_v1jobjsonを復元できる()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RescueWorkerApplicationTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);
        string jobJsonPath = Path.Combine(tempRoot, "job.json");
        File.WriteAllText(
            jobJsonPath,
            """
            {
              "contractVersion": "1",
              "mode": "rescue-main",
              "requestId": "req-001",
              "mainDbFullPath": ".\\sample.wb",
              "thumbFolderOverride": ".\\thumb",
              "logDirectoryPath": ".\\logs",
              "failureDbDirectoryPath": ".\\failure-db",
              "requestedFailureId": 12
            }
            """,
            new UTF8Encoding(false)
        );

        try
        {
            bool ok = RescueWorkerApplication.TryReadMainJobContract(
                jobJsonPath,
                out RescueWorkerApplication.RescueWorkerMainJobContract request,
                out string errorCode,
                out string errorMessage
            );

            Assert.That(ok, Is.True);
            Assert.That(errorCode, Is.Empty);
            Assert.That(errorMessage, Is.Empty);
            Assert.That(request.ContractVersion, Is.EqualTo("1"));
            Assert.That(request.Mode, Is.EqualTo("rescue-main"));
            Assert.That(request.RequestId, Is.EqualTo("req-001"));
            Assert.That(request.MainDbFullPath, Is.EqualTo(Path.Combine(tempRoot, "sample.wb")));
            Assert.That(
                request.ThumbFolderOverride,
                Is.EqualTo(Path.Combine(tempRoot, "thumb"))
            );
            Assert.That(
                request.LogDirectoryPath,
                Is.EqualTo(Path.Combine(tempRoot, "logs"))
            );
            Assert.That(
                request.FailureDbDirectoryPath,
                Is.EqualTo(Path.Combine(tempRoot, "failure-db"))
            );
            Assert.That(request.RequestedFailureId, Is.EqualTo(12));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void BuildMainJobResult_失敗時は互換versionとlogartifactを含む()
    {
        RescueWorkerApplication.RescueWorkerMainJobContract request = new()
        {
            ContractVersion = "1",
            Mode = "rescue-main",
            RequestId = "req-002",
            LogDirectoryPath = @"C:\logs\worker",
        };

        RescueWorkerApplication.RescueWorkerMainJobResult result =
            RescueWorkerApplication.BuildMainJobResult(
                request,
                exitCode: 1,
                startedAt: new DateTimeOffset(2026, 4, 4, 12, 0, 0, TimeSpan.FromHours(9)),
                finishedAt: new DateTimeOffset(2026, 4, 4, 12, 1, 0, TimeSpan.FromHours(9))
            );

        Assert.That(result.Status, Is.EqualTo("failed"));
        Assert.That(result.ResultCode, Is.EqualTo("RESCUE_FAILED"));
        Assert.That(
            result.CompatibilityVersion,
            Is.EqualTo(RescueWorkerArtifactContract.CompatibilityVersion)
        );
        Assert.That(
            result.Artifacts.Select(x => x.Type),
            Does.Contain("process-log").And.Contain("rescue-trace")
        );
        Assert.That(result.Errors.Count, Is.EqualTo(1));
    }

    [Test]
    public void BuildIsolatedAttemptFailureMessage_先頭の非空行を要約へ使う()
    {
        string message = RescueWorkerApplication.BuildIsolatedAttemptFailureMessage(
            1,
            "\r\n\r\nchild stdout",
            "\r\nchild stderr\r\nextra",
            "opencv"
        );

        Assert.That(
            message,
            Is.EqualTo(
                "isolated engine attempt failed: engine=opencv, exit_code=1, detail=child stderr"
            )
        );
    }

    [Test]
    public void IsFailurePlaceholderSuccess_placeholder成功はtrue()
    {
        ThumbnailCreateResult result = new()
        {
            SaveThumbFileName = @"C:\thumb\movie.#abcd1234.jpg",
            DurationSec = 10,
            IsSuccess = true,
            ProcessEngineId = "placeholder-unsupported",
        };

        bool isPlaceholder = RescueWorkerApplication.IsFailurePlaceholderSuccess(result);

        Assert.That(isPlaceholder, Is.True);
    }

    [Test]
    public void IsFailurePlaceholderSuccess_通常engine成功はfalse()
    {
        ThumbnailCreateResult result = new()
        {
            SaveThumbFileName = @"C:\thumb\movie.#abcd1234.jpg",
            DurationSec = 10,
            IsSuccess = true,
            ProcessEngineId = "ffmpeg1pass",
        };

        bool isPlaceholder = RescueWorkerApplication.IsFailurePlaceholderSuccess(result);

        Assert.That(isPlaceholder, Is.False);
    }

    [Test]
    public void ShouldTryIndexRepair_frameDecodeFailedAtSecならRepair候補に入る()
    {
        bool shouldRepair = RescueWorkerApplication.ShouldTryIndexRepair(
            @"E:\_anime\out1.avi",
            "frame decode failed at sec=14871"
        );

        Assert.That(shouldRepair, Is.True);
    }

    [Test]
    public void ShouldTryIndexRepair_NoFramesDecodedならRepair候補に入る()
    {
        bool shouldRepair = RescueWorkerApplication.ShouldTryIndexRepair(
            @"E:\_anime\out1.avi",
            "No frames decoded"
        );

        Assert.That(shouldRepair, Is.True);
    }

    [Test]
    public void ShouldTryIndexRepair_対象外拡張子はRepair候補に入れない()
    {
        bool shouldRepair = RescueWorkerApplication.ShouldTryIndexRepair(
            @"E:\_anime\out1.ts",
            "frame decode failed at sec=14871"
        );

        Assert.That(shouldRepair, Is.False);
    }

    [Test]
    public void ResolveFailureKind_FrameDecodeFailedならIndexCorruptionへ寄せる()
    {
        ThumbnailFailureKind kind = RescueWorkerApplication.ResolveFailureKind(
            ex: null,
            moviePath: "",
            failureReasonOverride: "frame decode failed at sec=14871"
        );

        Assert.That(kind, Is.EqualTo(ThumbnailFailureKind.IndexCorruption));
    }

    [Test]
    public void ResolveFailureKind_OnePassFailedならTransientDecodeFailureへ寄せる()
    {
        ThumbnailFailureKind kind = RescueWorkerApplication.ResolveFailureKind(
            ex: null,
            moviePath: "",
            failureReasonOverride: "ffmpeg one-pass failed"
        );

        Assert.That(kind, Is.EqualTo(ThumbnailFailureKind.TransientDecodeFailure));
    }

    [Test]
    public void ResolveFailureKind_Timeout文言だけでもHang扱いにする()
    {
        ThumbnailFailureKind kind = RescueWorkerApplication.ResolveFailureKind(
            ex: null,
            moviePath: "",
            failureReasonOverride: "engine attempt timeout: failure_id=25 engine=opencv timeout_sec=300"
        );

        Assert.That(kind, Is.EqualTo(ThumbnailFailureKind.HangSuspected));
    }

    [Test]
    public void ResolveFailureKind_VideoStreamNotFoundならNoVideo扱いにする()
    {
        ThumbnailFailureKind kind = RescueWorkerApplication.ResolveFailureKind(
            ex: null,
            moviePath: "",
            failureReasonOverride: "video stream not found"
        );

        Assert.That(kind, Is.EqualTo(ThumbnailFailureKind.NoVideoStream));
    }

    [Test]
    public void IsDefinitiveNoVideoStreamProbeResult_videoStreamNotFoundならtrue()
    {
        var probeResult = new VideoIndexProbeResult
        {
            DetectionReason = "video stream not found",
            ErrorCode = "video_stream_not_found",
            ContainerFormat = "matroska,webm",
        };

        bool result = RescueWorkerApplication.IsDefinitiveNoVideoStreamProbeResult(probeResult);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsDefinitiveNoVideoStreamProbeResult_probeOkならfalse()
    {
        var probeResult = new VideoIndexProbeResult
        {
            DetectionReason = "probe_ok",
            ErrorCode = "",
            ContainerFormat = "matroska,webm",
        };

        bool result = RescueWorkerApplication.IsDefinitiveNoVideoStreamProbeResult(probeResult);

        Assert.That(result, Is.False);
    }

    [Test]
    public void BuildNoVideoStreamProbeReason_コンテナ名付きで返す()
    {
        var probeResult = new VideoIndexProbeResult
        {
            DetectionReason = "video stream not found",
            ErrorCode = "video_stream_not_found",
            ContainerFormat = "matroska,webm",
        };

        string reason = RescueWorkerApplication.BuildNoVideoStreamProbeReason(probeResult);

        Assert.That(reason, Is.EqualTo("container probe confirmed: no video stream, format=matroska,webm"));
    }

    [Test]
    public void TryRejectNearBlackOutput_真っ黒jpgは削除してTrueを返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string imagePath = Path.Combine(tempRoot, "black.jpg");
            WriteSolidJpeg(imagePath, Color.Black);

            bool rejected = RescueWorkerApplication.TryRejectNearBlackOutput(
                imagePath,
                out string reason
            );

            Assert.That(rejected, Is.True);
            Assert.That(reason, Does.Contain("near-black thumbnail rejected"));
            Assert.That(File.Exists(imagePath), Is.False);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void TryRejectNearBlackOutput_通常jpgはFalseを返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string imagePath = Path.Combine(tempRoot, "white.jpg");
            WriteSolidJpeg(imagePath, Color.White);

            bool rejected = RescueWorkerApplication.TryRejectNearBlackOutput(
                imagePath,
                out string reason
            );

            Assert.That(rejected, Is.False);
            Assert.That(reason, Is.EqualTo(""));
            Assert.That(File.Exists(imagePath), Is.True);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void BuildNearBlackRetryThumbInfos_長尺なら割合候補を一意に返す()
    {
        var thumbInfos = RescueWorkerApplication.BuildNearBlackRetryThumbInfos(
            2,
            "難読",
            @"C:\thumb\難読",
            100d
        );

        Assert.That(thumbInfos.Select(x => string.Join(",", x.ThumbSec)), Is.EqualTo(new[]
        {
            "10",
            "35",
            "65",
            "85",
        }));
    }

    [Test]
    public void BuildNearBlackRetryThumbInfos_短すぎる時は空を返す()
    {
        var thumbInfos = RescueWorkerApplication.BuildNearBlackRetryThumbInfos(
            2,
            "難読",
            @"C:\thumb\難読",
            0.98d
        );

        Assert.That(thumbInfos, Is.Empty);
    }

    [Test]
    public void BuildUltraShortNearBlackRetryCaptureSeconds_超短尺なら小数秒候補を返す()
    {
        var captureSecs = RescueWorkerApplication.BuildUltraShortNearBlackRetryCaptureSeconds(0.166667d);

        Assert.That(captureSecs, Is.EqualTo(new[] { 0.017d, 0.042d, 0.083d, 0.125d, 0.150d }));
    }

    [Test]
    public void BuildUltraShortNearBlackRetryCaptureSeconds_1秒以上なら空を返す()
    {
        var captureSecs = RescueWorkerApplication.BuildUltraShortNearBlackRetryCaptureSeconds(1.2d);

        Assert.That(captureSecs, Is.Empty);
    }

    [Test]
    public void CalculateFrameVisualScore_鮮やかな画像は黒画像より高くなる()
    {
        using Bitmap dark = new(32, 32);
        using Bitmap vivid = new(32, 32);
        using (Graphics g = Graphics.FromImage(dark))
        {
            g.Clear(Color.FromArgb(3, 3, 3));
        }
        using (Graphics g = Graphics.FromImage(vivid))
        {
            g.Clear(Color.DeepPink);
        }

        double darkScore = RescueWorkerApplication.CalculateFrameVisualScore(
            dark,
            out double darkLuma,
            out _,
            out _
        );
        double vividScore = RescueWorkerApplication.CalculateFrameVisualScore(
            vivid,
            out double vividLuma,
            out _,
            out _
        );

        Assert.That(vividScore, Is.GreaterThan(darkScore));
        Assert.That(vividLuma, Is.GreaterThan(darkLuma));
    }

    [Test]
    public void SelectUltraShortRetryCandidates_採点上位を選んで時系列へ並べる()
    {
        var selected = RescueWorkerApplication.SelectUltraShortRetryCandidates(
            new[]
            {
                new RescueWorkerApplication.UltraShortFrameCandidate("a.jpg", 0.125d, 10d, 8d, 20d, 1d),
                new RescueWorkerApplication.UltraShortFrameCandidate("b.jpg", 0.017d, 30d, 30d, 50d, 4d),
                new RescueWorkerApplication.UltraShortFrameCandidate("c.jpg", 0.083d, 20d, 20d, 40d, 3d),
                new RescueWorkerApplication.UltraShortFrameCandidate("d.jpg", 0.150d, 5d, 5d, 10d, 1d),
            },
            panelCount: 3
        );

        Assert.That(selected.Select(x => x.ImagePath), Is.EqualTo(new[] { "b.jpg", "c.jpg", "a.jpg" }));
    }

    [Test]
    public void ResolveNearBlackRetryDurationSec_元の長さがあればそのまま使う()
    {
        double? durationSec = RescueWorkerApplication.ResolveNearBlackRetryDurationSec(
            0.166667d,
            1024,
            @"E:\_anime\short.mp4",
            _ => 0.5d
        );

        Assert.That(durationSec, Is.EqualTo(0.166667d));
    }

    [Test]
    public void ResolveNearBlackRetryDurationSec_長さ欠落時はprobe結果を使う()
    {
        double? durationSec = RescueWorkerApplication.ResolveNearBlackRetryDurationSec(
            null,
            1024,
            @"E:\_anime\short.mp4",
            _ => 0.166667d
        );

        Assert.That(durationSec, Is.EqualTo(0.166667d));
    }

    [Test]
    public void ResolveNearBlackRetryDurationSec_probe不能でも超短尺なら仮長さを返す()
    {
        double? durationSec = RescueWorkerApplication.ResolveNearBlackRetryDurationSec(
            null,
            1024,
            @"E:\_anime\short.mp4",
            _ => null
        );

        Assert.That(durationSec, Is.EqualTo(0.2d));
    }

    [Test]
    public void IsNearBlackFailureReason_nearBlack文言ならtrueを返す()
    {
        bool matched = RescueWorkerApplication.IsNearBlackFailureReason(
            "near-black thumbnail rejected: avg_luma=0"
        );

        Assert.That(matched, Is.True);
    }

    [Test]
    public void BuildThumbInfoFromCsv_タブ枚数へ展開して復元できる()
    {
        var thumbInfo = RescueWorkerApplication.BuildThumbInfoFromCsv(
            1,
            "難読",
            @"C:\thumb\難読",
            "15"
        );

        Assert.That(thumbInfo, Is.Not.Null);
        Assert.That(thumbInfo.ThumbSec, Is.EqualTo(new[] { 15, 15, 15 }));
    }

    [Test]
    public void ResolveSucceededEngineId_ProcessEngineIdがあればそちらを優先する()
    {
        ThumbnailCreateResult result = new()
        {
            IsSuccess = true,
            ProcessEngineId = "black-retry-decimal-ffmpeg",
        };

        string engineId = RescueWorkerApplication.ResolveSucceededEngineId("autogen", result);

        Assert.That(engineId, Is.EqualTo("black-retry-decimal-ffmpeg"));
    }

    [Test]
    public void ClassifyRescueSymptom_NoFramesDecodedかつ小容量ならUltraShort扱いにする()
    {
        string symptom = RescueWorkerApplication.ClassifyRescueSymptom(
            ThumbnailFailureKind.TransientDecodeFailure,
            "No frames decoded",
            1024 * 1024,
            @"E:\_anime\short.avi"
        );

        Assert.That(symptom, Is.EqualTo("ultra-short-no-frames"));
    }

    [Test]
    public void ClassifyRescueSymptom_NoFramesDecodedかつ大容量ならLong扱いにする()
    {
        string symptom = RescueWorkerApplication.ClassifyRescueSymptom(
            ThumbnailFailureKind.TransientDecodeFailure,
            "No frames decoded",
            32L * 1024L * 1024L,
            @"E:\_anime\long.mkv"
        );

        Assert.That(symptom, Is.EqualTo("long-no-frames"));
    }

    [Test]
    public void ClassifyRescueSymptom_NormalLaneTimeoutかつ長尺ならLong扱いにする()
    {
        string symptom = RescueWorkerApplication.ClassifyRescueSymptom(
            ThumbnailFailureKind.HangSuspected,
            "thumbnail normal lane timeout: timeout_sec=40",
            64L * 1024L * 1024L,
            @"E:\_anime\slow.wmv"
        );

        Assert.That(symptom, Is.EqualTo("long-no-frames"));
    }

    [Test]
    public void ClassifyRescueSymptom_IndexCorruptionならCorrupt扱いにする()
    {
        string symptom = RescueWorkerApplication.ClassifyRescueSymptom(
            ThumbnailFailureKind.IndexCorruption,
            "moov atom not found",
            64L * 1024L * 1024L,
            @"E:\_anime\broken.mp4"
        );

        Assert.That(symptom, Is.EqualTo("corrupt-or-partial"));
    }

    [Test]
    public void ClassifyRescueSymptom_NearBlack理由ならNearBlack扱いにする()
    {
        string symptom = RescueWorkerApplication.ClassifyRescueSymptom(
            ThumbnailFailureKind.Unknown,
            "near-black frame detected around sec=33",
            64L * 1024L * 1024L,
            @"E:\_anime\dark.mkv"
        );

        Assert.That(symptom, Is.EqualTo("near-black-or-old-frame"));
    }

    [Test]
    public void ClassifyRescueSymptom_判定不能ならUnclassifiedへ落とす()
    {
        string symptom = RescueWorkerApplication.ClassifyRescueSymptom(
            ThumbnailFailureKind.Unknown,
            "decoder returned unknown state",
            64L * 1024L * 1024L,
            @"E:\_anime\mystery.mkv"
        );

        Assert.That(symptom, Is.EqualTo("unclassified"));
    }

    [Test]
    public void BuildRescuePlan_UltraShortはAutogen先頭でRepairなし()
    {
        var plan = RescueWorkerApplication.BuildRescuePlan("ultra-short-no-frames");

        Assert.That(plan.RouteId, Is.EqualTo("route-ultra-short-no-frames"));
        Assert.That(plan.DirectEngineOrder, Is.EqualTo(new[] { "autogen", "ffmpeg1pass", "ffmediatoolkit", "opencv" }));
        Assert.That(plan.UseRepairAfterDirect, Is.False);
        Assert.That(plan.RepairEngineOrder, Is.Empty);
    }

    [Test]
    public void BuildRescuePlan_CorruptはOnePass先行でRepairあり()
    {
        var plan = RescueWorkerApplication.BuildRescuePlan("corrupt-or-partial");

        Assert.That(plan.RouteId, Is.EqualTo("route-corrupt-or-partial"));
        Assert.That(plan.DirectEngineOrder, Is.EqualTo(new[] { "ffmpeg1pass" }));
        Assert.That(plan.UseRepairAfterDirect, Is.True);
        Assert.That(plan.RepairEngineOrder, Is.EqualTo(new[] { "ffmpeg1pass", "ffmediatoolkit", "autogen", "opencv" }));
    }

    [Test]
    public void BuildRescuePlan_UnclassifiedはFixedへ戻す()
    {
        var plan = RescueWorkerApplication.BuildRescuePlan("unknown");

        Assert.That(plan.RouteId, Is.EqualTo("fixed"));
        Assert.That(plan.SymptomClass, Is.EqualTo("unclassified"));
        Assert.That(plan.DirectEngineOrder, Is.EqualTo(new[] { "ffmpeg1pass", "ffmediatoolkit", "autogen", "opencv" }));
        Assert.That(plan.UseRepairAfterDirect, Is.True);
    }

    [Test]
    public void ShouldRunRescuePreflightAutogen_LongNoFramesならtrue()
    {
        bool actual = RescueWorkerApplication.ShouldRunRescuePreflightAutogen(
            RescueWorkerApplication.BuildRescuePlan("long-no-frames"),
            forceIndexRepair: false,
            failureReason: ""
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldRunRescuePreflightAutogen_Unclassifiedでもautogen明白失敗でなければtrue()
    {
        bool actual = RescueWorkerApplication.ShouldRunRescuePreflightAutogen(
            RescueWorkerApplication.BuildRescuePlan("unknown"),
            forceIndexRepair: false,
            failureReason: "thumbnail normal lane timeout"
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldRunRescuePreflightAutogen_CorruptOrForcedRepairならfalse()
    {
        bool corruptActual = RescueWorkerApplication.ShouldRunRescuePreflightAutogen(
            RescueWorkerApplication.BuildRescuePlan("corrupt-or-partial"),
            forceIndexRepair: false,
            failureReason: ""
        );
        bool forcedActual = RescueWorkerApplication.ShouldRunRescuePreflightAutogen(
            RescueWorkerApplication.BuildRescuePlan("long-no-frames"),
            forceIndexRepair: true,
            failureReason: ""
        );

        Assert.That(corruptActual, Is.False);
        Assert.That(forcedActual, Is.False);
    }

    [Test]
    public void ShouldRunRescuePreflightAutogen_Autogen明白失敗ならfalse()
    {
        bool actual = RescueWorkerApplication.ShouldRunRescuePreflightAutogen(
            RescueWorkerApplication.BuildRescuePlan("unknown"),
            forceIndexRepair: false,
            failureReason: "near-black thumbnail rejected: avg_luma=0; engine=autogen"
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void IsHighRiskFfMediaToolkitMovie_超巨大Mkv高bitrateならtrue()
    {
        bool actual = RescueWorkerApplication.IsHighRiskFfMediaToolkitMovie(
            @"E:\_anime\huge-av1.mkv",
            73_763_711_551L,
            4318d
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ApplyFfMediaToolkitAvoidancePolicies_危険帯なら順番からffmediaを外す()
    {
        var plan = RescueWorkerApplication.ApplyFfMediaToolkitAvoidancePolicies(
            RescueWorkerApplication.BuildRescuePlan("long-no-frames"),
            @"E:\_anime\huge-av1.mkv",
            73_763_711_551L,
            4318d,
            ""
        );

        Assert.That(plan.DirectEngineOrder, Is.EqualTo(new[] { "ffmpeg1pass" }));
        Assert.That(plan.RepairEngineOrder, Is.EqualTo(new[] { "ffmpeg1pass", "autogen", "opencv" }));
    }

    [Test]
    public void ApplyFfMediaToolkitAvoidancePolicies_通常帯なら順番を変えない()
    {
        var original = RescueWorkerApplication.BuildRescuePlan("long-no-frames");
        var actual = RescueWorkerApplication.ApplyFfMediaToolkitAvoidancePolicies(
            original,
            @"E:\_anime\normal.mkv",
            512L * 1024L * 1024L,
            600d,
            ""
        );

        Assert.That(actual, Is.EqualTo(original));
    }

    [Test]
    public void ApplyFfMediaToolkitAvoidancePolicies_Ebml異常履歴なら通常サイズでもffmediaを外す()
    {
        var plan = RescueWorkerApplication.ApplyFfMediaToolkitAvoidancePolicies(
            RescueWorkerApplication.BuildRescuePlan("fixed"),
            @"E:\_anime\normal.mkv",
            512L * 1024L * 1024L,
            600d,
            "isolated engine attempt failed: engine=ffmediatoolkit, exit_code=1, detail=[matroska,webm @ 000001] 0x00 invalid as first byte of an EBML number"
        );

        Assert.That(plan.DirectEngineOrder, Does.Not.Contain("ffmediatoolkit"));
        Assert.That(plan.RepairEngineOrder, Does.Not.Contain("ffmediatoolkit"));
    }

    [Test]
    public void ShouldAvoidFfMediaToolkitByFailureReason_Ebml異常ならtrue()
    {
        bool actual = RescueWorkerApplication.ShouldAvoidFfMediaToolkitByFailureReason(
            "isolated engine attempt failed: engine=ffmediatoolkit, exit_code=1, detail=[matroska,webm @ 000001] 0x00 invalid as first byte of an EBML number"
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldEnterRepairPath_LongRouteかつOnePassFailedならRepairへ入る()
    {
        bool shouldRepair = RescueWorkerApplication.ShouldEnterRepairPath(
            "route-long-no-frames",
            @"E:\_anime\slow.wmv",
            ThumbnailFailureKind.Unknown,
            "ffmpeg one-pass failed"
        );

        Assert.That(shouldRepair, Is.True);
    }

    [Test]
    public void ShouldEnterRepairPath_CorruptRouteは文言が弱くてもRepairへ入る()
    {
        bool shouldRepair = RescueWorkerApplication.ShouldEnterRepairPath(
            "route-corrupt-or-partial",
            @"E:\_anime\broken.mp4",
            ThumbnailFailureKind.Unknown,
            "unknown failure"
        );

        Assert.That(shouldRepair, Is.True);
    }

    [Test]
    public void ShouldEnterRepairPath_NearBlackRouteはRepairへ入らない()
    {
        bool shouldRepair = RescueWorkerApplication.ShouldEnterRepairPath(
            "route-near-black-or-old-frame",
            @"E:\_anime\dark.mkv",
            ThumbnailFailureKind.Unknown,
            "near-black frame detected"
        );

        Assert.That(shouldRepair, Is.False);
    }

    [Test]
    public void TryPromoteRescuePlan_FixedからOnePassFailedならLongへ昇格する()
    {
        var promoted = RescueWorkerApplication.TryPromoteRescuePlan(
            RescueWorkerApplication.BuildRescuePlan("unclassified"),
            new[] { "ffmpeg1pass" },
            ThumbnailFailureKind.Unknown,
            "ffmpeg one-pass failed",
            64L * 1024L * 1024L,
            @"E:\_anime\slow.wmv",
            repairApplied: false
        );

        Assert.That(promoted.RouteId, Is.EqualTo("route-long-no-frames"));
    }

    [Test]
    public void TryPromoteRescuePlan_FixedでもPrefix非互換なら昇格しない()
    {
        var promoted = RescueWorkerApplication.TryPromoteRescuePlan(
            RescueWorkerApplication.BuildRescuePlan("unclassified"),
            new[] { "ffmpeg1pass" },
            ThumbnailFailureKind.TransientDecodeFailure,
            "No frames decoded",
            1024L * 1024L,
            @"E:\_anime\short.avi",
            repairApplied: false
        );

        Assert.That(promoted.RouteId, Is.EqualTo("fixed"));
    }

    [Test]
    public void TryPromoteAfterRepairProbeNegative_LongRouteでIndexCorruptionならCorruptへ昇格する()
    {
        var promoted = RescueWorkerApplication.TryPromoteAfterRepairProbeNegative(
            RescueWorkerApplication.BuildRescuePlan("long-no-frames"),
            ThumbnailFailureKind.IndexCorruption,
            "frame decode failed at sec=167",
            64L * 1024L * 1024L,
            @"E:\_anime\slow.wmv"
        );

        Assert.That(promoted.RouteId, Is.EqualTo("route-corrupt-or-partial"));
    }

    [Test]
    public void TryPromoteAfterDirectExhausted_UltraShortでIndexCorruptionならCorruptへ昇格する()
    {
        var promoted = RescueWorkerApplication.TryPromoteAfterDirectExhausted(
            RescueWorkerApplication.BuildRescuePlan("ultra-short-no-frames"),
            ThumbnailFailureKind.IndexCorruption,
            "frame decode failed at sec=14871",
            1024L * 1024L,
            @"E:\_anime\out1.avi"
        );

        Assert.That(promoted.RouteId, Is.EqualTo("route-corrupt-or-partial"));
    }

    [Test]
    public void ShouldContinueAfterRepairProbeNegative_LongRouteでIndexCorruptionなら継続する()
    {
        bool shouldContinue = RescueWorkerApplication.ShouldContinueAfterRepairProbeNegative(
            "route-long-no-frames",
            ThumbnailFailureKind.IndexCorruption,
            "frame decode failed at sec=167"
        );

        Assert.That(shouldContinue, Is.True);
    }

    [Test]
    public void ShouldForceRepairAfterProbeNegative_AviのIndexCorruptionならtrue()
    {
        bool shouldForce = RescueWorkerApplication.ShouldForceRepairAfterProbeNegative(
            "route-corrupt-or-partial",
            @"E:\_anime\out1.avi",
            ThumbnailFailureKind.IndexCorruption,
            "frame decode failed at sec=14871"
        );

        Assert.That(shouldForce, Is.True);
    }

    [Test]
    public void BuildRemainingEngineOrder_既試行を除いた残り順を返す()
    {
        var remaining = RescueWorkerApplication.BuildRemainingEngineOrder(
            new[] { "ffmpeg1pass", "ffmediatoolkit", "autogen", "opencv" },
            new[] { "ffmpeg1pass", "ffmediatoolkit" }
        );

        Assert.That(remaining, Is.EqualTo(new[] { "autogen", "opencv" }));
    }

    [Test]
    public void ResolveEffectiveEngineOrderAfterPromotion_fallback時は明示した残り順を維持する()
    {
        var promotedPlan = RescueWorkerApplication.BuildRescuePlan("corrupt-or-partial");
        var resolved = RescueWorkerApplication.ResolveEffectiveEngineOrderAfterPromotion(
            new[] { "autogen", "opencv" },
            promotedPlan,
            preserveProvidedEngineOrder: true
        );

        Assert.That(resolved, Is.EqualTo(new[] { "autogen", "opencv" }));
    }

    [Test]
    public void ResolveEffectiveEngineOrderAfterPromotion_direct時は昇格後route順へ差し替える()
    {
        var promotedPlan = RescueWorkerApplication.BuildRescuePlan("long-no-frames");
        var resolved = RescueWorkerApplication.ResolveEffectiveEngineOrderAfterPromotion(
            new[] { "ffmpeg1pass", "ffmediatoolkit", "autogen", "opencv" },
            promotedPlan,
            preserveProvidedEngineOrder: false
        );

        Assert.That(resolved, Is.EqualTo(new[] { "ffmpeg1pass", "ffmediatoolkit" }));
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateFailureKindTempFile(string extension)
    {
        string root = CreateTempRoot();
        string moviePath = Path.Combine(root, $"failure-kind{extension}");
        File.WriteAllBytes(moviePath, [1, 2, 3, 4]);
        return moviePath;
    }

    private static void WriteSolidJpeg(string savePath, Color color)
    {
        string dir = Path.GetDirectoryName(savePath) ?? "";
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using Bitmap bitmap = new(32, 32);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(color);
        }

        bitmap.Save(savePath, System.Drawing.Imaging.ImageFormat.Jpeg);
    }
}
