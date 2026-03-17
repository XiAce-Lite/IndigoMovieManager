using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public class ThumbnailCreationHostRuntimeTests
{
    [Test]
    public async Task PublicCtor_欠損動画時はHostRuntimeのplaceholderを使い既定ではログを書かない()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            string placeholderPath = Path.Combine(tempRoot, "public-no-file.jpg");
            File.WriteAllBytes(placeholderPath, [0x01, 0x02, 0x03, 0x04]);

            string logPath = Path.Combine(tempRoot, "logs", "thumbnail-create-process.csv");
            var hostRuntime = new TestThumbnailCreationHostRuntime(placeholderPath, logPath);
            var service = new ThumbnailCreationService(hostRuntime);

            ThumbnailCreateResult result = await service.CreateThumbAsync(
                new QueueObj
                {
                    MovieId = 70,
                    Tabindex = 3,
                    MovieFullPath = Path.Combine(tempRoot, "missing-public.mp4"),
                    Hash = "publichostruntime",
                },
                dbName: "testdb",
                thumbFolder: thumbRoot,
                isResizeThumb: true,
                isManual: false
            );

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(File.Exists(result.SaveThumbFileName), Is.True);
            Assert.That(
                File.ReadAllBytes(result.SaveThumbFileName),
                Is.EqualTo(File.ReadAllBytes(placeholderPath))
            );
            Assert.That(File.Exists(logPath), Is.False);
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
    public async Task CreateThumbAsync_欠損動画時はHostRuntimeのplaceholderを使い既定ではログを書かない()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            string placeholderPath = Path.Combine(tempRoot, "custom-no-file.jpg");
            File.WriteAllBytes(placeholderPath, [0x11, 0x22, 0x33, 0x44]);

            string logPath = Path.Combine(tempRoot, "logs", "thumbnail-create-process.csv");
            var hostRuntime = new TestThumbnailCreationHostRuntime(placeholderPath, logPath);

            var ffmedia = new RecordingEngine("ffmediatoolkit");
            var ffmpeg1pass = new RecordingEngine("ffmpeg1pass");
            var opencv = new RecordingEngine("opencv");
            var autogen = new RecordingEngine("autogen");
            var service = new ThumbnailCreationService(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                hostRuntime
            );

            ThumbnailCreateResult result = await service.CreateThumbAsync(
                new QueueObj
                {
                    MovieId = 77,
                    Tabindex = 3,
                    MovieFullPath = Path.Combine(tempRoot, "missing.mp4"),
                    Hash = "hostruntime",
                },
                dbName: "testdb",
                thumbFolder: thumbRoot,
                isResizeThumb: true,
                isManual: false
            );

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(File.Exists(result.SaveThumbFileName), Is.True);
            Assert.That(
                File.ReadAllBytes(result.SaveThumbFileName),
                Is.EqualTo(File.ReadAllBytes(placeholderPath))
            );
            Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
            Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
            Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
            Assert.That(autogen.CreateCallCount, Is.EqualTo(0));

            Assert.That(File.Exists(logPath), Is.False);
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
    public void DefaultHostRuntime_ログ出力先を明示できる()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string logDir = Path.Combine(tempRoot, "custom-logs");
            var hostRuntime = new DefaultThumbnailCreationHostRuntime(logDir);

            string logPath = hostRuntime.ResolveProcessLogPath("test.csv");

            Assert.That(logPath, Is.EqualTo(Path.Combine(logDir, "test.csv")));
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
    public async Task CreateThumbAsync_欠損動画時はProcessLogWriterへ委譲する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            string placeholderPath = Path.Combine(tempRoot, "custom-no-file.jpg");
            File.WriteAllBytes(placeholderPath, [0x21, 0x22, 0x23, 0x24]);

            string logPath = Path.Combine(tempRoot, "logs", "ignored.csv");
            var hostRuntime = new TestThumbnailCreationHostRuntime(placeholderPath, logPath);
            var processLogWriter = new RecordingProcessLogWriter();

            var ffmedia = new RecordingEngine("ffmediatoolkit");
            var ffmpeg1pass = new RecordingEngine("ffmpeg1pass");
            var opencv = new RecordingEngine("opencv");
            var autogen = new RecordingEngine("autogen");
            var service = new ThumbnailCreationService(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                hostRuntime,
                processLogWriter
            );

            ThumbnailCreateResult result = await service.CreateThumbAsync(
                new QueueObj
                {
                    MovieId = 88,
                    Tabindex = 0,
                    MovieFullPath = Path.Combine(tempRoot, "missing-writer.mp4"),
                    Hash = "writer",
                },
                dbName: "testdb",
                thumbFolder: thumbRoot,
                isResizeThumb: true,
                isManual: false
            );

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(processLogWriter.Entries.Count, Is.EqualTo(1));

            ThumbnailCreateProcessLogEntry entry = processLogWriter.Entries[0];
            Assert.That(entry.EngineId, Is.EqualTo("missing-movie"));
            Assert.That(entry.MovieFullPath, Does.EndWith("missing-writer.mp4"));
            Assert.That(entry.OutputPath, Is.EqualTo(result.SaveThumbFileName));
            Assert.That(entry.IsSuccess, Is.True);
            Assert.That(entry.ErrorMessage, Is.EqualTo(string.Empty));
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
    public async Task PublicCtor_欠損動画時はProcessLogWriterへ委譲する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            string placeholderPath = Path.Combine(tempRoot, "public-writer-no-file.jpg");
            File.WriteAllBytes(placeholderPath, [0x31, 0x32, 0x33, 0x34]);

            string logPath = Path.Combine(tempRoot, "logs", "ignored-public.csv");
            var hostRuntime = new TestThumbnailCreationHostRuntime(placeholderPath, logPath);
            var processLogWriter = new RecordingProcessLogWriter();
            var service = new ThumbnailCreationService(hostRuntime, processLogWriter);

            ThumbnailCreateResult result = await service.CreateThumbAsync(
                new QueueObj
                {
                    MovieId = 89,
                    Tabindex = 0,
                    MovieFullPath = Path.Combine(tempRoot, "missing-public-writer.mp4"),
                    Hash = "publicwriter",
                },
                dbName: "testdb",
                thumbFolder: thumbRoot,
                isResizeThumb: true,
                isManual: false
            );

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(processLogWriter.Entries.Count, Is.EqualTo(1));

            ThumbnailCreateProcessLogEntry entry = processLogWriter.Entries[0];
            Assert.That(entry.EngineId, Is.EqualTo("missing-movie"));
            Assert.That(entry.OutputPath, Is.EqualTo(result.SaveThumbFileName));
            Assert.That(entry.IsSuccess, Is.True);
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

    private sealed class TestThumbnailCreationHostRuntime : IThumbnailCreationHostRuntime
    {
        private readonly string placeholderPath;
        private readonly string logPath;

        public TestThumbnailCreationHostRuntime(string placeholderPath, string logPath)
        {
            this.placeholderPath = placeholderPath;
            this.logPath = logPath;
        }

        public string ResolveMissingMoviePlaceholderPath(int tabIndex)
        {
            return placeholderPath;
        }

        public string ResolveProcessLogPath(string fileName)
        {
            return logPath;
        }
    }

    private sealed class RecordingProcessLogWriter : IThumbnailCreateProcessLogWriter
    {
        public List<ThumbnailCreateProcessLogEntry> Entries { get; } = [];

        public void Write(ThumbnailCreateProcessLogEntry entry)
        {
            Entries.Add(
                new ThumbnailCreateProcessLogEntry
                {
                    EngineId = entry.EngineId,
                    MovieFullPath = entry.MovieFullPath,
                    Codec = entry.Codec,
                    DurationSec = entry.DurationSec,
                    FileSizeBytes = entry.FileSizeBytes,
                    OutputPath = entry.OutputPath,
                    IsSuccess = entry.IsSuccess,
                    ErrorMessage = entry.ErrorMessage,
                }
            );
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
            CancellationToken cts = default
        )
        {
            CreateCallCount++;
            return Task.FromResult(
                ThumbnailCreateResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    $"{EngineId} should not run"
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
    }
}
