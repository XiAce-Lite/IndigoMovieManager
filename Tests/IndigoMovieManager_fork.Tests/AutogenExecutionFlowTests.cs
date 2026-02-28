using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public class AutogenExecutionFlowTests
{
    private const string EngineEnvName = "IMM_THUMB_ENGINE";

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
