using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailCreateResultFinalizerTests
{
    [Test]
    public void FinalizeImmediate_既知durationを補完してProcessLogへ流す()
    {
        RecordingProcessLogWriter writer = new();
        ThumbnailMovieMetaResolver resolver = new(new FakeVideoMetadataProvider(""));
        ThumbnailCreateResultFinalizer finalizer = new(writer, resolver);

        ThumbnailCreateResult actual = finalizer.FinalizeImmediate(
            new ThumbnailImmediateFinalizationRequest
            {
                Result = ThumbnailCreateResultFactory.CreateSuccess(@"C:\thumb\ok.jpg", null),
                EngineId = "ffmediatoolkit",
                MovieFullPath = @"C:\movies\sample.mp4",
                Codec = "h264",
                KnownDurationSec = 12.5,
                FileSizeBytes = 1234,
                OutputPath = @"C:\thumb\ok.jpg",
            }
        );

        Assert.Multiple(() =>
        {
            Assert.That(actual.ProcessEngineId, Is.EqualTo("ffmediatoolkit"));
            Assert.That(writer.Entries.Count, Is.EqualTo(1));
            Assert.That(writer.Entries[0].DurationSec, Is.EqualTo(12.5));
            Assert.That(writer.Entries[0].EngineId, Is.EqualTo("ffmediatoolkit"));
            Assert.That(writer.Entries[0].OutputPath, Is.EqualTo(@"C:\thumb\ok.jpg"));
        });
    }

    [Test]
    public void FinalizeExecution_unsupported失敗ならplaceholder化して成功扱いにする()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            RecordingProcessLogWriter writer = new();
            ThumbnailMovieMetaResolver resolver = new(new FakeVideoMetadataProvider(""));
            ThumbnailCreateResultFinalizer finalizer = new(writer, resolver);
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            string outPath = Path.Combine(tempRoot, "thumb");
            string savePath = Path.Combine(outPath, "thumb.jpg");
            Directory.CreateDirectory(outPath);
            File.WriteAllBytes(moviePath, [0x01, 0x02, 0x03]);

            ThumbnailJobContext context = CreateContext(moviePath, outPath, savePath, "unknown codec");
            ThumbnailCreateResult actual = finalizer.FinalizeExecution(
                new ThumbnailExecutionFinalizationRequest
                {
                    Result = ThumbnailCreateResultFactory.CreateFailed(
                        savePath,
                        60,
                        "decoder not found"
                    ),
                    ProcessEngineId = "autogen",
                    Context = context,
                    EngineErrorMessages = ["[autogen] decoder not found"],
                    MovieFullPath = moviePath,
                    KnownDurationSec = 60,
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.IsSuccess, Is.True);
                Assert.That(actual.ProcessEngineId, Is.EqualTo("placeholder-unsupported"));
                Assert.That(File.Exists(savePath), Is.True);
                Assert.That(writer.Entries.Count, Is.EqualTo(1));
                Assert.That(writer.Entries[0].EngineId, Is.EqualTo("placeholder-unsupported"));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void FinalizeExecution_失敗のままならErrorMarkerとdurationCacheを更新する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            RecordingProcessLogWriter writer = new();
            ThumbnailMovieMetaResolver resolver = new(new FakeVideoMetadataProvider(""));
            ThumbnailCreateResultFinalizer finalizer = new(writer, resolver);
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            string outPath = Path.Combine(tempRoot, "thumb");
            string savePath = Path.Combine(outPath, "thumb.jpg");
            Directory.CreateDirectory(outPath);
            File.WriteAllBytes(moviePath, [0x01, 0x02, 0x03]);

            CachedMovieMeta cacheMeta = resolver.GetCachedMovieMeta(moviePath, "hashx", out string cacheKey);
            ThumbnailJobContext context = CreateContext(moviePath, outPath, savePath, "");

            ThumbnailCreateResult actual = finalizer.FinalizeExecution(
                new ThumbnailExecutionFinalizationRequest
                {
                    Result = ThumbnailCreateResultFactory.CreateFailed(
                        savePath,
                        33,
                        "unclassified failure"
                    ),
                    ProcessEngineId = "autogen",
                    Context = context,
                    EngineErrorMessages = ["[autogen] unclassified failure"],
                    MovieFullPath = moviePath,
                    KnownDurationSec = null,
                    CacheKey = cacheKey,
                    CacheMeta = cacheMeta,
                }
            );

            CachedMovieMeta refreshed = resolver.GetCachedMovieMeta(moviePath, "hashx", out _);
            string markerPath = ThumbnailPathResolver.BuildErrorMarkerPath(outPath, moviePath);

            Assert.Multiple(() =>
            {
                Assert.That(actual.IsSuccess, Is.False);
                Assert.That(File.Exists(markerPath), Is.True);
                Assert.That(refreshed.DurationSec, Is.EqualTo(33));
                Assert.That(writer.Entries.Count, Is.EqualTo(1));
                Assert.That(writer.Entries[0].DurationSec, Is.EqualTo(33));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static ThumbnailJobContext CreateContext(
        string moviePath,
        string outPath,
        string savePath,
        string videoCodec
    )
    {
        return new ThumbnailJobContext
        {
            Request = new ThumbnailRequest
            {
                MovieId = 1,
                TabIndex = 0,
                MovieFullPath = moviePath,
                Hash = "hash1",
            },
            LayoutProfile = ThumbnailLayoutProfileResolver.Small,
            ThumbnailOutPath = outPath,
            ThumbInfo = ThumbnailAutoThumbInfoBuilder.Build(
                ThumbnailLayoutProfileResolver.Small,
                60
            ),
            MovieFullPath = moviePath,
            SaveThumbFileName = savePath,
            IsResizeThumb = true,
            IsManual = false,
            DurationSec = 60,
            FileSizeBytes = 1024,
            AverageBitrateMbps = 1.0,
            HasEmojiPath = false,
            VideoCodec = videoCodec,
            InitialEngineHint = "",
        };
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
