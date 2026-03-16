using System.Drawing;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.RescueWorker;

namespace IndigoMovieManager_fork.Tests;

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
            @"C:\temp\attempt.json"
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
        Assert.That(request.ResultJsonPath, Is.EqualTo(@"C:\temp\attempt.json"));
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
            "thumbnail normal lane timeout: timeout_sec=10",
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
            "IndigoMovieManager_fork_tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
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
