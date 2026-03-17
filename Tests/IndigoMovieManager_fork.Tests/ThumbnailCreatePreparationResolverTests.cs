using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailCreatePreparationResolverTests
{
    [Test]
    public void Prepare_空hashならcache由来hashをrequestへ戻しsavePathを組み立てる()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = Path.Combine(tempRoot, "sample.mp4");
            string altMoviePath = Path.Combine(tempRoot, "alt-sample.mp4");
            string thumbRoot = Path.Combine(tempRoot, "thumb-root");
            File.WriteAllBytes(moviePath, [0x01, 0x02, 0x03, 0x04]);

            ThumbnailMovieMetaResolver resolver = new(new FakeVideoMetadataProvider("", 0));
            ThumbnailCreatePreparationResolver preparationResolver = new(resolver);
            ThumbnailRequest request = new()
            {
                MovieId = 1,
                TabIndex = 0,
                MovieFullPath = moviePath,
                Hash = "",
            };

            ThumbnailCreatePreparation actual = preparationResolver.Prepare(
                new ThumbnailCreatePreparationRequest
                {
                    Request = request,
                    DbName = "testdb",
                    ThumbFolder = thumbRoot,
                    SourceMovieFullPathOverride = altMoviePath,
                    InitialEngineHint = " auto ",
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(request.Hash, Is.Not.Empty);
                Assert.That(actual.Request.Hash, Is.EqualTo(request.Hash));
                Assert.That(actual.LayoutProfile.FolderName, Is.EqualTo("120x90x3x1"));
                Assert.That(actual.ThumbnailOutPath, Is.EqualTo(Path.Combine(thumbRoot, "120x90x3x1")));
                Assert.That(actual.MovieFullPath, Is.EqualTo(moviePath));
                Assert.That(actual.SourceMovieFullPath, Is.EqualTo(altMoviePath));
                Assert.That(actual.InitialEngineHint, Is.EqualTo("auto"));
                Assert.That(actual.SaveThumbFileName, Does.Contain(request.Hash));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void ResolveDurationIfMissing_未確定ならprovider値で補完する()
    {
        ThumbnailMovieMetaResolver resolver = new(new FakeVideoMetadataProvider("h264", 42));
        ThumbnailCreatePreparationResolver preparationResolver = new(resolver);
        string tempRoot = CreateTempRoot();

        try
        {
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            File.WriteAllBytes(moviePath, [0x01, 0x02, 0x03]);
            ThumbnailRequest request = new()
            {
                MovieId = 2,
                TabIndex = 0,
                MovieFullPath = moviePath,
                Hash = "hashx",
            };

            ThumbnailCreatePreparation preparation = preparationResolver.Prepare(
                new ThumbnailCreatePreparationRequest
                {
                    Request = request,
                    DbName = "testdb",
                    ThumbFolder = tempRoot,
                }
            );

            double? actual = preparationResolver.ResolveDurationIfMissing(preparation);

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.EqualTo(42));
                Assert.That(preparation.DurationSec, Is.EqualTo(42));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
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

    private sealed class FakeVideoMetadataProvider : IVideoMetadataProvider
    {
        private readonly string codec;
        private readonly double durationSec;

        public FakeVideoMetadataProvider(string codec, double durationSec)
        {
            this.codec = codec;
            this.durationSec = durationSec;
        }

        public bool TryGetVideoCodec(string moviePath, out string codec)
        {
            codec = this.codec;
            return !string.IsNullOrWhiteSpace(codec);
        }

        public bool TryGetDurationSec(string moviePath, out double durationSec)
        {
            durationSec = this.durationSec;
            return this.durationSec > 0;
        }
    }
}
