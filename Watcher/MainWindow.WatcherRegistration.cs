using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        /// <summary>
        /// FileSystemWatcherから「新入りが来たぞ！」と報告が上がった時の出迎え処理だぜ！🎉
        /// </summary>
        private async void FileChanged(object sender, FileSystemEventArgs e)
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
                        // ----- [1] ファイル利用可能性の確認 (ロック待機) -----
                        // コピー中などでファイルがOS・別プロセスにロックされているとメタデータ等を読めないため、
                        // 最大10回(約10秒間)オープンできるまで待つ。
                        const int maxRetry = 10;
                        int retry = 0;
                        bool fileReady = false;
                        while (retry < maxRetry)
                        {
                            try
                            {
                                using var stream = File.Open(
                                    e.FullPath,
                                    FileMode.Open,
                                    FileAccess.Read,
                                    FileShare.Read
                                );
                                fileReady = true;
                                break;
                            }
                            catch (IOException)
                            {
                                await Task.Delay(1000);
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
                        if (IsZeroByteMovieFile(e.FullPath, out long fileLength))
                        {
                            int? watchTabIndex = ResolveWatchMissingThumbnailTabIndex(
                                MainVM.DbInfo.CurrentTabIndex
                            );
                            if (watchTabIndex.HasValue)
                            {
                                TryCreateErrorMarkerForSkippedMovie(
                                    e.FullPath,
                                    watchTabIndex.Value,
                                    "zero-byte movie(created event)"
                                );
                            }
                            DebugRuntimeLog.Write(
                                "watch",
                                $"skip zero-byte movie on created event: '{e.FullPath}' size={fileLength}"
                            );
                            return;
                        }

                        // ----- [2] 基礎情報の取得とDB登録 -----
                        // OpenCV等を通じて尺やサイズを拾い、MovieCoreMapperなどを経由する前提の部分
                        MovieInfo mvi = await Task.Run(() => new MovieInfo(e.FullPath));
                        string currentDbFullPath = MainVM.DbInfo.DBFullPath;
                        int insertedCount = await InsertMovieToMainDbAsync(currentDbFullPath, mvi);
                        TryAdjustRegisteredMovieCount(currentDbFullPath, insertedCount);

                        // [MVVM向けの課題] ここで直接ViewDataの更新メソッドを叩いている
                        await TryAppendMovieToViewByPathAsync(
                            MainVM.DbInfo.DBFullPath,
                            mvi.MoviePath
                        );

                        int? autoQueueTabIndex = ResolveWatchMissingThumbnailTabIndex(
                            MainVM.DbInfo.CurrentTabIndex
                        );
                        if (!autoQueueTabIndex.HasValue)
                        {
                            return;
                        }

                        // ----- [3] サムネイル作成キューへ非同期投入 -----
                        QueueObj newFileForThumb = new()
                        {
                            MovieId = mvi.MovieId,
                            MovieFullPath = mvi.MoviePath,
                            Hash = mvi.Hash,
                            Tabindex = autoQueueTabIndex.Value,
                            Priority = ThumbnailQueuePriority.Normal,
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
                MessageBox.Show(
                    this,
                    $"ファイル変更の処理中にエラーが発生しました。\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Application.Current.Shutdown(); // アプリケーションを終了
            }
        }

        /// <summary>
        /// 「ファイル名が変わった！」と報告が入ったら、DBもサムネイルも全員まとめて追従改名させる怒涛の連鎖処理！🏃‍♂️💨
        /// </summary>
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

        /// <summary>
        /// 指定されたフォルダにFileSystemWatcher（監視カメラ）をガッチリ仕掛ける番人の儀式！👁️
        /// </summary>
        private void RunWatcher(string watchFolder, bool sub)
        {
            if (!Path.Exists(watchFolder))
            {
                DebugRuntimeLog.Write("watch", $"skip watcher: folder not found '{watchFolder}'");
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
                NotifyFilter =
                    NotifyFilters.LastAccess
                    | NotifyFilters.LastWrite
                    | NotifyFilters.FileName
                    | NotifyFilters.DirectoryName,

                // サブディレクトリ配下も含めるか指定する
                IncludeSubdirectories = sub,

                // 通知を格納する内部バッファ 既定値は 8192 (8 KB)  4 KB ～ 64 KB
                InternalBufferSize = 1024 * 32,
            };

            // ファイル変更、作成、削除のイベントをファイル変更メソッドにあげる
            item.Changed += new FileSystemEventHandler(FileChanged);
            item.Created += new FileSystemEventHandler(FileChanged);
            //item.Deleted += new FileSystemEventHandler(FileChanged);

            // ファイル名変更のイベントをファイル名変更メソッドにあげる
            item.Renamed += new RenamedEventHandler(FileRenamed);
            item.EnableRaisingEvents = true;

            // アプリ稼働中保持しておくリストに格納
            fileWatchers.Add(item);
            DebugRuntimeLog.Write("watch", $"watcher started: folder='{watchFolder}' sub={sub}");
        }

        /// <summary>
        /// DBに眠るすべての監視フォルダ設定を呼び覚まし、各地にFileSystemWatcher部隊を一斉配備する開幕の合図だ！📢
        /// </summary>
        private void CreateWatcher()
        {
            Stopwatch sw = Stopwatch.StartNew();
            int watcherCount = 0;
            int skippedByEverythingOnlyCount = 0;
            DebugRuntimeLog.TaskStart(nameof(CreateWatcher), $"db='{MainVM.DbInfo.DBFullPath}'");
            IntegrationMode integrationMode = GetEverythingIntegrationMode();
            AvailabilityResult availability = _indexProviderFacade.CheckAvailability(integrationMode);
            string availabilityCategory = FileIndexReasonTable.ToCategory(availability.Reason);
            string availabilityAxis = FileIndexReasonTable.ToLogAxis(availability.Reason);

            string sql = $"SELECT * FROM watch where watch = 1";
            GetWatchTable(MainVM.DbInfo.DBFullPath, sql);
            if (watchData == null)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watcher create canceled: watch table load failed. db='{MainVM.DbInfo.DBFullPath}'"
                );
                return;
            }

            foreach (DataRow row in watchData.Rows)
            {
                //存在しない監視フォルダは読み飛ばし。
                if (!Path.Exists(row["dir"].ToString()))
                {
                    continue;
                }
                string checkFolder = row["dir"].ToString();
                bool sub = (long)row["sub"] == 1;

                string watcherDecisionReason;
                if (
                    ShouldSkipFileSystemWatcherByEverything(
                        checkFolder,
                        integrationMode,
                        availability,
                        out watcherDecisionReason
                    )
                )
                {
                    skippedByEverythingOnlyCount++;
                    DebugRuntimeLog.Write(
                        "watch",
                        $"watcher skipped by everything-only: category={availabilityAxis} folder='{checkFolder}' reason_category={availabilityCategory} reason={watcherDecisionReason}"
                    );
                    continue;
                }

                if (integrationMode == IntegrationMode.On)
                {
                    DebugRuntimeLog.Write(
                        "watch",
                        $"watcher keep: category={availabilityAxis} folder='{checkFolder}' reason_category={availabilityCategory} reason={watcherDecisionReason}"
                    );
                }
                RunWatcher(checkFolder, sub);
                watcherCount++;
            }

            sw.Stop();
            DebugRuntimeLog.TaskEnd(
                nameof(CreateWatcher),
                $"count={watcherCount} skipped={skippedByEverythingOnlyCount} mode={integrationMode} availability_axis={availabilityAxis} availability_category={availabilityCategory} availability={availability.Reason} elapsed_ms={sw.ElapsedMilliseconds}"
            );
        }
    }
}
