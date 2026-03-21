using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WatchScanCoordinatorPolicyTests
{
    // 既存動画: バッチ閾値を超えるとenqueueをflushする
    [Test]
    public void ExistingMovieFlowDecision_閾値以上でenqueueFlushする()
    {
        var existingView = MainWindow.BuildMoviePathLookup(Array.Empty<string>());
        var displayed = MainWindow.BuildMoviePathLookup(Array.Empty<string>());
        var d = MainWindow.EvaluateWatchExistingMovieFlowDecision(
            allowViewConsistencyRepair: true,
            existingViewMoviePaths: existingView,
            searchKeyword: string.Empty,
            displayedMoviePaths: displayed,
            movieFullPath: @"E:\\Movies\\x.mp4",
            useIncrementalUiMode: false,
            pendingQueueCount: 100,
            batchSize: 50
        );

        Assert.That(d.ShouldFlushEnqueueBatch, Is.True);
        Assert.That(d.Stages, Does.Contain(MainWindow.WatchScanFlowStage.EnqueueBatchFlushed));
    }

    // 入口: visible-only gate通過時はVisibleOnlyGatePassedを記録する
    [Test]
    public void ScanEntryDecision_gate通過を記録する()
    {
        var visible = MainWindow.BuildMoviePathLookup([@"E:\\Movies\\ok.mp4"]);
        var entry = MainWindow.EvaluateWatchScanEntryDecision(
            restrictToVisibleMovies: true,
            visibleMoviePaths: visible,
            movieFullPath: @"E:\\Movies\\ok.mp4"
        );

        Assert.That(entry.ShouldSkipByVisibleOnlyGate, Is.False);
        Assert.That(entry.Stages, Does.Contain(MainWindow.WatchScanFlowStage.VisibleOnlyGatePassed));
    }
}