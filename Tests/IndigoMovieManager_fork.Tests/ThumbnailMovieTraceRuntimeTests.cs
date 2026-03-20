using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailMovieTraceRuntimeTests
{
    [TestCase("1", true)]
    [TestCase("true", true)]
    [TestCase("on", true)]
    [TestCase("yes", true)]
    [TestCase("0", false)]
    [TestCase("", false)]
    public void IsEnabledValue_環境変数の真偽を解釈する(string rawValue, bool expected)
    {
        bool actual = ThumbnailMovieTraceRuntime.IsEnabledValue(rawValue);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void ShouldTraceMovie_フィルタ空なら常にtrue()
    {
        bool actual = ThumbnailMovieTraceRuntime.ShouldTraceMovie(
            @"E:\movie\sample.mkv",
            "",
            ""
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldTraceMovie_フルパス一致ならtrue()
    {
        bool actual = ThumbnailMovieTraceRuntime.ShouldTraceMovie(
            @"E:\movie\sample.mkv",
            "",
            @"C:\other\miss.mp4;E:\movie\sample.mkv"
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldTraceMovie_ファイル名一致ならtrue()
    {
        bool actual = ThumbnailMovieTraceRuntime.ShouldTraceMovie(
            @"E:\movie\sample.mkv",
            "",
            "other.mp4;sample.mkv"
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldTraceMovie_ソース上書き側でも判定できる()
    {
        bool actual = ThumbnailMovieTraceRuntime.ShouldTraceMovie(
            @"E:\movie\sample.mkv",
            @"F:\repair\sample.fixed.mkv",
            "sample.fixed.mkv"
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void NormalizeTraceId_空白と長すぎる値を整える()
    {
        string actual = ThumbnailMovieTraceRuntime.NormalizeTraceId(
            "  " + new string('a', 200) + "  "
        );

        Assert.That(actual.Length, Is.EqualTo(128));
        Assert.That(actual, Does.StartWith("a"));
    }
}
