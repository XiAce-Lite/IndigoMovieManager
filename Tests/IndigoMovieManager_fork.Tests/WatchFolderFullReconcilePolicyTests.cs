using IndigoMovieManager;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WatchFolderFullReconcilePolicyTests
{
    [Test]
    public void ShouldRunWatchFolderFullReconcile_WatchEverything差分0件なら実行する()
    {
        bool result = MainWindow.ShouldRunWatchFolderFullReconcile(
            isWatchMode: true,
            FileIndexStrategies.Everything,
            newMovieCount: 0
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRunWatchFolderFullReconcile_WatchEverythingでも差分ありなら実行しない()
    {
        bool result = MainWindow.ShouldRunWatchFolderFullReconcile(
            isWatchMode: true,
            FileIndexStrategies.Everything,
            newMovieCount: 1
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldRunWatchFolderFullReconcile_Filesystem走査では実行しない()
    {
        bool result = MainWindow.ShouldRunWatchFolderFullReconcile(
            isWatchMode: true,
            FileIndexStrategies.Filesystem,
            newMovieCount: 0
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldRunWatchFolderFullReconcile_Manualでは実行しない()
    {
        bool result = MainWindow.ShouldRunWatchFolderFullReconcile(
            isWatchMode: false,
            FileIndexStrategies.Everything,
            newMovieCount: 0
        );

        Assert.That(result, Is.False);
    }
}
