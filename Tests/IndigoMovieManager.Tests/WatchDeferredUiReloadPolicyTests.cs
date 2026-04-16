using IndigoMovieManager;
using IndigoMovieManager.DB;
using IndigoMovieManager.ViewModels;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchDeferredUiReloadPolicyTests
{
    [Test]
    public void ShouldUseDeferredWatchUiReload_Watch更新ありなら遅延reloadを使う()
    {
        bool result = MainWindow.ShouldUseDeferredWatchUiReload(
            hasChanges: true,
            isWatchMode: true
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldUseDeferredWatchUiReload_Manual時は即時reloadのままにする()
    {
        bool result = MainWindow.ShouldUseDeferredWatchUiReload(
            hasChanges: true,
            isWatchMode: false
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldUseQueryOnlyWatchUiReload_安全条件が揃ったwatchだけTrueを返す()
    {
        Assert.That(
            MainWindow.ShouldUseQueryOnlyWatchUiReload(
                hasChanges: true,
                isWatchMode: true,
                canUseQueryOnlyReload: true
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldUseQueryOnlyWatchUiReload(
                hasChanges: true,
                isWatchMode: true,
                canUseQueryOnlyReload: false
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldUseQueryOnlyWatchUiReload(
                hasChanges: true,
                isWatchMode: false,
                canUseQueryOnlyReload: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldUseQueryOnlyWatchUiReload(
                hasChanges: false,
                isWatchMode: true,
                canUseQueryOnlyReload: true
            ),
            Is.False
        );
    }

    [Test]
    public void CanApplyDeferredWatchUiReload_同一DBかつ最新revisionなら適用する()
    {
        bool result = MainWindow.CanApplyDeferredWatchUiReload(
            currentDbFullPath: @"D:\Db\Main.wb",
            scheduledDbFullPath: @"d:\db\main.wb",
            isWatchSuppressedByUi: false,
            requestRevision: 4,
            currentRevision: 4
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void CanApplyDeferredWatchUiReload_revisionが古ければ適用しない()
    {
        bool result = MainWindow.CanApplyDeferredWatchUiReload(
            currentDbFullPath: @"D:\Db\Main.wb",
            scheduledDbFullPath: @"D:\Db\Main.wb",
            isWatchSuppressedByUi: false,
            requestRevision: 3,
            currentRevision: 4
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void CanApplyDeferredWatchUiReload_DBが切り替わっていたら適用しない()
    {
        bool result = MainWindow.CanApplyDeferredWatchUiReload(
            currentDbFullPath: @"D:\Db\Other.wb",
            scheduledDbFullPath: @"D:\Db\Main.wb",
            isWatchSuppressedByUi: false,
            requestRevision: 4,
            currentRevision: 4
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void CanApplyDeferredWatchUiReload_suppression中は適用しない()
    {
        bool result = MainWindow.CanApplyDeferredWatchUiReload(
            currentDbFullPath: @"D:\Db\Main.wb",
            scheduledDbFullPath: @"D:\Db\Main.wb",
            isWatchSuppressedByUi: true,
            requestRevision: 4,
            currentRevision: 4
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ApplyDeferredWatchUiReloadOnUiThread_suppression中はapplyせずdeferへ戻す()
    {
        const string dbFullPath = @"D:\Db\Main.wb";
        MainWindow window = CreateMainWindowForDeferredReloadTests(dbFullPath, "28");
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        SetPrivateField(window, "_watchUiSuppressionCount", 1);
        SetPrivateField(window, "_watchDeferredUiReloadSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadRevision", 4);
        SetPrivateField(window, "_watchDeferredUiReloadPending", true);

        int filterAndSortCount = 0;
        window.FilterAndSortForTesting = (_, _) => filterAndSortCount++;

        InvokeVoid(
            window,
            "ApplyDeferredWatchUiReloadOnUiThread",
            dbFullPath,
            4,
            "watch-test"
        );

        Assert.That(filterAndSortCount, Is.EqualTo(0));
        Assert.That(
            (bool)GetPrivateField(window, "_watchWorkDeferredWhileSuppressed"),
            Is.True
        );
    }

    [Test]
    public void ApplyDeferredWatchUiReloadOnUiThread_queryOnlyならin_memory再計算を呼ぶ()
    {
        const string dbFullPath = @"D:\Db\Main.wb";
        MainWindow window = CreateMainWindowForDeferredReloadTests(dbFullPath, "28");
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadRevision", 4);
        SetPrivateField(window, "_watchDeferredUiReloadPending", true);
        SetPrivateField(window, "_watchDeferredUiReloadQueryOnly", true);

        List<(string Sort, string Reason)> refreshCalls = [];
        List<(string Sort, bool IsGetNew)> fullReloadCalls = [];
        window.RefreshMovieViewFromCurrentSourceForTesting = (sort, reason) =>
        {
            refreshCalls.Add((sort, reason));
        };
        window.FilterAndSortForTesting = (sort, isGetNew) =>
        {
            fullReloadCalls.Add((sort, isGetNew));
        };

        InvokeVoid(
            window,
            "ApplyDeferredWatchUiReloadOnUiThread",
            dbFullPath,
            4,
            "watch-test"
        );

        Assert.That(refreshCalls, Has.Count.EqualTo(1));
        Assert.That(refreshCalls[0].Sort, Is.EqualTo("28"));
        Assert.That(refreshCalls[0].Reason, Is.EqualTo("deferred:watch-test"));
        Assert.That(fullReloadCalls, Is.Empty);
    }

    [Test]
    public void BeginWatchUiSuppression_予約済みold_reloadは解除後もapplyされずcatch_upへ戻す()
    {
        const string dbFullPath = @"D:\Db\Main.wb";
        MainWindow window = CreateMainWindowForDeferredReloadTests(dbFullPath, "28");
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        SetPrivateField(window, "_checkFolderRequestSync", new object());
        SetPrivateField(window, "_checkFolderRunLock", new SemaphoreSlim(0, 1));
        SetPrivateField(window, "_watchDeferredUiReloadSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadCts", new CancellationTokenSource());
        SetPrivateField(window, "_watchDeferredUiReloadRevision", 4);
        SetPrivateField(window, "_watchDeferredUiReloadPending", true);

        int filterAndSortCount = 0;
        List<string> queuedTriggers = [];
        window.FilterAndSortForTesting = (_, _) => filterAndSortCount++;
        window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
        {
            queuedTriggers.Add($"{mode}:{trigger}");
        };

        InvokeVoid(window, "BeginWatchUiSuppression", "drawer");
        InvokeVoid(window, "EndWatchUiSuppression", "drawer");
        InvokeVoid(
            window,
            "ApplyDeferredWatchUiReloadOnUiThread",
            dbFullPath,
            4,
            "watch-test"
        );

        Assert.That(filterAndSortCount, Is.EqualTo(0));
        Assert.That(queuedTriggers, Is.EqualTo(["Watch:ui-resume:drawer"]));
    }

    [Test]
    public void InvalidateWatchScanScope_同一DBでもold_reloadはapplyされない()
    {
        const string dbFullPath = @"D:\Db\Main.wb";
        MainWindow window = CreateMainWindowForDeferredReloadTests(dbFullPath, "28");
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadCts", new CancellationTokenSource());
        SetPrivateField(window, "_watchDeferredUiReloadRevision", 4);
        SetPrivateField(window, "_watchDeferredUiReloadPending", true);

        int filterAndSortCount = 0;
        window.FilterAndSortForTesting = (_, _) => filterAndSortCount++;

        InvokeVoid(window, "InvalidateWatchScanScope", "scope-reset");
        InvokeVoid(
            window,
            "ApplyDeferredWatchUiReloadOnUiThread",
            dbFullPath,
            4,
            "watch-test"
        );

        Assert.That(filterAndSortCount, Is.EqualTo(0));
    }

    private static MainWindow CreateMainWindowForDeferredReloadTests(
        string dbFullPath,
        string sort
    )
    {
        MainWindow window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        MainWindowViewModel mainVm =
            (MainWindowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainWindowViewModel));
        mainVm.DbInfo = new DBInfo
        {
            DBFullPath = dbFullPath,
            Sort = sort,
        };

        SetPrivateField(window, "MainVM", mainVm);
        return window;
    }

    private static void InvokeVoid(MainWindow window, string methodName, params object[] args)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null, methodName);
        method.Invoke(window, args);
    }

    private static void SetPrivateField(MainWindow window, string fieldName, object value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        )!;
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(window, value);
    }

    private static object GetPrivateField(MainWindow window, string fieldName)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(field, Is.Not.Null, fieldName);
        return field.GetValue(window)!;
    }
}
