using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailCreateWorkflowCoordinatorTests
{
    [Test]
    public async Task ExecuteAsync_欠損動画ならmissing_movieで即成功する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            ThumbnailCreateWorkflowCoordinator workflow = CreateWorkflow(
                tempRoot,
                out RecordingProcessLogWriter writer,
                out RecordingEngine ffmedia,
                out RecordingEngine ffmpeg1pass,
                out RecordingEngine opencv,
                out RecordingEngine autogen
            );
            string thumbRoot = Path.Combine(tempRoot, "thumb-root");
            Directory.CreateDirectory(thumbRoot);
            string missingMoviePath = Path.Combine(tempRoot, "missing.mp4");

            ThumbnailCreateResult actual = await workflow.ExecuteAsync(
                new ThumbnailCreateWorkflowRequest
                {
                    Request = new ThumbnailRequest
                    {
                        MovieId = 100,
                        TabIndex = 0,
                        MovieFullPath = missingMoviePath,
                        Hash = "workflow-missing",
                    },
                    DbName = "testdb",
                    ThumbFolder = thumbRoot,
                    IsResizeThumb = true,
                    IsManual = false,
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.IsSuccess, Is.True);
                Assert.That(actual.ProcessEngineId, Is.EqualTo("missing-movie"));
                Assert.That(File.Exists(actual.SaveThumbFileName), Is.True);
                Assert.That(writer.Entries.Count, Is.EqualTo(1));
                Assert.That(writer.Entries[0].EngineId, Is.EqualTo("missing-movie"));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
                Assert.That(autogen.CreateCallCount, Is.EqualTo(0));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ExecuteAsync_manualでWB互換メタが無ければfallback後にautogenで続行する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            ThumbnailCreateWorkflowCoordinator workflow = CreateWorkflow(
                tempRoot,
                out RecordingProcessLogWriter writer,
                out RecordingEngine ffmedia,
                out RecordingEngine ffmpeg1pass,
                out RecordingEngine opencv,
                out RecordingEngine autogen
            );
            string thumbRoot = Path.Combine(tempRoot, "thumb-root");
            Directory.CreateDirectory(thumbRoot);
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            File.WriteAllBytes(moviePath, [0x01, 0x02, 0x03, 0x04]);

            string savePath = ThumbnailPathResolver.BuildThumbnailPath(
                ThumbnailLayoutProfileResolver.Resolve(0).BuildOutPath(thumbRoot),
                moviePath,
                "workflow-manual"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            File.WriteAllBytes(savePath, [0x10, 0x20, 0x30, 0x40]);

            ThumbnailCreateResult actual = await workflow.ExecuteAsync(
                new ThumbnailCreateWorkflowRequest
                {
                    Request = new ThumbnailRequest
                    {
                        MovieId = 101,
                        TabIndex = 0,
                        MovieFullPath = moviePath,
                        Hash = "workflow-manual",
                    },
                    DbName = "testdb",
                    ThumbFolder = thumbRoot,
                    IsResizeThumb = true,
                    IsManual = true,
                }
            );

            Assert.Multiple(() =>
            {
                // 現行契約では manual metadata 欠落を precheck では落とさず、context builder 側で作り直しへ寄せる。
                Assert.That(actual.IsSuccess, Is.True);
                Assert.That(actual.ProcessEngineId, Is.EqualTo("autogen"));
                Assert.That(writer.Entries.Count, Is.EqualTo(1));
                Assert.That(writer.Entries[0].EngineId, Is.EqualTo("autogen"));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static ThumbnailCreateWorkflowCoordinator CreateWorkflow(
        string tempRoot,
        out RecordingProcessLogWriter writer,
        out RecordingEngine ffmedia,
        out RecordingEngine ffmpeg1pass,
        out RecordingEngine opencv,
        out RecordingEngine autogen
    )
    {
        string placeholderPath = Path.Combine(tempRoot, "no-file.jpg");
        File.WriteAllBytes(placeholderPath, [0x10, 0x20, 0x30]);

        writer = new RecordingProcessLogWriter();
        ffmedia = new RecordingEngine("ffmediatoolkit");
        ffmpeg1pass = new RecordingEngine("ffmpeg1pass");
        opencv = new RecordingEngine("opencv");
        autogen = new RecordingEngine("autogen");

        ThumbnailMovieMetaResolver resolver = new(new FakeVideoMetadataProvider(""));
        ThumbnailCreatePreparationResolver preparationResolver = new(resolver);
        ThumbnailJobContextBuilder jobContextBuilder = new(resolver);
        ThumbnailCreateResultFinalizer finalizer = new(writer, resolver);
        ThumbnailPrecheckCoordinator precheckCoordinator = new(
            new TestHostRuntime(placeholderPath),
            resolver,
            jobContextBuilder,
            finalizer
        );
        ThumbnailEngineRouter engineRouter = new([ffmedia, ffmpeg1pass, opencv, autogen]);
        ThumbnailEngineExecutionPolicy engineExecutionPolicy = new(
            ffmedia,
            ffmpeg1pass,
            opencv,
            autogen
        );
        ThumbnailEngineExecutionCoordinator engineExecutionCoordinator = new(
            engineExecutionPolicy
        );
        return new ThumbnailCreateWorkflowCoordinator(
            preparationResolver,
            precheckCoordinator,
            jobContextBuilder,
            engineRouter,
            engineExecutionPolicy,
            engineExecutionCoordinator,
            finalizer
        );
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

    private sealed class RecordingProcessLogWriter : IThumbnailCreateProcessLogWriter
    {
        public List<ThumbnailCreateProcessLogEntry> Entries { get; } = [];

        public void Write(ThumbnailCreateProcessLogEntry entry)
        {
            Entries.Add(entry);
        }
    }

    private sealed class RecordingEngine : IThumbnailGenerationEngine
    {
        public RecordingEngine(string engineId)
        {
            EngineId = engineId;
            EngineName = engineId;
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
            CancellationToken cancellationToken = default
        )
        {
            CreateCallCount++;
            return Task.FromResult(
                ThumbnailCreateResultFactory.CreateSuccess(
                    context?.SaveThumbFileName ?? "",
                    context?.DurationSec
                )
            );
        }

        public Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(false);
        }
    }

    private sealed class TestHostRuntime : IThumbnailCreationHostRuntime
    {
        private readonly string placeholderPath;

        public TestHostRuntime(string placeholderPath)
        {
            this.placeholderPath = placeholderPath;
        }

        public string ResolveMissingMoviePlaceholderPath(int tabIndex)
        {
            return placeholderPath;
        }

        public string ResolveProcessLogPath(string fileName)
        {
            return Path.Combine(Path.GetTempPath(), fileName ?? "");
        }
    }

    private sealed class FakeVideoMetadataProvider : IVideoMetadataProvider
    {
        private readonly string codec;

        public FakeVideoMetadataProvider(string codec)
        {
            this.codec = codec;
        }

        public bool TryGetVideoCodec(string moviePath, out string codec)
        {
            codec = this.codec;
            return !string.IsNullOrWhiteSpace(codec);
        }

        public bool TryGetDurationSec(string moviePath, out double durationSec)
        {
            durationSec = 0;
            return false;
        }
    }
}
