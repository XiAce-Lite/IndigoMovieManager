using System.Collections.Specialized;
using System.Data.SQLite;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using IndigoMovieManager.DB;
using IndigoMovieManager.ViewModels;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class MainWindowSearchBoxEnterTests
{
    private static readonly object UiThreadSync = new();
    private static Thread? uiThread;
    private static Dispatcher? uiDispatcher;
    private static TaskCompletionSource<bool>? uiThreadReady;

    [Test]
    public async Task SearchBox_Enterで検索実行と履歴保存ができる()
    {
        SearchEnterResult result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                SeedMovieRow(dbPath);
                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                window.MainVM.DbInfo.Sort = "1";
                window.MainVM.DbInfo.ThumbFolder = Path.Combine(
                    Path.GetTempPath(),
                    $"imm-search-enter-thumb-{Guid.NewGuid():N}"
                );
                Directory.CreateDirectory(window.MainVM.DbInfo.ThumbFolder);
                window.Tabs.SelectedIndex = 2;
                window.MainVM.DbInfo.CurrentTabIndex = 2;
                window.MainVM.DbInfo.SearchCount = 0;
                window.SearchBox.Text = "target";
                window.SearchBox.Focus();
                Keyboard.Focus(window.SearchBox);
                await WaitForDispatcherIdleAsync();

                KeyEventArgs args = CreatePreviewKeyEvent(window.SearchBox, Key.Enter);
                InvokeSearchBoxPreviewKeyDown(window, args);

                await WaitUntilAsync(
                    () => window.MainVM.DbInfo.SearchCount == 1 && window.MainVM.HistoryRecs.Count == 1,
                    TimeSpan.FromSeconds(5),
                    "Enter 検索の反映完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return new SearchEnterResult(
                    window.MainVM.DbInfo.SearchKeyword,
                    window.MainVM.DbInfo.SearchCount,
                    [.. window.MainVM.HistoryRecs.Select(x => x.Find_Text)],
                    [.. ReadHistoryTexts(dbPath)],
                    args.Handled
                );
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteDirectory(window.MainVM.DbInfo.ThumbFolder);
                TryDeleteFile(dbPath);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.SearchKeyword, Is.EqualTo("target"));
            Assert.That(result.SearchCount, Is.EqualTo(1));
            Assert.That(result.UiHistoryTexts, Is.EqualTo(["target"]));
            Assert.That(result.DbHistoryTexts, Is.EqualTo(["target"]));
            Assert.That(result.WasHandled, Is.True);
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

    private static KeyEventArgs CreatePreviewKeyEvent(UIElement source, Key key)
    {
        PresentationSource presentationSource = PresentationSource.FromVisual(source)
            ?? throw new AssertionException("PreviewKeyDown 用の PresentationSource を取得できません。");
        KeyEventArgs args = new(Keyboard.PrimaryDevice, presentationSource, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        args.Source = source;
        return args;
    }

    private static void InvokeSearchBoxPreviewKeyDown(MainWindow window, KeyEventArgs args)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "SearchBox_PreviewKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null, "SearchBox_PreviewKeyDown");
        method.Invoke(window, [window.SearchBox, args]);
    }

    private static string CreateTempMainDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-search-enter-{Guid.NewGuid():N}.wb");
        bool created = SQLite.TryCreateDatabase(dbPath, out string errorMessage);
        Assert.That(created, Is.True, errorMessage);
        return dbPath;
    }

    private static void SeedMovieRow(string dbPath)
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO movie (
    movie_id,
    movie_name,
    movie_path,
    movie_length,
    movie_size,
    last_date,
    file_date,
    regist_date,
    score,
    view_count,
    hash,
    container,
    video,
    audio,
    kana,
    tag,
    comment1,
    comment2,
    comment3
)
VALUES (
    1,
    'target movie',
    'C:\movies\target movie.mp4',
    60,
    100,
    '2026-04-07 10:00:00',
    '2026-04-07 10:00:00',
    '2026-04-07 10:00:00',
    1,
    1,
    'hash-1',
    'mp4',
    'h264',
    'aac',
    'target',
    '',
    '',
    '',
    ''
);";
        command.ExecuteNonQuery();
    }

    private static string[] ReadHistoryTexts(string dbPath)
    {
        using SQLiteConnection connection = SQLite.CreateReadOnlyConnection(dbPath);
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = "select find_text from history order by find_id";
        using SQLiteDataReader reader = command.ExecuteReader();
        List<string> result = [];
        while (reader.Read())
        {
            result.Add(reader["find_text"]?.ToString() ?? "");
        }

        return [.. result];
    }

    private static async Task CloseWindowAsync(MainWindow window)
    {
        if (window == null || !window.IsLoaded)
        {
            return;
        }

        window.Close();
        await WaitForDispatcherIdleAsync();
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string timeoutMessage
    )
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await WaitForDispatcherIdleAsync();
            await Task.Delay(50);
        }

        throw new AssertionException(timeoutMessage);
    }

    private static async Task WaitForDispatcherIdleAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Yield();
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
                $"imm-search-enter-{Guid.NewGuid():N}"
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
            TryDeleteDirectory(isolatedCurrentDirectory);
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
        _ = dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() =>
            {
                _ = ExecuteActionAsync();
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

    private static async Task WaitAsync(Task task, TimeSpan timeout, string timeoutMessage)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completedTask, task))
        {
            throw new AssertionException(timeoutMessage);
        }

        await task;
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

    private static void TryDeleteFile(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // 一時ファイル掃除失敗は本体判定を優先する。
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // 一時フォルダ掃除失敗は本体判定を優先する。
            }
        }
    }

    private readonly record struct SearchEnterResult(
        string SearchKeyword,
        int SearchCount,
        string[] UiHistoryTexts,
        string[] DbHistoryTexts,
        bool WasHandled
    );
}
