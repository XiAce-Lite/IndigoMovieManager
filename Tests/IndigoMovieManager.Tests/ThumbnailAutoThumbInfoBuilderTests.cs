using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailAutoThumbInfoBuilderTests
{
    [Test]
    public void Build_短尺動画でも末尾超えせずpanel数ぶん秒配列を返す()
    {
        ThumbInfo actual = ThumbnailAutoThumbInfoBuilder.Build(
            ThumbnailLayoutProfileResolver.Small,
            2.1
        );

        Assert.Multiple(() =>
        {
            Assert.That(actual.IsThumbnail, Is.True);
            Assert.That(actual.ThumbSec.Count, Is.EqualTo(3));
            Assert.That(actual.ThumbSec[^1], Is.LessThanOrEqualTo(2));
        });
    }

    [Test]
    public void ResolveSafeMaxCaptureSec_端数は末尾手前へ丸める()
    {
        int actual = ThumbnailAutoThumbInfoBuilder.ResolveSafeMaxCaptureSec(10.0001d);

        Assert.That(actual, Is.EqualTo(9));
    }
}
