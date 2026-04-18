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
        // watch 1回で処理する候補数を抑え、結果件数の多い差分でUIが詰まるのを防ぐ。
        private const int WatchScanProcessLimit = 200;
        // 左ドロワー表示中は、watch 起点の新規仕事だけ抑えて操作テンポを守る。
        private readonly object _watchUiSuppressionSync = new();
        private int _watchUiSuppressionCount;
        private bool _watchWorkDeferredWhileSuppressed;
        private long _watchScanScopeStamp = 1;

        // DB切替や shutdown で watch scope を進め、旧passをまとめて stale 化する。
        private long InvalidateWatchScanScope(string reason)
        {
            CancelDeferredWatchUiReload($"scope-invalidated:{reason}");
            long nextStamp = Interlocked.Increment(ref _watchScanScopeStamp);
            DebugRuntimeLog.Write(
                "watch-check",
                $"watch scan scope invalidated: stamp={nextStamp} reason={reason}"
            );
            return nextStamp;
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
        // watch 差分を1回で抱え込みすぎないよう、次回送り候補をフォルダ単位で保持する。
        private readonly object _deferredWatchScanSync = new();
        private readonly Dictionary<string, DeferredWatchScanState> _deferredWatchScanStateByScope =
            new(StringComparer.OrdinalIgnoreCase);
        // watch 起点の全件再読込は即時連打せず、短い遅延で最新1回へ圧縮する。
        private const int WatchDeferredUiReloadDelayMs = 350;
        private readonly object _watchDeferredUiReloadSync = new();
        private CancellationTokenSource _watchDeferredUiReloadCts = new();
        private int _watchDeferredUiReloadRevision;
        private bool _watchDeferredUiReloadPending;
        private bool _watchDeferredUiReloadQueryOnly;
        private List<WatchChangedMovie> _watchDeferredUiReloadChangedMovies = [];
        // テストでは本経路の呼び出し回数だけ観測し、既存の制御自体はそのまま通す。
        internal Action<string, string> QueueCheckFolderAsyncRequestedForTesting { get; set; }
        internal Action<string, bool> FilterAndSortForTesting { get; set; }
        internal Action<string, string, IReadOnlyList<WatchChangedMovie>> RefreshMovieViewFromCurrentSourceForTesting { get; set; }

        /// <summary>
        /// フォルダ更新要求をキューにブチ込む！連打されても後続1回に圧縮してPCの爆発を防ぐ超優秀な門番処理！🚧
        /// </summary>
        private Task QueueCheckFolderAsync(CheckMode mode, string trigger)
        {
            if (ShouldSuppressWatchWorkByUi(IsWatchSuppressedByUi(), mode == CheckMode.Watch))
            {
                MarkWatchWorkDeferredWhileSuppressed(trigger);
                return Task.CompletedTask;
            }

            QueueCheckFolderAsyncRequestedForTesting?.Invoke(mode.ToString(), trigger);

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
            using IDisposable uiHangScope = TrackUiHangActivity(UiHangActivityKind.Watch);
            if (ShouldSuppressWatchWorkByUi(IsWatchSuppressedByUi(), mode == CheckMode.Watch))
            {
                MarkWatchWorkDeferredWhileSuppressed($"check-start:{mode}");
                return;
            }

            Stopwatch sw = Stopwatch.StartNew();
            bool FolderCheckflg = false;
            List<WatchChangedMovie> changedMoviesForUiReload = [];
            int checkedFolderCount = 0;
            int enqueuedCount = 0;
            string checkExt = Properties.Settings.Default.CheckExt;
            bool watchStoppedByUiSuppression = false;

            // 🔥 開始時のDB情報をスナップショット！途中でDB切り替えが起きても混入しない！🛡️
            string snapshotDbFullPath = MainVM.DbInfo.DBFullPath;
            string snapshotThumbFolder = MainVM.DbInfo.ThumbFolder;
            string snapshotDbName = MainVM.DbInfo.DBName;
            int snapshotTabIndex = MainVM.DbInfo.CurrentTabIndex;
            int? autoEnqueueTabIndex = ResolveWatchMissingThumbnailTabIndex(snapshotTabIndex);
            bool allowMissingTabAutoEnqueue = autoEnqueueTabIndex.HasValue;
            long snapshotWatchScanScopeStamp = ReadCurrentWatchScanScopeStamp();
            bool canUseQueryOnlyWatchReload =
                mode == CheckMode.Watch && !IsStartupFeedPartialActive;

            void mergeChangedMovies(IEnumerable<WatchChangedMovie> changedMovies)
            {
                changedMoviesForUiReload = MergeChangedMovies(
                    changedMoviesForUiReload,
                    changedMovies
                );
            }

            DebugRuntimeLog.TaskStart(
                nameof(CheckFolderAsync),
                $"mode={mode} db='{snapshotDbFullPath}'"
            );

            // 呼び出し元（OpenDatafile等UIスレッド）をすぐ返すため、最初に非同期コンテキストへ切り替える。
            await Task.Yield();

            var title = "フォルダ監視中";
            var Message = "";
            // ----- [1] 既存DB/表示状態のスナップショット -----
            // movieテーブルを1回だけ読み、以降の存在確認は辞書参照で高速化する。
            Dictionary<string, WatchMainDbMovieSnapshot> existingMovieByPath = await Task.Run(() =>
                BuildExistingMovieSnapshotByPath(snapshotDbFullPath)
            );
            // 画面ソースに現在どこまで載っているかを先にスナップショット化し、既存DB行の表示欠落を補正する。
            HashSet<string> existingViewMoviePaths = await BuildCurrentViewMoviePathLookupAsync();
            (
                HashSet<string> displayedMoviePaths,
                string searchKeyword
            ) = await BuildCurrentDisplayedMovieStateAsync();
            HashSet<string> visibleMoviePaths = await BuildCurrentVisibleMoviePathLookupAsync();
            bool restrictWatchWorkToVisibleMovies = false;
            int currentWatchQueueActiveCount = 0;
            void RefreshWatchVisibleMovieGate(string reason)
            {
                if (mode != CheckMode.Watch || visibleMoviePaths.Count < 1)
                {
                    return;
                }

                if (!TryGetCurrentQueueActiveCount(out int refreshedActiveCount))
                {
                    return;
                }

                currentWatchQueueActiveCount = refreshedActiveCount;
                bool nextRestrict = ShouldRestrictWatchWorkToVisibleMovies(
                    mode == CheckMode.Watch,
                    currentWatchQueueActiveCount,
                    WatchVisibleOnlyQueueThreshold,
                    snapshotTabIndex,
                    visibleMoviePaths.Count
                );
                if (nextRestrict == restrictWatchWorkToVisibleMovies)
                {
                    return;
                }

                restrictWatchWorkToVisibleMovies = nextRestrict;
                DebugRuntimeLog.Write(
                    "watch-check",
                    nextRestrict
                        ? $"watch visible-only gate enabled: active={currentWatchQueueActiveCount} threshold={WatchVisibleOnlyQueueThreshold} tab={snapshotTabIndex} visible={visibleMoviePaths.Count} reason={reason}"
                        : $"watch visible-only gate disabled: active={currentWatchQueueActiveCount} threshold={WatchVisibleOnlyQueueThreshold} tab={snapshotTabIndex} reason={reason}"
                );
            }
            RefreshWatchVisibleMovieGate("initial");
            if (!allowMissingTabAutoEnqueue)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-tab-thumb auto enqueue suppressed: current_tab={snapshotTabIndex}"
                );
            }
            string thumbnailOutPath = allowMissingTabAutoEnqueue
                ? ResolveThumbnailOutPath(
                    autoEnqueueTabIndex.Value,
                    snapshotDbName,
                    snapshotThumbFolder
                )
                : "";
            HashSet<string> existingThumbnailFileNames = allowMissingTabAutoEnqueue
                ? await Task.Run(() => BuildThumbnailFileNameLookup(thumbnailOutPath))
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ThumbnailFailureDbService failureDbService = allowMissingTabAutoEnqueue
                ? ResolveCurrentThumbnailFailureDbService()
                : null;
            HashSet<string> openRescueRequestKeys = allowMissingTabAutoEnqueue
                ? failureDbService?.GetOpenRescueRequestKeys()
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool allowViewConsistencyRepair = !IsStartupFeedPartialActive;
            if (!allowViewConsistencyRepair)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    "view repair deferred: startup feed partial active."
                );
            }

            // モードに応じた監視設定の取得（自動更新対象のみか、全対象か）
            string sql = mode switch
            {
                CheckMode.Auto => $"SELECT * FROM watch where auto = 1",
                CheckMode.Watch => $"SELECT * FROM watch where watch = 1",
                _ => $"SELECT * FROM watch",
            };
            GetWatchTable(snapshotDbFullPath, sql);
            if (watchData == null)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan canceled: watch table load failed. db='{snapshotDbFullPath}' mode={mode}"
                );
                return;
            }

            // DB上の監視フォルダ定義1行ずつ検証していく
            foreach (DataRow row in watchData.Rows)
            {
                // 🔥 DB切り替え検知ガード！途中で別DBに切り替わったら即打ち切り！🛡️
                if (!IsCurrentWatchScanScope(snapshotDbFullPath, snapshotWatchScanScopeStamp))
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"abort scan: stale scope. snapshot_db='{snapshotDbFullPath}' current_db='{MainVM?.DbInfo?.DBFullPath ?? ""}'"
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
                long dbLookupTotalMs = 0;
                long dbInsertTotalMs = 0;
                long uiReflectTotalMs = 0;
                long enqueueFlushTotalMs = 0;
                WatchFolderScanContext folderScanContext = null;

                // Win10側の通知（トースト）領域へプログレスを出す
                if (!_hasShownFolderMonitoringNotice)
                {
                    _watchNotificationManager.Show(
                        title,
                        $"{checkFolder} 監視実施中…",
                        NotificationType.Notification,
                        "ProgressArea"
                    );
                    _hasShownFolderMonitoringNotice = true;
                }

                bool sub = ((long)row["sub"] == 1);
                if (
                    ShouldSkipWatchFolderByVisibleMovieGate(
                        restrictWatchWorkToVisibleMovies,
                        visibleMoviePaths,
                        checkFolder,
                        sub
                    )
                )
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan skipped by visible-only gate: folder='{checkFolder}' active={currentWatchQueueActiveCount} threshold={WatchVisibleOnlyQueueThreshold} visible={visibleMoviePaths.Count}"
                    );
                    continue;
                }

                try
                {
                    // ----- [2] 実際のフォルダ階層なめ (IOバウンド) を並列逃がし -----
                    // 重いファイル走査はUIスレッドを塞がないよう Task.Run(バックグラウンドスレッド) 上で実行する。
                    Stopwatch scanBackgroundStopwatch = Stopwatch.StartNew();
                    FolderScanWithStrategyResult scanStrategyResult = await Task.Run(() =>
                        ScanFolderWithStrategyInBackground(
                            mode,
                            snapshotDbFullPath,
                            snapshotWatchScanScopeStamp,
                            checkFolder,
                            sub,
                            checkExt,
                            restrictWatchWorkToVisibleMovies,
                            visibleMoviePaths
                        )
                    );
                    FolderScanResult scanResult = scanStrategyResult.ScanResult;
                    scanBackgroundStopwatch.Stop();
                    scanBackgroundElapsedMs = scanBackgroundStopwatch.ElapsedMilliseconds;
                    (string strategyDetailCode, string strategyDetailMessage) =
                        DescribeEverythingDetail(scanStrategyResult.Detail);
                    string strategyDetailCategory = FileIndexReasonTable.ToCategory(
                        scanStrategyResult.Detail
                    );
                    string strategyDetailAxis = FileIndexReasonTable.ToLogAxis(
                        scanStrategyResult.Detail
                    );
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan strategy: category={strategyDetailAxis} folder='{checkFolder}' strategy={scanStrategyResult.Strategy} detail_category={strategyDetailCategory} detail_code={strategyDetailCode} detail_message={strategyDetailMessage} scanned={scanResult.ScannedCount}"
                    );

                    if (
                        !restrictWatchWorkToVisibleMovies
                        && ShouldRunWatchFolderFullReconcile(
                            mode == CheckMode.Watch,
                            scanStrategyResult.Strategy,
                            scanResult.NewMoviePaths.Count
                        )
                    )
                    {
                        string reconcileScopeKey = BuildWatchFolderFullReconcileScopeKey(
                            snapshotDbFullPath,
                            checkFolder,
                            sub
                        );
                        if (
                            TryReserveWatchFolderFullReconcileWindow(
                                reconcileScopeKey,
                                DateTime.UtcNow,
                                out TimeSpan reconcileNextIn
                            )
                        )
                        {
                            DebugRuntimeLog.Write(
                                "watch-check",
                                $"scan reconcile start: folder='{checkFolder}' reason=watch_zero_diff"
                            );

                            Stopwatch reconcileStopwatch = Stopwatch.StartNew();
                            FolderScanWithStrategyResult reconcileResult = await Task.Run(() =>
                                ScanFolderWithStrategyInBackground(
                                    CheckMode.Manual,
                                    snapshotDbFullPath,
                                    snapshotWatchScanScopeStamp,
                                    checkFolder,
                                    sub,
                                    checkExt,
                                    false,
                                    null
                                )
                            );
                            reconcileStopwatch.Stop();

                            scanStrategyResult = reconcileResult;
                            scanResult = reconcileResult.ScanResult;
                            (
                                strategyDetailCode,
                                strategyDetailMessage
                            ) = DescribeEverythingDetail(scanStrategyResult.Detail);
                            strategyDetailCategory = FileIndexReasonTable.ToCategory(
                                scanStrategyResult.Detail
                            );
                            strategyDetailAxis = FileIndexReasonTable.ToLogAxis(
                                scanStrategyResult.Detail
                            );
                            DebugRuntimeLog.Write(
                                "watch-check",
                                $"scan reconcile end: category={strategyDetailAxis} folder='{checkFolder}' strategy={scanStrategyResult.Strategy} detail_category={strategyDetailCategory} detail_code={strategyDetailCode} detail_message={strategyDetailMessage} scanned={scanResult.ScannedCount} new={scanResult.NewMoviePaths.Count} elapsed_ms={reconcileStopwatch.ElapsedMilliseconds}"
                            );
                        }
                        else
                        {
                            DebugRuntimeLog.Write(
                                "watch-check",
                                $"scan reconcile throttled: folder='{checkFolder}' next_in_sec={Math.Ceiling(reconcileNextIn.TotalSeconds)}"
                            );
                        }
                    }

                    if (
                        scanStrategyResult.Strategy == FileIndexStrategies.Everything
                        && !_hasShownEverythingModeNotice
                    )
                    {
                        _watchNotificationManager.Show(
                            "Everything連携",
                            "Everything連携で高速スキャンを実行中です。",
                            NotificationType.Notification,
                            "ProgressArea"
                        );
                        _hasShownEverythingModeNotice = true;
                    }
                    else if (
                        scanStrategyResult.Strategy == FileIndexStrategies.Filesystem
                        && _indexProviderFacade.IsIntegrationConfigured(
                            GetEverythingIntegrationMode()
                        )
                        && !_hasShownEverythingFallbackNotice
                    )
                    {
                        _watchNotificationManager.Show(
                            "Everything連携",
                            $"Everything連携を利用できないため通常監視で継続します。({strategyDetailMessage})",
                            NotificationType.Information,
                            "ProgressArea"
                        );
                        _hasShownEverythingFallbackNotice = true;
                    }

                    useIncrementalUiMode =
                        scanResult.NewMoviePaths.Count <= IncrementalUiUpdateThreshold;
                    if (mode == CheckMode.Watch && !useIncrementalUiMode)
                    {
                        if (canUseQueryOnlyWatchReload)
                        {
                            DebugRuntimeLog.Write(
                                "watch-check",
                                $"watch final reload downgraded to full: folder='{checkFolder}' reason=bulk-watch-batch new={scanResult.NewMoviePaths.Count}"
                            );
                        }

                        canUseQueryOnlyWatchReload = false;
                    }
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan mode: folder='{checkFolder}' new={scanResult.NewMoviePaths.Count} mode={(useIncrementalUiMode ? "small" : "bulk")} threshold={IncrementalUiUpdateThreshold}"
                    );

                    List<PendingMovieRegistration> pendingNewMovies = [];
                    void WriteWatchCheckProbeIfNeeded(
                        WatchFolderScanMovieResult probeResult,
                        string movieFullPath
                    )
                    {
                        if (probeResult == null)
                        {
                            return;
                        }

                        bool isTarget = IsWatchCheckProbeTargetMovie(movieFullPath);
                        if (!isTarget && probeResult.TotalElapsedMs < WatchCheckProbeSlowThresholdMs)
                        {
                            return;
                        }

                        DebugRuntimeLog.Write(
                            "watch-check-probe",
                            $"tab={snapshotTabIndex} outcome={probeResult.Outcome} total_ms={probeResult.TotalElapsedMs} "
                                + $"db_lookup_ms={probeResult.DbLookupElapsedMs} thumb_exists_ms={probeResult.ThumbExistsElapsedMs} "
                                + $"movieinfo_ms={probeResult.MovieInfoElapsedMs} flush_wait_ms={probeResult.FlushWaitElapsedMs} path='{movieFullPath}'"
                        );
                    }

                    WatchPendingNewMovieFlushContext pendingMovieFlushContext =
                        new WatchPendingNewMovieFlushContext
                        {
                            SnapshotDbFullPath = snapshotDbFullPath,
                            ExistingMovieByPath = existingMovieByPath,
                            PendingNewMovies = pendingNewMovies,
                            UseIncrementalUiMode = useIncrementalUiMode,
                            AllowMissingTabAutoEnqueue = allowMissingTabAutoEnqueue,
                            AutoEnqueueTabIndex = autoEnqueueTabIndex,
                            ThumbnailOutPath = thumbnailOutPath,
                            ExistingThumbnailFileNames = existingThumbnailFileNames,
                            OpenRescueRequestKeys = openRescueRequestKeys,
                            AddFilesByFolder = addFilesByFolder,
                            CheckFolder = checkFolder,
                            RefreshWatchVisibleMovieGate = RefreshWatchVisibleMovieGate,
                            ShouldSuppressWatchWork = () =>
                                ShouldSuppressWatchWorkByUi(
                                    IsWatchSuppressedByUi(),
                                    mode == CheckMode.Watch
                                ),
                            IsCurrentWatchScanScope = () =>
                                mode != CheckMode.Watch
                                || IsCurrentWatchScanScope(
                                    snapshotDbFullPath,
                                    snapshotWatchScanScopeStamp
                                ),
                            MarkWatchWorkDeferredWhileSuppressedAction =
                                MarkWatchWorkDeferredWhileSuppressed,
                            InsertMoviesBatchAsync = InsertMoviesToMainDbBatchAsync,
                            AppendMovieToViewAsync = TryAppendMovieToViewByPathAsync,
                            RemovePendingMoviePlaceholderAction = RemovePendingMoviePlaceholder,
                            FlushPendingQueueItemsAction = FlushPendingQueueItems,
                        };
                    WatchScannedMovieContext scannedMovieContext = new WatchScannedMovieContext
                    {
                        SnapshotDbFullPath = snapshotDbFullPath,
                        SnapshotTabIndex = snapshotTabIndex,
                        ExistingMovieByPath = existingMovieByPath,
                        ExistingViewMoviePaths = existingViewMoviePaths,
                        DisplayedMoviePaths = displayedMoviePaths,
                        SearchKeyword = searchKeyword,
                        AllowViewConsistencyRepair = allowViewConsistencyRepair,
                        UseIncrementalUiMode = useIncrementalUiMode,
                        AllowExistingMovieDirtyTracking =
                            canUseQueryOnlyWatchReload
                            && mode == CheckMode.Watch
                            && string.Equals(
                                scanStrategyResult.Strategy,
                                FileIndexStrategies.Everything,
                                StringComparison.OrdinalIgnoreCase
                            ),
                        AllowMissingTabAutoEnqueue = allowMissingTabAutoEnqueue,
                        AutoEnqueueTabIndex = autoEnqueueTabIndex,
                        ThumbnailOutPath = thumbnailOutPath,
                        ExistingThumbnailFileNames = existingThumbnailFileNames,
                        OpenRescueRequestKeys = openRescueRequestKeys,
                        PendingMovieFlushContext = pendingMovieFlushContext,
                        ShouldSuppressWatchWork = () =>
                            ShouldSuppressWatchWorkByUi(
                                IsWatchSuppressedByUi(),
                                mode == CheckMode.Watch
                            ),
                        IsCurrentWatchScanScope = () =>
                            mode != CheckMode.Watch
                            || IsCurrentWatchScanScope(
                                snapshotDbFullPath,
                                snapshotWatchScanScopeStamp
                            ),
                        AppendMovieToViewAsync = TryAppendMovieToViewByPathAsync,
                    };
                    folderScanContext = new WatchFolderScanContext
                    {
                        RestrictWatchWorkToVisibleMovies = restrictWatchWorkToVisibleMovies,
                        VisibleMoviePaths = visibleMoviePaths,
                        AllowMissingTabAutoEnqueue = allowMissingTabAutoEnqueue,
                        AutoEnqueueTabIndex = autoEnqueueTabIndex,
                        ScannedMovieContext = scannedMovieContext,
                        TryDeferWatchFolderPreprocessByUiSuppressionAction = (
                            remainingScanPaths,
                            trigger
                        ) =>
                            TryDeferWatchFolderWorkByUiSuppression(
                                mode,
                                snapshotDbFullPath,
                                snapshotWatchScanScopeStamp,
                                checkFolder,
                                sub,
                                [],
                                remainingScanPaths,
                                pendingNewMovies,
                                addFilesByFolder,
                                trigger
                            ),
                        TryDeferWatchFolderMidByUiSuppressionAction = (
                            remainingScanPaths,
                            trigger
                        ) =>
                            TryDeferWatchFolderWorkByUiSuppression(
                                mode,
                                snapshotDbFullPath,
                                snapshotWatchScanScopeStamp,
                                checkFolder,
                                sub,
                                [],
                                remainingScanPaths,
                                pendingNewMovies,
                                addFilesByFolder,
                                trigger
                            ),
                        TryDeferWatchFolderWorkByUiSuppressionAction = trigger =>
                            TryDeferWatchFolderWorkByUiSuppression(
                                mode,
                                snapshotDbFullPath,
                                snapshotWatchScanScopeStamp,
                                checkFolder,
                                sub,
                                [],
                                [],
                                folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext?.PendingNewMovies,
                                folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext?.AddFilesByFolder,
                                trigger
                            ),
                        NotifyFolderFirstHit = () =>
                        {
                            Message = checkFolder;
                            if (!_hasShownFolderMonitoringNotice)
                            {
                                _watchNotificationManager.Show(
                                    title,
                                    $"{Message}に更新あり。",
                                    NotificationType.Notification,
                                    "ProgressArea"
                                );
                                _hasShownFolderMonitoringNotice = true;
                            }
                        },
                    };

                    if (
                        TryDeferWatchFolderPreprocess(
                            folderScanContext,
                            scanResult.NewMoviePaths
                        )
                    )
                    {
                        watchStoppedByUiSuppression = true;
                        break;
                    }

                    if (IsWatchFolderScopeStale(folderScanContext))
                    {
                        DebugRuntimeLog.Write(
                            "watch-check",
                            $"abort scan after background scan: stale scope. folder='{checkFolder}'"
                        );
                        return;
                    }

                    // ----- [3] 見つかった「新規ファイル」だけに対する処理 -----
                    for (int movieIndex = 0; movieIndex < scanResult.NewMoviePaths.Count; movieIndex++)
                    {
                        if (
                            TryDeferWatchFolderMid(
                                folderScanContext,
                                scanResult.NewMoviePaths.Skip(movieIndex)
                            )
                        )
                        {
                            watchStoppedByUiSuppression = true;
                            break;
                        }

                        if (IsWatchFolderScopeStale(folderScanContext))
                        {
                            DebugRuntimeLog.Write(
                                "watch-check",
                                $"abort scan mid folder: stale scope. folder='{checkFolder}'"
                            );
                            return;
                        }

                        string movieFullPath = scanResult.NewMoviePaths[movieIndex];
                        WatchFolderScanMovieResult processResult =
                            await ProcessWatchFolderScanMovieAsync(
                                folderScanContext,
                                movieFullPath
                            );
                        if (processResult.WasDroppedByStaleScope)
                        {
                            DebugRuntimeLog.Write(
                                "watch-check",
                                $"abort scan in coordinator: stale scope. folder='{checkFolder}' movie='{movieFullPath}'"
                            );
                            return;
                        }

                        dbLookupTotalMs += processResult.DbLookupElapsedMs;
                        movieInfoTotalMs += processResult.MovieInfoElapsedMs;
                        dbInsertTotalMs += processResult.DbInsertElapsedMs;
                        uiReflectTotalMs += processResult.UiReflectElapsedMs;
                        enqueueFlushTotalMs += processResult.EnqueueFlushElapsedMs;
                        addedByFolderCount += processResult.AddedByFolderCount;
                        enqueuedCount += processResult.EnqueuedCount;
                        FolderCheckflg |= processResult.HasFolderUpdate;
                        mergeChangedMovies(processResult.ChangedMovies);
                        WriteWatchCheckProbeIfNeeded(processResult, movieFullPath);
                        if (processResult.DeferredMoviePathsByUiSuppression.Count > 0)
                        {
                            MergeWatchFolderDeferredWorkByUiSuppression(
                                snapshotDbFullPath,
                                snapshotWatchScanScopeStamp,
                                checkFolder,
                                sub,
                                processResult.DeferredMoviePathsByUiSuppression,
                                scanResult.NewMoviePaths.Skip(movieIndex + 1),
                                pendingNewMovies,
                                addFilesByFolder
                            );
                            watchStoppedByUiSuppression = true;
                            break;
                        }
                    }

                    if (watchStoppedByUiSuppression)
                    {
                        break;
                    }

                    if (
                        TryDeferWatchFolderWorkByUiSuppression(
                            mode,
                            snapshotDbFullPath,
                            snapshotWatchScanScopeStamp,
                            checkFolder,
                            sub,
                            [],
                            [],
                            pendingNewMovies,
                            addFilesByFolder,
                            $"folder-before-final-flush:{checkFolder}"
                        )
                    )
                    {
                        watchStoppedByUiSuppression = true;
                        break;
                    }

                    if (IsWatchFolderScopeStale(folderScanContext))
                    {
                        DebugRuntimeLog.Write(
                            "watch-check",
                            $"abort scan before final flush: stale scope. folder='{checkFolder}'"
                        );
                        return;
                    }

                    // 端数の新規登録バッファを最後にまとめてDB反映する。
                    WatchPendingNewMovieFlushResult finalPendingMovieFlushResult =
                        await FlushPendingNewMoviesAsync(pendingMovieFlushContext);
                    if (finalPendingMovieFlushResult.WasDroppedByStaleScope)
                    {
                        DebugRuntimeLog.Write(
                            "watch-check",
                            $"abort scan in pending flush: stale scope. folder='{checkFolder}'"
                        );
                        return;
                    }

                    dbInsertTotalMs += finalPendingMovieFlushResult.DbInsertElapsedMs;
                    uiReflectTotalMs += finalPendingMovieFlushResult.UiReflectElapsedMs;
                    enqueueFlushTotalMs += finalPendingMovieFlushResult.EnqueueFlushElapsedMs;
                    addedByFolderCount += finalPendingMovieFlushResult.AddedByFolderCount;
                    enqueuedCount += finalPendingMovieFlushResult.EnqueuedCount;
                    mergeChangedMovies(finalPendingMovieFlushResult.ChangedMovies);
                    if (finalPendingMovieFlushResult.DeferredMoviePathsByUiSuppression.Count > 0)
                    {
                        MergeWatchFolderDeferredWorkByUiSuppression(
                            snapshotDbFullPath,
                            snapshotWatchScanScopeStamp,
                            checkFolder,
                            sub,
                            finalPendingMovieFlushResult.DeferredMoviePathsByUiSuppression,
                            [],
                            pendingNewMovies,
                            addFilesByFolder
                        );
                        watchStoppedByUiSuppression = true;
                        break;
                    }

                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan file summary: folder='{checkFolder}' scanned={scanResult.ScannedCount} new={scanResult.NewMoviePaths.Count}"
                    );
                }
                catch (Exception e)
                {
                    canUseQueryOnlyWatchReload = false;
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan folder failed: folder='{checkFolder}' type={e.GetType().Name} message='{e.Message}'"
                    );

                    // ここまでに検出済みの新規動画は、可能な限りDBへ逃がして全損を避ける。
                    WatchPendingNewMovieFlushResult recoveryFlushResult =
                        await TryFlushPendingNewMoviesAfterFolderFailureAsync(
                            checkFolder,
                            folderScanContext
                        );
                    dbInsertTotalMs += recoveryFlushResult.DbInsertElapsedMs;
                    uiReflectTotalMs += recoveryFlushResult.UiReflectElapsedMs;
                    enqueueFlushTotalMs += recoveryFlushResult.EnqueueFlushElapsedMs;
                    addedByFolderCount += recoveryFlushResult.AddedByFolderCount;
                    enqueuedCount += recoveryFlushResult.EnqueuedCount;
                    FolderCheckflg |= recoveryFlushResult.AddedByFolderCount > 0;
                    mergeChangedMovies(recoveryFlushResult.ChangedMovies);
                    if (recoveryFlushResult.DeferredMoviePathsByUiSuppression.Count > 0)
                    {
                        MergeWatchFolderDeferredWorkByUiSuppression(
                            snapshotDbFullPath,
                            snapshotWatchScanScopeStamp,
                            checkFolder,
                            sub,
                            recoveryFlushResult.DeferredMoviePathsByUiSuppression,
                            [],
                            folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext?.PendingNewMovies,
                            folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext?.AddFilesByFolder
                        );
                        watchStoppedByUiSuppression = true;
                    }

                    // 走査失敗時は仮表示を残し続けないよう、対象フォルダ分を掃除する。
                    ClearPendingMoviePlaceholdersByFolder(checkFolder);
                    //起動中に監視フォルダにファイルコピーされっと例外発生するんよね。
                    if (e.GetType() == typeof(IOException))
                    {
                        await Task.Delay(1000);
                    }
                }

                if (watchStoppedByUiSuppression)
                {
                    break;
                }

                if (
                    TryDeferWatchFolderWorkByUiSuppression(
                        mode,
                        snapshotDbFullPath,
                        snapshotWatchScanScopeStamp,
                        checkFolder,
                        sub,
                        [],
                        [],
                        folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext?.PendingNewMovies,
                        folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext?.AddFilesByFolder,
                        $"folder-final-queue:{checkFolder}"
                    )
                )
                {
                    watchStoppedByUiSuppression = true;
                    break;
                }

                if (IsWatchFolderScopeStale(folderScanContext))
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"abort scan before final queue flush: stale scope. folder='{checkFolder}'"
                    );
                    return;
                }

                // ----- [4] バッファの残りを全てキューに流す -----
                // 100件未満の端数を最後に流し切る。
                WatchFinalQueueFlushResult finalQueueFlushResult = FlushFinalWatchFolderQueue(
                    folderScanContext
                );
                if (finalQueueFlushResult.WasDroppedByStaleScope)
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"abort scan in final queue flush: stale scope. folder='{checkFolder}'"
                    );
                    return;
                }

                enqueueFlushTotalMs += finalQueueFlushResult.ElapsedMs;
                if (finalQueueFlushResult.WasStoppedByUiSuppression)
                {
                    watchStoppedByUiSuppression = true;
                    break;
                }
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan end: folder='{checkFolder}' added={addedByFolderCount} "
                        + $"mode={(useIncrementalUiMode ? "small" : "bulk")} "
                        + $"scan_bg_ms={scanBackgroundElapsedMs} movieinfo_ms={movieInfoTotalMs} db_lookup_ms={dbLookupTotalMs} "
                        + $"db_insert_ms={dbInsertTotalMs} ui_reflect_ms={uiReflectTotalMs} "
                        + $"enqueue_flush_ms={enqueueFlushTotalMs}"
                );
                await Task.Delay(100);
            }

            if (watchStoppedByUiSuppression)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan stopped by ui suppression: mode={mode} db='{snapshotDbFullPath}'"
                );
                return;
            }

            if (!IsCurrentWatchScanScope(snapshotDbFullPath, snapshotWatchScanScopeStamp))
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"abort scan before final reload: stale scope. snapshot_db='{snapshotDbFullPath}'"
                );
                return;
            }

            //stack : ファイル名を外部から変更したときに、エクステンションのファイル名が追従してなかった。強制チェックで反応はした。
            //再クリックで表示はリロードしたので、内部は変わってる。リフレッシュも漏れてる可能性あり。
            //と言うかですね。これは外部からのリネームでも、アプリでのリネームでも同じで。クリックすりゃ反映する（そりゃそうだ）

            // ----- [5] 走査全体を通していずれかのフォルダで変化があったらUI一覧を再描画 -----
            HandleFolderCheckUiReloadAfterChanges(
                FolderCheckflg,
                mode,
                snapshotDbFullPath,
                canUseQueryOnlyWatchReload,
                changedMoviesForUiReload
            );

            // Watch/Manual時は、削除されたサムネイルの取りこぼし救済を低頻度で実行する。
            if (ShouldSuppressWatchWorkByUi(IsWatchSuppressedByUi(), mode == CheckMode.Watch))
            {
                MarkWatchWorkDeferredWhileSuppressed($"missing-thumb-rescue:{mode}");
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"skip missing-thumb rescue by suppression: mode={mode} db='{snapshotDbFullPath}'"
                );
                return;
            }

            await TryRunMissingThumbnailRescueAsync(
                mode,
                snapshotDbFullPath,
                snapshotDbName,
                snapshotThumbFolder,
                snapshotTabIndex,
                snapshotWatchScanScopeStamp
            );

            sw.Stop();
            DebugRuntimeLog.TaskEnd(
                nameof(CheckFolderAsync),
                $"mode={mode} folders={checkedFolderCount} enqueued={enqueuedCount} updated={FolderCheckflg} elapsed_ms={sw.ElapsedMilliseconds}"
            );
        }

        // folder単位の例外でも、途中まで積めた新規動画だけはDBへ逃がして全損を避ける。
        private async Task<WatchPendingNewMovieFlushResult> TryFlushPendingNewMoviesAfterFolderFailureAsync(
            string checkFolder,
            WatchFolderScanContext folderScanContext
        )
        {
            WatchPendingNewMovieFlushContext pendingContext =
                folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext;
            if (pendingContext?.PendingNewMovies == null || pendingContext.PendingNewMovies.Count < 1)
            {
                return WatchPendingNewMovieFlushResult.None;
            }

            try
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan folder recovery flush start: folder='{checkFolder}' pending={pendingContext.PendingNewMovies.Count}"
                );
                WatchPendingNewMovieFlushResult result = await FlushPendingNewMoviesAsync(
                    pendingContext
                );
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan folder recovery flush end: folder='{checkFolder}' added={result.AddedByFolderCount} enqueued={result.EnqueuedCount} dropped={result.WasDroppedByStaleScope}"
                );
                return result;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan folder recovery flush failed: folder='{checkFolder}' type={ex.GetType().Name} message='{ex.Message}'"
                );
                return WatchPendingNewMovieFlushResult.None;
            }
        }

        // Watch差分では「動画更新なし + サムネ削除」の取りこぼしが起き得るため、低頻度で欠損救済を実行する。
        private async Task TryRunMissingThumbnailRescueAsync(
            CheckMode mode,
            string snapshotDbFullPath,
            string snapshotDbName,
            string snapshotThumbFolder,
            int snapshotTabIndex,
            long requestScopeStamp
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

            MissingThumbnailRescueGuardAction guardAction = GetMissingThumbnailRescueGuardAction(
                mode == CheckMode.Watch,
                snapshotDbFullPath,
                requestScopeStamp
            );
            if (guardAction == MissingThumbnailRescueGuardAction.DropStaleScope)
            {
                return;
            }

            if (guardAction == MissingThumbnailRescueGuardAction.DeferByUiSuppression)
            {
                MarkWatchWorkDeferredWhileSuppressed($"missing-thumb-rescue:{mode}");
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"skip missing-thumb rescue by suppression: mode={mode} db='{snapshotDbFullPath}'"
                );
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
                    snapshotThumbFolder,
                    mode == CheckMode.Watch,
                    requestScopeStamp
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

        // DB切り替え時は旧watch差分の持ち越しを残さない。
        private void ClearDeferredWatchScanStates()
        {
            InvalidateWatchScanScope("clear-deferred-state");
            lock (_deferredWatchScanSync)
            {
                _deferredWatchScanStateByScope.Clear();
            }
        }

        // deferred state は先読みだけにし、同じ watch 回で新規再収集との再マージへ使う。
        private bool TryPeekDeferredWatchScanState(
            string dbFullPath,
            long requestScopeStamp,
            string watchFolder,
            bool includeSubfolders,
            out DeferredWatchScanStateSnapshot stateSnapshot
        )
        {
            if (!IsCurrentWatchScanScope(dbFullPath, requestScopeStamp))
            {
                stateSnapshot = default;
                return false;
            }

            string scopeKey = BuildDeferredWatchScanScopeKey(
                dbFullPath,
                watchFolder,
                includeSubfolders
            );
            lock (_deferredWatchScanSync)
            {
                if (
                    !_deferredWatchScanStateByScope.TryGetValue(
                        scopeKey,
                        out DeferredWatchScanState state
                    )
                    || state.PendingPaths.Count < 1
                )
                {
                    stateSnapshot = default;
                    return false;
                }

                List<string> pendingPaths = state.PendingPaths
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                if (pendingPaths.Count < 1)
                {
                    stateSnapshot = default;
                    return false;
                }

                stateSnapshot = new DeferredWatchScanStateSnapshot(
                    pendingPaths,
                    state.DeferredCursorUtc
                );
                return true;
            }
        }

        // 今回処理しきれない watch 候補は、次回以降へ回す。
        private void ReplaceDeferredWatchScanBatch(
            string dbFullPath,
            long requestScopeStamp,
            string watchFolder,
            bool includeSubfolders,
            IEnumerable<string> deferredPaths,
            DateTime? deferredCursorUtc
        )
        {
            List<string> sanitizedPaths = deferredPaths?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList() ?? [];
            if (sanitizedPaths.Count < 1)
            {
                return;
            }

            if (!IsCurrentWatchScanScope(dbFullPath, requestScopeStamp))
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"deferred watch batch skipped stale: db='{dbFullPath}' folder='{watchFolder}'"
                );
                return;
            }

            string scopeKey = BuildDeferredWatchScanScopeKey(
                dbFullPath,
                watchFolder,
                includeSubfolders
            );
            lock (_deferredWatchScanSync)
            {
                _deferredWatchScanStateByScope[scopeKey] = new DeferredWatchScanState(
                    sanitizedPaths,
                    deferredCursorUtc
                );
            }
        }

        // manual / auto が同じフォルダを全量走査する時は、watch の持ち越し分を捨てて重複を防ぐ。
        private void RemoveDeferredWatchScanState(
            string dbFullPath,
            long requestScopeStamp,
            string watchFolder,
            bool includeSubfolders
        )
        {
            if (!IsCurrentWatchScanScope(dbFullPath, requestScopeStamp))
            {
                return;
            }

            string scopeKey = BuildDeferredWatchScanScopeKey(
                dbFullPath,
                watchFolder,
                includeSubfolders
            );
            lock (_deferredWatchScanSync)
            {
                _deferredWatchScanStateByScope.Remove(scopeKey);
            }
        }

        // suppression で止めた今回分は、既存deferredの先頭へ積み直して catch-up で先に回収する。
        private void MergeDeferredWatchScanBatch(
            string dbFullPath,
            long requestScopeStamp,
            string watchFolder,
            bool includeSubfolders,
            IEnumerable<string> deferredPaths
        )
        {
            List<string> mergedPaths = deferredPaths?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList() ?? [];
            if (mergedPaths.Count < 1)
            {
                return;
            }

            DeferredWatchScanStateSnapshot existingState = default;
            bool hasExistingState = TryPeekDeferredWatchScanState(
                dbFullPath,
                requestScopeStamp,
                watchFolder,
                includeSubfolders,
                out existingState
            );
            if (hasExistingState && existingState.PendingPaths.Count > 0)
            {
                mergedPaths = MergeWatchDeferredPathsForUiSuppression(
                    mergedPaths,
                    existingState.PendingPaths,
                    []
                );
            }

            ReplaceDeferredWatchScanBatch(
                dbFullPath,
                requestScopeStamp,
                watchFolder,
                includeSubfolders,
                mergedPaths,
                existingState.DeferredCursorUtc
            );
        }

        /// <summary>
        /// Everything連携を優先して走査し、利用不可時は既存のファイルシステム走査へフォールバックする。
        /// Everything優先で候補収集し、利用不可時だけ既存のファイルシステム走査へ戻す。
        /// </summary>
        private FolderScanWithStrategyResult ScanFolderWithStrategyInBackground(
            CheckMode mode,
            string snapshotDbFullPath,
            long requestScopeStamp,
            string checkFolder,
            bool sub,
            string checkExt,
            bool prioritizeVisibleMovies,
            ISet<string> visibleMoviePaths
        )
        {
            DeferredWatchScanStateSnapshot deferredState = default;
            if (mode != CheckMode.Watch)
            {
                RemoveDeferredWatchScanState(
                    snapshotDbFullPath,
                    requestScopeStamp,
                    checkFolder,
                    sub
                );
            }
            else
            {
                TryPeekDeferredWatchScanState(
                    snapshotDbFullPath,
                    requestScopeStamp,
                    checkFolder,
                    sub,
                    out deferredState
                );
            }

            if (!IsEverythingEligiblePath(checkFolder, out string eligibilityReason))
            {
                FolderScanResult notEligibleFallback = ScanFolderInBackground(
                    checkFolder,
                    sub,
                    checkExt
                );
                if (mode == CheckMode.Watch)
                {
                    return FinalizeWatchScanWithDeferredCandidates(
                        snapshotDbFullPath,
                        requestScopeStamp,
                        checkFolder,
                        sub,
                        deferredState,
                        notEligibleFallback.ScannedCount,
                        notEligibleFallback.NewMoviePaths,
                        FileIndexStrategies.Filesystem,
                        $"{EverythingReasonCodes.PathNotEligiblePrefix}{eligibilityReason}",
                        prioritizeVisibleMovies,
                        visibleMoviePaths,
                        changedSinceUtc: null,
                        observedCursorUtc: null
                    );
                }

                return new FolderScanWithStrategyResult(
                    notEligibleFallback,
                    FileIndexStrategies.Filesystem,
                    $"{EverythingReasonCodes.PathNotEligiblePrefix}{eligibilityReason}"
                );
            }

            DateTime? changedSinceUtc =
                mode == CheckMode.Watch
                    ? LoadEverythingLastSyncUtc(
                        snapshotDbFullPath,
                        requestScopeStamp,
                        checkFolder,
                        sub
                    )
                    : null;
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
                    // ゴミ箱配下は検出対象から外し、watch本流へ混ぜない。
                    if (WatchPathFilter.ShouldExcludeFromWatchScan(fullPath))
                    {
                        continue;
                    }

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
                if (mode == CheckMode.Watch)
                {
                    return FinalizeWatchScanWithDeferredCandidates(
                        snapshotDbFullPath,
                        requestScopeStamp,
                        checkFolder,
                        sub,
                        deferredState,
                        scannedCount,
                        newMoviePaths,
                        FileIndexStrategies.Everything,
                        reason,
                        prioritizeVisibleMovies,
                        visibleMoviePaths,
                        changedSinceUtc,
                        nextSyncUtc
                    );
                }

                if (FileIndexIncrementalSyncPolicy.ShouldAdvanceCursor(nextSyncUtc, changedSinceUtc))
                {
                    SaveEverythingLastSyncUtc(
                        snapshotDbFullPath,
                        requestScopeStamp,
                        checkFolder,
                        sub,
                        nextSyncUtc.Value
                    );
                }
                return new FolderScanWithStrategyResult(
                    new FolderScanResult(scannedCount, newMoviePaths),
                    FileIndexStrategies.Everything,
                    reason
                );
            }

            FolderScanResult fallbackResult = ScanFolderInBackground(checkFolder, sub, checkExt);
            if (mode == CheckMode.Watch)
            {
                return FinalizeWatchScanWithDeferredCandidates(
                    snapshotDbFullPath,
                    requestScopeStamp,
                    checkFolder,
                    sub,
                    deferredState,
                    fallbackResult.ScannedCount,
                    fallbackResult.NewMoviePaths,
                    FileIndexStrategies.Filesystem,
                    reason,
                    prioritizeVisibleMovies,
                    visibleMoviePaths,
                    changedSinceUtc,
                    observedCursorUtc: null
                );
            }
            return new FolderScanWithStrategyResult(
                fallbackResult,
                FileIndexStrategies.Filesystem,
                reason
            );
        }

        // deferred backlog と今回収集分を同一回で再マージし、visible-first と cursor 保持を崩さない。
        private FolderScanWithStrategyResult FinalizeWatchScanWithDeferredCandidates(
            string snapshotDbFullPath,
            long requestScopeStamp,
            string checkFolder,
            bool sub,
            DeferredWatchScanStateSnapshot deferredState,
            int scannedCount,
            IReadOnlyList<string> collectedPaths,
            string strategy,
            string reason,
            bool prioritizeVisibleMovies,
            ISet<string> visibleMoviePaths,
            DateTime? changedSinceUtc,
            DateTime? observedCursorUtc
        )
        {
            (
                List<string> immediatePaths,
                List<string> deferredPaths
            ) = MergeDeferredAndCollectedWatchScanMoviePaths(
                deferredState.PendingPaths,
                collectedPaths,
                WatchScanProcessLimit,
                prioritizeVisibleMovies,
                visibleMoviePaths
            );
            DateTime? cursorToPersistUtc = MergeDeferredWatchScanCursorUtc(
                deferredState.DeferredCursorUtc,
                observedCursorUtc
            );
            if (deferredPaths.Count > 0)
            {
                // 次回送りが残る間は cursor だけ state 側へ持たせ、watch またぎでも visible を拾い直す。
                ReplaceDeferredWatchScanBatch(
                    snapshotDbFullPath,
                    requestScopeStamp,
                    checkFolder,
                    sub,
                    deferredPaths,
                    cursorToPersistUtc
                );
                return new FolderScanWithStrategyResult(
                    new FolderScanResult(scannedCount, immediatePaths),
                    strategy,
                    $"{reason} watch_batch_limit={WatchScanProcessLimit} deferred={deferredPaths.Count}"
                );
            }

            RemoveDeferredWatchScanState(
                snapshotDbFullPath,
                requestScopeStamp,
                checkFolder,
                sub
            );
            if (
                cursorToPersistUtc.HasValue
                && (
                    string.Equals(
                        strategy,
                        FileIndexStrategies.Everything,
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? FileIndexIncrementalSyncPolicy.ShouldAdvanceCursor(
                            cursorToPersistUtc,
                            changedSinceUtc
                        )
                        : deferredState.DeferredCursorUtc.HasValue
                )
            )
            {
                SaveEverythingLastSyncUtc(
                    snapshotDbFullPath,
                    requestScopeStamp,
                    checkFolder,
                    sub,
                    cursorToPersistUtc.Value
                );
            }

            return new FolderScanWithStrategyResult(
                new FolderScanResult(scannedCount, immediatePaths),
                strategy,
                reason
            );
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

        // deferred state を先読みする時は、可変Queueの実体を外へ漏らさず値で扱う。
        private readonly record struct DeferredWatchScanStateSnapshot(
            List<string> PendingPaths,
            DateTime? DeferredCursorUtc
        );

        // 1回で処理しきれない watch 候補は、フォルダ単位で次回以降へ持ち越す。
        private sealed class DeferredWatchScanState
        {
            public DeferredWatchScanState(IEnumerable<string> pendingPaths, DateTime? deferredCursorUtc)
            {
                PendingPaths = new Queue<string>(pendingPaths ?? []);
                DeferredCursorUtc = deferredCursorUtc;
            }

            public Queue<string> PendingPaths { get; }
            public DateTime? DeferredCursorUtc { get; }
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
        internal sealed class PendingMovieRegistration
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
            await EnqueueMissingThumbnailsAsync(
                targetTabIndex,
                snapshotDbFullPath,
                snapshotDbName,
                snapshotThumbFolder,
                isWatchMode: false,
                requestScopeStamp: 0
            );
        }

        private async Task EnqueueMissingThumbnailsAsync(
            int targetTabIndex,
            string snapshotDbFullPath,
            string snapshotDbName,
            string snapshotThumbFolder,
            bool isWatchMode,
            long requestScopeStamp
        )
        {
            if (string.IsNullOrWhiteSpace(snapshotDbFullPath))
                return;

            MissingThumbnailRescueGuardAction guardAction = GetMissingThumbnailRescueGuardAction(
                isWatchMode,
                snapshotDbFullPath,
                requestScopeStamp
            );
            if (guardAction == MissingThumbnailRescueGuardAction.DropStaleScope)
            {
                return;
            }

            if (guardAction == MissingThumbnailRescueGuardAction.DeferByUiSuppression)
            {
                MarkWatchWorkDeferredWhileSuppressed("missing-thumb-rescue:enqueue");
                return;
            }

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
                            guardAction = GetMissingThumbnailRescueGuardAction(
                                isWatchMode,
                                snapshotDbFullPath,
                                requestScopeStamp
                            );
                            if (guardAction == MissingThumbnailRescueGuardAction.DropStaleScope)
                            {
                                return;
                            }

                            if (
                                guardAction
                                == MissingThumbnailRescueGuardAction.DeferByUiSuppression
                            )
                            {
                                MarkWatchWorkDeferredWhileSuppressed(
                                    "missing-thumb-rescue:flush"
                                );
                                return;
                            }

                            FlushPendingQueueItems(batch, "RescueMissingThumbnails");
                            await Task.Delay(50); // 少し息継ぎ
                        }
                    }
                }
            }

            // 残りを流し込む
            if (batch.Count > 0)
            {
                guardAction = GetMissingThumbnailRescueGuardAction(
                    isWatchMode,
                    snapshotDbFullPath,
                    requestScopeStamp
                );
                if (guardAction == MissingThumbnailRescueGuardAction.DropStaleScope)
                {
                    return;
                }

                if (guardAction == MissingThumbnailRescueGuardAction.DeferByUiSuppression)
                {
                    MarkWatchWorkDeferredWhileSuppressed("missing-thumb-rescue:flush-final");
                    return;
                }

                FlushPendingQueueItems(batch, "RescueMissingThumbnails");
            }

            DebugRuntimeLog.Write(
                "rescue-thumb",
                $"finished rescue missing thumbs for tab={targetTabIndex}. enqueued={enqueuedCount}"
            );
        }
    }
}
