using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WatchDeferredUiReloadPolicyTests
{
    // 既存動画: UI補正が抑止されている時はrepair要求を出さず、UiRepairSuppressed段階を記録する
    [Test]
    public void ExistingMovieFlowDecision_UI補正抑止ならUiRepairSuppressedとなる()
    {
        var existingView = MainWindow.BuildMoviePathLookup([@"E:\\Shown\\x.mp4"]);
        var d = MainWindow.EvaluateWatchExistingMovieFlowDecision(
            allowViewConsistencyRepair: false,
            existingViewMoviePaths: existingView,
            searchKeyword: string.Empty,
            displayedMoviePaths: MainWindow.BuildMoviePathLookup(Array.Empty<string>()),
            movieFullPath: @"E:\\Shown\\x.mp4",
            useIncrementalUiMode: false,
            pendingQueueCount: 0,
            batchSize: 10
        );

        Assert.That(d.IsViewRepairSuppressed, Is.True);
        Assert.That(d.Stages, Does.Contain(MainWindow.WatchScanFlowStage.UiRepairSuppressed));
    }

    // 既存動画: visible-only gateで不可視なら入口で短絡し、UI更新にも進まない
    [Test]
    public void ExistingMovieScanDecision_invisibleなら入口で短絡する()
    {
        var visible = MainWindow.BuildMoviePathLookup([@"E:\\Movies\\visible.mp4"]);
        var existingView = MainWindow.BuildMoviePathLookup(Array.Empty<string>());
        var displayed = MainWindow.BuildMoviePathLookup(Array.Empty<string>());
        var d = MainWindow.EvaluateWatchExistingMovieScanDecision(
            restrictToVisibleMovies: true,
            visibleMoviePaths: visible,
            movieFullPath: @"E:\\Movies\\hidden.mp4",
            allowViewConsistencyRepair: true,
            existingViewMoviePaths: existingView,
            searchKeyword: string.Empty,
            displayedMoviePaths: displayed,
            useIncrementalUiMode: false,
            pendingQueueCount: 0,
            batchSize: 10
        );

        Assert.That(d.ShouldSkipByVisibleOnlyGate, Is.True);
        Assert.That(d.Stages, Does.Contain(MainWindow.WatchScanFlowStage.VisibleOnlyGateSkipped));
    }
}