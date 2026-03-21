using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using IndigoMovieManager.Data;
using IndigoMovieManager.ViewModels;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
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
        // 仮表示は無制限に積まず、最新100件までを保持する。
        private const int PendingMovieUiKeepLimit = 100;
        private const string EverythingLastSyncAttrPrefix = "everything_last_sync_utc_";
        // 重い1件の原因切り分け用。対象動画IDを含むパスは常に詳細トレースする。
        private const string WatchCheckProbeMovieIdentity = "MH922SNIgTs_gggggggggg.mkv";
        // 対象外でも、1件処理が閾値を超えたら詳細トレースする。
        private const long WatchCheckProbeSlowThresholdMs = 120;
        // 欠損サムネ救済は重い全件確認になるため、DB+タブ単位で最小間隔を設ける。
        private static readonly TimeSpan MissingThumbnailRescueMinInterval = TimeSpan.FromSeconds(60);
        // Watch差分0件が続く時でも、低頻度で実フォルダとDBを再突合する。
        private static readonly TimeSpan WatchFolderFullReconcileMinInterval =
            TimeSpan.FromSeconds(60);
        // backlog が大きい時は、今見えている動画だけへ watch の仕事を絞ってUIテンポを守る。
        private const int WatchVisibleOnlyQueueThreshold = 500;

        // 自動監視中は通常キューを優先し、手動実行時は欠損救済を優先する。
        internal static bool ShouldSkipMissingThumbnailRescueForBusyQueue(
            bool isManualRequest,
            int activeCount,
            int busyThreshold
        )
        {
            return !isManualRequest && activeCount >= busyThreshold;
        }

        // Watch由来の欠損救済は通常キュー完走を優先し、アイドル時だけ許可する。
        internal static int ResolveMissingThumbnailRescueBusyThreshold(
            bool isWatchRequest,
            int defaultBusyThreshold
        )
        {
            return isWatchRequest ? 1 : Math.Max(1, defaultBusyThreshold);
        }

        // watch起点の通常サムネ自動投入は、実サムネを持つ上側タブ(0..4)だけへ限定する。
        internal static int? ResolveWatchMissingThumbnailTabIndex(int currentTabIndex)
        {
            return IsUpperThumbnailTabIndex(currentTabIndex) ? currentTabIndex : null;
        }

        internal enum MissingThumbnailAutoEnqueueBlockReason
        {
            None = 0,
            ErrorMarkerExists = 1,
            OpenRescueRequestExists = 2,
        }

        // 欠損サムネの自動再投入は、失敗マーカーか救済待ちが残っている間は止める。
        internal static MissingThumbnailAutoEnqueueBlockReason ResolveMissingThumbnailAutoEnqueueBlockReason(
            string movieFullPath,
            int tabIndex,
            HashSet<string> existingThumbnailFileNames,
            HashSet<string> openRescueRequestKeys
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return MissingThumbnailAutoEnqueueBlockReason.None;
            }

            string errorMarkerFileName = ThumbnailPathResolver.BuildErrorMarkerFileName(
                movieFullPath
            );
            if (
                existingThumbnailFileNames != null
                && !string.IsNullOrWhiteSpace(errorMarkerFileName)
                && existingThumbnailFileNames.Contains(errorMarkerFileName)
            )
            {
                return MissingThumbnailAutoEnqueueBlockReason.ErrorMarkerExists;
            }

            if (openRescueRequestKeys == null || openRescueRequestKeys.Count < 1)
            {
                return MissingThumbnailAutoEnqueueBlockReason.None;
            }

            string rescueRequestKey = BuildMissingThumbnailRescueBlockKey(movieFullPath, tabIndex);
            if (
                !string.IsNullOrWhiteSpace(rescueRequestKey)
                && openRescueRequestKeys.Contains(rescueRequestKey)
            )
            {
                return MissingThumbnailAutoEnqueueBlockReason.OpenRescueRequestExists;
            }

            return MissingThumbnailAutoEnqueueBlockReason.None;
        }

        // Watcher側でも moviePathKey + tab で揃え、FailureDb の open rescue 集合と突き合わせる。
        private static string BuildMissingThumbnailRescueBlockKey(string movieFullPath, int tabIndex)
        {
            string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(movieFullPath);
            if (string.IsNullOrWhiteSpace(moviePathKey))
            {
                return "";
            }

            return $"{moviePathKey}|{tabIndex}";
        }

        private static string DescribeMissingThumbnailAutoEnqueueBlockReason(
            MissingThumbnailAutoEnqueueBlockReason reason
        )
        {
            return reason switch
            {
                MissingThumbnailAutoEnqueueBlockReason.ErrorMarkerExists => "error-marker",
                MissingThumbnailAutoEnqueueBlockReason.OpenRescueRequestExists => "failuredb-open-rescue",
                _ => "",
            };
        }

        // WatchのEverything差分で0件だった時だけ、低頻度の全量再突合を許可する。
        internal static bool ShouldRunWatchFolderFullReconcile(
            bool isWatchMode,
            string strategy,
            int newMovieCount
        )
        {
            return isWatchMode
                && newMovieCount < 1
                && string.Equals(
                    strategy,
                    FileIndexStrategies.Everything,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        // backlog が閾値以上の watch 時だけ、現在表示中の visible 動画へ探索を絞る。
        internal static bool ShouldRestrictWatchWorkToVisibleMovies(
            bool isWatchMode,
            int activeQueueCount,
            int threshold,
            int currentTabIndex,
            int visibleMovieCount
        )
        {
            return isWatchMode
                && IsUpperThumbnailTabIndex(currentTabIndex)
                && visibleMovieCount > 0
                && activeQueueCount >= threshold;
        }

        // visible-only 中は、今画面に見えていない動画の追加処理と自動enqueueを止める。
        internal static bool ShouldSkipWatchWorkByVisibleMovieGate(
            bool restrictToVisibleMovies,
            ISet<string> visibleMoviePaths,
            string movieFullPath
        )
        {
            if (!restrictToVisibleMovies || string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            return visibleMoviePaths == null || !visibleMoviePaths.Contains(movieFullPath);
        }

        // visible-only 中は、画面内動画が1本も無い監視フォルダを丸ごと走査しない。
        internal static bool ShouldSkipWatchFolderByVisibleMovieGate(
            bool restrictToVisibleMovies,
            ISet<string> visibleMoviePaths,
            string watchFolder,
            bool includeSubfolders
        )
        {
            if (!restrictToVisibleMovies)
            {
                return false;
            }

            if (visibleMoviePaths == null || visibleMoviePaths.Count < 1)
            {
                return true;
            }

            foreach (string movieFullPath in visibleMoviePaths)
            {
                if (IsMoviePathInsideWatchFolder(movieFullPath, watchFolder, includeSubfolders))
                {
                    return false;
                }
            }

            return true;
        }

        // サブフォルダ監視の有無を含め、visible 動画が対象 watch フォルダ配下かを判定する。
        internal static bool IsMoviePathInsideWatchFolder(
            string movieFullPath,
            string watchFolder,
            bool includeSubfolders
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath) || string.IsNullOrWhiteSpace(watchFolder))
            {
                return false;
            }

            try
            {
                string movieDirectory = Path.GetDirectoryName(movieFullPath) ?? "";
                if (string.IsNullOrWhiteSpace(movieDirectory))
                {
                    return false;
                }

                string normalizedWatchFolder = NormalizeDirectoryPathForComparison(watchFolder);
                string normalizedMovieDirectory = NormalizeDirectoryPathForComparison(movieDirectory);
                if (string.IsNullOrWhiteSpace(normalizedWatchFolder))
                {
                    return false;
                }

                if (!includeSubfolders)
                {
                    return string.Equals(
                        normalizedMovieDirectory,
                        normalizedWatchFolder,
                        StringComparison.OrdinalIgnoreCase
                    );
                }

                return normalizedMovieDirectory.StartsWith(
                    normalizedWatchFolder,
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch
            {
                return false;
            }
        }

        // StartsWith 判定の誤爆を避けるため、比較前にフルパス化と末尾区切りを揃える。
        private static string NormalizeDirectoryPathForComparison(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return "";
            }

            string normalized = directoryPath;
            try
            {
                normalized = Path.GetFullPath(directoryPath);
            }
            catch
            {
                normalized = directoryPath;
            }

            normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalized + Path.DirectorySeparatorChar;
        }

        // 画面側の保持パスを大小文字差異なしで参照できるよう、比較用セットへ正規化する。
        internal static HashSet<string> BuildMoviePathLookup(IEnumerable<string> moviePaths)
        {
            HashSet<string> lookup = new(StringComparer.OrdinalIgnoreCase);
            if (moviePaths == null)
            {
                return lookup;
            }

            foreach (string moviePath in moviePaths)
            {
                if (!string.IsNullOrWhiteSpace(moviePath))
                {
                    lookup.Add(moviePath);
                }
            }

            return lookup;
        }

        // 実ファイルとDBは一致していても、画面ソースから抜けている既存動画は表示整合の補正対象にする。
        internal static bool ShouldRepairExistingMovieView(
            ISet<string> existingViewMoviePaths,
            string movieFullPath
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            return existingViewMoviePaths == null || !existingViewMoviePaths.Contains(movieFullPath);
        }

        // 検索未使用時は、表示側の一覧から抜けた既存動画も再描画対象として扱う。
        internal static bool ShouldRefreshDisplayedMovieView(
            string searchKeyword,
            ISet<string> displayedMoviePaths,
            string movieFullPath
        )
        {
            if (!string.IsNullOrWhiteSpace(searchKeyword) || string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            return displayedMoviePaths == null || !displayedMoviePaths.Contains(movieFullPath);
        }

        // 実ファイル・DB・画面ソース・表示一覧のズレを、監視側がどう補正するかを1か所で判定する。
        internal static MovieViewConsistencyDecision EvaluateMovieViewConsistency(
            bool allowViewConsistencyRepair,
            bool existsInDb,
            ISet<string> existingViewMoviePaths,
            string searchKeyword,
            ISet<string> displayedMoviePaths,
            string movieFullPath
        )
        {
            if (
                !allowViewConsistencyRepair
                || !existsInDb
                || string.IsNullOrWhiteSpace(movieFullPath)
            )
            {
                return MovieViewConsistencyDecision.None;
            }

            bool shouldRepairView = ShouldRepairExistingMovieView(
                existingViewMoviePaths,
                movieFullPath
            );
            if (shouldRepairView)
            {
                return new MovieViewConsistencyDecision(
                    ShouldRepairView: true,
                    ShouldRefreshDisplayedView: false
                );
            }

            bool shouldRefreshDisplayedView = ShouldRefreshDisplayedMovieView(
                searchKeyword,
                displayedMoviePaths,
                movieFullPath
            );
            return new MovieViewConsistencyDecision(
                ShouldRepairView: false,
                ShouldRefreshDisplayedView: shouldRefreshDisplayedView
            );
        }

        // Everything連携の判定と呼び出しを集約するFacade。
        private readonly IIndexProviderFacade _indexProviderFacade =
            FileIndexProviderFactory.CreateFacade();

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
        // NotificationManager は内部でウィンドウ資源を抱えるため、走査ごとに増やさず MainWindow で共有する。
        private readonly NotificationManager _watchNotificationManager = new();
        // DB+タブ単位で、欠損サムネ救済を直近いつ実行したかを記録する。
        private readonly object _missingThumbnailRescueSync = new();
        private readonly Dictionary<string, DateTime> _missingThumbnailRescueLastRunUtcByScope =
            new(StringComparer.OrdinalIgnoreCase);
        // DB+監視フォルダ単位で、低頻度の全量再突合を直近いつ実行したかを記録する。
        private readonly object _watchFolderFullReconcileSync = new();
        private readonly Dictionary<string, DateTime> _watchFolderFullReconcileLastRunUtcByScope =
            new(StringComparer.OrdinalIgnoreCase);

        // 設定値(0/1/2)をOFF/AUTO/ONへ丸める。
        private static IntegrationMode GetEverythingIntegrationMode()
        {
            int mode = Properties.Settings.Default.EverythingIntegrationMode;
            return mode switch
            {
                0 => IntegrationMode.Off,
                2 => IntegrationMode.On,
                _ => IntegrationMode.Auto,
            };
        }

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

        // Everything専用監視を有効にできる条件を満たす場合、FileSystemWatcher作成をスキップする。
        private static bool ShouldSkipFileSystemWatcherByEverything(
            string watchFolder,
            IntegrationMode mode,
            AvailabilityResult availability,
            out string reason
        )
        {
            if (mode != IntegrationMode.On)
            {
                reason = "mode_not_on";
                return false;
            }

            if (!availability.CanUse)
            {
                reason = $"everything_unavailable:{availability.Reason}";
                return false;
            }

            if (!IsEverythingEligiblePath(watchFolder, out string eligibilityReason))
            {
                reason = $"{EverythingReasonCodes.PathNotEligiblePrefix}{eligibilityReason}";
                return false;
            }

            reason = "everything_only_enabled";
            return true;
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
        // watch 走査の入口でだけ UI 実装詳細を束ね、coordinator 側へは port と snapshot を渡す。
        private WatchScanCoordinatorContext CreateWatchScanCoordinatorContext(CheckMode mode)
        {
            string snapshotDbFullPath = MainVM.DbInfo.DBFullPath;
            string snapshotThumbFolder = MainVM.DbInfo.ThumbFolder;
            string snapshotDbName = MainVM.DbInfo.DBName;
            int snapshotTabIndex = MainVM.DbInfo.CurrentTabIndex;
            int? autoEnqueueTabIndex = ResolveWatchMissingThumbnailTabIndex(snapshotTabIndex);

            return new WatchScanCoordinatorContext(
                mode,
                snapshotDbFullPath,
                snapshotThumbFolder,
                snapshotDbName,
                snapshotTabIndex,
                autoEnqueueTabIndex,
                CreateWatchScanUiBridge()
            );
        }

        // BuildCurrent... / 通知種別 / 画面反映はここに閉じ込め、coordinator から UI 実装名を消す。
        private WatchScanUiBridge CreateWatchScanUiBridge()
        {
            return new WatchScanUiBridge(
                async () =>
                {
                    HashSet<string> existingViewMoviePaths =
                        await BuildCurrentViewMoviePathLookupAsync();
                    (
                        HashSet<string> displayedMoviePaths,
                        string searchKeyword
                    ) = await BuildCurrentDisplayedMovieStateAsync();
                    HashSet<string> visibleMoviePaths = await BuildCurrentVisibleMoviePathLookupAsync();

                    return new WatchScanUiSnapshot(
                        existingViewMoviePaths,
                        displayedMoviePaths,
                        searchKeyword,
                        visibleMoviePaths,
                        !IsStartupFeedPartialActive
                    );
                },
                (snapshotDbFullPath, moviePath) =>
                    TryAppendMovieToViewByPathAsync(snapshotDbFullPath, moviePath),
                checkFolder =>
                {
                    if (_hasShownFolderMonitoringNotice)
                    {
                        return;
                    }

                    _watchNotificationManager.Show(
                        "フォルダ監視中",
                        $"{checkFolder} 監視実施中…",
                        NotificationType.Notification,
                        "ProgressArea"
                    );
                    _hasShownFolderMonitoringNotice = true;
                },
                checkFolder =>
                {
                    if (_hasShownFolderMonitoringNotice)
                    {
                        return;
                    }

                    _watchNotificationManager.Show(
                        "フォルダ監視中",
                        $"{checkFolder}に更新あり。",
                        NotificationType.Notification,
                        "ProgressArea"
                    );
                    _hasShownFolderMonitoringNotice = true;
                },
                _ =>
                {
                    if (_hasShownEverythingModeNotice)
                    {
                        return;
                    }

                    _watchNotificationManager.Show(
                        "Everything連携",
                        "Everything連携で高速スキャンを実行中です。",
                        NotificationType.Notification,
                        "ProgressArea"
                    );
                    _hasShownEverythingModeNotice = true;
                },
                strategyDetailMessage =>
                {
                    if (
                        _hasShownEverythingFallbackNotice
                        || !_indexProviderFacade.IsIntegrationConfigured(
                            GetEverythingIntegrationMode()
                        )
                    )
                    {
                        return;
                    }

                    _watchNotificationManager.Show(
                        "Everything連携",
                        $"Everything連携を利用できないため通常監視で継続します。({strategyDetailMessage})",
                        NotificationType.Information,
                        "ProgressArea"
                    );
                    _hasShownEverythingFallbackNotice = true;
                }
            );
        }

        private async Task CheckFolderAsync(CheckMode mode)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string checkExt = Properties.Settings.Default.CheckExt;
            WatchScanCoordinatorContext scanContext = CreateWatchScanCoordinatorContext(mode);

            DebugRuntimeLog.TaskStart(
                nameof(CheckFolderAsync),
                $"mode={mode} db='{scanContext.SnapshotDbFullPath}'"
            );

            // 呼び出し元（OpenDatafile等UIスレッド）をすぐ返すため、最初に非同期コンテキストへ切り替える。
            await Task.Yield();
            await InitializeWatchScanCoordinatorContextAsync(scanContext);

            // モードに応じた監視設定の取得（自動更新対象のみか、全対象か）
            string sql = mode switch
            {
                CheckMode.Auto => $"SELECT * FROM watch where auto = 1",
                CheckMode.Watch => $"SELECT * FROM watch where watch = 1",
                _ => $"SELECT * FROM watch",
            };
            GetWatchTable(scanContext.SnapshotDbFullPath, sql);
            if (watchData == null)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan canceled: watch table load failed. db='{scanContext.SnapshotDbFullPath}' mode={mode}"
                );
                return;
            }

            // DB上の監視フォルダ定義1行ずつ検証していく。
            foreach (DataRow row in watchData.Rows)
            {
                // DB切り替えを跨いだ旧スナップショット混入をここで止める。
                if (HasWatchScanDbSwitched(scanContext))
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"abort scan: db switched from '{scanContext.SnapshotDbFullPath}' to '{MainVM.DbInfo.DBFullPath}'"
                    );
                    return;
                }

                await ProcessWatchFolderAsync(scanContext, row, checkExt);
            }

            //stack : ファイル名を外部から変更したときに、エクステンションのファイル名が追従してなかった。強制チェックで反応はした。
            //再クリックで表示はリロードしたので、内部は変わってる。リフレッシュも漏れてる可能性あり。
            //と言うかですね。これは外部からのリネームでも、アプリでのリネームでも同じで。クリックすりゃ反映する（そりゃそうだ）

            // ----- [5] 走査全体を通していずれかのフォルダで変化があったらUI一覧を再描画 -----
            if (scanContext.HasAnyFolderUpdate)
            {
                FilterAndSort(MainVM.DbInfo.Sort, true); //チェックフォルダ時。監視対象があった場合の処理やな。
            }

            // Watch/Manual時は、削除されたサムネイルの取りこぼし救済を低頻度で実行する。
            await TryRunMissingThumbnailRescueAsync(
                mode,
                scanContext.SnapshotDbFullPath,
                scanContext.SnapshotDbName,
                scanContext.SnapshotThumbFolder,
                scanContext.SnapshotTabIndex
            );

            sw.Stop();
            DebugRuntimeLog.TaskEnd(
                nameof(CheckFolderAsync),
                $"mode={mode} folders={scanContext.CheckedFolderCount} enqueued={scanContext.EnqueuedCount} updated={scanContext.HasAnyFolderUpdate} elapsed_ms={sw.ElapsedMilliseconds}"
            );
        }

        private sealed class WatchScanUiBridge
        {
            private readonly Func<Task<WatchScanUiSnapshot>> _captureSnapshotAsync;
            private readonly Func<string, string, Task> _appendMovieToViewByPathAsync;
            private readonly Action<string> _showFolderMonitoringProgress;
            private readonly Action<string> _showFolderHit;
            private readonly Action<string> _showEverythingModeNotice;
            private readonly Action<string> _showEverythingFallbackNotice;

            public WatchScanUiBridge(
                Func<Task<WatchScanUiSnapshot>> captureSnapshotAsync,
                Func<string, string, Task> appendMovieToViewByPathAsync,
                Action<string> showFolderMonitoringProgress,
                Action<string> showFolderHit,
                Action<string> showEverythingModeNotice,
                Action<string> showEverythingFallbackNotice
            )
            {
                _captureSnapshotAsync =
                    captureSnapshotAsync ?? throw new ArgumentNullException(nameof(captureSnapshotAsync));
                _appendMovieToViewByPathAsync =
                    appendMovieToViewByPathAsync
                    ?? throw new ArgumentNullException(nameof(appendMovieToViewByPathAsync));
                _showFolderMonitoringProgress =
                    showFolderMonitoringProgress
                    ?? throw new ArgumentNullException(nameof(showFolderMonitoringProgress));
                _showFolderHit = showFolderHit ?? throw new ArgumentNullException(nameof(showFolderHit));
                _showEverythingModeNotice =
                    showEverythingModeNotice
                    ?? throw new ArgumentNullException(nameof(showEverythingModeNotice));
                _showEverythingFallbackNotice =
                    showEverythingFallbackNotice
                    ?? throw new ArgumentNullException(nameof(showEverythingFallbackNotice));
            }

            public Task<WatchScanUiSnapshot> CaptureSnapshotAsync()
            {
                return _captureSnapshotAsync();
            }

            public Task AppendMovieToViewByPathAsync(string snapshotDbFullPath, string moviePath)
            {
                return _appendMovieToViewByPathAsync(snapshotDbFullPath, moviePath);
            }

            public void TryShowFolderMonitoringProgress(string checkFolder)
            {
                _showFolderMonitoringProgress(checkFolder);
            }

            public void TryShowFolderHit(string checkFolder)
            {
                _showFolderHit(checkFolder);
            }

            public void TryShowEverythingModeNotice(string strategyDetailMessage)
            {
                _showEverythingModeNotice(strategyDetailMessage);
            }

            public void TryShowEverythingFallbackNotice(string strategyDetailMessage)
            {
                _showEverythingFallbackNotice(strategyDetailMessage);
            }
        }

        private sealed class WatchScanUiSnapshot
        {
            public WatchScanUiSnapshot(
                HashSet<string> existingViewMoviePaths,
                HashSet<string> displayedMoviePaths,
                string searchKeyword,
                HashSet<string> visibleMoviePaths,
                bool allowViewConsistencyRepair
            )
            {
                ExistingViewMoviePaths =
                    existingViewMoviePaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                DisplayedMoviePaths =
                    displayedMoviePaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                SearchKeyword = searchKeyword ?? "";
                VisibleMoviePaths =
                    visibleMoviePaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AllowViewConsistencyRepair = allowViewConsistencyRepair;
            }

            public HashSet<string> ExistingViewMoviePaths { get; }
            public HashSet<string> DisplayedMoviePaths { get; }
            public string SearchKeyword { get; }
            public HashSet<string> VisibleMoviePaths { get; }
            public bool AllowViewConsistencyRepair { get; }
        }

        // Watch差分では「動画更新なし + サムネ削除」の取りこぼしが起き得るため、低頻度で欠損救済を実行する。
        private async Task TryRunMissingThumbnailRescueAsync(
            CheckMode mode,
            string snapshotDbFullPath,
            string snapshotDbName,
            string snapshotThumbFolder,
            int snapshotTabIndex
        )
        {
            if (mode != CheckMode.Watch && mode != CheckMode.Manual)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(snapshotDbFullPath))
            {
                return;
            }

            if (snapshotTabIndex < 0)
            {
                return;
            }

            // キュー高負荷中は救済スキャンを見送り、通常処理を優先する。
            if (TryGetCurrentQueueActiveCount(out int activeCount))
            {
                int rescueBusyThreshold = ResolveMissingThumbnailRescueBusyThreshold(
                    mode == CheckMode.Watch,
                    EverythingWatchPollBusyThreshold
                );
                if (
                    ShouldSkipMissingThumbnailRescueForBusyQueue(
                        mode == CheckMode.Manual,
                        activeCount,
                        rescueBusyThreshold
                    )
                )
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"missing-thumb rescue skipped: queue busy active={activeCount} threshold={rescueBusyThreshold}"
                    );
                    return;
                }
            }

            DateTime nowUtc = DateTime.UtcNow;
            string scopeKey = BuildMissingThumbnailRescueScopeKey(snapshotDbFullPath, snapshotTabIndex);
            if (!TryReserveMissingThumbnailRescueWindow(scopeKey, nowUtc, out TimeSpan nextIn))
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-thumb rescue throttled: tab={snapshotTabIndex} next_in_sec={Math.Ceiling(nextIn.TotalSeconds)}"
                );
                return;
            }

            Stopwatch rescueStopwatch = Stopwatch.StartNew();
            try
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-thumb rescue start: mode={mode} tab={snapshotTabIndex} db='{snapshotDbFullPath}'"
                );
                await EnqueueMissingThumbnailsAsync(
                    snapshotTabIndex,
                    snapshotDbFullPath,
                    snapshotDbName,
                    snapshotThumbFolder
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-thumb rescue failed: mode={mode} tab={snapshotTabIndex} reason='{ex.Message}'"
                );
            }
            finally
            {
                rescueStopwatch.Stop();
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-thumb rescue end: mode={mode} tab={snapshotTabIndex} elapsed_ms={rescueStopwatch.ElapsedMilliseconds}"
                );
            }
        }

        // 現在のMainDBとタブを1つのスコープキーへ正規化する。
        private static string BuildMissingThumbnailRescueScopeKey(string dbFullPath, int tabIndex)
        {
            string normalized = dbFullPath ?? "";
            try
            {
                if (!string.IsNullOrWhiteSpace(normalized) && Path.IsPathFullyQualified(normalized))
                {
                    normalized = Path.GetFullPath(normalized);
                }
            }
            catch
            {
                // 正規化に失敗しても、元文字列をキーとして扱って処理継続する。
            }

            return $"{normalized.Trim().ToLowerInvariant()}|tab={tabIndex}";
        }

        // 同一スコープで短時間に救済処理を連打しないよう、最小実行間隔を適用する。
        private bool TryReserveMissingThumbnailRescueWindow(
            string scopeKey,
            DateTime nowUtc,
            out TimeSpan nextIn
        )
        {
            lock (_missingThumbnailRescueSync)
            {
                if (_missingThumbnailRescueLastRunUtcByScope.TryGetValue(scopeKey, out DateTime lastRunUtc))
                {
                    TimeSpan elapsed = nowUtc - lastRunUtc;
                    if (elapsed < MissingThumbnailRescueMinInterval)
                    {
                        nextIn = MissingThumbnailRescueMinInterval - elapsed;
                        return false;
                    }
                }

                _missingThumbnailRescueLastRunUtcByScope[scopeKey] = nowUtc;
                nextIn = TimeSpan.Zero;

                // スコープキーの肥大化を防ぐため、古い記録は定期的に掃除する。
                if (_missingThumbnailRescueLastRunUtcByScope.Count > 128)
                {
                    DateTime cutoff = nowUtc - TimeSpan.FromHours(24);
                    List<string> staleKeys = _missingThumbnailRescueLastRunUtcByScope
                        .Where(x => x.Value < cutoff)
                        .Select(x => x.Key)
                        .ToList();
                    foreach (string staleKey in staleKeys)
                    {
                        _missingThumbnailRescueLastRunUtcByScope.Remove(staleKey);
                    }
                }

                return true;
            }
        }

        // 差分0件が続いても、同じ監視フォルダへは一定間隔ごとにだけ全量再突合する。
        private bool TryReserveWatchFolderFullReconcileWindow(
            string scopeKey,
            DateTime nowUtc,
            out TimeSpan nextIn
        )
        {
            lock (_watchFolderFullReconcileSync)
            {
                if (
                    _watchFolderFullReconcileLastRunUtcByScope.TryGetValue(
                        scopeKey,
                        out DateTime lastRunUtc
                    )
                )
                {
                    TimeSpan elapsed = nowUtc - lastRunUtc;
                    if (elapsed < WatchFolderFullReconcileMinInterval)
                    {
                        nextIn = WatchFolderFullReconcileMinInterval - elapsed;
                        return false;
                    }
                }

                _watchFolderFullReconcileLastRunUtcByScope[scopeKey] = nowUtc;
                nextIn = TimeSpan.Zero;

                if (_watchFolderFullReconcileLastRunUtcByScope.Count > 128)
                {
                    DateTime cutoff = nowUtc - TimeSpan.FromHours(24);
                    List<string> staleKeys = _watchFolderFullReconcileLastRunUtcByScope
                        .Where(x => x.Value < cutoff)
                        .Select(x => x.Key)
                        .ToList();
                    foreach (string staleKey in staleKeys)
                    {
                        _watchFolderFullReconcileLastRunUtcByScope.Remove(staleKey);
                    }
                }

                return true;
            }
        }

        // DB切替や監視設定差分で混線しないよう、DB+フォルダ+sub単位で再突合スコープを固定する。
        private static string BuildWatchFolderFullReconcileScopeKey(
            string dbFullPath,
            string watchFolder,
            bool sub
        )
        {
            string normalizedDb = dbFullPath ?? "";
            string normalizedFolder = watchFolder ?? "";

            try
            {
                if (!string.IsNullOrWhiteSpace(normalizedDb) && Path.IsPathFullyQualified(normalizedDb))
                {
                    normalizedDb = Path.GetFullPath(normalizedDb);
                }
            }
            catch
            {
                // 正規化に失敗しても、元文字列をキーとして扱って処理継続する。
            }

            try
            {
                if (
                    !string.IsNullOrWhiteSpace(normalizedFolder)
                    && Path.IsPathFullyQualified(normalizedFolder)
                )
                {
                    normalizedFolder = Path.GetFullPath(normalizedFolder);
                }
            }
            catch
            {
                // 正規化に失敗しても、元文字列をキーとして扱って処理継続する。
            }

            return
                $"{normalizedDb.Trim().ToLowerInvariant()}|{normalizedFolder.Trim().ToLowerInvariant()}|sub={(sub ? 1 : 0)}";
        }

        // QueueDBのアクティブ件数を安全に取得する。取得不能時はfalseを返して救済判定を継続する。
        private bool TryGetCurrentQueueActiveCount(out int activeCount)
        {
            activeCount = 0;
            try
            {
                var queueDbService = ResolveCurrentQueueDbService();
                if (queueDbService == null)
                {
                    return false;
                }

                activeCount = queueDbService.GetActiveQueueCount(thumbnailQueueOwnerInstanceId);
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-thumb rescue queue count failed: {ex.Message}"
                );
                return false;
            }
        }

        // 100件たまるごとにサムネイルキューへ流すための共通処理。
        private void FlushPendingQueueItems(List<QueueObj> pendingItems, string folderPath)
        {
            if (pendingItems.Count < 1)
            {
                return;
            }

            bool bypassDebounce = string.Equals(
                folderPath,
                "RescueMissingThumbnails",
                StringComparison.Ordinal
            );
            int flushedCount = 0;
            foreach (QueueObj pending in pendingItems)
            {
                if (TryEnqueueThumbnailJob(pending, bypassDebounce))
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

        // 単体登録をバックグラウンドで実行し、監視イベント側の待機を短くする。
        private static Task<int> InsertMovieToMainDbAsync(string dbFullPath, MovieInfo movieInfo)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || movieInfo == null)
            {
                return Task.FromResult(0);
            }

            return Task.Run(() =>
                InsertMovieTable(dbFullPath, movieInfo)
                    .GetAwaiter()
                    .GetResult()
            );
        }

        // 全件再読込せず、対象パス1件だけDBから引いてUIへ反映する。
        private async Task TryAppendMovieToViewByPathAsync(
            string snapshotDbFullPath,
            string moviePath
        )
        {
            if (
                string.IsNullOrWhiteSpace(snapshotDbFullPath)
                || string.IsNullOrWhiteSpace(moviePath)
            )
            {
                return;
            }

            DataRow targetRow = await Task.Run(() =>
            {
                string escapedMoviePath = moviePath.Replace("'", "''");
                DataTable dt = GetData(
                    snapshotDbFullPath,
                    $"select * from movie where lower(movie_path) = lower('{escapedMoviePath}') order by movie_id desc limit 1"
                );
                return dt?.Rows.Count > 0 ? dt.Rows[0] : null;
            });

            if (targetRow == null)
            {
                return;
            }

            await Dispatcher.InvokeAsync(
                () => DataRowToViewData(targetRow),
                System.Windows.Threading.DispatcherPriority.Background
            );
        }

        // 現在の画面ソースに載っている動画パスを、走査側で安全に参照できるようスナップショット化する。
        private async Task<HashSet<string>> BuildCurrentViewMoviePathLookupAsync()
        {
            return await Dispatcher.InvokeAsync(
                () => BuildMoviePathLookup(MainVM?.MovieRecs?.Select(x => x.Movie_Path)),
                System.Windows.Threading.DispatcherPriority.Background
            );
        }

        // 現在の一覧表示ソースと検索条件をまとめて取り、再描画が必要かを判定できるようにする。
        private async Task<(HashSet<string> DisplayedMoviePaths, string SearchKeyword)> BuildCurrentDisplayedMovieStateAsync()
        {
            return await Dispatcher.InvokeAsync(
                () =>
                {
                    HashSet<string> displayedMoviePaths = BuildMoviePathLookup(
                        MainVM?.FilteredMovieRecs?.Select(x => x.Movie_Path)
                    );
                    string currentSearchKeyword = MainVM?.DbInfo?.SearchKeyword ?? "";
                    return (displayedMoviePaths, currentSearchKeyword);
                },
                System.Windows.Threading.DispatcherPriority.Background
            );
        }

        // viewport で実際に見えている動画だけを取り、watch の高負荷時ガードに使う。
        private async Task<HashSet<string>> BuildCurrentVisibleMoviePathLookupAsync()
        {
            return await Dispatcher.InvokeAsync(
                () =>
                {
                    HashSet<string> visibleMoviePaths = new(StringComparer.OrdinalIgnoreCase);
                    if (
                        GetCurrentUpperTabFixedIndex() is < 0 or > 4
                        || !_activeUpperTabVisibleRange.HasVisibleItems
                        || MainVM?.FilteredMovieRecs == null
                    )
                    {
                        return visibleMoviePaths;
                    }

                    int totalCount = MainVM.FilteredMovieRecs.Count;
                    int firstVisibleIndex = Math.Max(0, _activeUpperTabVisibleRange.FirstVisibleIndex);
                    int lastVisibleIndex = Math.Min(
                        totalCount - 1,
                        _activeUpperTabVisibleRange.LastVisibleIndex
                    );
                    if (lastVisibleIndex < firstVisibleIndex)
                    {
                        return visibleMoviePaths;
                    }

                    for (int index = firstVisibleIndex; index <= lastVisibleIndex; index++)
                    {
                        string moviePath = MainVM.FilteredMovieRecs[index]?.Movie_Path ?? "";
                        if (!string.IsNullOrWhiteSpace(moviePath))
                        {
                            visibleMoviePaths.Add(moviePath);
                        }
                    }

                    return visibleMoviePaths;
                },
                System.Windows.Threading.DispatcherPriority.Background
            );
        }

        // MainDB反映前の動画を「登録待ち」としてUIへ一時表示する。
        private void AddOrUpdatePendingMoviePlaceholder(
            string moviePath,
            string fileBody,
            int tabIndex,
            PendingMoviePlaceholderStatus status,
            string lastError = ""
        )
        {
            if (string.IsNullOrWhiteSpace(moviePath) || MainVM?.PendingMovieRecs == null)
            {
                return;
            }

            string safeFileBody = string.IsNullOrWhiteSpace(fileBody)
                ? (Path.GetFileNameWithoutExtension(moviePath) ?? "")
                : fileBody;

            _ = Dispatcher.InvokeAsync(() =>
            {
                PendingMoviePlaceholder item = MainVM.PendingMovieRecs
                    .FirstOrDefault(x =>
                        string.Equals(x.MoviePath, moviePath, StringComparison.OrdinalIgnoreCase)
                    );

                if (item == null)
                {
                    item = new PendingMoviePlaceholder
                    {
                        MoviePath = moviePath,
                        DetectedAtLocal = DateTime.Now,
                    };
                    MainVM.PendingMovieRecs.Add(item);
                }

                item.FileBody = safeFileBody;
                item.TabIndex = tabIndex;
                item.Status = status;
                item.LastError = lastError ?? "";
                item.UpdatedAtLocal = DateTime.Now;

                while (MainVM.PendingMovieRecs.Count > PendingMovieUiKeepLimit)
                {
                    MainVM.PendingMovieRecs.RemoveAt(0);
                }
            });
        }

        // DB反映が終わった動画は仮表示から取り除く。
        private void RemovePendingMoviePlaceholder(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath) || MainVM?.PendingMovieRecs == null)
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(() =>
            {
                PendingMoviePlaceholder item = MainVM.PendingMovieRecs
                    .FirstOrDefault(x =>
                        string.Equals(x.MoviePath, moviePath, StringComparison.OrdinalIgnoreCase)
                    );
                if (item != null)
                {
                    MainVM.PendingMovieRecs.Remove(item);
                }
            });
        }

        // 例外で走査が中断したフォルダ分の仮表示をクリアして残留を防ぐ。
        private void ClearPendingMoviePlaceholdersByFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || MainVM?.PendingMovieRecs == null)
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(() =>
            {
                List<PendingMoviePlaceholder> targets = MainVM.PendingMovieRecs
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.MoviePath)
                        && x.MoviePath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase)
                    )
                    .ToList();
                foreach (PendingMoviePlaceholder target in targets)
                {
                    MainVM.PendingMovieRecs.Remove(target);
                }
            });
        }

        // 1件トレース対象を判定する。動画ID部分で一致させることで、長いパス全体の表記ゆれに強くする。
        private static bool IsWatchCheckProbeTargetMovie(string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            return movieFullPath.Contains(
                WatchCheckProbeMovieIdentity,
                StringComparison.OrdinalIgnoreCase
            );
        }

        /// <summary>
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
            if (
                safeDetail.StartsWith(
                    EverythingReasonCodes.PathNotEligiblePrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                string rawReason = safeDetail[EverythingReasonCodes.PathNotEligiblePrefix.Length..];
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

            if (
                safeDetail.Equals(
                    EverythingReasonCodes.SettingDisabled,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (safeDetail, "設定でEverything連携が無効です");
            }

            if (
                safeDetail.Equals(
                    EverythingReasonCodes.EverythingNotAvailable,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (safeDetail, "Everythingが起動していないかIPC接続できません");
            }
            if (
                safeDetail.Equals(
                    EverythingReasonCodes.AutoNotAvailable,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (
                    safeDetail,
                    "AUTO設定中ですがEverythingが見つからないため通常監視で動作します"
                );
            }

            if (
                safeDetail.StartsWith(
                    EverythingReasonCodes.EverythingResultTruncatedPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (safeDetail, "検索結果が上限件数に達したため通常監視へ切り替えます");
            }

            if (
                safeDetail.StartsWith(
                    EverythingReasonCodes.AvailabilityErrorPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
                || safeDetail.StartsWith(
                    EverythingReasonCodes.EverythingQueryErrorPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
                || safeDetail.StartsWith(
                    EverythingReasonCodes.EverythingThumbQueryErrorPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (safeDetail, $"Everything連携で例外が発生しました ({safeDetail})");
            }

            if (
                safeDetail.StartsWith(
                    EverythingReasonCodes.OkPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (safeDetail, "Everything連携で候補収集に成功しました");
            }

            return (safeDetail, $"不明な理由のため通常監視へ切り替えます ({safeDetail})");
        }

        /// <summary>
        /// Everything連携を優先して走査し、利用不可時は既存のファイルシステム走査へフォールバックする。
        /// Everything優先で候補収集し、利用不可時だけ既存のファイルシステム走査へ戻す。
        /// </summary>
        private FolderScanWithStrategyResult ScanFolderWithStrategyInBackground(
            CheckMode mode,
            string checkFolder,
            bool sub,
            string checkExt
        )
        {
            if (!IsEverythingEligiblePath(checkFolder, out string eligibilityReason))
            {
                FolderScanResult notEligibleFallback = ScanFolderInBackground(
                    checkFolder,
                    sub,
                    checkExt
                );
                return new FolderScanWithStrategyResult(
                    notEligibleFallback,
                    FileIndexStrategies.Filesystem,
                    $"{EverythingReasonCodes.PathNotEligiblePrefix}{eligibilityReason}"
                );
            }

            DateTime? changedSinceUtc =
                mode == CheckMode.Watch ? LoadEverythingLastSyncUtc(checkFolder, sub) : null;
            FileIndexQueryOptions options = new()
            {
                RootPath = checkFolder,
                IncludeSubdirectories = sub,
                CheckExt = checkExt,
                ChangedSinceUtc = changedSinceUtc,
            };
            IntegrationMode integrationMode = GetEverythingIntegrationMode();
            ScanByProviderResult providerResult = _indexProviderFacade
                .CollectMoviePathsWithFallback(options, integrationMode);
            bool usedEverything = string.Equals(
                providerResult.Strategy,
                FileIndexStrategies.Everything,
                StringComparison.OrdinalIgnoreCase
            );
            List<string> candidatePaths = providerResult.MoviePaths;
            DateTime? maxObservedChangedUtc = providerResult.MaxObservedChangedUtc;
            string reason = providerResult.Reason;

            if (usedEverything)
            {
                List<string> newMoviePaths = [];
                int scannedCount = 0;
                foreach (string fullPath in candidatePaths)
                {
                    scannedCount++;

                    // タブ欠損サムネ再生成の回帰を避けるため、事前除外は行わず空文字だけ弾く。
                    string fileBody = Path.GetFileNameWithoutExtension(fullPath);
                    if (string.IsNullOrWhiteSpace(fileBody))
                    {
                        continue;
                    }

                    newMoviePaths.Add(fullPath);
                }

                // 取りこぼしを避けるため、問い合わせ時刻ではなく「観測できた変更時刻の高水位」を保存する。
                DateTime? nextSyncUtc = maxObservedChangedUtc;
                if (FileIndexIncrementalSyncPolicy.ShouldAdvanceCursor(nextSyncUtc, changedSinceUtc))
                {
                    SaveEverythingLastSyncUtc(checkFolder, sub, nextSyncUtc.Value);
                }
                return new FolderScanWithStrategyResult(
                    new FolderScanResult(scannedCount, newMoviePaths),
                    FileIndexStrategies.Everything,
                    reason
                );
            }

            FolderScanResult fallbackResult = ScanFolderInBackground(checkFolder, sub, checkExt);
            return new FolderScanWithStrategyResult(
                fallbackResult,
                FileIndexStrategies.Filesystem,
                reason
            );
        }

        /// <summary>
        /// 監視フォルダの重い直列走査を担う静的メソッド。
        /// Task.Run経由でバックグラウンドスレッドで実行し、候補ファイルのフルパスだけを返す。
        /// </summary>
        private static FolderScanResult ScanFolderInBackground(
            string checkFolder,
            bool sub,
            string checkExt
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

                    // タブ欠損サムネ再生成のため、事前重複除外は行わず空文字だけ弾く。
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

        internal readonly record struct MovieViewConsistencyDecision(
            bool ShouldRepairView,
            bool ShouldRefreshDisplayedView
        )
        {
            public static MovieViewConsistencyDecision None => new(false, false);
        }

        // スキャン中に検出した新規動画を一時的に保持するDTO。
        private sealed class PendingMovieRegistration
        {
            public PendingMovieRegistration(string movieFullPath, string fileBody, MovieInfo movie)
            {
                MovieFullPath = movieFullPath ?? "";
                FileBody = fileBody ?? "";
                Movie = movie;
            }

            public string MovieFullPath { get; }
            public string FileBody { get; }
            public MovieInfo Movie { get; }
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
            string thumbnailOutPath = ResolveThumbnailOutPath(
                targetTabIndex,
                snapshotDbName,
                snapshotThumbFolder
            );
            if (string.IsNullOrWhiteSpace(thumbnailOutPath))
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
            HashSet<string> existingThumbnailFileNames = await Task.Run(() =>
                BuildThumbnailFileNameLookup(thumbnailOutPath)
            );
            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            HashSet<string> openRescueRequestKeys =
                failureDbService?.GetOpenRescueRequestKeys()
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                string expectedThumbFileName = ThumbnailPathResolver.BuildThumbnailFileName(path, hash);
                if (!existingThumbnailFileNames.Contains(expectedThumbFileName))
                {
                    MissingThumbnailAutoEnqueueBlockReason blockReason =
                        ResolveMissingThumbnailAutoEnqueueBlockReason(
                            path,
                            targetTabIndex,
                            existingThumbnailFileNames,
                            openRescueRequestKeys
                        );
                    if (blockReason != MissingThumbnailAutoEnqueueBlockReason.None)
                    {
                        DebugRuntimeLog.Write(
                            "rescue-thumb",
                            $"skip enqueue by failure-state: tab={targetTabIndex}, movie='{path}', reason={DescribeMissingThumbnailAutoEnqueueBlockReason(blockReason)}"
                        );
                        continue;
                    }

                    // 動画ファイル自体が取れるかだけ確認し、0KBも後段で弾けるようサイズ取得系へ寄せる。
                    if (TryGetMovieFileLength(path, out _))
                    {
                        batch.Add(
                            new QueueObj
                            {
                                MovieId = movieId,
                                MovieFullPath = path,
                                Hash = hash,
                                Tabindex = targetTabIndex,
                                Priority = ThumbnailQueuePriority.Normal,
                            }
                        );

                        enqueuedCount++;
                        existingThumbnailFileNames.Add(expectedThumbFileName);
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
