using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class UiHangNotificationPolicyTests
{
    [SetUp]
    public void SetUp()
    {
        App.ResetDispatcherTimerInfrastructureFaultForTests();
    }

    [TearDown]
    public void TearDown()
    {
        App.ResetDispatcherTimerInfrastructureFaultForTests();
    }

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
    public void TryHandleDispatcherTimerStartWin32ExceptionCore_既知start失敗ならfault状態を立てる処理へ流す()
    {
        bool faultHandlerCalled = false;

        bool actual = MainWindow.TryHandleDispatcherTimerStartWin32ExceptionCore(
            new Win32Exception(8),
            _ =>
            {
                faultHandlerCalled = true;
                App.RecordDispatcherTimerInfrastructureFault();
            },
            """
            at System.Windows.Threading.Dispatcher.SetWin32Timer(Int32 dueTimeInTicks)
            at System.Windows.Threading.DispatcherTimer.Start()
            at System.Windows.Media.MediaContext.CommitChannelAfterNextVSync()
            """,
            GetDispatcherSetWin32TimerMethod()
        );

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.True);
            Assert.That(faultHandlerCalled, Is.True);
            Assert.That(App.HasDispatcherTimerInfrastructureFault, Is.True);
            Assert.That(
                MainWindow.ShouldAllowDispatcherTimerStartCore(
                    App.HasDispatcherTimerInfrastructureFault,
                    hasDispatcher: true,
                    hasShutdownStarted: false,
                    hasShutdownFinished: false
                ),
                Is.False
            );
        });
    }

    [Test]
    public void TryHandleDispatcherTimerStartWin32ExceptionCore_未知start失敗ならfault処理へ流さない()
    {
        bool faultHandlerCalled = false;

        bool actual = MainWindow.TryHandleDispatcherTimerStartWin32ExceptionCore(
            new Win32Exception(5),
            _ => faultHandlerCalled = true,
            """
            at System.Windows.Threading.Dispatcher.SetWin32Timer(Int32 dueTimeInTicks)
            at System.Windows.Threading.DispatcherTimer.Start()
            at System.Windows.Media.MediaContext.CommitChannelAfterNextVSync()
            """,
            GetDispatcherSetWin32TimerMethod()
        );

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.False);
            Assert.That(faultHandlerCalled, Is.False);
            Assert.That(App.HasDispatcherTimerInfrastructureFault, Is.False);
        });
    }

    [Test]
    public void TryStartDispatcherTimer_Win32ExceptionのcatchがstartFaultHelper経由でfaultHandlerへ配線されている()
    {
        string source = File.ReadAllText(
            GetRepositoryFilePath("Views/Main/MainWindow.DispatcherTimerSafety.cs")
        );
        string methodBody = ExtractBlock(
            source,
            "private bool TryStartDispatcherTimer(DispatcherTimer timer, string timerName)"
        );
        string catchBody = ExtractBlock(methodBody, "catch (Win32Exception ex)");

        // start 失敗は helper 判定を通してから fault handler へ渡す配線を source で固定する。
        AssertContainsInOrder(
            catchBody,
            "TryHandleDispatcherTimerStartWin32ExceptionCore(",
            "ex,",
            "handledException => HandleDispatcherTimerInfrastructureFault(timerName, handledException)"
        );
        Assert.That(catchBody, Does.Contain("throw;"));
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

    [Test]
    public void ShouldContinueDispatcherTimerFaultCleanupAfterStopExceptionCore_faultCleanup既知例外ならTrueを返す()
    {
        bool actual = MainWindow.ShouldContinueDispatcherTimerFaultCleanupAfterStopExceptionCore(
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

    [Test]
    public void TryHandleDispatcherTimerFaultCleanupStopWin32ExceptionCore_cleanup中例外でも継続して追加faultを立てない()
    {
        int cleanupContinuationCount = 0;

        App.RecordDispatcherTimerInfrastructureFault();

        bool actual = MainWindow.TryHandleDispatcherTimerFaultCleanupStopWin32ExceptionCore(
            new Win32Exception(8),
            _ => cleanupContinuationCount++,
            """
            at System.Windows.Threading.DispatcherTimer.Stop()
            at IndigoMovieManager.MainWindow.StopDispatcherTimerDuringInfrastructureFaultCleanup(DispatcherTimer timer, String timerName)
            at IndigoMovieManager.MainWindow.HandleDispatcherTimerInfrastructureFault(String origin, Win32Exception exception)
            """,
            typeof(DispatcherTimer).GetMethod(nameof(DispatcherTimer.Stop))
        );

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.True);
            Assert.That(cleanupContinuationCount, Is.EqualTo(1));
            Assert.That(App.HasDispatcherTimerInfrastructureFault, Is.True);
        });
    }

    [Test]
    public void StopDispatcherTimerDuringInfrastructureFaultCleanup_cleanupCatchへ到達するtrue経路で配線されている()
    {
        string source = File.ReadAllText(
            GetRepositoryFilePath("Views/Main/MainWindow.DispatcherTimerSafety.cs")
        );
        string wrapperBody = ExtractBlock(
            source,
            "private void StopDispatcherTimerDuringInfrastructureFaultCleanup("
        );

        // cleanup 専用 wrapper が core へ true を渡す前提を固定する。
        AssertContainsInOrder(
            wrapperBody,
            "StopDispatcherTimerCore(",
            "timer,",
            "timerName,",
            "isFaultCleanupStop: true"
        );
    }

    [Test]
    public void StopDispatcherTimerCore_cleanup側catchがhelper経由で継続する配線になっている()
    {
        string source = File.ReadAllText(
            GetRepositoryFilePath("Views/Main/MainWindow.DispatcherTimerSafety.cs")
        );
        string methodBody = ExtractBlock(source, "private void StopDispatcherTimerCore(");
        string catchBody = ExtractBlock(methodBody, "catch (Win32Exception ex)");

        // cleanup stop 例外は helper を通した時だけ return で継続し、通常 fault 経路へ落とさない。
        AssertContainsInOrder(
            catchBody,
            "isFaultCleanupStop",
            "TryHandleDispatcherTimerFaultCleanupStopWin32ExceptionCore(",
            "cleanupException =>",
            "LogDispatcherTimerFailureOnce(",
            "\"stop-cleanup\"",
            "return;",
            "!TryHandleDispatcherTimerStartWin32ExceptionCore("
        );
    }

    private static MethodBase GetDispatcherSetWin32TimerMethod()
    {
        return typeof(Dispatcher).GetMethod(
                "SetWin32Timer",
                BindingFlags.Instance | BindingFlags.NonPublic
            )
            ?? throw new InvalidOperationException("Dispatcher.SetWin32Timer が見つかりません。");
    }

    // 実装本体を広げず、source 上の配線順だけを最小コストで固定する。
    private static string ExtractBlock(string source, string anchor)
    {
        int anchorIndex = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.That(anchorIndex, Is.GreaterThanOrEqualTo(0), $"anchor が見つかりません: {anchor}");

        int blockStartIndex = source.IndexOf('{', anchorIndex);
        Assert.That(blockStartIndex, Is.GreaterThanOrEqualTo(0), $"block 開始が見つかりません: {anchor}");

        int depth = 0;
        for (int i = blockStartIndex; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(anchorIndex, i - anchorIndex + 1);
                }
            }
        }

        Assert.Fail($"block 終端が見つかりません: {anchor}");
        return "";
    }

    // 重要な呼び出し順だけを固定し、余計な実装詳細には依存しない。
    private static void AssertContainsInOrder(string source, params string[] snippets)
    {
        int currentIndex = 0;
        foreach (string snippet in snippets)
        {
            int nextIndex = source.IndexOf(snippet, currentIndex, StringComparison.Ordinal);
            Assert.That(nextIndex, Is.GreaterThanOrEqualTo(0), $"snippet が見つかりません: {snippet}");
            currentIndex = nextIndex + snippet.Length;
        }
    }

    // テスト実行場所に依存しないよう、repo ルートまで親をたどって対象ファイルを見つける。
    private static string GetRepositoryFilePath(string relativePath)
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"テスト対象ファイルが見つかりません: {relativePath}");
    }
}
