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
                "wmv3",
                ["[autogen] playready protected content"]
            );

        Assert.That(actual, Is.EqualTo(ThumbnailFailurePlaceholderKind.DrmSuspected));
    }

    [Test]
    public void ClassifyFailureKind_UnsupportedKeywordならUnsupportedCodec()
    {
        ThumbnailFailurePlaceholderKind actual =
            ThumbnailFailurePlaceholderWriter.ClassifyFailureKind(
                "unknown codec",
                ["[ffmediatoolkit] decoder not found"]
            );

        Assert.That(actual, Is.EqualTo(ThumbnailFailurePlaceholderKind.UnsupportedCodec));
    }

    [Test]
    public void ResolveProcessEngineId_UnsupportedCodecならplaceholder_unsupported()
    {
        string actual = ThumbnailFailurePlaceholderWriter.ResolveProcessEngineId(
            ThumbnailFailurePlaceholderKind.UnsupportedCodec
        );

        Assert.That(actual, Is.EqualTo("placeholder-unsupported"));
    }
}
