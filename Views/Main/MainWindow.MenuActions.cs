using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private readonly IMainDbMovieMutationFacade _mainDbMovieMutationFacade =
            new MainDbMovieMutationFacade();

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

        private void DeleteMovieRecord_Click(object sender, RoutedEventArgs e)
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

            if (!(keyName.ToLower() is "delete" or "deletemovie" or "deletefile" or "deletewithrecycle"))
            {
                return;
            }

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

            bool isDeleteFileMode = keyName.Equals("deletefile", StringComparison.CurrentCultureIgnoreCase);
            bool isDeleteWithRecycleMode = keyName.Equals(
                "deletewithrecycle",
                StringComparison.CurrentCultureIgnoreCase
            );

            string msg = $"登録からデータを削除します\n（監視対象の場合、再監視で復活します）";
            string title = "登録から削除します";
            string radio1Content = "";
            string radio2Content = "";
            bool useRadio = false;

            if (isDeleteFileMode)
            {
                msg = "登録元のファイルを削除します。";
                title = "ファイル削除";
                useRadio = true;
                radio1Content = "ゴミ箱に移動して削除";
                radio2Content = "ディスクから完全に削除";
            }
            else if (isDeleteWithRecycleMode)
            {
                // Delキー設定で選ばれた時は、登録解除＋ゴミ箱移動を固定で実行する。
                msg = "登録を解除し、登録元のファイルをゴミ箱に移動します。";
                title = "動画をゴミ箱へ移動";
            }

            var dialogWindow = new MessageBoxEx(this)
            {
                CheckBoxContent = "サムネイルも削除する",
                UseRadioButton = useRadio,
                UseCheckBox = true,
                CheckBoxIsChecked = true,
                DlogMessage = msg,
                DlogTitle = title,
                Radio1Content = radio1Content,
                Radio2Content = radio2Content,
                PackIconKind = MaterialDesignThemes.Wpf.PackIconKind.ExclamationBold,
            };

            dialogWindow.ShowDialog();
            if (dialogWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            foreach (var rec in mv)
            {
                if (dialogWindow.checkBox.IsChecked == true)
                {
                    // サムネイルも消す。
                    var checkFileName = rec.Movie_Body;
                    string thumbFolder = ResolveCurrentThumbnailRoot();

                    if (Path.Exists(thumbFolder))
                    {
                        var di = new DirectoryInfo(thumbFolder);
                        EnumerationOptions enumOption = new() { RecurseSubdirectories = true };

                        // 生成時と同じ命名規則を優先し、旧命名は互換フォールバックで拾う。
                        string primaryFileName = ThumbnailPathResolver.BuildThumbnailFileName(
                            rec.Movie_Path,
                            rec.Hash
                        );
                        IEnumerable<FileInfo> primaryFiles = di.EnumerateFiles(
                            primaryFileName,
                            enumOption
                        );

                        string legacyPattern = $"*{rec.Movie_Body}.#{rec.Hash}*.jpg";
                        IEnumerable<FileInfo> legacyFiles = di.EnumerateFiles(
                            legacyPattern,
                            enumOption
                        );

                        foreach (
                            var item in primaryFiles
                                .Concat(legacyFiles)
                                .GroupBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
                                .Select(x => x.First())
                        )
                        {
                            // 「動画削除（ゴミ箱）」時は、サムネイルもゴミ箱へ送る。
                            if (isDeleteWithRecycleMode)
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
            }
            FilterAndSort(MainVM.DbInfo.Sort, true);
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
                : "救済要求を登録中です。";
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
