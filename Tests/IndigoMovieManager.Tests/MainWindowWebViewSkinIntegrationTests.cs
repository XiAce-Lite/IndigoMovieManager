using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using IndigoMovieManager.Skin.Host;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class MainWindowWebViewSkinIntegrationTests
{
    private static readonly object UiThreadSync = new();
    private static Thread? uiThread;
    private static Dispatcher? uiDispatcher;
    private static TaskCompletionSource<bool>? uiThreadReady;

    [Test]
    public async Task 外部skin有効かつhost_readyならTabsを畳んでhostを表示する()
    {
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> applied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPrepareAsyncForTesting = (_, _) => Task.FromResult(true);
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal) && hostReady)
                {
                    applied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.Skin = $"MainWindowWebViewSkinHostReady_{Guid.NewGuid():N}";

                HostPresentationEvent appliedEvent = await WaitAsync(
                    applied.Task,
                    TimeSpan.FromSeconds(10),
                    "外部 skin 表示への切替完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return CaptureSnapshot(window, appliedEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.True);
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterContent, Is.InstanceOf<WhiteBrowserSkinHostControl>());
        });
    }

    [Test]
    public async Task 外部skinのhtml欠落ならWpfFallbackへ戻る()
    {
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> applied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal) && !hostReady)
                {
                    applied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                // 実在しない外部 skin 名を流し、html missing 分岐を本物の MainWindow で踏む。
                window.MainVM.DbInfo.Skin = $"MissingExternalSkin_{Guid.NewGuid():N}";

                HostPresentationEvent appliedEvent = await WaitAsync(
                    applied.Task,
                    TimeSpan.FromSeconds(10),
                    "html missing fallback の適用完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return CaptureSnapshot(window, appliedEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.False);
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterContent, Is.Null);
        });
    }

    [Test]
    public async Task host準備失敗でもWpfFallbackへ戻る()
    {
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> applied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPrepareAsyncForTesting = (_, _) => Task.FromResult(false);
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal) && !hostReady)
                {
                    applied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.Skin = $"NavigateFailureSkin_{Guid.NewGuid():N}";

                HostPresentationEvent appliedEvent = await WaitAsync(
                    applied.Task,
                    TimeSpan.FromSeconds(10),
                    "host 準備失敗 fallback の適用完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return CaptureSnapshot(window, appliedEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.False);
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterContent, Is.Null);
        });
    }

    [Test]
    public async Task skin切替競合でも古いrefresh完了で表示が巻き戻らない()
    {
        RacePresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            List<HostPresentationEvent> appliedEvents = [];
            TaskCompletionSource<bool> firstPrepareStarted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> releaseFirstPrepare = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<HostPresentationEvent> latestApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            int prepareCallCount = 0;

            window.ExternalSkinHostPrepareAsyncForTesting = async (_, _) =>
            {
                int callCount = Interlocked.Increment(ref prepareCallCount);
                if (callCount == 1)
                {
                    firstPrepareStarted.TrySetResult(true);
                    await releaseFirstPrepare.Task;
                }

                return true;
            };
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (!string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                {
                    return;
                }

                HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                appliedEvents.Add(appliedEvent);
                if (!hostReady)
                {
                    latestApplied.TrySetResult(appliedEvent);
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();
                appliedEvents.Clear();

                window.MainVM.DbInfo.Skin = $"RaceExternalSkin_{Guid.NewGuid():N}";
                await WaitAsync(
                    firstPrepareStarted.Task,
                    TimeSpan.FromSeconds(10),
                    "最初の host 準備開始を待てませんでした。"
                );

                // 新しい refresh を built-in 側へ切り替えて積み、古い完了で巻き戻らないことを見る。
                window.MainVM.DbInfo.Skin = "DefaultGrid";
                releaseFirstPrepare.TrySetResult(true);

                HostPresentationEvent latestEvent = await WaitAsync(
                    latestApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "最新 refresh の fallback 適用完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return new RacePresentationSnapshot(
                    prepareCallCount,
                    appliedEvents.ToArray(),
                    latestEvent,
                    window.Tabs.Visibility,
                    window.ExternalSkinHostPresenter.Visibility,
                    window.ExternalSkinHostPresenter.Content
                );
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.PrepareCallCount, Is.EqualTo(1));
            Assert.That(result.AppliedEvents, Has.Length.EqualTo(1));
            Assert.That(result.AppliedEvents[0].HostReady, Is.False);
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterContent, Is.Null);
        });
    }

    private static MainWindow CreateHiddenMainWindow()
    {
        return new MainWindow
        {
            Left = -20000,
            Top = -20000,
            Width = 960,
            Height = 720,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SkipMainWindowClosingSideEffectsForTesting = true,
        };
    }

    private static HostPresentationSnapshot CaptureSnapshot(
        MainWindow window,
        HostPresentationEvent appliedEvent
    )
    {
        return new HostPresentationSnapshot(
            appliedEvent,
            window.Tabs.Visibility,
            window.ExternalSkinHostPresenter.Visibility,
            window.ExternalSkinHostPresenter.Content
        );
    }

    private static async Task CloseWindowAsync(MainWindow window)
    {
        if (window == null)
        {
            return;
        }

        if (window.IsLoaded)
        {
            window.ExternalSkinHostPrepareAsyncForTesting = null;
            window.ExternalSkinHostPresentationAppliedForTesting = null;
            window.Close();
            await WaitForDispatcherIdleAsync();
        }
    }

    private static async Task WaitForDispatcherIdleAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Yield();
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout, string timeoutMessage)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completedTask, task))
        {
            throw new AssertionException(timeoutMessage);
        }

        return await task;
    }

    private static async Task WaitAsync(Task task, TimeSpan timeout, string timeoutMessage)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completedTask, task))
        {
            throw new AssertionException(timeoutMessage);
        }

        await task;
    }

    private static Task<T> RunOnStaDispatcherAsync<T>(Func<Task<T>> action)
    {
        return RunOnSharedUiThreadAsync(action);
    }

    private sealed class TestEnvironmentScope : IDisposable
    {
        private readonly bool originalAutoOpen;
        private readonly bool originalConfirmExit;
        private readonly string originalLastDoc;
        private readonly System.Drawing.Point originalMainLocation;
        private readonly System.Drawing.Size originalMainSize;
        private readonly int originalEverythingIntegrationMode;
        private readonly string originalThemeMode;
        private readonly StringCollection originalRecentFiles;
        private readonly string originalCurrentDirectory;
        private readonly string isolatedCurrentDirectory;

        private TestEnvironmentScope(
            bool originalAutoOpen,
            bool originalConfirmExit,
            string originalLastDoc,
            System.Drawing.Point originalMainLocation,
            System.Drawing.Size originalMainSize,
            int originalEverythingIntegrationMode,
            string originalThemeMode,
            StringCollection originalRecentFiles,
            string originalCurrentDirectory,
            string isolatedCurrentDirectory
        )
        {
            this.originalAutoOpen = originalAutoOpen;
            this.originalConfirmExit = originalConfirmExit;
            this.originalLastDoc = originalLastDoc;
            this.originalMainLocation = originalMainLocation;
            this.originalMainSize = originalMainSize;
            this.originalEverythingIntegrationMode = originalEverythingIntegrationMode;
            this.originalThemeMode = originalThemeMode;
            this.originalRecentFiles = originalRecentFiles;
            this.originalCurrentDirectory = originalCurrentDirectory;
            this.isolatedCurrentDirectory = isolatedCurrentDirectory;
        }

        public static TestEnvironmentScope Create()
        {
            string originalCurrentDirectory = Environment.CurrentDirectory;
            string isolatedCurrentDirectory = Path.Combine(
                Path.GetTempPath(),
                $"imm-mainwindow-webviewskin-{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(isolatedCurrentDirectory);
            Environment.CurrentDirectory = isolatedCurrentDirectory;

            IndigoMovieManager.Properties.Settings settings = IndigoMovieManager.Properties.Settings.Default;
            StringCollection originalRecentFiles = CloneStringCollection(settings.RecentFiles);
            TestEnvironmentScope scope = new(
                settings.AutoOpen,
                settings.ConfirmExit,
                settings.LastDoc ?? "",
                settings.MainLocation,
                settings.MainSize,
                settings.EverythingIntegrationMode,
                settings.ThemeMode ?? "",
                originalRecentFiles,
                originalCurrentDirectory,
                isolatedCurrentDirectory
            );

            settings.AutoOpen = false;
            settings.ConfirmExit = false;
            settings.LastDoc = "";
            settings.EverythingIntegrationMode = 0;
            settings.ThemeMode = "Original";
            settings.MainLocation = new System.Drawing.Point(10, 10);
            settings.MainSize = new System.Drawing.Size(960, 720);
            settings.RecentFiles = new StringCollection();

            return scope;
        }

        public void Dispose()
        {
            IndigoMovieManager.Properties.Settings settings = IndigoMovieManager.Properties.Settings.Default;
            settings.AutoOpen = originalAutoOpen;
            settings.ConfirmExit = originalConfirmExit;
            settings.LastDoc = originalLastDoc;
            settings.MainLocation = originalMainLocation;
            settings.MainSize = originalMainSize;
            settings.EverythingIntegrationMode = originalEverythingIntegrationMode;
            settings.ThemeMode = originalThemeMode;
            settings.RecentFiles = CloneStringCollection(originalRecentFiles);

            Environment.CurrentDirectory = originalCurrentDirectory;
            WhiteBrowserSkinTestData.DeleteDirectorySafe(isolatedCurrentDirectory);
        }

        private static StringCollection CloneStringCollection(StringCollection source)
        {
            StringCollection clone = new();
            if (source == null)
            {
                return clone;
            }

            clone.AddRange(source.Cast<string>().ToArray());
            return clone;
        }
    }

    private static async Task<T> RunOnSharedUiThreadAsync<T>(Func<Task<T>> action)
    {
        await WaitAsync(
            EnsureSharedUiThreadReadyAsync(),
            TimeSpan.FromSeconds(10),
            "共有 UI スレッドの初期化が 10 秒以内に完了しませんでした。"
        );

        Dispatcher dispatcher = uiDispatcher
            ?? throw new AssertionException("共有 UI Dispatcher が初期化されていません。");
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherOperation ignoredOperation = dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() =>
            {
                Task ignored = ExecuteActionAsync();
            })
        );

        return await completion.Task;

        async Task ExecuteActionAsync()
        {
            try
            {
                T result = await action();
                completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }
    }

    private static Task EnsureSharedUiThreadReadyAsync()
    {
        lock (UiThreadSync)
        {
            if (uiThreadReady?.Task != null)
            {
                return uiThreadReady.Task;
            }

            uiThreadReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            uiThread = new Thread(
                () =>
                {
                    try
                    {
                        SynchronizationContext.SetSynchronizationContext(
                            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)
                        );
                        uiDispatcher = Dispatcher.CurrentDispatcher;
                        InitializeSharedUiApplication();
                        uiThreadReady.TrySetResult(true);
                        Dispatcher.Run();
                    }
                    catch (Exception ex)
                    {
                        uiThreadReady.TrySetException(ex);
                    }
                }
            );
            uiThread.IsBackground = true;
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            return uiThreadReady.Task;
        }
    }

    private static void InitializeSharedUiApplication()
    {
        if (Application.ResourceAssembly == null)
        {
            Application.ResourceAssembly = typeof(MainWindow).Assembly;
        }

        if (Application.Current == null)
        {
            Application application = new()
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            application.Resources.MergedDictionaries.Add(
                new BundledTheme
                {
                    BaseTheme = BaseTheme.Inherit,
                    PrimaryColor = PrimaryColor.Indigo,
                    SecondaryColor = SecondaryColor.DeepPurple,
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Button.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.CheckBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.ComboBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.DataGrid.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.GroupBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.ListView.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Slider.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.TextBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/IndigoMovieManager;component/Themes/Generic.xaml",
                        UriKind.Absolute
                    ),
                }
            );
        }

        IndigoMovieManager.Properties.Settings.Default.ThemeMode = "Original";
        App.ApplyTheme(IndigoMovieManager.Properties.Settings.Default.ThemeMode);
    }

    private sealed record HostPresentationEvent(int Generation, string Reason, bool HostReady);

    private sealed record HostPresentationSnapshot(
        HostPresentationEvent Applied,
        Visibility TabsVisibility,
        Visibility PresenterVisibility,
        object PresenterContent
    );

    private sealed record RacePresentationSnapshot(
        int PrepareCallCount,
        HostPresentationEvent[] AppliedEvents,
        HostPresentationEvent LatestApplied,
        Visibility TabsVisibility,
        Visibility PresenterVisibility,
        object PresenterContent
    );
}
