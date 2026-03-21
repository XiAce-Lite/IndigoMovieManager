using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WatchUiSuppressionPolicyTests
{
    // watch起点の欠損救済は通常キュー優先: watch要求時は busy 閾値を 1 に下げる
    [Test]
    public void ResolveMissingThumbRescueBusyThreshold_watch要求時は1になる()
    {
        int t = MainWindow.ResolveMissingThumbnailRescueBusyThreshold(
            isWatchRequest: true,
            defaultBusyThreshold: 500
        );
        Assert.That(t, Is.EqualTo(1));
    }

    // 自動監視中は通常キュー優先: 手動でない&active>=閾値なら救済をスキップ
    [Test]
    public void ShouldSkipMissingThumbRescue_通常キューが忙しいならスキップ()
    {
        bool skip = MainWindow.ShouldSkipMissingThumbnailRescueForBusyQueue(
            isManualRequest: false,
            activeCount: 5,
            busyThreshold: 5
        );
        Assert.That(skip, Is.True);
    }
}