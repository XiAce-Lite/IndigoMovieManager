using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class AppDispatcherTimerExceptionPolicyTests
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
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_targetSite補完で既知renderTimer経路が揃えばTrueを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new Win32Exception(8),
            """
            at System.Windows.Threading.DispatcherTimer.Start()
            at System.Windows.Media.MediaContext.CommitChannelAfterNextVSync()
            """,
            GetDispatcherSetWin32TimerMethod()
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_allowlist外nativeErrorでは既知stackでもFalseを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
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
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_Rendering経路だけではFalseを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new Win32Exception(8),
            """
            at System.Windows.Threading.DispatcherTimer.Start()
            at System.Windows.Media.MediaContext.CommitChannelAfterNextVSync()
            """,
            typeof(DispatcherTimer).GetMethod(nameof(DispatcherTimer.Start))
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_既知renderTimer経路がstackだけで揃えばTrueを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new Win32Exception(8),
            """
            at System.Windows.Threading.Dispatcher.SetWin32Timer(Int32 dueTimeInTicks)
            at System.Windows.Threading.DispatcherTimer.Start()
            at System.Windows.Media.MediaContext.CommitChannelAfterNextVSync()
            """,
            typeof(AppDispatcherTimerExceptionPolicyTests).GetMethod(
                nameof(NonDispatcherTimerTarget),
                BindingFlags.Static | BindingFlags.NonPublic
            )
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_SetWin32TimerだけのstackではFalseを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new Win32Exception(8),
            """
            at System.Windows.Threading.Dispatcher.SetWin32Timer(Int32 dueTimeInTicks)
            """,
            GetDispatcherSetWin32TimerMethod()
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_targetSiteだけ一致してもstack不一致ならFalseを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new Win32Exception(8),
            "at IndigoMovieManager.SomeOtherComponent.Run()",
            GetDispatcherSetWin32TimerMethod()
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_stack欠落時はSetWin32TimerのtargetSiteだけではFalseを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new Win32Exception(8),
            "",
            GetDispatcherSetWin32TimerMethod()
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void TryHandleDispatcherUnhandledExceptionCore_knownExceptionならhandled相当のTrueを返してfaultReporterを呼ぶ()
    {
        Exception? loggedException = null;
        Win32Exception? reportedException = null;

        bool actual = App.TryHandleDispatcherUnhandledExceptionCore(
            new Win32Exception(8),
            exception => loggedException = exception,
            exception => reportedException = exception,
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
            Assert.That(loggedException, Is.TypeOf<Win32Exception>());
            Assert.That(reportedException, Is.TypeOf<Win32Exception>());
            Assert.That(reportedException?.NativeErrorCode, Is.EqualTo(8));
        });
    }

    [Test]
    public void TryHandleDispatcherUnhandledExceptionCore_unknownExceptionならhandled相当のFalseを返してfaultReporterを呼ばない()
    {
        bool loggerCalled = false;
        bool reporterCalled = false;

        bool actual = App.TryHandleDispatcherUnhandledExceptionCore(
            new InvalidOperationException("x"),
            _ => loggerCalled = true,
            _ => reporterCalled = true,
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
            Assert.That(loggerCalled, Is.False);
            Assert.That(reporterCalled, Is.False);
        });
    }

    [Test]
    public void RecordDispatcherTimerInfrastructureFault_MainWindow生成前でも後続判定へ残る()
    {
        App.RecordDispatcherTimerInfrastructureFault();

        Assert.Multiple(() =>
        {
            Assert.That(App.HasDispatcherTimerInfrastructureFault, Is.True);
            Assert.That(
                MainWindow.ShouldStartUiHangNotificationSupportCore(
                    App.HasDispatcherTimerInfrastructureFault
                ),
                Is.False
            );
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
    public void TryHandleDispatcherUnhandledExceptionCore_fault記録後はUiHangSupport起動不可になる()
    {
        bool actual = App.TryHandleDispatcherUnhandledExceptionCore(
            new Win32Exception(8),
            _ => { },
            _ => App.RecordDispatcherTimerInfrastructureFault(),
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
            Assert.That(App.HasDispatcherTimerInfrastructureFault, Is.True);
            Assert.That(
                MainWindow.ShouldStartUiHangNotificationSupportCore(
                    App.HasDispatcherTimerInfrastructureFault
                ),
                Is.False
            );
        });
    }

    [Test]
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_Win32Exception以外はFalseを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new InvalidOperationException("x"),
            """
            at System.Windows.Threading.Dispatcher.SetWin32Timer(Int32 dueTimeInTicks)
            """,
            GetDispatcherSetWin32TimerMethod()
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void OnStartup_MainWindowのStartupUri前提でDispatcherUnhandledException登録をbase呼び出し前に行う()
    {
        string appXamlSource = File.ReadAllText(GetRepositoryFilePath("App.xaml"));
        string appSource = File.ReadAllText(GetRepositoryFilePath("App.xaml.cs"));

        // StartupUri で MainWindow が先に立ち上がる前提を source で固定する。
        Assert.That(appXamlSource, Does.Contain("StartupUri=\"Views/Main/MainWindow.xaml\""));
        AssertCallOrderInMethodBody(
            appSource,
            "protected override void OnStartup(StartupEventArgs e)",
            "RegisterDispatcherUnhandledExceptionHandler();",
            "base.OnStartup(e);"
        );
    }

    [Test]
    public void DispatcherTimerSafety_fix4対象ファイルに直呼びDispatcherTimerStartStopを残さない()
    {
        Assert.Multiple(() =>
        {
            string playerSource = File.ReadAllText(
                GetRepositoryFilePath("Views/Main/MainWindow.Player.cs")
            );
            Assert.That(playerSource, Does.Not.Contain("timer.Start();"));
            Assert.That(playerSource, Does.Not.Contain("timer.Stop();"));

            string debugTabSource = File.ReadAllText(
                GetRepositoryFilePath("BottomTabs/DebugTab/MainWindow.BottomTab.Debug.cs")
            );
            Assert.That(debugTabSource, Does.Not.Contain("_debugTabRefreshTimer.Start();"));
            Assert.That(debugTabSource, Does.Not.Contain("_debugTabRefreshTimer.Stop();"));

            string startupSource = File.ReadAllText(
                GetRepositoryFilePath("Views/Main/MainWindow.Startup.cs")
            );
            Assert.That(
                startupSource,
                Does.Not.Contain("_upperTabStartupAppendRetryTimer?.Stop();")
            );

            string bookmarkSource = File.ReadAllText(
                GetRepositoryFilePath("BottomTabs/Bookmark/MainWindow.BottomTab.Bookmark.cs")
            );
            Assert.That(bookmarkSource, Does.Not.Contain("timer.Stop();"));

            string viewportSource = File.ReadAllText(
                GetRepositoryFilePath("UpperTabs/Common/MainWindow.UpperTabs.Viewport.cs")
            );
            Assert.That(
                viewportSource,
                Does.Not.Contain("_upperTabViewportRefreshTimer.Stop();")
            );
            Assert.That(
                viewportSource,
                Does.Not.Contain("_upperTabViewportRefreshTimer.Start();")
            );
            Assert.That(
                viewportSource,
                Does.Not.Contain("_upperTabStartupAppendRetryTimer.Stop();")
            );
            Assert.That(
                viewportSource,
                Does.Not.Contain("_upperTabStartupAppendRetryTimer.Start();")
            );
        });
    }

    private static MethodBase GetDispatcherSetWin32TimerMethod()
    {
        return typeof(Dispatcher).GetMethod(
                "SetWin32Timer",
                BindingFlags.Instance | BindingFlags.NonPublic
            )
            ?? throw new InvalidOperationException("Dispatcher.SetWin32Timer が見つかりません。");
    }

    // 誤 suppress の固定用に、WPF timer と無関係な target site を明示する。
    private static void NonDispatcherTimerTarget() { }

    // 起動時 handler 登録順の契約だけを読みやすく固定する。
    private static void AssertCallOrderInMethodBody(
        string source,
        string methodSignature,
        string firstCall,
        string secondCall
    )
    {
        int methodIndex = source.IndexOf(methodSignature, StringComparison.Ordinal);
        int firstCallIndex = source.IndexOf(firstCall, methodIndex, StringComparison.Ordinal);
        int secondCallIndex = source.IndexOf(secondCall, methodIndex, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(methodIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(firstCallIndex, Is.GreaterThan(methodIndex));
            Assert.That(secondCallIndex, Is.GreaterThan(firstCallIndex));
        });
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
