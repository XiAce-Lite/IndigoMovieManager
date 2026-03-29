using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reflection;
using System.Windows;
using IndigoMovieManager.DB;
using MaterialDesignThemes.Wpf;
using Notification.Wpf;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string MainWindowDropToastAreaName = "ProgressArea";
        private readonly NotificationManager _watchFolderDropNotificationManager = new();

        internal enum WatchFolderDropMainDbAction
        {
            Cancel = 0,
            CreateNew = 1,
            OpenExisting = 2,
        }

        internal enum DroppedMainDbSwitchToastKind
        {
            Switched = 0,
            AlreadyOpen = 1,
            Failed = 2,
        }

        // 有効なフォルダドロップだけ受け付け、DB未選択時の分岐は drop 時点で処理する。
        internal static bool CanAcceptWatchFolderDrop(
            string dbFullPath,
            IEnumerable<string> droppedPaths
        )
        {
            IEnumerable<string> paths = droppedPaths ?? [];
            return WatchFolderDropRegistrationPolicy.CanAccept(paths)
                || !string.IsNullOrWhiteSpace(ResolveDroppedMainDbPath(paths));
        }

        // immダイアログの OK/Cancel とラジオ状態から、次に進む分岐だけを切り出す。
        internal static WatchFolderDropMainDbAction ResolveWatchFolderDropMainDbAction(
            MessageBoxResult dialogResult,
            bool openExistingSelected
        )
        {
            if (dialogResult != MessageBoxResult.OK)
            {
                return WatchFolderDropMainDbAction.Cancel;
            }

            return openExistingSelected
                ? WatchFolderDropMainDbAction.OpenExisting
                : WatchFolderDropMainDbAction.CreateNew;
        }

        private void MainWindow_PreviewDragOver(object sender, DragEventArgs e)
        {
            // 画面へ直接フォルダを落とした時だけ、監視フォルダ追加導線を有効にする。
            string[] droppedPaths = GetWatchFolderDroppedPaths(e.Data);
            e.Effects = CanAcceptWatchFolderDrop(MainVM?.DbInfo?.DBFullPath, droppedPaths)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            // .wb はDB切替、フォルダは watch テーブルへ直接追加として扱い、混在時は先にDBを切り替える。
            string[] droppedPaths = GetWatchFolderDroppedPaths(e.Data);
            if (!CanAcceptWatchFolderDrop(MainVM?.DbInfo?.DBFullPath, droppedPaths))
            {
                e.Handled = true;
                return;
            }

            string droppedMainDbPath = ResolveDroppedMainDbPath(droppedPaths);
            if (!string.IsNullOrWhiteSpace(droppedMainDbPath))
            {
                if (!HandleDroppedMainDbSwitch(droppedMainDbPath))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (!WatchFolderDropRegistrationPolicy.CanAccept(droppedPaths))
            {
                e.Handled = true;
                return;
            }

            if (!EnsureMainDbReadyForWatchFolderDrop())
            {
                e.Handled = true;
                return;
            }

            ApplyDroppedWatchFolders(droppedPaths);
            e.Handled = true;
        }

        // DB未選択の時だけ immダイアログを挟み、.wb の新規/選択を決めてから監視フォルダ編集へ進める。
        private bool EnsureMainDbReadyForWatchFolderDrop()
        {
            if (!string.IsNullOrWhiteSpace(MainVM?.DbInfo?.DBFullPath))
            {
                return true;
            }

            WatchFolderDropMainDbAction action = ShowWatchFolderDropMainDbDialog();
            return action switch
            {
                WatchFolderDropMainDbAction.CreateNew => TryCreateMainDbFromDialog(),
                WatchFolderDropMainDbAction.OpenExisting => TryOpenMainDbFromDialog(),
                _ => false,
            };
        }

        // 管理ファイル未選択時は、ここで「新規作成」か「既存を開く」かを選ばせる。
        private WatchFolderDropMainDbAction ShowWatchFolderDropMainDbDialog()
        {
            var dialogWindow = new MessageBoxEx(this)
            {
                DlogTitle = "監視フォルダ追加",
                DlogHeadline = "管理ファイル(.wb)を選んでください",
                DlogMessage =
                    "監視フォルダを追加するには、先に管理ファイルを用意する必要があります。",
                PackIconKind = PackIconKind.FolderCog,
                DialogAccentBrush = DeleteDialogBlueBrush,
                DialogAccentForegroundBrush = System.Windows.Media.Brushes.White,
                UseRadioButton = true,
                Radio1Content = "新規作成する",
                Radio2Content = "既存ファイルを開く",
                Radio1IsChecked = true,
                Radio2IsChecked = false,
            };
            dialogWindow.ShowDialog();

            return ResolveWatchFolderDropMainDbAction(
                dialogWindow.CloseStatus(),
                dialogWindow.Radio2IsChecked
            );
        }

        // 監視フォルダ編集ダイアログを開く入口を1か所へ寄せ、メニュー起動とドロップ起動を揃える。
        private void OpenWatchFolderEditorDialog(IEnumerable<string> initialDroppedPaths = null)
        {
            if (string.IsNullOrWhiteSpace(MainVM?.DbInfo?.DBFullPath))
            {
                MessageBox.Show(
                    "管理ファイルが選択されていません。",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Exclamation
                );
                return;
            }

            MenuToggleButton.IsChecked = false;
            var watchWindow = new WatchWindow(MainVM.DbInfo.DBFullPath, initialDroppedPaths)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            watchWindow.ShowDialog();
        }

        // Explorer からの file drop を安全に取り出し、判定ロジック側へ渡す。
        private static string[] GetWatchFolderDroppedPaths(IDataObject dataObject)
        {
            if (dataObject == null || !dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                return [];
            }

            return dataObject.GetData(DataFormats.FileDrop) as string[] ?? [];
        }

        // file drop の中から、切替対象にできる .wb だけを拾う。
        internal static string ResolveDroppedMainDbPath(IEnumerable<string> droppedPaths)
        {
            foreach (string droppedPath in droppedPaths ?? [])
            {
                string normalizedPath = NormalizeDroppedMainDbPath(droppedPath);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                if (!File.Exists(normalizedPath))
                {
                    continue;
                }

                if (!string.Equals(Path.GetExtension(normalizedPath), ".wb", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return normalizedPath;
            }

            return "";
        }

        // .wb ドロップ時は同一DBの再オープンを避け、結果をトーストで返す。
        private bool HandleDroppedMainDbSwitch(string droppedMainDbPath)
        {
            if (string.IsNullOrWhiteSpace(droppedMainDbPath))
            {
                return true;
            }

            if (AreSameMainDbPath(MainVM?.DbInfo?.DBFullPath, droppedMainDbPath))
            {
                ShowDroppedMainDbSwitchToast(droppedMainDbPath, DroppedMainDbSwitchToastKind.AlreadyOpen);
                return true;
            }

            bool switched = TrySwitchMainDb(droppedMainDbPath, MainDbSwitchSource.DragDrop);
            ShowDroppedMainDbSwitchToast(
                droppedMainDbPath,
                switched
                    ? DroppedMainDbSwitchToastKind.Switched
                    : DroppedMainDbSwitchToastKind.Failed
            );
            return switched;
        }

        // DBドロップ結果を短いトースト文言へ変換する。
        internal static (string Title, string Message, NotificationType Type) BuildDroppedMainDbSwitchToast(
            string droppedMainDbPath,
            DroppedMainDbSwitchToastKind kind
        )
        {
            string fileName = Path.GetFileName(droppedMainDbPath);
            string displayName = string.IsNullOrWhiteSpace(fileName) ? droppedMainDbPath : fileName;

            return kind switch
            {
                DroppedMainDbSwitchToastKind.AlreadyOpen => (
                    "DB切替",
                    $"既に開いています: {displayName}",
                    NotificationType.Information
                ),
                DroppedMainDbSwitchToastKind.Failed => (
                    "DB切替",
                    $"DBを開けませんでした: {displayName}",
                    NotificationType.Error
                ),
                _ => (
                    "DB切替",
                    $"DBを切り替えました: {displayName}",
                    NotificationType.Success
                ),
            };
        }

        private void ShowDroppedMainDbSwitchToast(
            string droppedMainDbPath,
            DroppedMainDbSwitchToastKind kind
        )
        {
            try
            {
                (string title, string message, NotificationType type) =
                    BuildDroppedMainDbSwitchToast(droppedMainDbPath, kind);
                _watchFolderDropNotificationManager.Show(
                    title,
                    message,
                    type,
                    MainWindowDropToastAreaName,
                    TimeSpan.FromSeconds(4)
                );
            }
            catch
            {
                // トースト失敗でDB切替結果は変えない。
            }
        }

        // メイン画面のフォルダドロップは、その場で watch テーブルへ追加してトーストだけ返す。
        private void ApplyDroppedWatchFolders(IEnumerable<string> droppedPaths)
        {
            if (string.IsNullOrWhiteSpace(MainVM?.DbInfo?.DBFullPath))
            {
                return;
            }

            const string watchTableSql = "SELECT * FROM watch";
            GetWatchTable(MainVM.DbInfo.DBFullPath, watchTableSql);
            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                droppedPaths,
                EnumerateCurrentWatchDirectories()
            );

            foreach (string directoryPath in result.DirectoriesToAdd)
            {
                SQLite.InsertWatchTable(
                    MainVM.DbInfo.DBFullPath,
                    new WatchRecords
                    {
                        Auto = true,
                        Watch = true,
                        Sub = true,
                        Dir = directoryPath,
                    }
                );
            }

            GetWatchTable(MainVM.DbInfo.DBFullPath, watchTableSql);
            ShowDroppedWatchFolderToast(result);
        }

        private IEnumerable<string> EnumerateCurrentWatchDirectories()
        {
            if (watchData == null || watchData.Rows.Count == 0)
            {
                return [];
            }

            List<string> directories = [];
            foreach (DataRow row in watchData.Rows)
            {
                string directoryPath = row["dir"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(directoryPath))
                {
                    directories.Add(directoryPath);
                }
            }

            return directories;
        }

        private void ShowDroppedWatchFolderToast(WatchFolderDropResult result)
        {
            try
            {
                (string title, string message, NotificationType type) =
                    WatchWindow.BuildDropSummaryToast(result);
                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                _watchFolderDropNotificationManager.Show(
                    title,
                    message,
                    type,
                    MainWindowDropToastAreaName,
                    TimeSpan.FromSeconds(4)
                );
            }
            catch
            {
                // トースト失敗で watch 追加結果は変えない。
            }
        }

        // Explorer から来るパスの表記揺れだけ吸収し、不正文字は空扱いにする。
        private static string NormalizeDroppedMainDbPath(string droppedPath)
        {
            if (string.IsNullOrWhiteSpace(droppedPath))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(droppedPath.Trim());
            }
            catch
            {
                return "";
            }
        }
    }
}
