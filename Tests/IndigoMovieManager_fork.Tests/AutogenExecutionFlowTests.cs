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
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = new ThumbnailCreationService(ffmedia, ffmpeg1pass, opencv, autogen);

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj { MovieId = 1, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
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
    public async Task CreateThumbAsync_AutogenInitFailure_FallsBackToFfMediaToolkit()
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
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = new ThumbnailCreationService(ffmedia, ffmpeg1pass, opencv, autogen);

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj { MovieId = 2, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                // autogen 例外時でも ffmediatoolkit へフォールバックして成功できること。
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(1));
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
    public async Task CreateThumbAsync_AutogenTransientFailure_1回リトライ後に成功する()
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
                            ThumbnailCreationService.CreateFailedResult(
                                ctx.SaveThumbFileName,
                                ctx.DurationSec,
                                "timeout"
                            )
                        );
                    }

                    return Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
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
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = new ThumbnailCreationService(ffmedia, ffmpeg1pass, opencv, autogen);

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
                    new QueueObj { MovieId = 3, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(2));
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
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = new ThumbnailCreationService(ffmedia, ffmpeg1pass, opencv, autogen);

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj { MovieId = 4, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
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
