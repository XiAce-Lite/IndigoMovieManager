using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Debug;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;
using System.Data.SQLite;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string DebugToolContentId = "ToolDebug";
        private const int DebugLogRefreshIntervalMs = 3000;
        private const int DebugLogPreviewMaxBytes = 65536;
        private const int DebugLogPreviewMaxChars = 16000;
        private static readonly bool ShouldShowDebugTab = EvaluateShowDebugTab();

        private DateTime _debugLogLastWriteTimeUtc = DateTime.MinValue;
        private DispatcherTimer _debugTabRefreshTimer;
        private DebugTabPresenter _debugTabPresenter;
        private string _debugCurrentDbRecordCountPath = "";
        private string _debugCurrentQueueDbRecordCountPath = "";
        private string _debugCurrentFailureDbRecordCountPath = "";

        private void InitializeDebugTabSupport()
        {
            if (!ShouldShowDebugTab || DebugTab == null)
            {
                return;
            }

            if (_debugTabPresenter != null || DebugTab == null)
            {
                return;
            }

            _debugTabRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DebugLogRefreshIntervalMs),
            };
            _debugTabRefreshTimer.Tick += DebugTabRefreshTimer_Tick;
            _debugTabPresenter = new DebugTabPresenter(
                DebugTab,
                _debugTabRefreshTimer,
                () => ShouldShowDebugTab,
                forceRefresh => UpdateDebugTabRefreshState(forceRefresh),
                isActive => UpdateDebugTabRefreshTimerState(isActive)
            );
            _debugTabPresenter.Initialize();
        }

        private static bool EvaluateShowDebugTab()
        {
#if DEBUG
            // Release ビルドでは強制的に非表示にする。たとえシンボル定義が混入していても想定外表示を防ぐ。
            return !IsReleaseBuild();
#else
            return false;
#endif
        }

        private static bool IsReleaseBuild()
        {
            string configuration = typeof(MainWindow).Assembly
                .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
                ?? "";
            return string.Equals(
                configuration,
                "Release",
                StringComparison.OrdinalIgnoreCase
            );
        }

        private bool IsDebugTabActive()
        {
            return _debugTabPresenter?.IsActive() == true;
        }

        private void DebugTabRefreshTimer_Tick(object sender, EventArgs e)
        {
            _debugTabPresenter?.HandleTimerTick(() => RefreshDebugLogPreview());
        }

        // Debugタブがアクティブな間だけ低頻度で更新し、前面に来た瞬間だけ強制反映する。
        private void UpdateDebugTabRefreshState(bool forceRefresh)
        {
            bool isActive = IsDebugTabActive();
            UpdateDebugTabRefreshTimerState(isActive);

            if (isActive && (forceRefresh || !(_debugTabPresenter?.WasActive ?? false)))
            {
                RefreshDebugRecordCounts(force: true);
                RefreshDebugLogPreview(force: true);
            }

            _debugTabPresenter?.RecordRefreshState(isActive);
        }

        private void UpdateDebugTabRefreshTimerState(bool isActive)
        {
            if (_debugTabRefreshTimer == null)
            {
                return;
            }

            if (ShouldShowDebugTab && isActive)
            {
                if (!_debugTabRefreshTimer.IsEnabled)
                {
                    TryStartDispatcherTimer(
                        _debugTabRefreshTimer,
                        nameof(_debugTabRefreshTimer)
                    );
                }

                return;
            }

            if (_debugTabRefreshTimer.IsEnabled)
            {
                StopDispatcherTimerSafely(
                    _debugTabRefreshTimer,
                    nameof(_debugTabRefreshTimer)
                );
            }
        }

        // 開発用タブは Debug 構成か debugger 接続時だけ下部ペインへ残す。
        private void ApplyDebugTabVisibility()
        {
            if (DebugTab == null || uxAnchorablePane2 == null)
            {
                DebugRuntimeLog.Write(
                    "debug-tab",
                    $"skip apply. DebugTabNull={DebugTab == null} PaneNull={uxAnchorablePane2 == null}"
                );
                return;
            }

            if (!ShouldShowDebugTab)
            {
                DebugRuntimeLog.Write("debug-tab", "hide because ShouldShowDebugTab=false");
                DebugTab.IsSelected = false;
                DebugTab.IsActive = false;
                DebugTab.Hide();
                return;
            }

            // 旧レイアウト復元で Hidden 側や別ペインへ流れても、必ず下部ペインへ戻す。
            if (
                DebugTab.Parent is ILayoutContainer currentParent
                && !ReferenceEquals(currentParent, uxAnchorablePane2)
            )
            {
                DebugRuntimeLog.Write(
                    "debug-tab",
                    $"move from parent={currentParent.GetType().Name} to uxAnchorablePane2"
                );
                currentParent.RemoveChild(DebugTab);
            }

            if (!uxAnchorablePane2.Children.Contains(DebugTab))
            {
                DebugRuntimeLog.Write("debug-tab", "add DebugTab to uxAnchorablePane2");
                uxAnchorablePane2.Children.Add(DebugTab);
            }

            DebugTab.Show();
            DebugTab.IsSelected = true;
            DebugRuntimeLog.Write(
                "debug-tab",
                $"show complete. Parent={DebugTab.Parent?.GetType().Name ?? "null"} Selected={DebugTab.IsSelected} Hidden={DebugTab.IsHidden}"
            );
            UpdateDebugTabRefreshState(forceRefresh: true);
        }

        // ログ更新があった時だけ末尾を読み直し、UI負荷を増やしすぎないようにする。
        private void RefreshDebugLogPreview(bool force = false)
        {
            if (
                !ShouldShowDebugTab
                || (!force && !IsDebugTabActive())
                || DebugTabViewHost?.LogTextBox == null
                || DebugTabViewHost?.LogPathTextBlock == null
            )
            {
                return;
            }

            RefreshDebugArtifactPaths();

            string logPath = Path.Combine(AppLocalDataPaths.LogsPath, "debug-runtime.log");
            SetTextIfChanged(DebugTabViewHost.LogPathTextBlock, logPath);

            DateTime lastWriteTimeUtc = File.Exists(logPath)
                ? File.GetLastWriteTimeUtc(logPath)
                : DateTime.MinValue;
            if (!force && lastWriteTimeUtc == _debugLogLastWriteTimeUtc)
            {
                return;
            }

            _debugLogLastWriteTimeUtc = lastWriteTimeUtc;
            SetTextIfChanged(DebugTabViewHost.LogTextBox, ReadDebugLogPreview(logPath));
            SetTextIfChanged(
                DebugTabViewHost.LogInfoTextBlock,
                lastWriteTimeUtc == DateTime.MinValue
                    ? "debug-runtime.log はまだ作成されていません。"
                    : $"最終更新: {lastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            );

            if (force)
            {
                DebugTabViewHost.ScrollLogToEnd();
            }
        }

        // 巨大ログを丸読みせず、末尾だけ拾って確認用に見せる。
        private static string ReadDebugLogPreview(string logPath)
        {
            if (!File.Exists(logPath))
            {
                return "debug-runtime.log はまだ作成されていません。";
            }

            try
            {
                using var stream = new FileStream(
                    logPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                );
                long start = Math.Max(0, stream.Length - DebugLogPreviewMaxBytes);
                stream.Seek(start, SeekOrigin.Begin);

                using var reader = new StreamReader(stream);
                string text = reader.ReadToEnd();

                if (start > 0)
                {
                    int firstNewLineIndex = text.IndexOf('\n');
                    if (firstNewLineIndex >= 0 && firstNewLineIndex + 1 < text.Length)
                    {
                        text = text[(firstNewLineIndex + 1)..];
                    }
                }

                text = text.TrimStart('\r', '\n');
                if (text.Length > DebugLogPreviewMaxChars)
                {
                    text = text[^DebugLogPreviewMaxChars..];
                }

                return string.IsNullOrWhiteSpace(text)
                    ? "debug-runtime.log は空です。"
                    : text;
            }
            catch (Exception ex)
            {
                return $"debug-runtime.log の読込に失敗しました: {ex.Message}";
            }
        }

        // Debugタブの各パス表示を、現在の選択DBに追従させる。
        private void RefreshDebugArtifactPaths()
        {
            if (!ShouldShowDebugTab || !IsDebugTabActive())
            {
                return;
            }

            string currentDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string currentQueueDbPath = ResolveCurrentQueueDbPathForDebug();
            string currentFailureDbPath = ResolveCurrentFailureDbPathForDebug();

            if (DebugTabViewHost?.CurrentDbPathTextBox != null)
            {
                SetTextIfChanged(
                    DebugTabViewHost.CurrentDbPathTextBox,
                    FormatDebugPath(currentDbPath, "現在DBは未選択です。")
                );
            }

            if (DebugTabViewHost?.CurrentQueueDbPathTextBox != null)
            {
                SetTextIfChanged(
                    DebugTabViewHost.CurrentQueueDbPathTextBox,
                    FormatDebugPath(
                        currentQueueDbPath,
                        "現在QueueDBは未解決です。"
                    )
                );
            }

            if (DebugTabViewHost?.CurrentFailureDbPathTextBox != null)
            {
                SetTextIfChanged(
                    DebugTabViewHost.CurrentFailureDbPathTextBox,
                    FormatDebugPath(
                        currentFailureDbPath,
                        "現在FailureDBは未解決です。"
                    )
                );
            }

            if (DebugTabViewHost?.CurrentThumbnailPathTextBox != null)
            {
                SetTextIfChanged(
                    DebugTabViewHost.CurrentThumbnailPathTextBox,
                    FormatDebugPath(
                        ResolveCurrentThumbnailRootForDebug(),
                        "現在サムネイルパスは未解決です。"
                    )
                );
            }

            if (!string.Equals(_debugCurrentDbRecordCountPath, currentDbPath, StringComparison.OrdinalIgnoreCase))
            {
                RefreshDebugCurrentDbRecordCount(currentDbPath, force: true);
            }

            if (
                !string.Equals(
                    _debugCurrentQueueDbRecordCountPath,
                    currentQueueDbPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                RefreshDebugCurrentQueueDbRecordCount(currentQueueDbPath, force: true);
            }

            if (
                !string.Equals(
                    _debugCurrentFailureDbRecordCountPath,
                    currentFailureDbPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                RefreshDebugCurrentFailureDbRecordCount(currentFailureDbPath, force: true);
            }
        }

        private void RefreshDebugRecordCounts(bool force = false)
        {
            if (!IsDebugTabActive())
            {
                return;
            }

            RefreshDebugCurrentDbRecordCount(MainVM?.DbInfo?.DBFullPath ?? "", force);
            RefreshDebugCurrentQueueDbRecordCount(ResolveCurrentQueueDbPathForDebug(), force);
            RefreshDebugCurrentFailureDbRecordCount(ResolveCurrentFailureDbPathForDebug(), force);
        }

        private void RefreshDebugCurrentDbRecordCount(string dbPath, bool force)
        {
            if (DebugTabViewHost?.CurrentDbRecordCountTextBlock == null)
            {
                return;
            }

            if (
                !force
                && string.Equals(
                    _debugCurrentDbRecordCountPath,
                    dbPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            _debugCurrentDbRecordCountPath = dbPath ?? "";
            SetTextIfChanged(
                DebugTabViewHost.CurrentDbRecordCountTextBlock,
                BuildDebugCurrentDbRecordCountText(dbPath)
            );
        }

        private void RefreshDebugCurrentQueueDbRecordCount(string queueDbPath, bool force)
        {
            if (DebugTabViewHost?.CurrentQueueDbRecordCountTextBlock == null)
            {
                return;
            }

            if (
                !force
                && string.Equals(
                    _debugCurrentQueueDbRecordCountPath,
                    queueDbPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            _debugCurrentQueueDbRecordCountPath = queueDbPath ?? "";
            SetTextIfChanged(
                DebugTabViewHost.CurrentQueueDbRecordCountTextBlock,
                BuildDebugCurrentQueueDbRecordCountText(queueDbPath)
            );
        }

        private void RefreshDebugCurrentFailureDbRecordCount(string failureDbPath, bool force)
        {
            if (DebugTabViewHost?.CurrentFailureDbRecordCountTextBlock == null)
            {
                return;
            }

            if (
                !force
                && string.Equals(
                    _debugCurrentFailureDbRecordCountPath,
                    failureDbPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            _debugCurrentFailureDbRecordCountPath = failureDbPath ?? "";
            SetTextIfChanged(
                DebugTabViewHost.CurrentFailureDbRecordCountTextBlock,
                BuildDebugCurrentFailureDbRecordCountText(failureDbPath)
            );
        }

        private static string BuildDebugCurrentDbRecordCountText(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                return "レコード数: DB未選択";
            }

            if (!File.Exists(dbPath))
            {
                return "レコード数: DBなし";
            }

            try
            {
                using SQLiteConnection connection = CreateReadOnlyConnection(dbPath);
                connection.Open();

                int movieCount = ReadDebugTableCount(connection, "movie");
                int bookmarkCount = ReadDebugTableCount(connection, "bookmark");
                int historyCount = ReadDebugTableCount(connection, "history");
                int findFactCount = ReadDebugTableCount(connection, "findfact");
                int watchCount = ReadDebugTableCount(connection, "watch");

                return
                    $"レコード数 movie={movieCount} / bookmark={bookmarkCount} / history={historyCount} / findfact={findFactCount} / watch={watchCount}";
            }
            catch (Exception ex)
            {
                return $"レコード数: 取得失敗 ({ex.Message})";
            }
        }

        private static string BuildDebugCurrentQueueDbRecordCountText(string queueDbPath)
        {
            if (string.IsNullOrWhiteSpace(queueDbPath))
            {
                return "レコード数: QueueDB未解決";
            }

            if (!File.Exists(queueDbPath))
            {
                return "レコード数: QueueDBなし";
            }

            try
            {
                SQLiteConnectionStringBuilder builder = new()
                {
                    DataSource = queueDbPath,
                    ReadOnly = true,
                };
                using SQLiteConnection connection = new(builder.ConnectionString);
                connection.Open();

                int queueCount = ReadDebugTableCount(connection, "ThumbnailQueue");
                return $"レコード数 ThumbnailQueue={queueCount}";
            }
            catch (Exception ex)
            {
                return $"レコード数: 取得失敗 ({ex.Message})";
            }
        }

        private static string BuildDebugCurrentFailureDbRecordCountText(string failureDbPath)
        {
            if (string.IsNullOrWhiteSpace(failureDbPath))
            {
                return "レコード数: FailureDB未解決";
            }

            if (!File.Exists(failureDbPath))
            {
                return "レコード数: FailureDBなし";
            }

            try
            {
                SQLiteConnectionStringBuilder builder = new()
                {
                    DataSource = failureDbPath,
                    ReadOnly = true,
                };
                using SQLiteConnection connection = new(builder.ConnectionString);
                connection.Open();

                int failureCount = ReadDebugTableCount(connection, "ThumbnailFailure");
                return $"レコード数 ThumbnailFailure={failureCount}";
            }
            catch (Exception ex)
            {
                return $"レコード数: 取得失敗 ({ex.Message})";
            }
        }

        private string ResolveCurrentFailureDbPathForDebug()
        {
            string mainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(mainDbPath))
            {
                return "";
            }

            return ThumbnailFailureDbPathResolver.ResolveFailureDbPath(mainDbPath);
        }

        private static int ReadDebugTableCount(SQLiteConnection connection, string tableName)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(1) FROM [{tableName}]";
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        private static void SetTextIfChanged(TextBox textBox, string nextText)
        {
            if (textBox == null)
            {
                return;
            }

            string safeText = nextText ?? "";
            if (string.Equals(textBox.Text, safeText, StringComparison.Ordinal))
            {
                return;
            }

            textBox.Text = safeText;
        }

        private static void SetTextIfChanged(TextBlock textBlock, string nextText)
        {
            if (textBlock == null)
            {
                return;
            }

            string safeText = nextText ?? "";
            if (string.Equals(textBlock.Text, safeText, StringComparison.Ordinal))
            {
                return;
            }

            textBlock.Text = safeText;
        }

        private static string FormatDebugPath(string path, string emptyMessage)
        {
            return string.IsNullOrWhiteSpace(path) ? emptyMessage : path;
        }

        // 現在DBがあれば対応QueueDBを返し、未選択時は最後に使っていたQueueDBを見せる。
        private string ResolveCurrentQueueDbPathForDebug()
        {
            string mainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (!string.IsNullOrWhiteSpace(mainDbPath))
            {
                return QueueDbPathResolver.ResolveQueueDbPath(mainDbPath);
            }

            return currentQueueDbService?.QueueDbFullPath ?? "";
        }

        // 個別設定が無い時は既定のThumbルートを採用する。
        private string ResolveCurrentThumbnailRootForDebug()
        {
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            if (string.IsNullOrWhiteSpace(dbName))
            {
                return "";
            }

            string thumbRoot = MainVM?.DbInfo?.ThumbFolder ?? "";
            return string.IsNullOrWhiteSpace(thumbRoot)
                ? ThumbRootResolver.GetDefaultThumbRoot(dbName)
                : thumbRoot;
        }

        private void DebugOpenAppDataDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(AppLocalDataPaths.RootPath, preferSelectFile: false);
        }

        private void DebugOpenCurrentDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(Environment.CurrentDirectory, preferSelectFile: false);
        }

        private void DebugOpenThumbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(ResolveCurrentThumbnailRootForDebug(), preferSelectFile: false);
        }

        private void DebugOpenDbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(MainVM?.DbInfo?.DBFullPath ?? "", preferSelectFile: true);
        }

        private void DebugOpenLogDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(AppLocalDataPaths.LogsPath, preferSelectFile: false);
        }

        private void DebugOpenCurrentDbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(MainVM?.DbInfo?.DBFullPath ?? "", preferSelectFile: true);
        }

        private void DebugOpenQueueDbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(ResolveCurrentQueueDbPathForDebug(), preferSelectFile: true);
        }

        private void DebugOpenFailureDbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(ResolveCurrentFailureDbPathForDebug(), preferSelectFile: true);
        }

        private void DebugOpenThumbnailDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(ResolveCurrentThumbnailRootForDebug(), preferSelectFile: false);
        }

        private void DebugRefreshCurrentDbRecordCount_Click(object sender, RoutedEventArgs e)
        {
            RefreshDebugCurrentDbRecordCount(MainVM?.DbInfo?.DBFullPath ?? "", force: true);
        }

        private void DebugClearCurrentDbRecords_Click(object sender, RoutedEventArgs e)
        {
            string dbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                ShowDebugPathMissingMessage("現在DBが選択されていません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在DBのレコードをクリア",
                    "movie / bookmark / history / findfact / watch を空にします。"
                )
            )
            {
                return;
            }

            ClearMainDataRecords(dbPath);

            QueueDbService queueDbService = ResolveDebugQueueDbService();
            if (queueDbService != null)
            {
                int queueDeleted = queueDbService.ClearAll();
                DebugRuntimeLog.Write(
                    "debug-ui",
                    $"debug clear queue db after main clear: deleted={queueDeleted} path='{queueDbService.QueueDbFullPath}'"
                );
            }

            ClearThumbnailQueue();
            OpenDatafile(dbPath);
            RefreshDebugRecordCounts(force: true);
            RefreshDebugLogPreview(force: true);
        }

        private void DebugDeleteCurrentDb_Click(object sender, RoutedEventArgs e)
        {
            string dbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                ShowDebugPathMissingMessage("現在DBが選択されていません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在DBを削除",
                    "現在開いているMainDBファイルを削除し、画面のDB選択も外します。"
                )
            )
            {
                return;
            }

            ShutdownCurrentDb();

            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }

                if (string.Equals(Properties.Settings.Default.LastDoc, dbPath, StringComparison.OrdinalIgnoreCase))
                {
                    Properties.Settings.Default.LastDoc = "";
                    Properties.Settings.Default.Save();
                }

                ResetDebugCurrentDbUiState();
                DebugRuntimeLog.Write("debug-ui", $"debug delete main db: path='{dbPath}'");
            }
            catch (Exception ex)
            {
                if (File.Exists(dbPath))
                {
                    OpenDatafile(dbPath);
                }

                MessageBox.Show(
                    this,
                    $"DB削除に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            RefreshDebugRecordCounts(force: true);
            RefreshDebugLogPreview(force: true);
        }

        private void DebugRefreshQueueDbRecordCount_Click(object sender, RoutedEventArgs e)
        {
            RefreshDebugCurrentQueueDbRecordCount(ResolveCurrentQueueDbPathForDebug(), force: true);
        }

        private void DebugRefreshFailureDbRecordCount_Click(object sender, RoutedEventArgs e)
        {
            RefreshDebugCurrentFailureDbRecordCount(
                ResolveCurrentFailureDbPathForDebug(),
                force: true
            );
        }

        private void DebugClearFailureDbRecords_Click(object sender, RoutedEventArgs e)
        {
            string mainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(mainDbPath))
            {
                ShowDebugPathMissingMessage("現在FailureDBの元DBが選択されていません。");
                return;
            }

            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            if (failureDbService == null)
            {
                ShowDebugPathMissingMessage("現在FailureDBが特定できません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在FailureDBのレコードをクリア",
                    "ThumbnailFailure テーブルのレコードをすべて削除します。"
                )
            )
            {
                return;
            }

            try
            {
                int deleted = failureDbService.ClearMainFailureRecords();
                DebugRuntimeLog.Write(
                    "debug-ui",
                    $"debug clear failure db: deleted={deleted} path='{failureDbService.FailureDbFullPath}'"
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"FailureDB削除に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            RefreshDebugRecordCounts(force: true);
            RefreshDebugLogPreview(force: true);
        }

        private void DebugDeleteFailureDb_Click(object sender, RoutedEventArgs e)
        {
            string failureDbPath = ResolveCurrentFailureDbPathForDebug();
            if (string.IsNullOrWhiteSpace(failureDbPath))
            {
                ShowDebugPathMissingMessage("現在FailureDBが特定できません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在FailureDBを削除",
                    "現在FailureDBファイルを削除します。必要になれば再作成されます。"
                )
            )
            {
                return;
            }

            try
            {
                if (File.Exists(failureDbPath))
                {
                    File.Delete(failureDbPath);
                }

                DebugRuntimeLog.Write("debug-ui", $"debug delete failure db: path='{failureDbPath}'");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"FailureDB削除に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            RefreshDebugRecordCounts(force: true);
            RefreshDebugLogPreview(force: true);
        }

        private void DebugClearQueueDbRecords_Click(object sender, RoutedEventArgs e)
        {
            QueueDbService queueDbService = ResolveDebugQueueDbService();
            if (queueDbService == null)
            {
                ShowDebugPathMissingMessage("現在QueueDBが特定できません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在QueueDBのレコードをクリア",
                    "ThumbnailQueue テーブルのレコードをすべて削除します。"
                )
            )
            {
                return;
            }

            int deleted = queueDbService.ClearAll();
            ClearThumbnailQueue();
            DebugRuntimeLog.Write(
                "debug-ui",
                $"debug clear queue db: deleted={deleted} path='{queueDbService.QueueDbFullPath}'"
            );
            RefreshDebugRecordCounts(force: true);
            RefreshDebugLogPreview(force: true);
        }

        private void DebugDeleteQueueDb_Click(object sender, RoutedEventArgs e)
        {
            string queueDbPath = ResolveCurrentQueueDbPathForDebug();
            if (string.IsNullOrWhiteSpace(queueDbPath))
            {
                ShowDebugPathMissingMessage("現在QueueDBが特定できません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在QueueDBを削除",
                    "現在QueueDBファイルを削除します。必要になれば再作成されます。"
                )
            )
            {
                return;
            }

            try
            {
                ClearThumbnailQueue();
                if (File.Exists(queueDbPath))
                {
                    File.Delete(queueDbPath);
                }

                DebugRuntimeLog.Write("debug-ui", $"debug delete queue db: path='{queueDbPath}'");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"QueueDB削除に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            RefreshDebugRecordCounts(force: true);
            RefreshDebugLogPreview(force: true);
        }

        private void DebugDeleteThumbnailDir_Click(object sender, RoutedEventArgs e)
        {
            string thumbnailRoot = ResolveCurrentThumbnailRootForDebug();
            if (string.IsNullOrWhiteSpace(thumbnailRoot))
            {
                ShowDebugPathMissingMessage("現在サムネイルパスが特定できません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在サムネイルを削除",
                    "現在サムネイルフォルダ配下を再帰的に削除します。"
                )
            )
            {
                return;
            }

            try
            {
                if (Directory.Exists(thumbnailRoot))
                {
                    Directory.Delete(thumbnailRoot, true);
                }

                if (!string.IsNullOrWhiteSpace(MainVM?.DbInfo?.Sort))
                {
                    FilterAndSort(MainVM.DbInfo.Sort, true);
                    Refresh();
                }

                DebugRuntimeLog.Write("debug-ui", $"debug delete thumbnail dir: path='{thumbnailRoot}'");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"サムネイル削除に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            RefreshDebugLogPreview(force: true);
        }

        private void DebugRecreateAllThumbnails_Click(object sender, RoutedEventArgs e)
        {
            if (QueueRecreateAllThumbnailsFromCurrentTab(closeMenu: false))
            {
                DebugRuntimeLog.Write(
                    "debug-ui",
                    $"debug recreate all thumbnails queued: tab={GetCurrentUpperTabFixedIndex()}"
                );
                RefreshDebugLogPreview(force: true);
            }
        }

        // 現在DBが無くても、直前に握っていたQueueDbServiceがあればそれを使う。
        private QueueDbService ResolveDebugQueueDbService()
        {
            string mainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (!string.IsNullOrWhiteSpace(mainDbPath))
            {
                return ResolveCurrentQueueDbService();
            }

            return currentQueueDbService;
        }

        // DB削除後に、UI側の現在DB状態だけを空へ戻す。
        private void ResetDebugCurrentDbUiState()
        {
            Tabs.SelectedIndex = -1;
            SearchBox.Text = "";
            HideExtensionDetail();

            movieData?.Clear();
            bookmarkData?.Clear();
            historyData?.Clear();
            watchData?.Clear();
            systemData?.Clear();

            MainVM.MovieRecs.Clear();
            MainVM.ReplaceFilteredMovieRecs([]);
            MainVM.PendingMovieRecs.Clear();
            MainVM.BookmarkRecs.Clear();
            MainVM.HistoryRecs.Clear();

            MainVM.DbInfo.DBFullPath = "";
            MainVM.DbInfo.DBName = "";
            MainVM.DbInfo.Skin = "";
            MainVM.DbInfo.Sort = "";
            MainVM.DbInfo.ThumbFolder = "";
            MainVM.DbInfo.BookmarkFolder = "";
            MainVM.DbInfo.SearchKeyword = "";
            ResetMainHeaderCounts();
            MainVM.DbInfo.CurrentTabIndex = -1;
            _debugCurrentDbRecordCountPath = "";
            _debugCurrentQueueDbRecordCountPath = "";
            _debugCurrentFailureDbRecordCountPath = "";
        }

        private void OpenDebugPathInExplorer(string path, bool preferSelectFile)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowDebugPathMissingMessage("対象パスがありません。");
                return;
            }

            try
            {
                if (preferSelectFile && File.Exists(path))
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                    return;
                }

                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", $"\"{path}\"");
                    return;
                }

                string parentDir = Path.GetDirectoryName(path) ?? "";
                if (Directory.Exists(parentDir))
                {
                    if (preferSelectFile)
                    {
                        Process.Start("explorer.exe", $"/select,\"{path}\"");
                    }
                    else
                    {
                        Process.Start("explorer.exe", $"\"{parentDir}\"");
                    }
                    return;
                }

                ShowDebugPathMissingMessage($"パスが存在しません。\n{path}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Explorer起動に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private bool ConfirmDebugAction(string title, string message)
        {
            return MessageBox.Show(
                    this,
                    $"{message}\n\n続行しますか？",
                    title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                ) == MessageBoxResult.Yes;
        }

        private void ShowDebugPathMissingMessage(string message)
        {
            MessageBox.Show(
                this,
                message,
                Assembly.GetExecutingAssembly().GetName().Name,
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }
}
