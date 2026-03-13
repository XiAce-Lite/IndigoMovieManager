using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class TabInfoTests
{
    [Test]
    public void Constructor_ThumbFolder未指定時_Exe直下のThumb配下を使う()
    {
        TabInfo tabInfo = new(0, "sampledb");
        string expected = Path.Combine(
            AppContext.BaseDirectory,
            "Thumb",
            "sampledb",
            "120x90x3x1"
        );

        Assert.That(tabInfo.OutPath, Is.EqualTo(expected));
    }

    [Test]
    public void Constructor_ThumbFolder指定時_指定ルートを優先する()
    {
        string customRoot = Path.Combine(Path.GetTempPath(), "imm-thumb-test");
        TabInfo tabInfo = new(4, "sampledb", customRoot);
        string expected = Path.Combine(customRoot, "120x90x5x2");

        Assert.That(tabInfo.OutPath, Is.EqualTo(expected));
    }
}
