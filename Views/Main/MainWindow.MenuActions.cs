using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IndigoMovieManager.Data;
using IndigoMovieManager.DB;
using IndigoMovieManager.Thumbnail;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using Notification.Wpf;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private enum DeleteActionMode
        {
            UnregisterOnly = 0,
            DeleteMovieToRecycleBin = 1,
            DeleteThumbnailsOnly = 2,
            DeletePermanently = 3,
            DeleteFileWithChoice = 4,
        }

        private enum DeleteDialogAccent
        {
            Blue,
            Orange,
            Green,
            Red,
        }

        private static readonly Brush DeleteDialogOrangeBrush = new SolidColorBrush(
            Color.FromRgb(239, 108, 0)
        );
        private static readonly Brush DeleteDialogBlueBrush = new SolidColorBrush(
            Color.FromRgb(25, 118, 210)
        );
        private static readonly Brush DeleteDialogGreenBrush = new SolidColorBrush(
            Color.FromRgb(46, 125, 50)
        );
        private static readonly Brush DeleteDialogRedBrush = new SolidColorBrush(
            Color.FromRgb(198, 40, 40)
        );

        private readonly IMainDbMovieMutationFacade _mainDbMovieMutationFacade =
            new MainDbMovieMutationFacade();

        // 通常版だけは専用パネル運用に寄せ、通常一覧では誤操作の入口を出さない。
        internal static Visibility ResolveDarkHeavyBackgroundRescueMenuVisibility(
            bool isUpperTabRescueSelected
        )
        {
            return isUpperTabRescueSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MenuContext_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu contextMenu)
            {
                return;
            }

            Visibility darkHeavyMenuVisibility = ResolveDarkHeavyBackgroundRescueMenuVisibility(
                IsUpperTabRescueSelected()
            );
            MenuItem darkHeavyMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(item =>
                string.Equals(
                    item.Name,
                    "ThumbnailDarkHeavyBackgroundRescueMenu",
                    StringComparison.Ordinal
                )
            );
            if (darkHeavyMenu != null)
            {
                darkHeavyMenu.Visibility = darkHeavyMenuVisibility;
            }

            MenuItem darkHeavyLiteMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(item =>
                string.Equals(
                    item.Name,
                    "ThumbnailDarkHeavyBackgroundLiteRescueMenu",
                    StringComparison.Ordinal
                )
            );
            if (darkHeavyLiteMenu != null)
            {
                darkHeavyLiteMenu.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// ファイルのお引越しはおまかせ！コピー＆ムーブを華麗にこなすメニューの要だ！🚚💨
        /// </summary>
        private void MenuCopyAndMove_Click(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;

            if (!(item.Name is "FileCopy" or "FileMove"))
            {
                return;
            }

            var dlgTitle = item.Name == "FileCopy" ? "コピー先の選択" : "移動先の選択";
            var dlg = new OpenFolderDialog
            {
                Title = dlgTitle,
                Multiselect = false,
                AddToRecent = true,
            };

            var ret = dlg.ShowDialog();

            if (ret == true)
            {
                if (Tabs.SelectedItem == null)
                {
                    return;
                }

                List<MovieRecords> mv;
                mv = GetSelectedItemsByTabIndex();
                if (mv == null)
                {
                    return;
                }

                var destFolder = dlg.FolderName;
                foreach (var watcher in fileWatchers)
                {
                    if (watcher.Path == destFolder)
                    {
                        watcher.EnableRaisingEvents = false;
                    }
                }

                foreach (var rec in mv)
                {
                    var destName = Path.Combine(dlg.FolderName, Path.GetFileName(rec.Movie_Path));

                    if (item.Name == "FileCopy")
                    {
                        File.Copy(rec.Movie_Path, destName, true);
                    }
                    else
                    {
                        File.Move(rec.Movie_Path, destName, true);
                        rec.Movie_Path = destName;
                        rec.Dir = destFolder;
                        _mainDbMovieMutationFacade.UpdateMoviePath(
                            MainVM.DbInfo.DBFullPath,
                            rec.Movie_Id,
                            destName
                        );
                        Refresh();
                    }
                }

                foreach (var watcher in fileWatchers)
                {
                    if (watcher.Path == destFolder)
                    {
                        watcher.EnableRaisingEvents = true;
                    }
                }
            }
        }

        private void MenuScore_Click(object sender, RoutedEventArgs e)
        {
            string keyName = "";
            if (sender is not MenuItem menuItem)
            {
                if (e is KeyEventArgs key)
                {
                    keyName = key.Key.ToString();
                }
            }
            else
            {
                keyName = menuItem.Name;
            }

            if (Tabs.SelectedItem == null)
            {
                return;
            }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            if (keyName.ToLower() is "add" or "scoreplus")
            {
                mv.Score += 1;
            }
            else if (keyName.ToLower() is "subtract" or "scoreminus")
            {
                mv.Score -= 1;
            }

            // DBのスコアを更新する。
            _mainDbMovieMutationFacade.UpdateScore(
                MainVM.DbInfo.DBFullPath,
                mv.Movie_Id,
                mv.Score
            );
        }

        private void OpenParentFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            if (Path.Exists(mv.Movie_Path))
            {
                if (Path.Exists(mv.Dir))
                {
                    Process.Start("explorer.exe", $"/select,{mv.Movie_Path}");
                }
            }
        }

        /// <summary>
        /// 選択中の動画パスをまとめてクリップボードへ流し込む。
        /// 複数選択時は改行区切りにして、そのまま貼り付けやすくする。
        /// </summary>
        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            List<MovieRecords> records = GetSelectedItemsByTabIndex();
            if (records == null || records.Count == 0)
            {
                return;
            }

            // 空文字や null を落として、貼り付け先で扱いやすいパス一覧だけに整える。
            List<string> paths = records
                .Select(record => record.Movie_Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct()
                .ToList();
            if (paths.Count == 0)
            {
                return;
            }

            Clipboard.SetText(string.Join(Environment.NewLine, paths));
        }

        private void RenameFile_Click(object sender, RoutedEventArgs e)
        {
            string keyName = "";
            if (sender is not MenuItem menuItem)
            {
                if (e is KeyEventArgs keyEvent)
                {
                    keyName = keyEvent.Key.ToString();
                }
            }
            else
            {
                keyName = menuItem.Name;
            }

            if (!(keyName.ToLower() is "f2" or "renamefile"))
            {
                return;
            }

            if (Tabs.SelectedItem == null)
            {
                return;
            }
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            // mvをそのまま渡さず、編集に必要な項目だけをコピーする。
            var body = Path.GetFileNameWithoutExtension(mv.Movie_Path);
            MovieRecords dt = new()
            {
                Movie_Id = mv.Movie_Id,
                Movie_Body = body,
                Movie_Path = mv.Movie_Path,
                Movie_Name = mv.Movie_Name,
                Ext = mv.Ext,
            };

            var renameWindow = new RenameFile
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = dt,
            };
            renameWindow.ShowDialog();

            if (renameWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            if (dt.Movie_Body == mv.Movie_Body && dt.Ext == mv.Ext)
            {
                return;
            }

            // リネーム。
            var checkFileName = mv.Movie_Body;
            var newFilePath = dt.Movie_Body;
            var checkExt = mv.Ext;
            var newExt = dt.Ext;

            // 実体ファイルのリネームと新旧ファイルパス作成。
            FileInfo mvFile = new(mv.Movie_Path);
            var destMoveFile = mv.Movie_Path.Replace(checkFileName, newFilePath);
            var destFolder = Path.GetDirectoryName(destMoveFile);
            destMoveFile = destMoveFile.Replace(checkExt, newExt);
            try
            {
                mvFile.MoveTo(destMoveFile, true);
            }
            catch (IOException ex)
            {
                MessageBox.Show(
                    $"ファイルのリネームに失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }

            // 監視の一時停止（あれば）
            foreach (var watcher in fileWatchers)
            {
                if (watcher.Path == destFolder)
                {
                    watcher.EnableRaisingEvents = false;
                }
            }

            // 監視時のリネーム処理の実体を呼び出す。
            RenameThumb(destMoveFile, mv.Movie_Path);

            // 監視の再開（あれば）
            foreach (var watcher in fileWatchers)
            {
                if (watcher.Path == destFolder)
                {
                    watcher.EnableRaisingEvents = true;
                }
            }
        }

        // Delete系メニューの入口を、ショートカット共通の実処理へ寄せる。
        private void DeleteMovieRecord_Click(object sender, RoutedEventArgs e)
        {
            if (!TryResolveDeleteMenuRequest(sender, e, out DeleteActionMode actionMode))
            {
                return;
            }

            ExecuteDeleteAction(actionMode);
        }

        // Del / Shift+Del / Ctrl+Del を、それぞれ別設定と色の確認ダイアログへ流す。
        private bool TryHandleDeleteShortcut(KeyEventArgs e)
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                return false;
            }

            DeleteActionMode actionMode;
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                actionMode = NormalizeDeleteActionMode(
                    Properties.Settings.Default.CtrlDeleteKeyActionMode,
                    DeleteActionMode.DeleteMovieToRecycleBin
                );
            }
            else if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                actionMode = NormalizeDeleteActionMode(
                    Properties.Settings.Default.ShiftDeleteKeyActionMode,
                    DeleteActionMode.DeleteThumbnailsOnly
                );
            }
            else
            {
                actionMode = NormalizeDeleteActionMode(
                    Properties.Settings.Default.DeleteKeyActionMode,
                    DeleteActionMode.UnregisterOnly
                );
            }

            ExecuteDeleteAction(actionMode);
            e.Handled = true;
            return true;
        }

        private static DeleteActionMode NormalizeDeleteActionMode(
            int configuredValue,
            DeleteActionMode fallback
        )
        {
            return configuredValue switch
            {
                0 => DeleteActionMode.UnregisterOnly,
                1 => DeleteActionMode.DeleteMovieToRecycleBin,
                2 => DeleteActionMode.DeleteThumbnailsOnly,
                3 => DeleteActionMode.DeletePermanently,
                _ => fallback,
            };
        }

        private static bool TryResolveDeleteMenuRequest(
            object sender,
            RoutedEventArgs e,
            out DeleteActionMode actionMode
        )
        {
            actionMode = DeleteActionMode.UnregisterOnly;

            if (sender is MenuItem menuItem)
            {
                switch (menuItem.Name)
                {
                    case "DeleteMovie":
                        actionMode = DeleteActionMode.UnregisterOnly;
                        return true;
                    case "DeleteFile":
                        actionMode = DeleteActionMode.DeleteFileWithChoice;
                        return true;
                    case "DeleteWithRecycle":
                        actionMode = DeleteActionMode.DeleteMovieToRecycleBin;
                        return true;
                    case "DeleteThumbnailOnly":
                        actionMode = DeleteActionMode.DeleteThumbnailsOnly;
                        return true;
                    case "DeletePermanent":
                        actionMode = DeleteActionMode.DeletePermanently;
                        return true;
                    default:
                        return false;
                }
            }

            if (e is KeyEventArgs keyEvent && keyEvent.Key == Key.Delete)
            {
                actionMode = NormalizeDeleteActionMode(
                    Properties.Settings.Default.DeleteKeyActionMode,
                    DeleteActionMode.UnregisterOnly
                );
                return true;
            }

            return false;
        }

        // 確認ダイアログを出してから、登録解除 / サムネイル削除 / 動画削除をまとめて処理する。
        private void ExecuteDeleteAction(DeleteActionMode actionMode)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            List<MovieRecords> mv = GetSelectedItemsByTabIndex();
            if (mv == null || mv.Count == 0)
            {
                return;
            }

            bool isDeleteFileMode = actionMode == DeleteActionMode.DeleteFileWithChoice;
            bool isDeleteWithRecycleMode = actionMode == DeleteActionMode.DeleteMovieToRecycleBin;
            bool isDeleteThumbnailOnlyMode = actionMode == DeleteActionMode.DeleteThumbnailsOnly;
            bool isDeletePermanentMode = actionMode == DeleteActionMode.DeletePermanently;

            string msg = $"登録からデータを削除します\n（監視対象の場合、再監視で復活します）";
            string title = "登録から削除します";
            string headline = "";
            string radio1Content = "";
            string radio2Content = "";
            bool useRadio = false;
            bool useCheckBox = true;
            bool checkBoxIsChecked = true;
            string checkBoxContent = "サムネイルも削除する";
            MaterialDesignThemes.Wpf.PackIconKind dialogIconKind =
                MaterialDesignThemes.Wpf.PackIconKind.ExclamationBold;
            DeleteDialogAccent dialogAccent = DeleteDialogAccent.Blue;
            MaterialDesignThemes.Wpf.PackIconKind? radio1IconKind = null;
            MaterialDesignThemes.Wpf.PackIconKind? radio2IconKind = null;
            DeleteDialogAccent? radio1Accent = null;
            DeleteDialogAccent? radio2Accent = null;

            if (isDeleteFileMode)
            {
                msg = "登録元のファイルを削除します。";
                title = "ファイル削除";
                useRadio = true;
                radio1Content = "ゴミ箱に移動して削除";
                radio2Content = "ディスクから完全に削除";
                dialogIconKind = MaterialDesignThemes.Wpf.PackIconKind.DeleteRestore;
                dialogAccent = DeleteDialogAccent.Orange;
                radio1IconKind = MaterialDesignThemes.Wpf.PackIconKind.DeleteRestore;
                radio2IconKind = MaterialDesignThemes.Wpf.PackIconKind.DeleteForever;
                radio1Accent = DeleteDialogAccent.Orange;
                radio2Accent = DeleteDialogAccent.Red;
            }
            else if (isDeleteWithRecycleMode)
            {
                // Delキー設定で選ばれた時は、登録解除＋ゴミ箱移動を固定で実行する。
                headline = BuildDeleteDialogHeadline(mv);
                msg = "ゴミ箱に移動します。\nゴミ箱に入らない大きさの場合は削除されます。";
                title = "動画をゴミ箱へ移動";
                dialogIconKind = MaterialDesignThemes.Wpf.PackIconKind.DeleteRestore;
                dialogAccent = DeleteDialogAccent.Orange;
            }
            else if (isDeleteThumbnailOnlyMode)
            {
                msg = "選択した動画のサムネイルを削除します。";
                title = "サムネイル削除";
                useCheckBox = false;
                checkBoxIsChecked = false;
                checkBoxContent = "";
                dialogIconKind = MaterialDesignThemes.Wpf.PackIconKind.ImageRemove;
                dialogAccent = DeleteDialogAccent.Green;
            }
            else if (isDeletePermanentMode)
            {
                headline = BuildDeleteDialogHeadline(mv);
                msg = "元に戻せません。";
                title = "削除";
                dialogIconKind = MaterialDesignThemes.Wpf.PackIconKind.DeleteForever;
                dialogAccent = DeleteDialogAccent.Red;
            }

            var dialogWindow = new MessageBoxEx(this)
            {
                CheckBoxContent = checkBoxContent,
                UseRadioButton = useRadio,
                UseCheckBox = useCheckBox,
                CheckBoxIsChecked = checkBoxIsChecked,
                DlogHeadline = headline,
                DlogMessage = msg,
                DlogTitle = title,
                Radio1Content = radio1Content,
                Radio2Content = radio2Content,
                PackIconKind = dialogIconKind,
                Radio1PackIconKind = radio1IconKind,
                Radio2PackIconKind = radio2IconKind,
            };
            ApplyDeleteDialogAccent(dialogWindow, dialogAccent);
            ApplyDeleteDialogAccent(dialogWindow, radio1Accent, isRadio1: true);
            ApplyDeleteDialogAccent(dialogWindow, radio2Accent, isRadio1: false);

            dialogWindow.ShowDialog();
            if (dialogWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            foreach (var rec in mv)
            {
                bool shouldDeleteThumbnail = isDeleteThumbnailOnlyMode || dialogWindow.checkBox.IsChecked == true;
                if (shouldDeleteThumbnail)
                {
                    DeleteThumbnailsForMovie(rec, sendToRecycleBin: isDeleteWithRecycleMode);
                }

                if (isDeleteThumbnailOnlyMode)
                {
                    continue;
                }

                int deletedCount = DeleteMovieTable(MainVM.DbInfo.DBFullPath, rec.Movie_Id);
                TryAdjustRegisteredMovieCount(MainVM.DbInfo.DBFullPath, -deletedCount);

                // 実ファイルの削除、2パターン。
                if (isDeleteFileMode)
                {
                    if (dialogWindow.radioButton1.IsChecked == true)
                    {
                        // ゴミ箱送り。
                        FileSystem.DeleteFile(
                            rec.Movie_Path,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin
                        );
                    }
                    else
                    {
                        // 実削除。
                        File.Delete(rec.Movie_Path);
                    }
                }
                else if (isDeleteWithRecycleMode)
                {
                    // Delキー設定の「動画削除」は常にゴミ箱送りで実行する。
                    FileSystem.DeleteFile(
                        rec.Movie_Path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin
                    );
                }
                else if (isDeletePermanentMode)
                {
                    File.Delete(rec.Movie_Path);
                }
            }
            FilterAndSort(MainVM.DbInfo.Sort, true);
        }

        // 選択動画に紐づくサムネイル本体と ERROR マーカーをまとめて片付ける。
        private void DeleteThumbnailsForMovie(MovieRecords rec, bool sendToRecycleBin)
        {
            string thumbFolder = ResolveCurrentThumbnailRoot();
            if (Path.Exists(thumbFolder))
            {
                DirectoryInfo di = new(thumbFolder);
                EnumerationOptions enumOption = new() { RecurseSubdirectories = true };

                // 生成時と同じ命名規則を優先し、旧命名は互換フォールバックで拾う。
                string primaryFileName = ThumbnailPathResolver.BuildThumbnailFileName(
                    rec.Movie_Path,
                    rec.Hash
                );
                IEnumerable<FileInfo> primaryFiles = di.EnumerateFiles(primaryFileName, enumOption);

                string legacyPattern = $"*{rec.Movie_Body}.#{rec.Hash}*.jpg";
                IEnumerable<FileInfo> legacyFiles = di.EnumerateFiles(legacyPattern, enumOption);

                foreach (
                    FileInfo item in primaryFiles
                        .Concat(legacyFiles)
                        .GroupBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.First())
                )
                {
                    if (sendToRecycleBin)
                    {
                        FileSystem.DeleteFile(
                            item.FullName,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin
                        );
                    }
                    else
                    {
                        item.Delete();
                    }
                }
            }

            TryDeleteThumbnailErrorMarker(
                ResolveCurrentThumbnailOutPath(GetCurrentThumbnailActionTabIndex()),
                rec.Movie_Path
            );
        }

        private static void ApplyDeleteDialogAccent(
            MessageBoxEx dialogWindow,
            DeleteDialogAccent dialogAccent
        )
        {
            ApplyDeleteDialogAccentCore(
                dialogWindow,
                dialogAccent,
                assignBaseAccent: true,
                isRadio1: false
            );
        }

        private static void ApplyDeleteDialogAccent(
            MessageBoxEx dialogWindow,
            DeleteDialogAccent? dialogAccent,
            bool isRadio1
        )
        {
            if (!dialogAccent.HasValue)
            {
                return;
            }

            ApplyDeleteDialogAccentCore(
                dialogWindow,
                dialogAccent.Value,
                assignBaseAccent: false,
                isRadio1: isRadio1
            );
        }

        private static void ApplyDeleteDialogAccentCore(
            MessageBoxEx dialogWindow,
            DeleteDialogAccent dialogAccent,
            bool assignBaseAccent,
            bool isRadio1
        )
        {
            Brush accentBrush;
            Brush foregroundBrush = Brushes.White;
            switch (dialogAccent)
            {
                case DeleteDialogAccent.Blue:
                    accentBrush = DeleteDialogBlueBrush;
                    break;
                case DeleteDialogAccent.Orange:
                    accentBrush = DeleteDialogOrangeBrush;
                    break;
                case DeleteDialogAccent.Green:
                    accentBrush = DeleteDialogGreenBrush;
                    break;
                case DeleteDialogAccent.Red:
                    accentBrush = DeleteDialogRedBrush;
                    break;
                default:
                    accentBrush = null;
                    foregroundBrush = Brushes.White;
                    break;
            }

            if (assignBaseAccent)
            {
                dialogWindow.DialogAccentBrush = accentBrush;
                dialogWindow.DialogAccentForegroundBrush = foregroundBrush;
                return;
            }

            if (isRadio1)
            {
                dialogWindow.Radio1AccentBrush = accentBrush;
                dialogWindow.Radio1AccentForegroundBrush = foregroundBrush;
            }
            else
            {
                dialogWindow.Radio2AccentBrush = accentBrush;
                dialogWindow.Radio2AccentForegroundBrush = foregroundBrush;
            }
        }

        // 単体は動画名をそのまま出し、複数選択時は件数付きで見出しへ圧縮する。
        private static string BuildDeleteDialogHeadline(IReadOnlyList<MovieRecords> records)
        {
            if (records == null || records.Count == 0)
            {
                return "動画を削除します";
            }

            string movieLabel = BuildDeleteDialogMovieLabel(records[0]);
            if (records.Count == 1)
            {
                return $"{movieLabel}を削除します";
            }

            return $"{movieLabel} ほか{records.Count}件を削除します";
        }

        private static string BuildDeleteDialogMovieLabel(MovieRecords record)
        {
            string movieName = record?.Movie_Body;
            if (string.IsNullOrWhiteSpace(movieName))
            {
                movieName = Path.GetFileNameWithoutExtension(record?.Movie_Path ?? "");
            }
            if (string.IsNullOrWhiteSpace(movieName))
            {
                movieName = "動画";
            }

            return $"「{movieName}」";
        }

        private void BtnReCreateThumbnail_Click(object sender, RoutedEventArgs e)
        {
            QueueRecreateAllThumbnailsFromCurrentTab(closeMenu: true);
        }

        // 現在タブの全動画をサムネイル再作成キューへ積む共通入口。
        private bool QueueRecreateAllThumbnailsFromCurrentTab(bool closeMenu)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                MessageBox.Show(
                    "管理ファイルが選択されていません。",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Exclamation
                );
                return false;
            }

            if (Tabs.SelectedItem == null)
            {
                return false;
            }

            var dialogWindow = new MessageBoxEx(this)
            {
                DlogTitle = "サムネイルの再作成",
                DlogMessage = "サムネイルを再作成します。よろしいですか？",
                PackIconKind = MaterialDesignThemes.Wpf.PackIconKind.EventQuestion,
            };

            dialogWindow.ShowDialog();
            if (dialogWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (closeMenu)
            {
                MenuToggleButton.IsChecked = false;
            }

            foreach (var item in MainVM.MovieRecs)
            {
                int currentTabIndex = GetCurrentThumbnailActionTabIndex();
                QueueObj tempObj = new()
                {
                    MovieId = item.Movie_Id,
                    MovieFullPath = item.Movie_Path,
                    Hash = item.Hash,
                    Tabindex = currentTabIndex,
                    Priority = ThumbnailQueuePriority.Normal,
                };
                _ = TryEnqueueThumbnailJob(tempObj);
            }

            return true;
        }

        // 右クリック明示救済の入口を一本化し、mode 指定だけ差し替えて再利用する。
        private void RunThumbnailRescueMenuAction(
            string rescueMode,
            string upperReason,
            string normalReason,
            string toastTitle
        )
        {
            int currentTabIndex = GetCurrentUpperTabFixedIndex();
            int targetTabIndex = GetCurrentThumbnailActionTabIndex();
            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"context rescue clicked: tab={targetTabIndex} mode={rescueMode ?? ""}"
            );

            if (Tabs.SelectedItem == null)
            {
                return;
            }

            if (currentTabIndex == ThumbnailErrorTabIndex)
            {
                List<MovieRecords> rescueRecords = GetSelectedUpperTabRescueMovieRecords();
                if (rescueRecords.Count == 0)
                {
                    return;
                }

                RememberManualThumbnailRescueMoviePath(rescueRecords[0].Movie_Path);
                ReportManualThumbnailRescueProgress(
                    BuildManualThumbnailRescueModeProgressMessage(rescueMode),
                    true
                );
                int upperRescueQueuedCount = 0;
                int upperDuplicateRequestCount = 0;
                int upperExistingSuccessCount = 0;
                foreach (MovieRecords record in rescueRecords)
                {
                    QueueObj queueObj = new()
                    {
                        MovieId = record.Movie_Id,
                        MovieFullPath = record.Movie_Path,
                        Hash = record.Hash,
                        Tabindex = targetTabIndex,
                    };

                    TryDeleteThumbnailErrorMarker(
                        ResolveCurrentThumbnailOutPath(queueObj.Tabindex),
                        queueObj.MovieFullPath
                    );

                    ThumbnailRescueRequestResult enqueueResult =
                        TryEnqueueThumbnailRescueJobDetailed(
                            queueObj,
                            requiresIdle: false,
                            reason: upperReason,
                            useDedicatedManualWorkerSlot: true,
                            skipWhenSuccessExists: false,
                            rescueMode: rescueMode
                        );
                    switch (enqueueResult)
                    {
                        case ThumbnailRescueRequestResult.Accepted:
                        case ThumbnailRescueRequestResult.Promoted:
                            upperRescueQueuedCount++;
                            break;
                        case ThumbnailRescueRequestResult.DuplicateExistingRequest:
                            upperDuplicateRequestCount++;
                            break;
                        case ThumbnailRescueRequestResult.SkippedExistingSuccess:
                            upperExistingSuccessCount++;
                            break;
                    }
                }

                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"context rescue enqueue end: tab={targetTabIndex} selected={rescueRecords.Count} queued={upperRescueQueuedCount}"
                );
                if (upperRescueQueuedCount == 0)
                {
                    ReportManualThumbnailRescueNotice(
                        BuildManualThumbnailRescueSkipMessage(
                            upperDuplicateRequestCount,
                            upperExistingSuccessCount
                        )
                    );
                }
                else if (
                    upperDuplicateRequestCount > 0
                    || upperExistingSuccessCount > 0
                )
                {
                    ShowManualThumbnailRescueToast(
                        toastTitle,
                        BuildManualThumbnailRescueSkipMessage(
                            upperDuplicateRequestCount,
                            upperExistingSuccessCount
                        ),
                        NotificationType.Information
                    );
                }
                Refresh();
                return;
            }

            if (currentTabIndex < 0 || currentTabIndex > 4)
            {
                return;
            }

            List<MovieRecords> records = GetSelectedItemsByTabIndex();
            if (records == null || records.Count == 0)
            {
                return;
            }

            RememberManualThumbnailRescueMoviePath(records[0].Movie_Path);
            ReportManualThumbnailRescueProgress(
                BuildManualThumbnailRescueModeProgressMessage(rescueMode),
                true
            );
            int queuedCount = 0;
            int duplicateRequestCount = 0;
            int existingSuccessCount = 0;
            foreach (MovieRecords record in records)
            {
                QueueObj queueObj = new()
                {
                    MovieId = record.Movie_Id,
                    MovieFullPath = record.Movie_Path,
                    Hash = record.Hash,
                    Tabindex = targetTabIndex,
                };

                TryDeleteThumbnailErrorMarker(
                    ResolveCurrentThumbnailOutPath(queueObj.Tabindex),
                    queueObj.MovieFullPath
                );

                ThumbnailRescueRequestResult enqueueResult =
                    TryEnqueueThumbnailRescueJobDetailed(
                        queueObj,
                        requiresIdle: false,
                        reason: normalReason,
                        useDedicatedManualWorkerSlot: true,
                        skipWhenSuccessExists: false,
                        rescueMode: rescueMode
                    );
                switch (enqueueResult)
                {
                    case ThumbnailRescueRequestResult.Accepted:
                    case ThumbnailRescueRequestResult.Promoted:
                        queuedCount++;
                        break;
                    case ThumbnailRescueRequestResult.DuplicateExistingRequest:
                        duplicateRequestCount++;
                        break;
                    case ThumbnailRescueRequestResult.SkippedExistingSuccess:
                        existingSuccessCount++;
                        break;
                }
            }

            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"context rescue enqueue end: tab={targetTabIndex} selected={records.Count} queued={queuedCount}"
            );
            if (queuedCount == 0)
            {
                ReportManualThumbnailRescueNotice(
                    BuildManualThumbnailRescueSkipMessage(
                        duplicateRequestCount,
                        existingSuccessCount
                    )
                );
            }
            else if (
                duplicateRequestCount > 0
                || existingSuccessCount > 0
            )
            {
                ShowManualThumbnailRescueToast(
                    toastTitle,
                    BuildManualThumbnailRescueSkipMessage(
                        duplicateRequestCount,
                        existingSuccessCount
                    ),
                    NotificationType.Information
                );
            }
            Refresh();
        }

        // 右クリックからも rescue レーンへ送れるようにし、難動画を通常キューへ戻さない。
        private void ThumbnailRescueMenu_Click(object sender, RoutedEventArgs e)
        {
            RunThumbnailRescueMenuAction(
                rescueMode: "",
                upperReason: "context-upper-rescue-tab",
                normalReason: "context-manual-rescue",
                toastTitle: "手動救済"
            );
        }

        // 黒多め背景専用の手動救済は、通常 route に混ぜず明示指定時だけ mode を載せる。
        private void ThumbnailDarkHeavyBackgroundRescueMenu_Click(object sender, RoutedEventArgs e)
        {
            RunThumbnailRescueMenuAction(
                rescueMode: "dark-heavy-background",
                upperReason: "context-upper-rescue-tab-dark-heavy-background",
                normalReason: "context-manual-rescue-dark-heavy-background",
                toastTitle: "黒多め背景救済"
            );
        }

        // Lite は near-black 候補を落とし過ぎず、とにかく1枚返す寄りで走らせる。
        private void ThumbnailDarkHeavyBackgroundLiteRescueMenu_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            RunThumbnailRescueMenuAction(
                rescueMode: "dark-heavy-background-lite",
                upperReason: "context-upper-rescue-tab-dark-heavy-background-lite",
                normalReason: "context-manual-rescue-dark-heavy-background-lite",
                toastTitle: "黒多め背景救済Lite"
            );
        }

        // 右クリックからも強制 repair 救済へ送れるようにし、救済タブと同じ確認ダイアログを使う。
        private void ThumbnailIndexRepairMenu_Click(object sender, RoutedEventArgs e)
        {
            RunThumbnailIndexRepairMenuAction(
                upperReason: "context-upper-rescue-tab-index-rebuild",
                normalReason: "context-manual-rescue-index-rebuild",
                toastTitle: "インデックス再構築"
            );
        }

        // インデックス再構築は重い処理なので、確認後に対象だけを manual slot へ流す。
        private void RunThumbnailIndexRepairMenuAction(
            string upperReason,
            string normalReason,
            string toastTitle
        )
        {
            if (!ConfirmThumbnailIndexRepair())
            {
                return;
            }

            int currentTabIndex = GetCurrentUpperTabFixedIndex();
            int targetTabIndex = GetCurrentThumbnailActionTabIndex();
            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"context index repair clicked: tab={targetTabIndex}"
            );

            if (Tabs.SelectedItem == null)
            {
                return;
            }

            if (currentTabIndex == ThumbnailErrorTabIndex)
            {
                List<MovieRecords> rescueRecords = GetSelectedUpperTabRescueMovieRecords();
                RunThumbnailIndexRepairMenuActionCore(
                    rescueRecords,
                    targetTabIndex,
                    upperReason,
                    toastTitle
                );
                return;
            }

            if (currentTabIndex < 0 || currentTabIndex > 4)
            {
                return;
            }

            List<MovieRecords> records = GetSelectedItemsByTabIndex();
            RunThumbnailIndexRepairMenuActionCore(
                records,
                targetTabIndex,
                normalReason,
                toastTitle
            );
        }

        // 選択動画を絞り込んで強制 repair 救済へ送り、受付結果だけ UI へ返す。
        private void RunThumbnailIndexRepairMenuActionCore(
            List<MovieRecords> records,
            int targetTabIndex,
            string reason,
            string toastTitle
        )
        {
            if (records == null || records.Count == 0)
            {
                return;
            }

            MovieRecords firstEligibleRecord = records.FirstOrDefault(record =>
                record != null && CanTryThumbnailIndexRepair(record.Movie_Path)
            );
            if (firstEligibleRecord == null)
            {
                ReportManualThumbnailRescueNotice("インデックス再構築対象の動画がありません。");
                return;
            }

            RememberManualThumbnailRescueMoviePath(firstEligibleRecord.Movie_Path);
            ReportManualThumbnailRescueProgress(
                BuildManualThumbnailRescueModeProgressMessage("force-index-repair"),
                true
            );

            int queuedCount = 0;
            int skippedUnsupportedCount = 0;
            int busyCount = 0;
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (MovieRecords record in records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.Movie_Path))
                {
                    continue;
                }

                if (!CanTryThumbnailIndexRepair(record.Movie_Path))
                {
                    skippedUnsupportedCount++;
                    continue;
                }

                string dedupeKey = $"{record.Movie_Path}|{targetTabIndex}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                bool started = TryStartThumbnailDirectIndexRepairWorker(record.Movie_Path);
                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"context index repair direct start: movie='{record.Movie_Path}' tab={targetTabIndex} started={started} reason={reason}"
                );
                if (started)
                {
                    queuedCount++;
                }
                else
                {
                    busyCount++;
                }
            }

            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"context index repair direct end: tab={targetTabIndex} selected={records.Count} started={queuedCount} busy={busyCount} unsupported={skippedUnsupportedCount}"
            );

            if (queuedCount == 0)
            {
                if (busyCount > 0)
                {
                    ReportManualThumbnailRescueNotice(
                        "手動救済worker 2本が稼働中です。空いてから再実行してください。"
                    );
                }
                else if (skippedUnsupportedCount > 0)
                {
                    ReportManualThumbnailRescueNotice("インデックス再構築対象の動画がありません。");
                }
            }
            else if (busyCount > 0 || skippedUnsupportedCount > 0)
            {
                string detailMessage = busyCount > 0
                    ? $"開始 {queuedCount}件 / 空き不足 {busyCount}件"
                    : $"対象外 {skippedUnsupportedCount}件を除外しました。";
                ShowManualThumbnailRescueToast(
                    toastTitle,
                    detailMessage,
                    NotificationType.Information
                );
            }

            Refresh();
        }

        // 進捗表示だけは mode 名を短い文へ変換し、手動操作の意図を UI に返す。
        private static string BuildManualThumbnailRescueModeProgressMessage(string rescueMode)
        {
            return string.Equals(
                rescueMode,
                "dark-heavy-background",
                StringComparison.OrdinalIgnoreCase
            )
                ? "黒多め背景救済を登録中です。"
                : string.Equals(
                    rescueMode,
                "dark-heavy-background-lite",
                StringComparison.OrdinalIgnoreCase
            )
                    ? "黒多め背景救済Liteを登録中です。"
                : string.Equals(
                    rescueMode,
                    "force-index-repair",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "インデックス再構築を開始中です。"
                : "救済要求を登録中です。";
        }

        // 救済タブと右クリックで同じ確認文言を使い、意図の差分をなくす。
        private static bool ConfirmThumbnailIndexRepair()
        {
            MessageBoxResult confirmResult = MessageBox.Show(
                "動画を別名でコピーしてインデックスを再生します。　シークが出来ない動画を復旧できる可能性が有ります",
                "インデックス再構築",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information
            );
            return confirmResult == MessageBoxResult.OK;
        }

        // duplicate / 既存成功を1本の短い案内へまとめ、手動救済の反応を必ず返す。
        private static string BuildManualThumbnailRescueSkipMessage(
            int duplicateRequestCount,
            int existingSuccessCount
        )
        {
            if (duplicateRequestCount > 0)
            {
                return duplicateRequestCount == 1
                    ? "同じ動画は既に救済中、または救済待ちです。"
                    : $"{duplicateRequestCount}件は既に救済中、または救済待ちです。";
            }

            if (existingSuccessCount > 0)
            {
                return existingSuccessCount == 1
                    ? "既に正常サムネイルがあります。"
                    : $"{existingSuccessCount}件は既に正常サムネイルがあります。";
            }

            return "救済要求は受け付けられませんでした。";
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                InitialDirectory = GetMainDbDialogInitialDirectory(),
                RestoreDirectory = true,
                Filter = "設定ファイル(*.wb)|*.wb|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Title = "設定ファイル(.wb）の選択",
                OverwritePrompt = false,
            };

            var result = sfd.ShowDialog();
            if (result == true)
            {
                RememberMainDbDialogDirectory(sfd.FileName);
                if (Path.Exists(sfd.FileName))
                {
                    MessageBox.Show(
                        $"{sfd.FileName}は既に存在します。",
                        "新規作成",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }
                CreateDatabase(sfd.FileName);
                _ = TrySwitchMainDb(sfd.FileName, MainDbSwitchSource.New);
            }
        }

        /// <summary>
        /// 最近使ったファイル履歴（Recent）を先頭優先で再構築する！新しい順に並べて使いやすさをグッと押し上げるぜ！🔄
        /// </summary>
        private void ReStackRecentTree(string newItem)
        {
            var rootItem = MainVM.RecentTreeRoot[0];
            Stack<string> temp = new();

            foreach (var item in recentFiles.Reverse())
            {
                if (item != newItem)
                {
                    temp.Push(item);
                }
            }
            recentFiles.Clear();
            recentFiles = temp;

            while (recentFiles.Count + 1 > Properties.Settings.Default.RecentFilesCount)
            {
                recentFiles = new Stack<string>(recentFiles.Reverse().Skip(1));
            }

            recentFiles.Push(newItem);
            rootItem.Children?.Clear();

            foreach (var item in recentFiles)
            {
                var childItem = new TreeSource { Text = item, IsExpanded = false };
                rootItem.Add(childItem);
            }
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                InitialDirectory = GetMainDbDialogInitialDirectory(),
                RestoreDirectory = true,
                Filter = "設定ファイル(*.wb)|*.wb|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Multiselect = false,
                Title = "設定ファイル(.wb）の選択",
            };

            var result = ofd.ShowDialog();

            if (result == true)
            {
                RememberMainDbDialogDirectory(ofd.FileName);
                _ = TrySwitchMainDb(ofd.FileName, MainDbSwitchSource.OpenDialog);
            }
        }

        /// <summary>
        /// 開発者用テストボタン！各表示部材を手動で強制リロードする禁断の力だ！🔧
        /// </summary>
        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            ReloadBookmarkTabData();
            FilterAndSort(MainVM.DbInfo.Sort, true);
            Refresh();
        }

        private void MenuBtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button item)
            {
                if (!string.IsNullOrEmpty(item.Tag.ToString()))
                {
                    var tag = item.Tag.ToString();
                    if (tag != "設定")
                    {
                        switch (tag)
                        {
                            case "共通設定":
                                MenuToggleButton.IsChecked = false;
                                var commonSettingsWindow = new CommonSettingsWindow
                                {
                                    Owner = this,
                                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                };
                                commonSettingsWindow.ShowDialog();
                                ApplyThumbnailGpuDecodeSetting();
                                break;
                            case "個別設定":
                                if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
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
                                var sysData = new DbSettings(MainVM.DbInfo.DBFullPath);
                                var settingsWindow = new SettingsWindow
                                {
                                    Owner = this,
                                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                    DataContext = sysData,
                                };
                                settingsWindow.ShowDialog();

                                UpsertSystemTable(
                                    MainVM.DbInfo.DBFullPath,
                                    "thum",
                                    settingsWindow.ThumbFolder.Text
                                );
                                UpsertSystemTable(
                                    MainVM.DbInfo.DBFullPath,
                                    "bookmark",
                                    settingsWindow.BookmarkFolder.Text
                                );
                                UpsertSystemTable(
                                    MainVM.DbInfo.DBFullPath,
                                    "keepHistory",
                                    settingsWindow.KeepHistory.Text
                                );
                                UpsertSystemTable(
                                    MainVM.DbInfo.DBFullPath,
                                    "playerPrg",
                                    settingsWindow.PlayerPrg.Text
                                );
                                var param =
                                    settingsWindow.PlayerParam.Text == null
                                        ? ""
                                        : settingsWindow.PlayerParam.Text.ToString();
                                UpsertSystemTable(MainVM.DbInfo.DBFullPath, "playerParam", param);

                                GetSystemTable(MainVM.DbInfo.DBFullPath);
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        if (MenuConfig.Items.Count > 0)
                        {
                            if (MenuConfig.Items[0] is TreeSource topNode)
                            {
                                topNode.IsExpanded = !topNode.IsExpanded;
                            }
                        }
                    }
                }
            }
        }

        private void MenuBtnTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button item)
            {
                if (!string.IsNullOrEmpty(item.Tag.ToString()))
                {
                    var tag = item.Tag.ToString();
                    if (tag != "ツール")
                    {
                        if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
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

                        switch (tag)
                        {
                            case "監視フォルダ編集":
                                var watchWindow = new WatchWindow(MainVM.DbInfo.DBFullPath)
                                {
                                    Owner = this,
                                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                };
                                watchWindow.ShowDialog();
                                break;

                            case "監視フォルダ更新チェック":
                                _ = QueueCheckFolderAsync(
                                    CheckMode.Manual,
                                    "Menu.ManualWatchCheck"
                                );
                                break;

                            case "全ファイルサムネイル再作成":
                                _ = QueueRecreateAllThumbnailsFromCurrentTab(closeMenu: false);
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        if (MenuTool.Items.Count > 0)
                        {
                            if (MenuTool.Items[0] is TreeSource topNode)
                            {
                                topNode.IsExpanded = !topNode.IsExpanded;
                            }
                        }
                    }
                }
            }
        }

        private void MenuRecentTree_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button item)
            {
                if (!string.IsNullOrEmpty(item.Tag.ToString()))
                {
                    var tag = item.Tag.ToString();
                    if (tag != RECENT_OPEN_FILE_LABEL)
                    {
                        _ = TrySwitchMainDb(tag, MainDbSwitchSource.RecentMenu);
                    }
                    else
                    {
                        if (MenuRecent.Items.Count > 0)
                        {
                            if (MenuRecent.Items[0] is TreeSource topNode)
                            {
                                topNode.IsExpanded = !topNode.IsExpanded;
                            }
                        }
                    }
                }
            }
        }
    }
}
