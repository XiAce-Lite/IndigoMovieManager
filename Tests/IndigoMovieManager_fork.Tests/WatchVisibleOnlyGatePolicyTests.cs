using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

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
}
