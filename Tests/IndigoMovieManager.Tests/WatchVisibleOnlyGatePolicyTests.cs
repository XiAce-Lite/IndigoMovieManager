using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchVisibleOnlyGatePolicyTests
{
    [Test]
    public void ShouldRestrictWatchWorkToVisibleMovies_Watch上側タブかつ500件以上で有効にする()
    {
        bool result = MainWindow.ShouldRestrictWatchWorkToVisibleMovies(
            isWatchMode: true,
            activeQueueCount: 500,
            threshold: 500,
            currentTabIndex: 2,
            visibleMovieCount: 12
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRestrictWatchWorkToVisibleMovies_visibleが無ければ有効にしない()
    {
        bool result = MainWindow.ShouldRestrictWatchWorkToVisibleMovies(
            isWatchMode: true,
            activeQueueCount: 999,
            threshold: 500,
            currentTabIndex: 2,
            visibleMovieCount: 0
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldSkipWatchWorkByVisibleMovieGate_visible外の動画は止める()
    {
        HashSet<string> visibleMoviePaths = MainWindow.BuildMoviePathLookup(
            [@"E:\Movies\visible.mp4"]
        );

        bool result = MainWindow.ShouldSkipWatchWorkByVisibleMovieGate(
            restrictToVisibleMovies: true,
            visibleMoviePaths,
            movieFullPath: @"E:\Movies\hidden.mp4"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsMoviePathInsideWatchFolder_sub監視なら子孫フォルダも含む()
    {
        bool result = MainWindow.IsMoviePathInsideWatchFolder(
            movieFullPath: @"E:\Movies\Child\sample.mp4",
            watchFolder: @"E:\Movies",
            includeSubfolders: true
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldSkipWatchFolderByVisibleMovieGate_visible動画が無い監視フォルダは止める()
    {
        HashSet<string> visibleMoviePaths = MainWindow.BuildMoviePathLookup(
            [@"E:\Shown\sample.mp4"]
        );

        bool result = MainWindow.ShouldSkipWatchFolderByVisibleMovieGate(
            restrictToVisibleMovies: true,
            visibleMoviePaths,
            watchFolder: @"E:\Hidden",
            includeSubfolders: true
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void SplitWatchScanMoviePaths_上限超過時は今回分と次回送りへ分ける()
    {
        var (immediatePaths, deferredPaths) = MainWindow.SplitWatchScanMoviePaths(
            [@"E:\Movies\1.mp4", @"E:\Movies\2.mp4", @"E:\Movies\3.mp4"],
            limit: 2
        );

        Assert.That(immediatePaths, Is.EqualTo([@"E:\Movies\1.mp4", @"E:\Movies\2.mp4"]));
        Assert.That(deferredPaths, Is.EqualTo([@"E:\Movies\3.mp4"]));
    }

    [Test]
    public void SplitWatchScanMoviePaths_上限以下なら全部今回分へ残す()
    {
        var (immediatePaths, deferredPaths) = MainWindow.SplitWatchScanMoviePaths(
            [@"E:\Movies\1.mp4", @"E:\Movies\2.mp4"],
            limit: 5
        );

        Assert.That(immediatePaths, Is.EqualTo([@"E:\Movies\1.mp4", @"E:\Movies\2.mp4"]));
        Assert.That(deferredPaths, Is.Empty);
    }

    [Test]
    public void SplitWatchScanMoviePaths_visible_only時は表示中動画を先に今回分へ残す()
    {
        HashSet<string> visibleMoviePaths = MainWindow.BuildMoviePathLookup(
            [@"E:\Movies\visible-1.mp4", @"E:\Movies\visible-2.mp4"]
        );

        var (immediatePaths, deferredPaths) = MainWindow.SplitWatchScanMoviePaths(
            [
                @"E:\Movies\hidden-1.mp4",
                @"E:\Movies\visible-1.mp4",
                @"E:\Movies\hidden-2.mp4",
                @"E:\Movies\visible-2.mp4",
            ],
            limit: 2,
            prioritizeVisibleMovies: true,
            visibleMoviePaths
        );

        Assert.That(
            immediatePaths,
            Is.EqualTo([@"E:\Movies\visible-1.mp4", @"E:\Movies\visible-2.mp4"])
        );
        Assert.That(
            deferredPaths,
            Is.EqualTo([@"E:\Movies\hidden-1.mp4", @"E:\Movies\hidden-2.mp4"])
        );
    }

    [Test]
    public void MergeDeferredAndCollectedWatchScanMoviePaths_visible_only時は新規visibleを旧deferredより先に返す()
    {
        HashSet<string> visibleMoviePaths = MainWindow.BuildMoviePathLookup(
            [@"E:\Movies\visible-new.mp4"]
        );

        var (immediatePaths, deferredPaths) = MainWindow.MergeDeferredAndCollectedWatchScanMoviePaths(
            [
                @"E:\Movies\hidden-old-1.mp4",
                @"E:\Movies\hidden-old-2.mp4",
                @"E:\Movies\hidden-old-3.mp4",
            ],
            [@"E:\Movies\visible-new.mp4"],
            limit: 2,
            prioritizeVisibleMovies: true,
            visibleMoviePaths
        );

        Assert.That(
            immediatePaths,
            Is.EqualTo([@"E:\Movies\visible-new.mp4"])
        );
        Assert.That(
            deferredPaths,
            Is.EqualTo(
                [
                    @"E:\Movies\hidden-old-1.mp4",
                    @"E:\Movies\hidden-old-2.mp4",
                    @"E:\Movies\hidden-old-3.mp4",
                ]
            )
        );
    }
}
