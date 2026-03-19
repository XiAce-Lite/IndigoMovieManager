using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class ThumbnailFailurePlaceholderWriterTests
{
    [Test]
    public void ClassifyFailureKind_DrmKeywordならDrmSuspected()
    {
        ThumbnailFailurePlaceholderKind actual =
            ThumbnailFailurePlaceholderWriter.ClassifyFailureKind(
                @"C:\movies\sample.wmv",
                "wmv3",
                ["[autogen] playready protected content"],
                1024
            );

        Assert.That(actual, Is.EqualTo(ThumbnailFailurePlaceholderKind.DrmSuspected));
    }

    [Test]
    public void ClassifyFailureKind_UnsupportedKeywordならUnsupportedCodec()
    {
        ThumbnailFailurePlaceholderKind actual =
            ThumbnailFailurePlaceholderWriter.ClassifyFailureKind(
                @"C:\movies\sample.mp4",
                "unknown codec",
                ["[ffmediatoolkit] decoder not found"],
                1024
            );

        Assert.That(actual, Is.EqualTo(ThumbnailFailurePlaceholderKind.UnsupportedCodec));
    }

    [Test]
    public void ClassifyFailureKind_0バイト動画ならNoData()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "IndigoMovieManager_fork_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string moviePath = Path.Combine(tempRoot, "empty.mp4");
            File.WriteAllBytes(moviePath, []);

            ThumbnailFailurePlaceholderKind actual =
                ThumbnailFailurePlaceholderWriter.ClassifyFailureKind(
                    moviePath,
                    "",
                    [],
                    0
                );

            Assert.That(actual, Is.EqualTo(ThumbnailFailurePlaceholderKind.NoData));
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
    public void ClassifyFailureKind_AppleDoubleヘッダーならAppleDouble()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "IndigoMovieManager_fork_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string moviePath = Path.Combine(tempRoot, "._sample.mp4");
            File.WriteAllBytes(moviePath, [0x00, 0x05, 0x16, 0x07, 0x00, 0x02, 0x00, 0x00]);

            ThumbnailFailurePlaceholderKind actual =
                ThumbnailFailurePlaceholderWriter.ClassifyFailureKind(
                    moviePath,
                    "",
                    [],
                    new FileInfo(moviePath).Length
                );

            Assert.That(actual, Is.EqualTo(ThumbnailFailurePlaceholderKind.AppleDouble));
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
    public void ClassifyFailureKind_FlashシグネチャならShockwaveFlash()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "IndigoMovieManager_fork_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string moviePath = Path.Combine(tempRoot, "movie.swf");
            File.WriteAllBytes(moviePath, [0x46, 0x57, 0x53, 0x09, 0x00]);

            ThumbnailFailurePlaceholderKind actual =
                ThumbnailFailurePlaceholderWriter.ClassifyFailureKind(
                    moviePath,
                    "",
                    ["[autogen] invalid data found when processing input"],
                    new FileInfo(moviePath).Length
                );

            Assert.That(actual, Is.EqualTo(ThumbnailFailurePlaceholderKind.ShockwaveFlash));
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
    public void ResolveProcessEngineId_UnsupportedCodecならplaceholder_unsupported()
    {
        string actual = ThumbnailFailurePlaceholderWriter.ResolveProcessEngineId(
            ThumbnailFailurePlaceholderKind.UnsupportedCodec
        );

        Assert.That(actual, Is.EqualTo("placeholder-unsupported"));
    }

    [Test]
    public void ResolveProcessEngineId_NoDataならplaceholder_no_data()
    {
        string actual = ThumbnailFailurePlaceholderWriter.ResolveProcessEngineId(
            ThumbnailFailurePlaceholderKind.NoData
        );

        Assert.That(actual, Is.EqualTo("placeholder-no-data"));
    }

    [Test]
    public void ResolveProcessEngineId_AppleDoubleならplaceholder_appledouble()
    {
        string actual = ThumbnailFailurePlaceholderWriter.ResolveProcessEngineId(
            ThumbnailFailurePlaceholderKind.AppleDouble
        );

        Assert.That(actual, Is.EqualTo("placeholder-appledouble"));
    }

    [Test]
    public void ResolveProcessEngineId_ShockwaveFlashならplaceholder_flash()
    {
        string actual = ThumbnailFailurePlaceholderWriter.ResolveProcessEngineId(
            ThumbnailFailurePlaceholderKind.ShockwaveFlash
        );

        Assert.That(actual, Is.EqualTo("placeholder-flash"));
    }
}
