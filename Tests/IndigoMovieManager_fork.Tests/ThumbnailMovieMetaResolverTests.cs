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

    [Test]
    public void GetCachedMovieMeta_Wmv旧DRM_GUIDならDRM疑いを保持する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateAsfDrmFile(
                tempRoot,
                "legacy-drm.wmv",
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
                ]
            );
            var resolver = new ThumbnailMovieMetaResolver(new FakeVideoMetadataProvider());

            CachedMovieMeta actual = resolver.GetCachedMovieMeta(moviePath, "hash-legacy", out _);

            Assert.Multiple(() =>
            {
                Assert.That(actual.IsDrmSuspected, Is.True);
                Assert.That(
                    actual.DrmDetail,
                    Does.Contain("content_encryption_object_guid_found_offset=")
                );
                Assert.That(
                    actual.DrmDetail,
                    Does.Contain("2211B3FB-BD23-11D2-B4B7-00A0C955FC6E")
                );
            });
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
    public void GetCachedMovieMeta_Wmv拡張DRM_GUIDならDRM疑いを保持する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateAsfDrmFile(
                tempRoot,
                "extended-drm.wmv",
                [
                    0x14,
                    0xE6,
                    0x8A,
                    0x29,
                    0x22,
                    0x26,
                    0x17,
                    0x4C,
                    0xB9,
                    0x35,
                    0xDA,
                    0xE0,
                    0x7E,
                    0xE9,
                    0x28,
                    0x9C,
                ]
            );
            var resolver = new ThumbnailMovieMetaResolver(new FakeVideoMetadataProvider());

            CachedMovieMeta actual = resolver.GetCachedMovieMeta(moviePath, "hash-extended", out _);

            Assert.Multiple(() =>
            {
                Assert.That(actual.IsDrmSuspected, Is.True);
                Assert.That(
                    actual.DrmDetail,
                    Does.Contain("extended_content_encryption_object_guid_found_offset=")
                );
                Assert.That(
                    actual.DrmDetail,
                    Does.Contain("298AE614-2622-4C17-B935-DAE07EE9289C")
                );
            });
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

    private static string CreateAsfDrmFile(string tempRoot, string fileName, byte[] drmGuid)
    {
        string moviePath = Path.Combine(tempRoot, fileName);
        byte[] header = new byte[4096];
        Array.Copy(drmGuid, 0, header, 512, drmGuid.Length);
        File.WriteAllBytes(moviePath, header);
        return moviePath;
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
