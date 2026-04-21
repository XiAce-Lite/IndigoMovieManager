using IndigoMovieManager;
using IndigoMovieManager.Watcher;
using System.Reflection;

namespace IndigoMovieManager.Tests;

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

    [Test]
    public void ResolveWatchFolderFullReconcileEntryPlan_visibleOnly中は開始しない()
    {
        (
            bool shouldStart,
            bool shouldDeferByUserPriority
        ) = MainWindow.ResolveWatchFolderFullReconcileEntryPlan(
            restrictWatchWorkToVisibleMovies: true,
            isWatchMode: true,
            FileIndexStrategies.Everything,
            newMovieCount: 0,
            shouldDeferByUserPriority: false
        );

        Assert.That(shouldStart, Is.False);
        Assert.That(shouldDeferByUserPriority, Is.False);
    }

    [Test]
    public void ResolveWatchFolderFullReconcileEntryPlan_入口条件成立かつ優先作業中ならdeferを返す()
    {
        (
            bool shouldStart,
            bool shouldDeferByUserPriority
        ) = MainWindow.ResolveWatchFolderFullReconcileEntryPlan(
            restrictWatchWorkToVisibleMovies: false,
            isWatchMode: true,
            FileIndexStrategies.Everything,
            newMovieCount: 0,
            shouldDeferByUserPriority: true
        );

        Assert.That(shouldStart, Is.True);
        Assert.That(shouldDeferByUserPriority, Is.True);
    }

    [Test]
    public void ResolveWatchFolderFullReconcilePlanForCurrentRun_Watchモードなら開始条件へ変換する()
    {
        Type checkModeType = typeof(MainWindow).GetNestedType(
            "CheckMode",
            BindingFlags.NonPublic
        );
        object watchMode = Enum.Parse(checkModeType, "Watch");
        (
            bool shouldStart,
            bool shouldDeferByUserPriority
        ) = MainWindow.ResolveWatchFolderFullReconcilePlanForCurrentRun(
            restrictWatchWorkToVisibleMovies: false,
            mode: watchMode,
            FileIndexStrategies.Everything,
            newMovieCount: 0,
            shouldDeferByUserPriority: false
        );

        Assert.That(shouldStart, Is.True);
        Assert.That(shouldDeferByUserPriority, Is.False);
    }

    [Test]
    public void BuildWatchFolderFullReconcileScopeKey_DBとフォルダを正規化して連結する()
    {
        string result = MainWindow.BuildWatchFolderFullReconcileScopeKey(
            dbFullPath: @"C:\Temp\Movies.wb",
            watchFolder: @"D:\Watch",
            sub: true
        );

        Assert.That(result, Is.EqualTo("c:\\temp\\movies.wb|d:\\watch|sub=1"));
    }

    [Test]
    public void BuildWatchFolderFullReconcileScopeKey_sub違いで別キーになる()
    {
        string withSub = MainWindow.BuildWatchFolderFullReconcileScopeKey(
            dbFullPath: @"C:\Temp\Movies.wb",
            watchFolder: @"D:\Watch",
            sub: true
        );
        string withoutSub = MainWindow.BuildWatchFolderFullReconcileScopeKey(
            dbFullPath: @"C:\Temp\Movies.wb",
            watchFolder: @"D:\Watch",
            sub: false
        );

        Assert.That(withSub, Is.Not.EqualTo(withoutSub));
        Assert.That(withoutSub, Does.EndWith("|sub=0"));
    }
}
