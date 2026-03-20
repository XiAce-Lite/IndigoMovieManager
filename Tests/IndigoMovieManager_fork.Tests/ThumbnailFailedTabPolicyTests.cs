using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailFailedTabPolicyTests
{
    [Test]
    public void IsExcludedThumbnailErrorMoviePath_swfは対象外として扱う()
    {
        Assert.That(
            MainWindow.IsExcludedThumbnailErrorMoviePath(@"E:\_サムネイル作成困難動画\SWF\nightmare.swf"),
            Is.True
        );
        Assert.That(
            MainWindow.IsExcludedThumbnailErrorMoviePath(@"E:\_サムネイル作成困難動画\movie.mp4"),
            Is.False
        );
    }

    [Test]
    public void ShouldDisplayThumbnailErrorFailureRecord_成功jpgがある時は一覧へ出さない()
    {
        ThumbnailFailureRecord record = new()
        {
            Status = "gave_up",
        };

        bool result = MainWindow.ShouldDisplayThumbnailErrorFailureRecord(
            record,
            hasSuccessThumbnail: true,
            moviePath: @"E:\_サムネイル作成困難動画\作成1ショットOK\みずがめ座 (2).mp4"
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldDisplayThumbnailErrorFailureRecord_swfは管理行だけでも一覧へ出さない()
    {
        ThumbnailFailureRecord record = new()
        {
            Status = "gave_up",
        };

        bool result = MainWindow.ShouldDisplayThumbnailErrorFailureRecord(
            record,
            hasSuccessThumbnail: false,
            moviePath: @"E:\_サムネイル作成困難動画\SWF\nightmare.swf"
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldDisplayThumbnailErrorFailureRecord_未解決管理行は一覧へ出す()
    {
        ThumbnailFailureRecord record = new()
        {
            Status = "pending_rescue",
        };

        bool result = MainWindow.ShouldDisplayThumbnailErrorFailureRecord(
            record,
            hasSuccessThumbnail: false,
            moviePath: @"E:\_サムネイル作成困難動画\作成1ショットOK\真空エラー2_ghq5_temp.mp4"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRefreshThumbnailErrorRecordsImmediately_下側失敗タブが見えていれば上側失敗タブ選択中でも即時更新する()
    {
        bool result = MainWindow.ShouldRefreshThumbnailErrorRecordsImmediately(
            refreshIfVisible: true,
            isThumbnailErrorTabActive: true,
            isUpperTabRescueSelected: true
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRefreshThumbnailErrorRecordsImmediately_上側失敗タブだけ選択中なら即時更新しない()
    {
        bool result = MainWindow.ShouldRefreshThumbnailErrorRecordsImmediately(
            refreshIfVisible: true,
            isThumbnailErrorTabActive: false,
            isUpperTabRescueSelected: true
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldRefreshThumbnailErrorRecordsImmediately_通常経路では即時更新する()
    {
        bool result = MainWindow.ShouldRefreshThumbnailErrorRecordsImmediately(
            refreshIfVisible: true,
            isThumbnailErrorTabActive: false,
            isUpperTabRescueSelected: false
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRefreshThumbnailErrorSortCountsWithoutBottomTab_下段タブ非表示かつSort28だけTrue()
    {
        Assert.That(
            MainWindow.ShouldRefreshThumbnailErrorSortCountsWithoutBottomTab(
                hasThumbnailErrorBottomTabHost: false,
                currentSort: "28"
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldRefreshThumbnailErrorSortCountsWithoutBottomTab(
                hasThumbnailErrorBottomTabHost: true,
                currentSort: "28"
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldRefreshThumbnailErrorSortCountsWithoutBottomTab(
                hasThumbnailErrorBottomTabHost: false,
                currentSort: "01"
            ),
            Is.False
        );
    }

    [Test]
    public void ShouldRequireThumbnailErrorBottomTabInLayoutRestore_非表示構成では欠落していても必須にしない()
    {
        bool result = MainWindow.ShouldRequireThumbnailErrorBottomTabInLayoutRestore(
            "<LayoutRoot />",
            shouldShowThumbnailErrorBottomTab: false
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldRequireThumbnailErrorBottomTabInLayoutRestore_表示構成では欠落時だけ必須にする()
    {
        bool missingResult = MainWindow.ShouldRequireThumbnailErrorBottomTabInLayoutRestore(
            "<LayoutRoot />",
            shouldShowThumbnailErrorBottomTab: true
        );
        bool existingResult = MainWindow.ShouldRequireThumbnailErrorBottomTabInLayoutRestore(
            "<LayoutRoot ContentId=\"ToolThumbnailError\" />",
            shouldShowThumbnailErrorBottomTab: true
        );

        Assert.That(missingResult, Is.True);
        Assert.That(existingResult, Is.False);
    }
}
