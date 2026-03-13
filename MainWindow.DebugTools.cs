using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueueDb;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // Debugタブの各パス表示を、現在の選択DBに追従させる。
        private void RefreshDebugArtifactPaths()
        {
            if (!ShouldShowDebugTab)
            {
                return;
            }

            if (DebugCurrentDbPathText != null)
            {
                DebugCurrentDbPathText.Text = FormatDebugPath(
                    MainVM?.DbInfo?.DBFullPath,
                    "現在DBは未選択です。"
                );
            }

            if (DebugCurrentQueueDbPathText != null)
            {
                DebugCurrentQueueDbPathText.Text = FormatDebugPath(
                    ResolveCurrentQueueDbPathForDebug(),
                    "現在QueueDBは未解決です。"
                );
            }

            if (DebugCurrentThumbnailPathText != null)
            {
                DebugCurrentThumbnailPathText.Text = FormatDebugPath(
                    ResolveCurrentThumbnailRootForDebug(),
                    "現在サムネイルパスは未解決です。"
                );
            }
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
            return string.IsNullOrWhiteSpace(thumbRoot) ? TabInfo.GetDefaultThumbRoot(dbName) : thumbRoot;
        }

        private void DebugOpenCurrentDbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(MainVM?.DbInfo?.DBFullPath ?? "", preferSelectFile: true);
        }

        private void DebugOpenQueueDbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(ResolveCurrentQueueDbPathForDebug(), preferSelectFile: true);
        }

        private void DebugOpenThumbnailDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(ResolveCurrentThumbnailRootForDebug(), preferSelectFile: false);
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
                    $"debug recreate all thumbnails queued: tab={Tabs?.SelectedIndex ?? -1}"
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
            viewExtDetail.DataContext = null;
            viewExtDetail.Visibility = Visibility.Hidden;

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
            MainVM.DbInfo.SearchCount = 0;
            MainVM.DbInfo.CurrentTabIndex = -1;
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
