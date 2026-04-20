using IndigoMovieManager;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchUiSuppressionPolicyTests
{
    [Test]
    public void ShouldSuppressWatchWorkByUi_抑制中のwatchだけTrueを返す()
    {
        Assert.That(
            MainWindow.ShouldSuppressWatchWorkByUi(
                isWatchSuppressedByUi: true,
                isWatchMode: true
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldSuppressWatchWorkByUi(
                isWatchSuppressedByUi: true,
                isWatchMode: false
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldSuppressWatchWorkByUi(
                isWatchSuppressedByUi: false,
                isWatchMode: true
            ),
            Is.False
        );
    }

    [Test]
    public void ShouldQueueWatchCatchUpAfterUiSuppression_解除済みかつ保留ありだけTrueを返す()
    {
        Assert.That(
            MainWindow.ShouldQueueWatchCatchUpAfterUiSuppression(
                isStillSuppressed: false,
                hasDeferredWatchWork: true
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldQueueWatchCatchUpAfterUiSuppression(
                isStillSuppressed: true,
                hasDeferredWatchWork: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldQueueWatchCatchUpAfterUiSuppression(
                isStillSuppressed: false,
                hasDeferredWatchWork: false
            ),
            Is.False
        );
    }

    [Test]
    public void ShouldDeferBackgroundWorkForUserPriority_手動以外の背後処理だけTrueを返す()
    {
        Assert.That(
            MainWindow.ShouldDeferBackgroundWorkForUserPriority(
                isUserPriorityActive: true,
                isManualMode: false
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldDeferBackgroundWorkForUserPriority(
                isUserPriorityActive: true,
                isManualMode: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldDeferBackgroundWorkForUserPriority(
                isUserPriorityActive: false,
                isManualMode: false
            ),
            Is.False
        );
    }

    [Test]
    public void ShouldQueueBackgroundCatchUpAfterUserPriority_解除済みかつ保留ありだけTrueを返す()
    {
        Assert.That(
            MainWindow.ShouldQueueBackgroundCatchUpAfterUserPriority(
                isStillActive: false,
                hasDeferredWatchWork: true
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldQueueBackgroundCatchUpAfterUserPriority(
                isStillActive: true,
                hasDeferredWatchWork: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldQueueBackgroundCatchUpAfterUserPriority(
                isStillActive: false,
                hasDeferredWatchWork: false
            ),
            Is.False
        );
    }

    [Test]
    public void ShouldSkipWatchCatchUpAfterUiSuppression_manual_reloadだけTrueを返す()
    {
        Assert.That(
            MainWindow.ShouldSkipWatchCatchUpAfterUiSuppression("manual-reload"),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldSkipWatchCatchUpAfterUiSuppression("drawer-root"),
            Is.False
        );
    }

    [Test]
    public void MergeWatchDeferredPathsForUiSuppression_未flush分と残件を重複排除して返す()
    {
        List<string> result = MainWindow.MergeWatchDeferredPathsForUiSuppression(
            remainingScanPaths: [@"E:\Movies\scan-2.mp4", @"E:\Movies\scan-3.mp4"],
            pendingInsertPaths: [@"E:\Movies\scan-1.mp4", @"E:\Movies\scan-2.mp4"],
            pendingEnqueuePaths: [@"E:\Movies\scan-1.mp4", @"E:\Movies\queue-1.mp4"]
        );

        Assert.That(
            result,
            Is.EqualTo(
                [
                    @"E:\Movies\scan-2.mp4",
                    @"E:\Movies\scan-3.mp4",
                    @"E:\Movies\scan-1.mp4",
                    @"E:\Movies\queue-1.mp4",
                ]
            )
        );
    }

    [Test]
    public void MergeWatchDeferredPathsForUiSuppression_current_itemは残件より先頭へ戻す()
    {
        List<string> result = MainWindow.MergeWatchDeferredPathsForUiSuppression(
            currentScanPaths: [@"E:\Movies\current.mp4"],
            remainingScanPaths: [@"E:\Movies\remain-1.mp4", @"E:\Movies\current.mp4"],
            pendingInsertPaths: [@"E:\Movies\pending-insert.mp4"],
            pendingEnqueuePaths: [@"E:\Movies\pending-queue.mp4"]
        );

        Assert.That(
            result,
            Is.EqualTo(
                [
                    @"E:\Movies\current.mp4",
                    @"E:\Movies\remain-1.mp4",
                    @"E:\Movies\pending-insert.mp4",
                    @"E:\Movies\pending-queue.mp4",
                ]
            )
        );
    }

    [Test]
    public void EndWatchUiSuppression_defer複数回でもcatch_upは1回だけQueueCheckFolderAsyncする()
    {
        MainWindow window = CreateMainWindowForSuppressionTests();
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        SetPrivateField(window, "_checkFolderRequestSync", new object());
        SetPrivateField(window, "_checkFolderRunLock", new SemaphoreSlim(0, 1));

        List<string> queuedTriggers = [];
        window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
        {
            queuedTriggers.Add($"{mode}:{trigger}");
        };

        InvokeVoid(window, "BeginWatchUiSuppression", "drawer-root");
        InvokeVoid(window, "BeginWatchUiSuppression", "drawer-child");
        InvokeVoid(window, "MarkWatchWorkDeferredWhileSuppressed", "watch-created");
        InvokeVoid(window, "MarkWatchWorkDeferredWhileSuppressed", "watch-renamed");
        InvokeVoid(window, "EndWatchUiSuppression", "drawer-child");
        InvokeVoid(window, "MarkWatchWorkDeferredWhileSuppressed", "watch-changed");
        InvokeVoid(window, "EndWatchUiSuppression", "drawer-root");
        InvokeVoid(window, "EndWatchUiSuppression", "drawer-extra");

        Assert.That(queuedTriggers, Is.EqualTo(["Watch:ui-resume:drawer-root"]));
    }

    [Test]
    public void EndWatchUiSuppression_manual_reloadではdeferありでもcatch_upをQueueしない()
    {
        MainWindow window = CreateMainWindowForSuppressionTests();
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        SetPrivateField(window, "_checkFolderRequestSync", new object());
        SetPrivateField(window, "_checkFolderRunLock", new SemaphoreSlim(0, 1));

        List<string> queuedTriggers = [];
        window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
        {
            queuedTriggers.Add($"{mode}:{trigger}");
        };

        InvokeVoid(window, "BeginWatchUiSuppression", "manual-reload");
        InvokeVoid(window, "MarkWatchWorkDeferredWhileSuppressed", "watch-created");
        InvokeVoid(window, "EndWatchUiSuppression", "manual-reload");

        Assert.That(queuedTriggers, Is.Empty);
        Assert.That(
            (bool)GetPrivateField(window, "_watchWorkDeferredWhileSuppressed"),
            Is.False
        );
    }

    [Test]
    public void HandleFolderCheckUiReloadAfterChanges_suppression中は最後のFilterAndSortを走らせずdeferへ戻す()
    {
        MainWindow window = CreateMainWindowForSuppressionTests();
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        SetPrivateField(window, "_watchUiSuppressionCount", 1);

        int filterAndSortCount = 0;
        window.FilterAndSortForTesting = (_, _) => filterAndSortCount++;

        InvokeVoid(
            window,
            "HandleFolderCheckUiReloadAfterChanges",
            true,
            ParseCheckMode("Watch"),
            @"D:\Db\Main.wb",
            true,
            Array.Empty<MainWindow.WatchChangedMovie>()
        );

        Assert.That(filterAndSortCount, Is.EqualTo(0));
        Assert.That(
            (bool)GetPrivateField(window, "_watchWorkDeferredWhileSuppressed"),
            Is.True
        );
    }

    [Test]
    public void ResolveMissingThumbnailRescueGuardAction_watch_suppression中はcatch_upへ戻す()
    {
        MainWindow.MissingThumbnailRescueGuardAction result =
            MainWindow.ResolveMissingThumbnailRescueGuardAction(
                isWatchMode: true,
                isWatchSuppressedByUi: true,
                isBackgroundWorkSuppressedByUserPriority: false,
                isCurrentWatchScope: true
            );

        Assert.That(
            result,
            Is.EqualTo(MainWindow.MissingThumbnailRescueGuardAction.DeferByUiSuppression)
        );
    }

    [Test]
    public void ResolveMissingThumbnailRescueGuardAction_manualはsuppression中でも継続する()
    {
        MainWindow.MissingThumbnailRescueGuardAction result =
            MainWindow.ResolveMissingThumbnailRescueGuardAction(
                isWatchMode: false,
                isWatchSuppressedByUi: true,
                isBackgroundWorkSuppressedByUserPriority: false,
                isCurrentWatchScope: false
            );

        Assert.That(
            result,
            Is.EqualTo(MainWindow.MissingThumbnailRescueGuardAction.Continue)
        );
    }

    [Test]
    public void ResolveMissingThumbnailRescueGuardAction_watch_検索優先中はcatch_upへ戻す()
    {
        MainWindow.MissingThumbnailRescueGuardAction result =
            MainWindow.ResolveMissingThumbnailRescueGuardAction(
                isWatchMode: true,
                isWatchSuppressedByUi: false,
                isBackgroundWorkSuppressedByUserPriority: true,
                isCurrentWatchScope: true
            );

        Assert.That(
            result,
            Is.EqualTo(MainWindow.MissingThumbnailRescueGuardAction.DeferByUiSuppression)
        );
    }

    private static MainWindow CreateMainWindowForSuppressionTests()
    {
        return (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
    }

    private static object ParseCheckMode(string name)
    {
        Type enumType = typeof(MainWindow).GetNestedType(
            "CheckMode",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        return Enum.Parse(enumType, name);
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
            BindingFlags.Instance | BindingFlags.NonPublic
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
