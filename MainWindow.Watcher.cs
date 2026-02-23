using IndigoMovieManager.Thumbnail;
using Notification.Wpf;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        /// <summary>
        /// ファイル追加
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                var ext = Path.GetExtension(e.FullPath);
                string checkExt = Properties.Settings.Default.CheckExt.Replace("*", "");
                string[] checkExts = checkExt.Split(",");

                if (checkExts.Contains(ext))
                {
                    if (e.ChangeType == WatcherChangeTypes.Created)
                    {
                        // ファイルが使用中の場合のリトライ処理
                        const int maxRetry = 10;
                        int retry = 0;
                        bool fileReady = false;
                        while (retry < maxRetry)
                        {
                            try
                            {
                                using var stream = File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                fileReady = true;
                                break;
                            }
                            catch (IOException)
                            {
                                Thread.Sleep(1000);
                                retry++;
                            }
                        }
                        if (!fileReady)
                        {
#if DEBUG
                            Debug.WriteLine($"ファイル {e.FullPath} にアクセスできません。");
#endif
                            return;
                        }

                        MovieInfo mvi = new(e.FullPath);
                        _ = InsertMovieTable(MainVM.DbInfo.DBFullPath, mvi);
                        DataTable dt = GetData(MainVM.DbInfo.DBFullPath, "select * from movie order by movie_id desc");
                        if (dt.Rows.Count > 0)
                        {
                            DataRowToViewData(dt.Rows[0]);
                        }

                        QueueObj newFileForThumb = new()
                        {
                            MovieId = mvi.MovieId,
                            MovieFullPath = mvi.MoviePath,
                            Tabindex = MainVM.DbInfo.CurrentTabIndex
                        };
                        _ = TryEnqueueThumbnailJob(newFileForThumb);
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"FileChangedで例外発生: {ex.Message}");
#endif
                MessageBox.Show(this, $"ファイル変更の処理中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown(); // アプリケーションを終了
            }
        }

        /// <summary>
        /// ファイル名変更
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileRenamed(object sender, RenamedEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath);
            string checkExt = Properties.Settings.Default.CheckExt.Replace("*", "");
            string[] checkExts = checkExt.Split(",");
            var eFullPath = e.FullPath;
            var oldFullPath = e.OldFullPath;

            if (checkExts.Contains(ext))
            {
#if DEBUG
                string s = string.Format($"{DateTime.Now:yyyy/MM/dd HH:mm:ss} :");
                s += $"【{e.ChangeType}】{e.OldName} → {e.FullPath}";
                Debug.WriteLine(s);
#endif
                //本家では、Renameは即反映してる様子。
                //このタイミングでは、新旧のファイル名がフルパスで取得可能。
                //旧ファイル名でDB検索、対象がヒットしたら、新ファイル名に変更。
                RenameThumb(eFullPath, oldFullPath);
            }
        }

        private void RunWatcher(string watchFolder, bool sub)
        {
            if (!Path.Exists(watchFolder))
            {
                return;
            }

            // パクリ元：https://dxo.co.jp/blog/archives/3323
            FileSystemWatcher item = new()
            {
                // 監視対象ディレクトリを指定する
                Path = watchFolder,

                // 監視対象の拡張子を指定する（全てを指定する場合は空にする）
                Filter = "",

                // 監視する変更を指定する
                NotifyFilter = NotifyFilters.LastAccess |
                                NotifyFilters.LastWrite |
                                NotifyFilters.FileName |
                                NotifyFilters.DirectoryName,

                // サブディレクトリ配下も含めるか指定する
                IncludeSubdirectories = sub,

                // 通知を格納する内部バッファ 既定値は 8192 (8 KB)  4 KB ～ 64 KB
                InternalBufferSize = 1024 * 32
            };

            // ファイル変更、作成、削除のイベントをファイル変更メソッドにあげる
            item.Changed += new FileSystemEventHandler(FileChanged);
            item.Created += new FileSystemEventHandler(FileChanged);
            //item.Deleted += new FileSystemEventHandler(FileChanged);

            // ファイル名変更のイベントをファイル名変更メソッドにあげる
            item.Renamed += new RenamedEventHandler(FileRenamed);
            item.EnableRaisingEvents = true;

            fileWatchers.Add(item);
        }

        private void CreateWatcher()
        {
            string sql = $"SELECT * FROM watch where watch = 1";
            GetWatchTable(MainVM.DbInfo.DBFullPath, sql);

            foreach (DataRow row in watchData.Rows)
            {
                //存在しない監視フォルダは読み飛ばし。
                if (!Path.Exists(row["dir"].ToString())) { continue; }
                string checkFolder = row["dir"].ToString();
                bool sub = (long)row["sub"] == 1;

                RunWatcher(checkFolder, sub);
            }
        }

        /// <summary>
        /// 起動時と手動時のフォルダチェック。
        /// DB内レコードとフォルダ内対象ファイルの差分比較し、差分があれば追加。
        /// リネームや削除には対応出来ず。
        /// </summary>
        /// <returns></returns>
        /// <exception cref="OperationCanceledException"></exception>
        private async Task CheckFolderAsync(CheckMode mode)
        {
            bool FolderCheckflg = false;
            List<QueueObj> addFiles = [];
            string checkExt = Properties.Settings.Default.CheckExt;

            var title = "フォルダ監視中";
            var Message = "";
            NotificationManager notificationManager = new();

            string sql = mode switch
            {
                CheckMode.Auto => $"SELECT * FROM watch where auto = 1",
                CheckMode.Watch => $"SELECT * FROM watch where watch = 1",
                _ => $"SELECT * FROM watch",
            };
            GetWatchTable(MainVM.DbInfo.DBFullPath, sql);

            foreach (DataRow row in watchData.Rows)
            {
                //存在しない監視フォルダは読み飛ばし。
                if (!Path.Exists(row["dir"].ToString())) { continue; }
                string checkFolder = row["dir"].ToString();

                notificationManager.Show(title, $"{checkFolder} 監視実施中…", NotificationType.Notification, "ProgressArea");

                bool sub = ((long)row["sub"] == 1);

                // ファイルリスト
                var di = new DirectoryInfo(checkFolder);
                EnumerationOptions enumOption = new()
                {
                    RecurseSubdirectories = sub
                };

                try
                {
                    IEnumerable<FileInfo> ssFiles = checkExt.Split(',').SelectMany(filter => di.EnumerateFiles(filter, enumOption));
                    bool IsHit = false;
                    foreach (var ssFile in ssFiles)
                    {
                        var searchFileName = ssFile.FullName.Replace("'", "''");
                        DataRow[] movies = movieData.Select($"movie_path = '{searchFileName}'");
                        if (movies.Length == 0)
                        {
                            Message = checkFolder;
                            if (IsHit == false)
                            {
                                notificationManager.Show(title, $"{Message}に更新あり。", NotificationType.Notification, "ProgressArea");
                                //MessageBox.Show("更新しています。","更新あり",MessageBoxButton.OK,MessageBoxImage.Information);
                                IsHit = true;
                            }

                            MovieInfo mvi = new(ssFile.FullName);
                            await InsertMovieTable(MainVM.DbInfo.DBFullPath, mvi);

                            FolderCheckflg = true;

                            //ここでQueueの元ネタに入れてるのな。
                            //サムネイルファイルが存在するかどうかチェック。あればQueueに入れない。
                            TabInfo tbi = new(MainVM.DbInfo.CurrentTabIndex, MainVM.DbInfo.DBName, MainVM.DbInfo.ThumbFolder);

                            // ファイルハッシュ取得
                            var hash = mvi.Hash;

                            // 拡張子なしのファイル名取得。
                            var fileBody = Path.GetFileNameWithoutExtension(mvi.MoviePath);

                            // 結合したサムネイルのファイル名作成
                            var saveThumbFileName = Path.Combine(tbi.OutPath, $"{fileBody}.#{hash}.jpg");

                            if (Path.Exists(saveThumbFileName))
                            {
                                continue;
                            }

                            QueueObj temp = new()
                            {
                                MovieId = mvi.MovieId,
                                MovieFullPath = mvi.MoviePath,
                                Tabindex = MainVM.DbInfo.CurrentTabIndex
                            };
                            addFiles.Add(temp);

                            DataTable dt = GetData(MainVM.DbInfo.DBFullPath, "select * from movie order by movie_id desc");
                            DataRowToViewData(dt.Rows[0]);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (e.GetType() == typeof(IOException))
                    {
                        //起動中に監視フォルダにファイルコピーされっと例外発生するんよね。
                        await Task.Delay(1000);
                    }
                }
                await Task.Delay(100);
            }

            //stack : ファイル名を外部から変更したときに、エクステンションのファイル名が追従してなかった。強制チェックで反応はした。
            //再クリックで表示はリロードしたので、内部は変わってる。リフレッシュも漏れてる可能性あり。
            //と言うかですね。これは外部からのリネームでも、アプリでのリネームでも同じで。クリックすりゃ反映する（そりゃそうだ）
            if (FolderCheckflg)
            {
                FilterAndSort(MainVM.DbInfo.Sort, true);    //チェックフォルダ時。監視対象があった場合の処理やな。

                foreach (var item in addFiles)
                {
                    _ = TryEnqueueThumbnailJob(item);
                }
            }
        }

    }
}
