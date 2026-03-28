using System.Drawing;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ThumbnailEngineExecutionCoordinatorTests
{
    private const string AutogenRetryEnvName = "IMM_THUMB_AUTOGEN_RETRY";
    private const string AutogenRetryDelayMsEnvName = "IMM_THUMB_AUTOGEN_RETRY_DELAY_MS";

    [Test]
    public async Task ExecuteAsync_既知破損シグネチャ後はffmpeg1passをskipする()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            var ffmedia = new SequenceEngine(
                "ffmediatoolkit",
                _ => FailedResult("invalid data found when processing input")
            );
            var ffmpeg1pass = new SequenceEngine("ffmpeg1pass", _ => SuccessResult());
            var opencv = new SequenceEngine("opencv", _ => FailedResult("unused"));
            var autogen = new SequenceEngine("autogen", _ => FailedResult("unused"));

            ThumbnailEngineExecutionCoordinator coordinator = CreateCoordinator(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );
            ThumbnailJobContext context = CreateContext(tempRoot, "skip.mp4", isManual: false);

            ThumbnailEngineExecutionOutcome actual = await coordinator.ExecuteAsync(
                ffmedia,
                [ffmedia, ffmpeg1pass],
                context,
                context.Request.MovieFullPath
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.Result.IsSuccess, Is.False);
                Assert.That(actual.ProcessEngineId, Is.EqualTo("ffmpeg1pass"));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(1));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(
                    actual.EngineErrorMessages,
                    Does.Contain("[ffmpeg1pass] skipped: known invalid input signature")
                );
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ExecuteAsync_Autogen一時失敗は統計記録して失敗を返す()
    {
        string tempRoot = CreateTempRoot();
        string? oldRetry = Environment.GetEnvironmentVariable(AutogenRetryEnvName);
        string? oldDelay = Environment.GetEnvironmentVariable(AutogenRetryDelayMsEnvName);
        try
        {
            Environment.SetEnvironmentVariable(AutogenRetryEnvName, "on");
            Environment.SetEnvironmentVariable(AutogenRetryDelayMsEnvName, "0");
            _ = ThumbnailEngineRuntimeStats.ConsumeWindow();

            var ffmedia = new SequenceEngine("ffmediatoolkit", _ => FailedResult("unused"));
            var ffmpeg1pass = new SequenceEngine("ffmpeg1pass", _ => FailedResult("unused"));
            var opencv = new SequenceEngine("opencv", _ => FailedResult("unused"));
            var autogen = new SequenceEngine("autogen", _ => FailedResult("timeout"));

            ThumbnailEngineExecutionCoordinator coordinator = CreateCoordinator(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );
            ThumbnailJobContext context = CreateContext(tempRoot, "retry.mp4", isManual: false);

            ThumbnailEngineExecutionOutcome actual = await coordinator.ExecuteAsync(
                autogen,
                [autogen],
                context,
                context.Request.MovieFullPath
            );
            ThumbnailEngineRuntimeSnapshot snapshot = ThumbnailEngineRuntimeStats.ConsumeWindow();

            Assert.Multiple(() =>
            {
                Assert.That(actual.Result.IsSuccess, Is.False);
                Assert.That(actual.Result.ErrorMessage, Is.EqualTo("timeout"));
                Assert.That(actual.ProcessEngineId, Is.EqualTo("autogen"));
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
                Assert.That(snapshot.AutogenTransientFailureCount, Is.EqualTo(1));
                Assert.That(snapshot.AutogenRetrySuccessCount, Is.EqualTo(0));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(AutogenRetryEnvName, oldRetry);
            Environment.SetEnvironmentVariable(AutogenRetryDelayMsEnvName, oldDelay);
            TryDeleteDirectory(tempRoot);
        }
    }

    private static ThumbnailEngineExecutionCoordinator CreateCoordinator(
        IThumbnailGenerationEngine ffmedia,
        IThumbnailGenerationEngine ffmpeg1pass,
        IThumbnailGenerationEngine opencv,
        IThumbnailGenerationEngine autogen
    )
    {
        ThumbnailEngineExecutionPolicy policy = new(ffmedia, ffmpeg1pass, opencv, autogen);
        return new ThumbnailEngineExecutionCoordinator(policy);
    }

    private static ThumbnailJobContext CreateContext(
        string tempRoot,
        string movieName,
        bool isManual
    )
    {
        string moviePath = Path.Combine(tempRoot, movieName);
        string saveThumbPath = Path.Combine(tempRoot, "thumb.jpg");
        Directory.CreateDirectory(tempRoot);
        File.WriteAllBytes(moviePath, [0x01, 0x02, 0x03]);

        return new ThumbnailJobContext
        {
            Request = new ThumbnailRequest
            {
                MovieId = 1,
                TabIndex = 0,
                MovieFullPath = moviePath,
                Hash = "testhash",
            },
            LayoutProfile = ThumbnailLayoutProfileResolver.Small,
            ThumbnailOutPath = tempRoot,
            ThumbInfo = new ThumbInfo(),
            MovieFullPath = moviePath,
            SaveThumbFileName = saveThumbPath,
            IsResizeThumb = true,
            IsManual = isManual,
            DurationSec = 120,
            FileSizeBytes = 1024,
            AverageBitrateMbps = 8,
            HasEmojiPath = false,
            VideoCodec = "h264",
            InitialEngineHint = "",
        };
    }

    private static ThumbnailCreateResult FailedResult(string message)
    {
        return ThumbnailCreateResultFactory.CreateFailed(@"C:\dummy\out.jpg", 120, message);
    }

    private static ThumbnailCreateResult SuccessResult()
    {
        return ThumbnailCreateResultFactory.CreateSuccess(@"C:\dummy\out.jpg", 120);
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

    private static void TryDeleteDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
            // 一時ディレクトリ削除失敗はテスト本体より優先しない。
        }
    }

    private sealed class SequenceEngine : IThumbnailGenerationEngine
    {
        private readonly Func<int, ThumbnailCreateResult> resultFactory;

        public SequenceEngine(string engineId, Func<int, ThumbnailCreateResult> resultFactory)
        {
            EngineId = engineId;
            EngineName = engineId;
            this.resultFactory = resultFactory;
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
            ThumbnailCreateResult result = resultFactory(CreateCallCount);
            if (result.IsSuccess)
            {
                // 成功経路の出力存在チェックに引っかかるよう、最小の jpg を置いておく。
                WriteSolidJpeg(context.SaveThumbFileName, Color.White);
                return Task.FromResult(
                    ThumbnailCreateResultFactory.CreateSuccess(
                        context.SaveThumbFileName,
                        context.DurationSec
                    )
                );
            }

            return Task.FromResult(
                ThumbnailCreateResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    result.ErrorMessage
                )
            );
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

        private static void WriteSolidJpeg(string savePath, Color color)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(savePath) ?? ".");
            using Bitmap bitmap = new(32, 24);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(color);
            bitmap.Save(savePath);
        }
    }
}
