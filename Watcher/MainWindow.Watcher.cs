using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Watcher;
using Notification.Wpf;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // =================================================================================
        // フォルダ監視・走査に関するバックグラウンド処理 (Modelに近く、インフラ層にまたがる)
        // ローカルディスク上のファイル増減を検知し、DBやサムネイル作成キューと同期させる役割。
        // =================================================================================

        // フォルダ走査で見つけた新規動画を、何件単位でサムネイルキューへ流すか。
        // 走査完了を待たずに段階投入することで、初動を早めつつI/O競合を抑える。
        private const int FolderScanEnqueueBatchSize = 100;

        // UIへ逐次反映する上限件数。小規模時は体感を優先して1件ずつ表示する。
        private const int IncrementalUiUpdateThreshold = 20;
        private const string EverythingLastSyncAttrPrefix = "everything_last_sync_utc_";

        // Everything連携の可否判定と検索呼び出しをまとめたサービス。
        private readonly EverythingFolderSyncService _everythingFolderSyncService = new();

        // フォルダ再走査は単一実行に固定し、重複要求は後続1回へ圧縮する。
        private readonly SemaphoreSlim _checkFolderRunLock = new(1, 1);
        private readonly object _checkFolderRequestSync = new();
        private bool _hasPendingCheckFolderRequest;
        private CheckMode _pendingCheckFolderMode = CheckMode.Auto;

        // Everything連携の通知は監視中に一度だけ出し、同じ内容を繰り返し表示しない。
        private bool _hasShownEverythingModeNotice;
        private bool _hasShownEverythingFallbackNotice;

        // 「フォルダ監視中」通知も監視中は一度だけに抑制する。
        private bool _hasShownFolderMonitoringNotice;

        /// <summary>
        /// FileSystemWatcherから「新入りが来たぞ！」と報告が上がった時の出迎え処理だぜ！🎉
        /// </summary>
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
                        if (IsZeroByteMovieFile(e.FullPath, out long fileLength))
                        {
                            TryCreateErrorMarkerForSkippedMovie(
                                e.FullPath,
                                MainVM.DbInfo.CurrentTabIndex,
                                "zero-byte movie(created event)"
                            );
                            DebugRuntimeLog.Write(
                                "watch",
                                $"skip zero-byte movie on created event: '{e.FullPath}' size={fileLength}"
                            );
                            return;
                        }

                        // ----- [2] 基礎情報の取得とDB登録 -----
                        // OpenCV等を通じて尺やサイズを拾い、MovieCoreMapperなどを経由する前提の部分
                        MovieInfo mvi = new(e.FullPath);
                        _ = InsertMovieTable(MainVM.DbInfo.DBFullPath, mvi);

                        // [MVVM向けの課題] ここで直接ViewDataの更新メソッドを叩いている
                        DataTable dt = GetData(
                            MainVM.DbInfo.DBFullPath,
                            "select * from movie order by movie_id desc"
                        );
                        if (dt.Rows.Count > 0)
                        {
                            DataRowToViewData(dt.Rows[0]);
                        }

                        // ----- [3] サムネイル作成キューへ非同期投入 -----
                        QueueObj newFileForThumb = new()
                        {
                            MovieId = mvi.MovieId,
                            MovieFullPath = mvi.MoviePath,
                            Tabindex = MainVM.DbInfo.CurrentTabIndex,
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
            DebugRuntimeLog.TaskStart(nameof(CreateWatcher), $"db='{MainVM.DbInfo.DBFullPath}'");

            string sql = $"SELECT * FROM watch where watch = 1";
            GetWatchTable(MainVM.DbInfo.DBFullPath, sql);

            foreach (DataRow row in watchData.Rows)
            {
                //存在しない監視フォルダは読み飛ばし。
                if (!Path.Exists(row["dir"].ToString()))
                {
                    continue;
                }
                string checkFolder = row["dir"].ToString();
                bool sub = (long)row["sub"] == 1;

                RunWatcher(checkFolder, sub);
                watcherCount++;
            }

            sw.Stop();
            DebugRuntimeLog.TaskEnd(
                nameof(CreateWatcher),
                $"count={watcherCount} elapsed_ms={sw.ElapsedMilliseconds}"
            );
        }

        /// <summary>
        /// フォルダ更新要求をキューにブチ込む！連打されても後続1回に圧縮してPCの爆発を防ぐ超優秀な門番処理！🚧
        /// </summary>
        private Task QueueCheckFolderAsync(CheckMode mode, string trigger)
        {
            lock (_checkFolderRequestSync)
            {
                if (_hasPendingCheckFolderRequest)
                {
                    _pendingCheckFolderMode = MergeCheckMode(_pendingCheckFolderMode, mode);
                }
                else
                {
                    _pendingCheckFolderMode = mode;
                    _hasPendingCheckFolderRequest = true;
                }
            }

            DebugRuntimeLog.Write(
                "watch-check",
                $"scan request queued: mode={mode} trigger={trigger}"
            );
            return ProcessCheckFolderQueueAsync();
        }

        // 強いモード（Manual > Watch > Auto）を優先して圧縮する。
        private static CheckMode MergeCheckMode(CheckMode current, CheckMode incoming)
        {
            return GetCheckModePriority(incoming) > GetCheckModePriority(current)
                ? incoming
                : current;
        }

        private static int GetCheckModePriority(CheckMode mode)
        {
            return mode switch
            {
                CheckMode.Manual => 3,
                CheckMode.Watch => 2,
                _ => 1,
            };
        }

        // 単一ランナーでキューを消化し、同時実行を防ぐ。
        private async Task ProcessCheckFolderQueueAsync()
        {
            // 0ms待機だと、解放直前に入った要求を取りこぼす競合が起きるため、順番待ちで必ず取得する。
            await _checkFolderRunLock.WaitAsync();

            try
            {
                while (true)
                {
                    CheckMode modeToRun;
                    lock (_checkFolderRequestSync)
                    {
                        if (!_hasPendingCheckFolderRequest)
                        {
                            break;
                        }

                        modeToRun = _pendingCheckFolderMode;
                        _hasPendingCheckFolderRequest = false;
                    }

                    await CheckFolderAsync(modeToRun);
                }
            }
            finally
            {
                _checkFolderRunLock.Release();
            }
        }

        /// <summary>
        /// 起動時や手動更新で発動する「全フォルダ・ローラー作戦」！DBの知識と実際のファイルを突き合わせ、新顔だけを神速で迎え入れるぜ！（削除には気づかないお茶目仕様！）🛼✨
        /// </summary>
        private async Task CheckFolderAsync(CheckMode mode)
        {
            Stopwatch sw = Stopwatch.StartNew();
            bool FolderCheckflg = false;
            int checkedFolderCount = 0;
            int enqueuedCount = 0;
            string checkExt = Properties.Settings.Default.CheckExt;

            // 🔥 開始時のDB情報をスナップショット！途中でDB切り替えが起きても混入しない！🛡️
            string snapshotDbFullPath = MainVM.DbInfo.DBFullPath;
            string snapshotThumbFolder = MainVM.DbInfo.ThumbFolder;
            string snapshotDbName = MainVM.DbInfo.DBName;
            int snapshotTabIndex = MainVM.DbInfo.CurrentTabIndex;

            DebugRuntimeLog.TaskStart(
                nameof(CheckFolderAsync),
                $"mode={mode} db='{snapshotDbFullPath}'"
            );

            // 呼び出し元（OpenDatafile等UIスレッド）をすぐ返すため、最初に非同期コンテキストへ切り替える。
            await Task.Yield();

            var title = "フォルダ監視中";
            var Message = "";
            NotificationManager notificationManager = new();
            bool hasShownEverythingModeNotice = false;
            bool hasShownEverythingFallbackNotice = false;

            // ----- [1] 既存ファイル(サムネイル)の全量キャッシュ -----
            // スキャン中に都度DB検索したり全走査すると遅いため、予め HashSet(ハッシュテーブル) を作ってメモリに乗せておく。
            // DB上のパスではなく、出力フォルダにある「サムネイル画像(.jpg)のファイル名本体」を取得する。
            HashSet<string> existingThumbBodies = await Task.Run(() =>
                BuildExistingThumbnailBodySet(snapshotThumbFolder)
            );

            // モードに応じた監視設定の取得（自動更新対象のみか、全対象か）
            string sql = mode switch
            {
                CheckMode.Auto => $"SELECT * FROM watch where auto = 1",
                CheckMode.Watch => $"SELECT * FROM watch where watch = 1",
                _ => $"SELECT * FROM watch",
            };
            GetWatchTable(snapshotDbFullPath, sql);

            // DB上の監視フォルダ定義1行ずつ検証していく
            foreach (DataRow row in watchData.Rows)
            {
                // 🔥 DB切り替え検知ガード！途中で別DBに切り替わったら即打ち切り！🛡️
                if (
                    !string.Equals(
                        MainVM.DbInfo.DBFullPath,
                        snapshotDbFullPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"abort scan: db switched from '{snapshotDbFullPath}' to '{MainVM.DbInfo.DBFullPath}'"
                    );
                    return;
                }

                //存在しない監視フォルダは読み飛ばし。
                if (!Path.Exists(row["dir"].ToString()))
                {
                    continue;
                }
                string checkFolder = row["dir"].ToString();
                checkedFolderCount++;
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan start: folder='{checkFolder}' mode={mode}"
                );

                // 1フォルダ単位で検知した分を積み、走査が終わったら（あるいは規定バッチ数で）即キュー投入するバッファ。
                List<QueueObj> addFilesByFolder = [];
                int addedByFolderCount = 0;
                bool useIncrementalUiMode = false;
                long scanBackgroundElapsedMs = 0;
                long movieInfoTotalMs = 0;
                long dbInsertTotalMs = 0;
                long uiReflectTotalMs = 0;
                long enqueueFlushTotalMs = 0;

                // Win10側の通知（トースト）領域へプログレスを出す
                if (!_hasShownFolderMonitoringNotice)
                {
                    notificationManager.Show(
                        title,
                        $"{checkFolder} 監視実施中…",
                        NotificationType.Notification,
                        "ProgressArea"
                    );
                    _hasShownFolderMonitoringNotice = true;
                }

                bool sub = ((long)row["sub"] == 1);

                try
                {
                    // ----- [2] 実際のフォルダ階層なめ (IOバウンド) を並列逃がし -----
                    // 重いファイル走査はUIスレッドを塞がないよう Task.Run(バックグラウンドスレッド) 上で実行する。
                    Stopwatch scanBackgroundStopwatch = Stopwatch.StartNew();
                    FolderScanWithStrategyResult scanStrategyResult = await Task.Run(() =>
                        ScanFolderWithStrategyInBackground(
                            mode,
                            checkFolder,
                            sub,
                            checkExt,
                            existingThumbBodies
                        )
                    );
                    FolderScanResult scanResult = scanStrategyResult.ScanResult;
                    scanBackgroundStopwatch.Stop();
                    scanBackgroundElapsedMs = scanBackgroundStopwatch.ElapsedMilliseconds;
                    (string strategyDetailCode, string strategyDetailMessage) =
                        DescribeEverythingDetail(scanStrategyResult.Detail);
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan strategy: folder='{checkFolder}' strategy={scanStrategyResult.Strategy} detail_code={strategyDetailCode} detail_message={strategyDetailMessage} scanned={scanResult.ScannedCount}"
                    );

                    if (
                        scanStrategyResult.Strategy == "everything"
                        && !_hasShownEverythingModeNotice
                    )
                    {
                        notificationManager.Show(
                            "Everything連携",
                            "Everything連携で高速スキャンを実行中です。",
                            NotificationType.Notification,
                            "ProgressArea"
                        );
                        _hasShownEverythingModeNotice = true;
                    }
                    else if (
                        scanStrategyResult.Strategy == "filesystem"
                        && _everythingFolderSyncService.IsIntegrationConfigured()
                        && !_hasShownEverythingFallbackNotice
                    )
                    {
                        notificationManager.Show(
                            "Everything連携",
                            $"Everything連携を利用できないため通常監視で継続します。({strategyDetailMessage})",
                            NotificationType.Information,
                            "ProgressArea"
                        );
                        _hasShownEverythingFallbackNotice = true;
                    }

                    useIncrementalUiMode =
                        scanResult.NewMoviePaths.Count <= IncrementalUiUpdateThreshold;
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan mode: folder='{checkFolder}' new={scanResult.NewMoviePaths.Count} mode={(useIncrementalUiMode ? "small" : "bulk")} threshold={IncrementalUiUpdateThreshold}"
                    );

                    bool IsHit = false;
                    TabInfo tbi = new(snapshotTabIndex, snapshotDbName, snapshotThumbFolder);

                    // ----- [3] 見つかった「新規ファイル」だけに対する処理 -----
                    foreach (string movieFullPath in scanResult.NewMoviePaths)
                    {
                        if (!IsHit)
                        {
                            Message = checkFolder;
                            if (!_hasShownFolderMonitoringNotice)
                            {
                                notificationManager.Show(
                                    title,
                                    $"{Message}に更新あり。",
                                    NotificationType.Notification,
                                    "ProgressArea"
                                );
                                _hasShownFolderMonitoringNotice = true;
                            }
                            IsHit = true;
                        }
                        if (IsZeroByteMovieFile(movieFullPath, out long zeroFileLength))
                        {
                            TryCreateErrorMarkerForSkippedMovie(
                                movieFullPath,
                                snapshotTabIndex,
                                "zero-byte movie(folder scan)"
                            );
                            DebugRuntimeLog.Write(
                                "watch-check",
                                $"skip zero-byte movie before queue: '{movieFullPath}' size={zeroFileLength}"
                            );
                            continue;
                        }

                        // ----- 🚨DBの能力全開ガード（ハイブリッド方式）🚨 -----
                        // DBに同じパスが既に存在するかをピンポイントで確認し、MediaInfo解析前に重い処理を回避する！
                        string escapedMoviePath = movieFullPath.Replace("'", "''");
                        DataTable dtCheck = GetData(
                            snapshotDbFullPath,
                            $"select movie_id, hash, title from movie where movie_path = '{escapedMoviePath}' order by movie_id desc limit 1"
                        );

                        bool existsInDb = dtCheck?.Rows.Count > 0;
                        long currentMovieId = 0;
                        string currentHash = "";

                        Stopwatch stepStopwatch = Stopwatch.StartNew();

                        if (!existsInDb)
                        {
                            // 動画解析は重いためUIスレッドから外し、固まりを避ける。
                            MovieInfo mvi = await Task.Run(() => new MovieInfo(movieFullPath));
                            stepStopwatch.Stop();
                            movieInfoTotalMs += stepStopwatch.ElapsedMilliseconds;

                            // 本当の新規動画（DB未登録）の場合は即座に登録（INSERT）
                            stepStopwatch.Restart();
                            await Task.Run(() =>
                                InsertMovieTable(snapshotDbFullPath, mvi).GetAwaiter().GetResult()
                            );
                            stepStopwatch.Stop();
                            dbInsertTotalMs += stepStopwatch.ElapsedMilliseconds;

                            FolderCheckflg = true;
                            currentMovieId = mvi.MovieId;
                            currentHash = mvi.Hash;

                            // 小規模時は1件ずつUIへ反映し、追加の体感を優先する。
                            if (useIncrementalUiMode)
                            {
                                stepStopwatch.Restart();
                                TryAppendMovieToViewByPath(snapshotDbFullPath, mvi.MoviePath);
                                stepStopwatch.Stop();
                                uiReflectTotalMs += stepStopwatch.ElapsedMilliseconds;
                            }
                        }
                        else
                        {
                            // 既にDBにはある！（＝サムネイルだけがエラーか欠損で無い状態）
                            // DBINSERTはスキップしてキューに流すための情報を組み立てる
                            DebugRuntimeLog.Write(
                                "watch-check",
                                $"skip db insert (already exists in db): '{movieFullPath}'"
                            );
                            if (
                                long.TryParse(
                                    dtCheck.Rows[0]["movie_id"]?.ToString(),
                                    out long existingId
                                )
                            )
                            {
                                currentMovieId = existingId;
                            }
                            currentHash = dtCheck.Rows[0]["hash"]?.ToString() ?? "";
                        }

                        var fileBody = Path.GetFileNameWithoutExtension(movieFullPath);
                        if (!string.IsNullOrWhiteSpace(fileBody))
                        {
                            existingThumbBodies.Add(fileBody);
                        }

                        // 結合したサムネイルのファイル名作成（存在チェック用）
                        var saveThumbFileName = Path.Combine(
                            tbi.OutPath,
                            $"{fileBody}.#{currentHash}.jpg"
                        );

                        // 既にサムネ画像が存在しているなら作成処理はスキップ
                        if (Path.Exists(saveThumbFileName))
                        {
                            DebugRuntimeLog.Write(
                                "watch-check",
                                $"skip enqueue by existing-tab-thumb: tab={snapshotTabIndex}, movie='{movieFullPath}'"
                            );
                            continue;
                        }

                        DebugRuntimeLog.Write(
                            "watch-check",
                            $"enqueue by missing-tab-thumb: tab={snapshotTabIndex}, movie='{movieFullPath}'"
                        );

                        // サムネイル作成キュー用のオブジェクトを用意してバッファのリストへ積む
                        QueueObj temp = new()
                        {
                            MovieId = currentMovieId,
                            MovieFullPath = movieFullPath,
                            Tabindex = snapshotTabIndex,
                        };
                        addFilesByFolder.Add(temp);
                        addedByFolderCount++;

                        // 小規模時は1件ずつ即投入する。大規模時は100件単位で先行投入する。
                        if (useIncrementalUiMode)
                        {
                            stepStopwatch.Restart();
                            FlushPendingQueueItems(addFilesByFolder, checkFolder);
                            stepStopwatch.Stop();
                            enqueueFlushTotalMs += stepStopwatch.ElapsedMilliseconds;
                        }
                        else if (addFilesByFolder.Count >= FolderScanEnqueueBatchSize)
                        {
                            stepStopwatch.Restart();
                            FlushPendingQueueItems(addFilesByFolder, checkFolder);
                            stepStopwatch.Stop();
                            enqueueFlushTotalMs += stepStopwatch.ElapsedMilliseconds;
                        }
                    }

                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan file summary: folder='{checkFolder}' scanned={scanResult.ScannedCount} new={scanResult.NewMoviePaths.Count}"
                    );
                }
                catch (Exception e)
                {
                    //起動中に監視フォルダにファイルコピーされっと例外発生するんよね。
                    if (e.GetType() == typeof(IOException))
                    {
                        await Task.Delay(1000);
                    }
                }

                // ----- [4] バッファの残りを全てキューに流す -----
                // 100件未満の端数を最後に流し切る。
                Stopwatch finalFlushStopwatch = Stopwatch.StartNew();
                FlushPendingQueueItems(addFilesByFolder, checkFolder);
                finalFlushStopwatch.Stop();
                enqueueFlushTotalMs += finalFlushStopwatch.ElapsedMilliseconds;
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan end: folder='{checkFolder}' added={addedByFolderCount} "
                        + $"mode={(useIncrementalUiMode ? "small" : "bulk")} "
                        + $"scan_bg_ms={scanBackgroundElapsedMs} movieinfo_ms={movieInfoTotalMs} "
                        + $"db_insert_ms={dbInsertTotalMs} ui_reflect_ms={uiReflectTotalMs} "
                        + $"enqueue_flush_ms={enqueueFlushTotalMs}"
                );
                await Task.Delay(100);
            }

            //stack : ファイル名を外部から変更したときに、エクステンションのファイル名が追従してなかった。強制チェックで反応はした。
            //再クリックで表示はリロードしたので、内部は変わってる。リフレッシュも漏れてる可能性あり。
            //と言うかですね。これは外部からのリネームでも、アプリでのリネームでも同じで。クリックすりゃ反映する（そりゃそうだ）

            // ----- [5] 走査全体を通していずれかのフォルダで変化があったらUI一覧を再描画 -----
            if (FolderCheckflg)
            {
                FilterAndSort(MainVM.DbInfo.Sort, true); //チェックフォルダ時。監視対象があった場合の処理やな。
            }

            sw.Stop();
            DebugRuntimeLog.TaskEnd(
                nameof(CheckFolderAsync),
                $"mode={mode} folders={checkedFolderCount} enqueued={enqueuedCount} updated={FolderCheckflg} elapsed_ms={sw.ElapsedMilliseconds}"
            );
        }

        // 100件たまるごとにサムネイルキューへ流すための共通処理。
        private void FlushPendingQueueItems(List<QueueObj> pendingItems, string folderPath)
        {
            if (pendingItems.Count < 1)
            {
                return;
            }

            int flushedCount = 0;
            foreach (QueueObj pending in pendingItems)
            {
                if (TryEnqueueThumbnailJob(pending))
                {
                    flushedCount++;
                }
            }
            DebugRuntimeLog.Write(
                "watch-check",
                $"enqueue batch: folder='{folderPath}' requested={pendingItems.Count} flushed={flushedCount}"
            );
            pendingItems.Clear();
        }

        // 全件再読込せず、対象パス1件だけDBから引いてUIへ反映する。
        private void TryAppendMovieToViewByPath(string snapshotDbFullPath, string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return;
            }

            string escapedMoviePath = moviePath.Replace("'", "''");
            DataTable dt = GetData(
                snapshotDbFullPath,
                $"select * from movie where movie_path = '{escapedMoviePath}' order by movie_id desc limit 1"
            );
            if (dt?.Rows.Count > 0)
            {
                DataRowToViewData(dt.Rows[0]);
            }
        }

        /// <summary>
        /// 出力済みのサムネイル達を探索し、ハッシュを削ぎ落とした「名前（Body）」だけの最強セットを練り上げる！Everythingの爆速力を借り、ダメなら己の足（Directory走査）で稼ぐ！👟💨
        /// </summary>
        private HashSet<string> BuildExistingThumbnailBodySet(string thumbFolder)
        {
            if (string.IsNullOrWhiteSpace(thumbFolder) || !Directory.Exists(thumbFolder))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            // [ルート1] Everythingの爆速照会
            if (
                _everythingFolderSyncService.TryCollectThumbnailBodies(
                    thumbFolder,
                    out HashSet<string> existingFromEverything,
                    out string reason
                )
            )
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"BuildExistingThumbnailBodySet: use everything (reason={reason}, count={existingFromEverything.Count})"
                );
                return existingFromEverything;
            }

            // [ルート2] フォールバック (Directory走査)
            DebugRuntimeLog.Write(
                "watch-check",
                $"BuildExistingThumbnailBodySet: fallback to filesystem (reason={reason})"
            );
            HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                IEnumerable<string> files = Directory.EnumerateFiles(thumbFolder, "*.jpg");
                foreach (string fullPath in files)
                {
                    string fileName = Path.GetFileName(fullPath);
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        continue;
                    }

                    string body = ExtractThumbnailBody(fileName);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        existing.Add(body);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"BuildExistingThumbnailBodySet: fallback failed: {ex.GetType().Name}"
                );
            }

            return existing;
        }

        // "{body}.#{hash}.jpg" 等から "{body}" だけを取り出す。
        private static string ExtractThumbnailBody(string fileName)
        {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(nameWithoutExt))
            {
                return "";
            }
            int hashMarkerIndex = nameWithoutExt.LastIndexOf(
                ".#",
                StringComparison.OrdinalIgnoreCase
            );
            if (hashMarkerIndex >= 0)
            {
                return nameWithoutExt[..hashMarkerIndex];
            }
            return nameWithoutExt;
        }

        // Everything高速経路を使う対象かを判定する（ローカル固定ドライブ + NTFSのみ）。
        // NAS/UNC、リムーバブル、非NTFSは既存経路へ寄せる。
        private static bool IsEverythingEligiblePath(string watchFolder, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(watchFolder))
            {
                reason = "empty_path";
                return false;
            }

            try
            {
                string normalized = Path.GetFullPath(watchFolder);
                if (normalized.StartsWith(@"\\"))
                {
                    reason = "unc_path";
                    return false;
                }

                string root = Path.GetPathRoot(normalized) ?? "";
                if (string.IsNullOrWhiteSpace(root))
                {
                    reason = "no_root";
                    return false;
                }

                DriveInfo drive = new(root);
                if (drive.DriveType != DriveType.Fixed)
                {
                    reason = $"drive_type_{drive.DriveType}";
                    return false;
                }

                if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"drive_format_{drive.DriveFormat}";
                    return false;
                }

                reason = "ok";
                return true;
            }
            catch (Exception ex)
            {
                reason = $"eligibility_error:{ex.GetType().Name}";
                return false;
            }
        }

        // 監視フォルダごとの増分同期基準時刻をsystemテーブルから読む。
        private DateTime? LoadEverythingLastSyncUtc(string watchFolder, bool sub)
        {
            if (string.IsNullOrWhiteSpace(MainVM.DbInfo.DBFullPath))
            {
                return null;
            }

            try
            {
                string attr = BuildEverythingLastSyncAttr(watchFolder, sub);
                string escapedAttr = attr.Replace("'", "''");
                DataTable dt = GetData(
                    MainVM.DbInfo.DBFullPath,
                    $"select value from system where attr = '{escapedAttr}' limit 1"
                );
                if (dt?.Rows.Count < 1)
                {
                    return null;
                }

                string raw = dt.Rows[0]["value"]?.ToString() ?? "";
                if (
                    DateTime.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out DateTime parsedUtc
                    )
                )
                {
                    return parsedUtc;
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"load last_sync failed: folder='{watchFolder}' reason={ex.GetType().Name}"
                );
            }

            return null;
        }

        // 増分同期基準時刻をsystemテーブルへ保存する。
        private void SaveEverythingLastSyncUtc(string watchFolder, bool sub, DateTime lastSyncUtc)
        {
            if (string.IsNullOrWhiteSpace(MainVM.DbInfo.DBFullPath))
            {
                return;
            }

            try
            {
                string attr = BuildEverythingLastSyncAttr(watchFolder, sub);
                string normalizedUtc = lastSyncUtc
                    .ToUniversalTime()
                    .ToString("O", CultureInfo.InvariantCulture);
                UpsertSystemTable(MainVM.DbInfo.DBFullPath, attr, normalizedUtc);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"save last_sync failed: folder='{watchFolder}' reason={ex.GetType().Name}"
                );
            }
        }

        private static string BuildEverythingLastSyncAttr(string watchFolder, bool sub)
        {
            string normalized = Path.GetFullPath(watchFolder).Trim().ToLowerInvariant();
            string material = $"{normalized}|sub={(sub ? 1 : 0)}";
            byte[] bytes = Encoding.UTF8.GetBytes(material);
            byte[] hash = SHA256.HashData(bytes);
            string hex = Convert.ToHexString(hash).ToLowerInvariant();
            return $"{EverythingLastSyncAttrPrefix}{hex[..16]}";
        }

        // Everything連携の詳細コードを、ログとUI通知で同じ解釈に統一する。
        private static (string Code, string Message) DescribeEverythingDetail(string detail)
        {
            string safeDetail = string.IsNullOrWhiteSpace(detail) ? "unknown" : detail;
            if (safeDetail.StartsWith("path_not_eligible:", StringComparison.OrdinalIgnoreCase))
            {
                string rawReason = safeDetail["path_not_eligible:".Length..];
                string message = rawReason switch
                {
                    "empty_path" => "監視フォルダが未設定です",
                    "unc_path" => "UNC/NASパスはEverything高速経路の対象外です",
                    "no_root" => "ドライブ情報を解決できません",
                    "ok" => "対象フォルダ判定は正常です",
                    _ when rawReason.StartsWith(
                            "drive_type_",
                            StringComparison.OrdinalIgnoreCase
                        ) => $"ローカル固定ドライブ以外のため対象外です ({rawReason})",
                    _ when rawReason.StartsWith(
                            "drive_format_",
                            StringComparison.OrdinalIgnoreCase
                        ) => $"NTFS以外のため対象外です ({rawReason})",
                    _ when rawReason.StartsWith(
                            "eligibility_error:",
                            StringComparison.OrdinalIgnoreCase
                        ) => $"対象判定で例外が発生しました ({rawReason})",
                    _ => $"対象外です ({rawReason})",
                };
                return (safeDetail, message);
            }

            if (safeDetail.Equals("setting_disabled", StringComparison.OrdinalIgnoreCase))
            {
                return (safeDetail, "設定でEverything連携が無効です");
            }

            if (safeDetail.Equals("everything_not_available", StringComparison.OrdinalIgnoreCase))
            {
                return (safeDetail, "Everythingが起動していないかIPC接続できません");
            }
            if (safeDetail.Equals("auto_not_available", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    safeDetail,
                    "AUTO設定中ですがEverythingが見つからないため通常監視で動作します"
                );
            }

            if (
                safeDetail.StartsWith(
                    "everything_result_truncated:",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (safeDetail, "検索結果が上限件数に達したため通常監視へ切り替えます");
            }

            if (
                safeDetail.StartsWith("availability_error:", StringComparison.OrdinalIgnoreCase)
                || safeDetail.StartsWith(
                    "everything_query_error:",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (safeDetail, $"Everything連携で例外が発生しました ({safeDetail})");
            }

            if (safeDetail.StartsWith("ok:", StringComparison.OrdinalIgnoreCase))
            {
                return (safeDetail, "Everything連携で候補収集に成功しました");
            }

            return (safeDetail, $"不明な理由のため通常監視へ切り替えます ({safeDetail})");
        }

        /// <summary>
        /// Everything連携を優先して走査し、利用不可時は既存のファイルシステム走査へフォールバックする。
        /// 返却時点で既存パスセットへの反映（重複除外）まで完了させる。
        /// </summary>
        private FolderScanWithStrategyResult ScanFolderWithStrategyInBackground(
            CheckMode mode,
            string checkFolder,
            bool sub,
            string checkExt,
            HashSet<string> existingThumbBodies
        )
        {
            if (!IsEverythingEligiblePath(checkFolder, out string eligibilityReason))
            {
                FolderScanResult notEligibleFallback = ScanFolderInBackground(
                    checkFolder,
                    sub,
                    checkExt,
                    existingThumbBodies
                );
                return new FolderScanWithStrategyResult(
                    notEligibleFallback,
                    "filesystem",
                    $"path_not_eligible:{eligibilityReason}"
                );
            }

            DateTime? changedSinceUtc =
                mode == CheckMode.Watch ? LoadEverythingLastSyncUtc(checkFolder, sub) : null;
            bool usedEverything = _everythingFolderSyncService.TryCollectMoviePaths(
                checkFolder,
                sub,
                checkExt,
                changedSinceUtc,
                out List<string> candidatePaths,
                out DateTime? maxObservedChangedUtc,
                out string reason
            );

            if (usedEverything)
            {
                List<string> newMoviePaths = [];
                int scannedCount = 0;
                foreach (string fullPath in candidatePaths)
                {
                    scannedCount++;

                    // DBパスではなく、「画像ファイルの本体名（Body）」で空文字チェックのみ行う
                    // (タブ欠損サムネ再生成の回帰を避けるため existingThumbBodies による事前除外を撤去)
                    string fileBody = Path.GetFileNameWithoutExtension(fullPath);
                    if (string.IsNullOrWhiteSpace(fileBody))
                    {
                        continue;
                    }

                    newMoviePaths.Add(fullPath);
                }

                // 取りこぼしを避けるため、問い合わせ時刻ではなく「観測できた変更時刻の高水位」を保存する。
                DateTime? nextSyncUtc = maxObservedChangedUtc;
                if (
                    nextSyncUtc.HasValue
                    && (!changedSinceUtc.HasValue || nextSyncUtc.Value > changedSinceUtc.Value)
                )
                {
                    SaveEverythingLastSyncUtc(checkFolder, sub, nextSyncUtc.Value);
                }
                return new FolderScanWithStrategyResult(
                    new FolderScanResult(scannedCount, newMoviePaths),
                    "everything",
                    reason
                );
            }

            FolderScanResult fallbackResult = ScanFolderInBackground(
                checkFolder,
                sub,
                checkExt,
                existingThumbBodies
            );
            return new FolderScanWithStrategyResult(fallbackResult, "filesystem", reason);
        }

        /// <summary>
        /// 監視フォルダの重い直列走査を担う静的メソッド。
        /// Task.Run経由でバックグラウンドスレッドで実行される。メモリ上に構築されたHashSetと突き合わせ、「新顔」だけを返す。
        /// </summary>
        private static FolderScanResult ScanFolderInBackground(
            string checkFolder,
            bool sub,
            string checkExt,
            HashSet<string> existingThumbBodies
        )
        {
            List<string> newMoviePaths = [];
            int scannedCount = 0;
            DirectoryInfo di = new(checkFolder);
            EnumerationOptions enumOption = new() { RecurseSubdirectories = sub };

            string[] filters = checkExt.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawFilter in filters)
            {
                string filter = rawFilter.Trim();
                if (string.IsNullOrWhiteSpace(filter))
                {
                    continue;
                }

                IEnumerable<FileInfo> files;
                try
                {
                    files = di.EnumerateFiles(filter, enumOption);
                }
                catch
                {
                    // アクセス権なし等の場合はパターン単位で失敗しても、他の拡張子走査(次のループ)は継続する。
                    continue;
                }

                foreach (FileInfo file in files)
                {
                    scannedCount++;
                    string fullPath = file.FullName;

                    // DEBUG時の1ファイル単位ログは出力量が多く、走査を著しく遅くするため停止。
                    // 必要なときは次の1行コメントを外して再度有効化する。
                    // DebugRuntimeLog.Write("watch-check-file", $"scan file: '{fullPath}'");

                    // 空文字チェックのみ行う
                    // (タブ欠損サムネ再生成のため existingThumbBodies による重複除外を撤去)
                    string fileBody = Path.GetFileNameWithoutExtension(fullPath);
                    if (string.IsNullOrWhiteSpace(fileBody))
                    {
                        continue;
                    }

                    newMoviePaths.Add(fullPath);
                }
            }

            // 新顔と、走査したファイル総数などの情報をDTOに詰めて戻す。
            return new FolderScanResult(scannedCount, newMoviePaths);
        }

        /// <summary>
        /// フォルダ走査結果の情報をひとまとめにして返すための軽量DTO(Data Transfer Object)。
        /// ScanFolderInBackgroundから呼び出し元のCheckFolderAsyncへ結果を受け渡す際に使われる。
        /// </summary>
        private sealed class FolderScanWithStrategyResult
        {
            public FolderScanWithStrategyResult(
                FolderScanResult scanResult,
                string strategy,
                string detail
            )
            {
                ScanResult = scanResult;
                Strategy = strategy;
                Detail = detail;
            }

            public FolderScanResult ScanResult { get; }
            public string Strategy { get; }
            public string Detail { get; }
        }

        /// <summary>
        /// フォルダ走査結果の情報をひとまとめにして返すための軽量DTO(Data Transfer Object)。
        /// ScanFolderInBackgroundから呼び出し元のCheckFolderAsyncへ結果を受け渡す際に使われる。
        /// </summary>
        private sealed class FolderScanResult
        {
            public FolderScanResult(int scannedCount, List<string> newMoviePaths)
            {
                ScannedCount = scannedCount;
                NewMoviePaths = newMoviePaths;
            }

            public int ScannedCount { get; }
            public List<string> NewMoviePaths { get; }
        }

        /// <summary>
        /// 既存のDBに存在する動画のうち、指定タブ（例：Tab=2）のサムネイルが欠損しているものを探し出し、
        /// 再度サムネイル生成キューへ投入するワンショット救済処理！これで抜け漏れも安心だぜ！🚀
        /// </summary>
        public async Task EnqueueMissingThumbnailsAsync(
            int targetTabIndex,
            string snapshotDbFullPath,
            string snapshotDbName,
            string snapshotThumbFolder
        )
        {
            if (string.IsNullOrWhiteSpace(snapshotDbFullPath))
                return;

            // 1. そのタブの設定情報を構築（出力先フォルダなどを得るため）
            TabInfo tbi = new(targetTabIndex, snapshotDbName, snapshotThumbFolder);
            if (string.IsNullOrWhiteSpace(tbi.OutPath))
                return;

            // 2. DBから全ての動画(movie_id, movie_path, hash)を引く
            DataTable dt = GetData(
                snapshotDbFullPath,
                "SELECT movie_id, movie_path, hash FROM movie ORDER BY movie_id DESC"
            );
            if (dt == null || dt.Rows.Count == 0)
                return;

            int enqueuedCount = 0;
            List<QueueObj> batch = [];

            DebugRuntimeLog.Write(
                "rescue-thumb",
                $"start rescue missing thumbs for tab={targetTabIndex}. total docs={dt.Rows.Count}"
            );

            foreach (DataRow row in dt.Rows)
            {
                long.TryParse(row["movie_id"]?.ToString(), out long movieId);
                string path = row["movie_path"]?.ToString() ?? "";
                string hash = row["hash"]?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(path))
                    continue;

                string fileBody = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(fileBody))
                    continue;

                // 対象タブの予想サムネイルパス
                string expectedThumbPath = Path.Combine(tbi.OutPath, $"{fileBody}.#{hash}.jpg");

                if (!Path.Exists(expectedThumbPath))
                {
                    // 動画ファイル自体が存在するかどうかも軽くチェック（削除済みの動画ならキューしない）
                    if (Path.Exists(path))
                    {
                        batch.Add(
                            new QueueObj
                            {
                                MovieId = movieId,
                                MovieFullPath = path,
                                Tabindex = targetTabIndex,
                            }
                        );

                        enqueuedCount++;
                        DebugRuntimeLog.Write(
                            "rescue-thumb",
                            $"enqueue by rescue: tab={targetTabIndex}, movie='{path}'"
                        );

                        // 100件単位でキューへ放り投げてUIスレッドの一時的な固まりを防ぐ！
                        if (batch.Count >= FolderScanEnqueueBatchSize)
                        {
                            FlushPendingQueueItems(batch, "RescueMissingThumbnails");
                            await Task.Delay(50); // 少し息継ぎ
                        }
                    }
                }
            }

            // 残りを流し込む
            if (batch.Count > 0)
            {
                FlushPendingQueueItems(batch, "RescueMissingThumbnails");
            }

            DebugRuntimeLog.Write(
                "rescue-thumb",
                $"finished rescue missing thumbs for tab={targetTabIndex}. enqueued={enqueuedCount}"
            );
        }
    }
}
