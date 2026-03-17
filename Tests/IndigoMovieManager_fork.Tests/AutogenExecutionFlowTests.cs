using System.Drawing;
using System.Drawing.Imaging;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public class AutogenExecutionFlowTests
{
    private const string EngineEnvName = "IMM_THUMB_ENGINE";
    private const string AutogenRetryEnvName = "IMM_THUMB_AUTOGEN_RETRY";
    private const string AutogenRetryDelayMsEnvName = "IMM_THUMB_AUTOGEN_RETRY_DELAY_MS";

    [Test]
    public async Task CreateThumbAsync_AutogenSuccess_DoesNotFallback()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.CreateForTesting(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    CreateArgs(
                        new QueueObj { MovieId = 1, Tabindex = 0, MovieFullPath = moviePath },
                        thumbRoot
                    )
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_既存placeholderがあってもengine前に掃除してから生成する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var queueObj = new QueueObj
            {
                MovieId = 11,
                Tabindex = 0,
                MovieFullPath = moviePath,
                Hash = "deadbeef",
            };
            string savePath = ThumbnailPathResolver.BuildThumbnailPath(
                ThumbnailLayoutProfileResolver.Resolve(queueObj.Tabindex).BuildOutPath(thumbRoot),
                moviePath,
                queueObj.Hash
            );
            WriteSolidJpeg(savePath, Color.FromArgb(45, 45, 45));

            bool outputMissingAtCreate = false;
            var autogen = new RecordingEngine(
                "autogen",
                (ctx, _) =>
                {
                    outputMissingAtCreate = !File.Exists(ctx.SaveThumbFileName);
                    WriteSolidJpeg(ctx.SaveThumbFileName, Color.Aqua);
                    return Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    );
                }
            );
            var ffmedia = new RecordingEngine("ffmediatoolkit", (_, _) => Task.FromResult(
                ThumbnailCreateResultFactory.CreateFailed(savePath, 0, "should not run")
            ));
            var ffmpeg1pass = new RecordingEngine("ffmpeg1pass", (_, _) => Task.FromResult(
                ThumbnailCreateResultFactory.CreateFailed(savePath, 0, "should not run")
            ));
            var opencv = new RecordingEngine("opencv", (_, _) => Task.FromResult(
                ThumbnailCreateResultFactory.CreateFailed(savePath, 0, "should not run")
            ));
            var service = ThumbnailCreationServiceFactory.CreateForTesting(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    CreateArgs(queueObj, thumbRoot)
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(outputMissingAtCreate, Is.True);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
                Assert.That(File.Exists(savePath), Is.True);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_AutogenInitFailure_NormalLaneではその場で失敗する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (_, _) =>
                    throw new InvalidOperationException("simulated autogen init failure")
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.CreateForTesting(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    CreateArgs(
                        new QueueObj { MovieId = 2, Tabindex = 0, MovieFullPath = moviePath },
                        thumbRoot
                    )
                );

                // 通常本線は autogen だけで見切るため、後続フォールバックへ進まない。
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorMessage, Does.Contain("simulated autogen init failure"));
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_AutogenTransientFailure_Phase4ではリトライもフォールバックもしない()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            int failureCount = 0;
            var autogen = new RecordingEngine(
                "autogen",
                (ctx, _) =>
                {
                    if (failureCount < 1)
                    {
                        failureCount++;
                        return Task.FromResult(
                            ThumbnailCreateResultFactory.CreateFailed(
                                ctx.SaveThumbFileName,
                                ctx.DurationSec,
                                "timeout"
                            )
                        );
                    }

                    return Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    );
                }
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.CreateForTesting(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            string? oldAutogenRetry = Environment.GetEnvironmentVariable(AutogenRetryEnvName);
            string? oldAutogenRetryDelay = Environment.GetEnvironmentVariable(
                AutogenRetryDelayMsEnvName
            );
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");
                Environment.SetEnvironmentVariable(AutogenRetryEnvName, "on");
                Environment.SetEnvironmentVariable(AutogenRetryDelayMsEnvName, "0");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    CreateArgs(
                        new QueueObj { MovieId = 3, Tabindex = 0, MovieFullPath = moviePath },
                        thumbRoot
                    )
                );

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorMessage, Is.EqualTo("timeout"));
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
                Environment.SetEnvironmentVariable(AutogenRetryEnvName, oldAutogenRetry);
                Environment.SetEnvironmentVariable(
                    AutogenRetryDelayMsEnvName,
                    oldAutogenRetryDelay
                );
            }
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
    public async Task CreateThumbAsync_AutogenBlackSuccess_通常本線では失敗へ戻す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                (ctx, _) =>
                {
                    WriteSolidJpeg(ctx.SaveThumbFileName, Color.Black);
                    return Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    );
                }
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.CreateForTesting(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    CreateArgs(
                        new QueueObj { MovieId = 30, Tabindex = 0, MovieFullPath = moviePath },
                        thumbRoot
                    )
                );

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(
                    result.ErrorMessage,
                    Does.Contain("near-black thumbnail rejected")
                );
                Assert.That(Path.Exists(result.SaveThumbFileName), Is.False);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_成功時はStaleErrorMarkerを削除する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            string outPath = ThumbnailLayoutProfileResolver.Resolve(
                99,
                ThumbnailDetailModeRuntime.ReadRuntimeMode()
            ).BuildOutPath(thumbRoot);
            Directory.CreateDirectory(outPath);
            string staleErrorMarker = ThumbnailPathResolver.BuildErrorMarkerPath(outPath, moviePath);
            File.WriteAllBytes(staleErrorMarker, []);

            var autogen = new RecordingEngine(
                "autogen",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                (ctx, _) =>
                {
                    WriteSolidJpeg(ctx.SaveThumbFileName, Color.White);
                    return Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    );
                }
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.CreateForTesting(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "ffmediatoolkit");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    CreateArgs(
                        new QueueObj { MovieId = 31, Tabindex = 99, MovieFullPath = moviePath },
                        thumbRoot
                    )
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(Path.Exists(result.SaveThumbFileName), Is.True);
                Assert.That(Path.Exists(staleErrorMarker), Is.False);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_既存成功jpgがある失敗時はErrorMarkerを再生成しない()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            string outPath = Path.Combine(thumbRoot, "120x90x1x1");
            Directory.CreateDirectory(outPath);

            string existingSuccessPath = Path.Combine(outPath, "dummy.#abc12345.jpg");
            WriteSolidJpeg(existingSuccessPath, Color.White);

            string staleErrorMarker = ThumbnailPathResolver.BuildErrorMarkerPath(outPath, moviePath);
            File.WriteAllBytes(staleErrorMarker, []);

            var autogen = new RecordingEngine(
                "autogen",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "No frames decoded"
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "frame decode failed at sec=9"
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "ffmpeg one-pass failed"
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "engine attempt timeout"
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.CreateForTesting(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    CreateArgs(
                        new QueueObj { MovieId = 32, Tabindex = 99, MovieFullPath = moviePath },
                        thumbRoot
                    )
                );

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(Path.Exists(existingSuccessPath), Is.True);
                Assert.That(Path.Exists(staleErrorMarker), Is.False);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_WmvDrmPrecheckHit_エンジン実行せずプレースホルダーで成功する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyWmvWithDrmHeaderFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreateResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.CreateForTesting(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    CreateArgs(
                        new QueueObj { MovieId = 4, Tabindex = 0, MovieFullPath = moviePath },
                        thumbRoot
                    )
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(Path.Exists(result.SaveThumbFileName), Is.True);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
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

    private static string CreateDummyMovieFile(string tempRoot)
    {
        string path = Path.Combine(tempRoot, "dummy.mp4");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
        return path;
    }

    private static ThumbnailCreateArgs CreateArgs(QueueObj queueObj, string thumbRoot)
    {
        return new ThumbnailCreateArgs
        {
            QueueObj = queueObj,
            DbName = "testdb",
            ThumbFolder = thumbRoot,
            IsResizeThumb = true,
            IsManual = false,
        };
    }

    private static string CreateDummyWmvWithDrmHeaderFile(string tempRoot)
    {
        string path = Path.Combine(tempRoot, "drm-sample.wmv");
        byte[] header = new byte[4096];
        byte[] drmGuid =
        [
            0xFB,
            0xB3,
            0x11,
            0x22,
            0x23,
            0xBD,
            0xD2,
            0x11,
            0xB4,
            0xB7,
            0x00,
            0xA0,
            0xC9,
            0x55,
            0xFC,
            0x6E,
        ];
        Array.Copy(drmGuid, 0, header, 256, drmGuid.Length);
        File.WriteAllBytes(path, header);
        return path;
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

        bitmap.Save(savePath, ImageFormat.Jpeg);
    }

    private sealed class RecordingEngine : IThumbnailGenerationEngine
    {
        private readonly Func<ThumbnailJobContext, CancellationToken, Task<ThumbnailCreateResult>> createAsync;

        public RecordingEngine(
            string engineId,
            Func<ThumbnailJobContext, CancellationToken, Task<ThumbnailCreateResult>> createAsync
        )
        {
            EngineId = engineId;
            EngineName = engineId;
            this.createAsync = createAsync;
        }

        public string EngineId { get; }
        public string EngineName { get; }
        public int CreateCallCount { get; private set; }

        public bool CanHandle(ThumbnailJobContext context)
        {
            return true;
        }

        public Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken cts = default
        )
        {
            CreateCallCount++;
            return createAsync(context, cts);
        }

        public Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            return Task.FromResult(false);
        }
    }
}
