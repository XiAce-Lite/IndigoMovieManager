using System.ComponentModel;
using System.Reflection;
using System.Windows.Threading;
using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class UiHangNotificationPolicyTests
{
    [Test]
    public void IsUiHangDangerStateCore_pending閾値超過ならハンドル未確定でもTrueを返す()
    {
        bool actual = MainWindow.IsUiHangDangerStateCore(
            new UiHangHeartbeatSample(5000, true),
            5000,
            0,
            _ => false
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void IsUiHangDangerStateCore_pending閾値未満ならHungWindow判定へ委譲する()
    {
        bool resolverCalled = false;

        bool actual = MainWindow.IsUiHangDangerStateCore(
            new UiHangHeartbeatSample(1000, true),
            5000,
            (nint)123,
            _ =>
            {
                resolverCalled = true;
                return true;
            }
        );

        Assert.That(resolverCalled, Is.True);
        Assert.That(actual, Is.True);
    }

    [Test]
    public void IsUiHangDangerStateCore_ハンドル未確定ならHungWindow判定せずFalseを返す()
    {
        bool resolverCalled = false;

        bool actual = MainWindow.IsUiHangDangerStateCore(
            new UiHangHeartbeatSample(1000, false),
            5000,
            0,
            _ =>
            {
                resolverCalled = true;
                return true;
            }
        );

        Assert.That(resolverCalled, Is.False);
        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldDisplayUiHangNotificationCore_criticalは最小化中でもTrueを返す()
    {
        bool actual = MainWindow.ShouldDisplayUiHangNotificationCore(
            UiHangNotificationLevel.Critical,
            isMinimized: true,
            (nint)0,
            () => 0
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldDisplayUiHangNotificationCore_最小化中はcritical以外を抑止する()
    {
        bool actual = MainWindow.ShouldDisplayUiHangNotificationCore(
            UiHangNotificationLevel.Warning,
            isMinimized: true,
            (nint)123,
            () => (nint)123
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldDisplayUiHangNotificationCore_前面ウインドウ一致時だけTrueを返す()
    {
        bool actual = MainWindow.ShouldDisplayUiHangNotificationCore(
            UiHangNotificationLevel.Warning,
            isMinimized: false,
            (nint)123,
            () => (nint)123
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldStartUiHangNotificationSupportCore_fault後はFalseを返す()
    {
        bool actual = MainWindow.ShouldStartUiHangNotificationSupportCore(
            hasDispatcherTimerInfrastructureFault: true
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldStartUiHangNotificationSupportCore_fault前はTrueを返す()
    {
        bool actual = MainWindow.ShouldStartUiHangNotificationSupportCore(
            hasDispatcherTimerInfrastructureFault: false
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldAllowDispatcherTimerStartCore_fault後はFalseを返す()
    {
        bool actual = MainWindow.ShouldAllowDispatcherTimerStartCore(
            hasDispatcherTimerInfrastructureFault: true,
            hasDispatcher: true,
            hasShutdownStarted: false,
            hasShutdownFinished: false
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldAllowDispatcherTimerStartCore_dispatcher正常時だけTrueを返す()
    {
        bool actual = MainWindow.ShouldAllowDispatcherTimerStartCore(
            hasDispatcherTimerInfrastructureFault: false,
            hasDispatcher: true,
            hasShutdownStarted: false,
            hasShutdownFinished: false
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldHandleDispatcherTimerInfrastructureFaultCore_未知Win32ExceptionはFalseを返す()
    {
        bool actual = MainWindow.ShouldHandleDispatcherTimerInfrastructureFaultCore(
            new Win32Exception(5),
            """
            at System.Windows.Threading.Dispatcher.SetWin32Timer(Int32 dueTimeInTicks)
            at System.Windows.Threading.DispatcherTimer.Start()
            at System.Windows.Media.MediaContext.CommitChannelAfterNextVSync()
            """,
            GetDispatcherSetWin32TimerMethod()
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldHandleDispatcherTimerInfrastructureFaultCore_既知renderTimer経路だけTrueを返す()
    {
        bool actual = MainWindow.ShouldHandleDispatcherTimerInfrastructureFaultCore(
            new Win32Exception(8),
            """
            at System.Windows.Threading.Dispatcher.SetWin32Timer(Int32 dueTimeInTicks)
            at System.Windows.Threading.DispatcherTimer.Start()
            at System.Windows.Media.MediaContext.CommitChannelAfterNextVSync()
            """,
            GetDispatcherSetWin32TimerMethod()
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldSuppressDispatcherTimerStopWin32ExceptionCore_通常stopではnarrow判定外ならFalseを返す()
    {
        bool actual = MainWindow.ShouldSuppressDispatcherTimerStopWin32ExceptionCore(
            isFaultCleanupStop: false,
            new Win32Exception(8),
            """
            at System.Windows.Threading.DispatcherTimer.Stop()
            at IndigoMovieManager.MainWindow.StopDispatcherTimerSafely(DispatcherTimer timer, String timerName)
            """,
            typeof(DispatcherTimer).GetMethod(nameof(DispatcherTimer.Stop))
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldSuppressDispatcherTimerStopWin32ExceptionCore_faultCleanup中でも無関係stackならFalseを返す()
    {
        bool actual = MainWindow.ShouldSuppressDispatcherTimerStopWin32ExceptionCore(
            isFaultCleanupStop: true,
            new Win32Exception(8),
            """
            at System.Windows.Threading.DispatcherTimer.Stop()
            at IndigoMovieManager.MainWindow.SomeOtherCleanupPath()
            """,
            typeof(DispatcherTimer).GetMethod(nameof(DispatcherTimer.Stop))
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldSuppressDispatcherTimerStopWin32ExceptionCore_faultCleanup中かつcleanupStop経路ならTrueを返す()
    {
        bool actual = MainWindow.ShouldSuppressDispatcherTimerStopWin32ExceptionCore(
            isFaultCleanupStop: true,
            new Win32Exception(8),
            """
            at System.Windows.Threading.DispatcherTimer.Stop()
            at IndigoMovieManager.MainWindow.StopDispatcherTimerDuringInfrastructureFaultCleanup(DispatcherTimer timer, String timerName)
            at IndigoMovieManager.MainWindow.HandleDispatcherTimerInfrastructureFault(String origin, Win32Exception exception)
            """,
            typeof(DispatcherTimer).GetMethod(nameof(DispatcherTimer.Stop))
        );

        Assert.That(actual, Is.True);
    }

    private static MethodBase GetDispatcherSetWin32TimerMethod()
    {
        return typeof(Dispatcher).GetMethod(
                "SetWin32Timer",
                BindingFlags.Instance | BindingFlags.NonPublic
            )
            ?? throw new InvalidOperationException("Dispatcher.SetWin32Timer が見つかりません。");
    }
}
