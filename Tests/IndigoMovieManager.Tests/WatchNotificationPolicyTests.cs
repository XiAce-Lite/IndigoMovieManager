using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchNotificationPolicyTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void CanShowWatchFolderNotice_空値はFalseを返す(string? folderPath)
    {
        bool result = MainWindow.CanShowWatchFolderNotice(folderPath);

        Assert.That(result, Is.False);
    }

    [Test]
    public void CanShowWatchFolderNotice_前後空白を除去して判定する()
    {
        bool result = MainWindow.CanShowWatchFolderNotice("  E:\\Movies\\idol  ");

        Assert.That(result, Is.True);
    }

    [Test]
    public void BuildWatchFolderScanStartNoticeMessage_前後空白を除去した文言を返す()
    {
        string message = MainWindow.BuildWatchFolderScanStartNoticeMessage("  E:\\Movies\\idol  ");

        Assert.That(message, Is.EqualTo("E:\\Movies\\idol 監視実施中…"));
    }

    [Test]
    public void BuildWatchFolderUpdatedNoticeMessage_前後空白を除去した文言を返す()
    {
        string message = MainWindow.BuildWatchFolderUpdatedNoticeMessage("  E:\\Movies\\idol  ");

        Assert.That(message, Is.EqualTo("E:\\Movies\\idolに更新あり。"));
    }

    [Test]
    public void BuildEverythingFallbackNoticeMessage_detail空値なら括弧なし文言を返す()
    {
        string message = MainWindow.BuildEverythingFallbackNoticeMessage("   ");

        Assert.That(
            message,
            Is.EqualTo("Everything連携を利用できないため通常監視で継続します。")
        );
    }

    [Test]
    public void BuildEverythingFallbackNoticeMessage_detailありなら括弧付き文言を返す()
    {
        string message = MainWindow.BuildEverythingFallbackNoticeMessage("index-not-ready");

        Assert.That(
            message,
            Is.EqualTo("Everything連携を利用できないため通常監視で継続します。(index-not-ready)")
        );
    }
}
