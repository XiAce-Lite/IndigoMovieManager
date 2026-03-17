using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class ThumbnailMovieMetaResolverTests
{
    [Test]
    public void ResolveFileSizeBytes_正のhintがあればファイルを見に行かない()
    {
        var resolver = new ThumbnailMovieMetaResolver(new FakeVideoMetadataProvider());

        long actual = resolver.ResolveFileSizeBytes(@"C:\dummy\missing.mp4", 1234);

        Assert.That(actual, Is.EqualTo(1234));
    }

    [Test]
    public void ResolveDurationSec_Provider値を返し同一cacheKeyへ保持する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            File.WriteAllBytes(moviePath, [0x01, 0x02, 0x03]);

            var resolver = new ThumbnailMovieMetaResolver(
                new FakeVideoMetadataProvider(durationSec: 42d)
            );

            CachedMovieMeta meta = resolver.GetCachedMovieMeta(moviePath, "hash-hint", out string cacheKey);
            double? resolved = resolver.ResolveDurationSec(moviePath, cacheKey, meta);
            CachedMovieMeta cached = resolver.GetCachedMovieMeta(moviePath, "hash-hint", out _);

            Assert.That(resolved, Is.EqualTo(42d));
            Assert.That(cached.DurationSec, Is.EqualTo(42d));
            Assert.That(cached.Hash, Is.EqualTo("hash-hint"));
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
    public void ResolveVideoCodec_Providerが返したcodecをそのまま使う()
    {
        var resolver = new ThumbnailMovieMetaResolver(
            new FakeVideoMetadataProvider(codec: "hevc")
        );

        string actual = resolver.ResolveVideoCodec(@"C:\dummy\movie.mp4");

        Assert.That(actual, Is.EqualTo("hevc"));
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

    private sealed class FakeVideoMetadataProvider : IVideoMetadataProvider
    {
        private readonly string codec;
        private readonly double? durationSec;

        public FakeVideoMetadataProvider(string codec = "", double? durationSec = null)
        {
            this.codec = codec ?? "";
            this.durationSec = durationSec;
        }

        public bool TryGetVideoCodec(string moviePath, out string codec)
        {
            codec = this.codec;
            return !string.IsNullOrWhiteSpace(this.codec);
        }

        public bool TryGetDurationSec(string moviePath, out double durationSec)
        {
            if (this.durationSec.HasValue)
            {
                durationSec = this.durationSec.Value;
                return true;
            }

            durationSec = 0;
            return false;
        }
    }
}
