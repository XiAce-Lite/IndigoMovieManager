using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WatchDeferredScanStatePolicyTests
{
    // 新規動画スキャン: visible-onlyを通過し、pendingが閾値未満ならflushせずdeferする
    [Test]
    public void NewMovieScanDecision_閾値未満ならdeferする()
    {
        var visible = MainWindow.BuildMoviePathLookup([@"E:\\Movies\\a.mp4"]);
        var d = MainWindow.EvaluateWatchNewMovieScanDecision(
            restrictToVisibleMovies: false,
            visibleMoviePaths: visible,
            movieFullPath: @"E:\\Movies\\a.mp4",
            useIncrementalUiMode: false,
            pendingMovieCount: 3,
            batchSize: 100
        );

        Assert.That(d.ShouldSkipByVisibleOnlyGate, Is.False);
        Assert.That(d.ShouldFlushPendingMovieBatch, Is.False);
        Assert.That(d.Stages, Does.Contain(MainWindow.WatchScanFlowStage.PendingMovieBatchDeferred));
    }

    // 新規動画スキャン: 増分UIモード時は件数が少なくても逐次flushする
    [Test]
    public void NewMovieScanDecision_増分UIModeなら即flushする()
    {
        var visible = MainWindow.BuildMoviePathLookup([@"E:\\Movies\\b.mp4"]);
        var d = MainWindow.EvaluateWatchNewMovieScanDecision(
            restrictToVisibleMovies: false,
            visibleMoviePaths: visible,
            movieFullPath: @"E:\\Movies\\b.mp4",
            useIncrementalUiMode: true,
            pendingMovieCount: 1,
            batchSize: 100
        );

        Assert.That(d.ShouldSkipByVisibleOnlyGate, Is.False);
        Assert.That(d.ShouldFlushPendingMovieBatch, Is.True);
        Assert.That(d.Stages, Does.Contain(MainWindow.WatchScanFlowStage.PendingMovieBatchFlushed));
    }

    // 新規動画スキャン: visible-only gateで不可視なら入口で短絡する
    [Test]
    public void NewMovieScanDecision_invisibleなら入口で短絡する()
    {
        var visible = MainWindow.BuildMoviePathLookup([@"E:\\Movies\\visible.mp4"]);
        var d = MainWindow.EvaluateWatchNewMovieScanDecision(
            restrictToVisibleMovies: true,
            visibleMoviePaths: visible,
            movieFullPath: @"E:\\Movies\\hidden.mp4",
            useIncrementalUiMode: false,
            pendingMovieCount: 50,
            batchSize: 10
        );

        Assert.That(d.ShouldSkipByVisibleOnlyGate, Is.True);
        Assert.That(d.Stages, Does.Contain(MainWindow.WatchScanFlowStage.VisibleOnlyGateSkipped));
    }
}