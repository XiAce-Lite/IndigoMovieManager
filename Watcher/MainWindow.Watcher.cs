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
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // =================================================================================
        // フォルダ監視・走査に関するバックグラウンド処理 (Modelに近く、インフラ層にまたがる)
        // ローカルディスク上のファイル増減を検知し、DBやサムネイル作成キューと同期させる役割。
        // =================================================================================

        // UIへ逐次反映する上限件数。小規模時は体感を優先して1件ずつ表示する。
        private const int IncrementalUiUpdateThreshold = 20;
        // backlog が大きい時は、今見えている動画だけへ watch の仕事を絞ってUIテンポを守る。
        private const int WatchVisibleOnlyQueueThreshold = 500;

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

            DebugRuntimeLog.TaskStart(
                nameof(CheckFolderAsync),
                $"mode={mode} db='{snapshotDbFullPath}'"
            );

            // 呼び出し元（OpenDatafile等UIスレッド）をすぐ返すため、最初に非同期コンテキストへ切り替える。
            await Task.Yield();

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
            (restrictWatchWorkToVisibleMovies, currentWatchQueueActiveCount) =
                RefreshWatchVisibleMovieGate(
                    mode == CheckMode.Watch,
                    visibleMoviePaths,
                    WatchVisibleOnlyQueueThreshold,
                    snapshotTabIndex,
                    () =>
                        TryGetCurrentQueueActiveCount(out int refreshedActiveCount)
                            ? refreshedActiveCount
                            : (int?)null,
                    restrictWatchWorkToVisibleMovies,
                    currentWatchQueueActiveCount,
                    "initial"
                );
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
                if (
                    TryAbortWatchScanForStaleScope(
                        snapshotDbFullPath,
                        snapshotWatchScanScopeStamp,
                        "",
                        includeCurrentDb: true
                    )
                )
                {
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
                ShowFolderMonitoringNoticeIfNeeded(
                    "フォルダ監視中",
                    $"{checkFolder} 監視実施中…"
                );

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

                    if (scanStrategyResult.Strategy == FileIndexStrategies.Everything)
                    {
                        ShowEverythingModeNoticeIfNeeded();
                    }
                    else if (
                        scanStrategyResult.Strategy == FileIndexStrategies.Filesystem
                        && _indexProviderFacade.IsIntegrationConfigured(
                            GetEverythingIntegrationMode()
                        )
                    )
                    {
                        ShowEverythingFallbackNoticeIfNeeded(strategyDetailMessage);
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
                            RefreshWatchVisibleMovieGate = reason =>
                            {
                                (restrictWatchWorkToVisibleMovies, currentWatchQueueActiveCount) =
                                    RefreshWatchVisibleMovieGate(
                                        mode == CheckMode.Watch,
                                        visibleMoviePaths,
                                        WatchVisibleOnlyQueueThreshold,
                                        snapshotTabIndex,
                                        () =>
                                            TryGetCurrentQueueActiveCount(
                                                out int refreshedActiveCount
                                            )
                                                ? refreshedActiveCount
                                                : (int?)null,
                                        restrictWatchWorkToVisibleMovies,
                                        currentWatchQueueActiveCount,
                                        reason
                                    );
                            },
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
                        Owner = this,
                        IsWatchMode = mode == CheckMode.Watch,
                        SnapshotDbFullPath = snapshotDbFullPath,
                        SnapshotWatchScanScopeStamp = snapshotWatchScanScopeStamp,
                        Sub = sub,
                        RestrictWatchWorkToVisibleMovies = restrictWatchWorkToVisibleMovies,
                        VisibleMoviePaths = visibleMoviePaths,
                        AllowMissingTabAutoEnqueue = allowMissingTabAutoEnqueue,
                        AutoEnqueueTabIndex = autoEnqueueTabIndex,
                        ScannedMovieContext = scannedMovieContext,
                        NotifyFolderFirstHit = () =>
                            BuildNotifyFolderFirstHitAction(checkFolder),
                    };
                    folderScanContext.TryDeferWatchFolderPreprocessByUiSuppressionAction =
                        folderScanContext.TryDeferWatchFolderPreprocessByUiSuppression;
                    folderScanContext.TryDeferWatchFolderMidByUiSuppressionAction =
                        folderScanContext.TryDeferWatchFolderMidByUiSuppression;
                    folderScanContext.TryDeferWatchFolderWorkByUiSuppressionAction =
                        folderScanContext.TryDeferWatchFolderWorkByUiSuppression;

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

                    if (
                        TryAbortWatchFolderForStaleScope(
                            folderScanContext,
                            checkFolder,
                            "after background scan"
                        )
                    )
                    {
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

                        if (
                            TryAbortWatchFolderForStaleScope(
                                folderScanContext,
                                checkFolder,
                                "mid folder"
                            )
                        )
                        {
                            return;
                        }

                        string movieFullPath = scanResult.NewMoviePaths[movieIndex];
                        WatchFolderScanMovieResult processResult =
                            await ProcessWatchFolderScanMovieAsync(
                                folderScanContext,
                                movieFullPath
                            );
                        if (
                            TryAbortWatchFolderForCoordinatorStaleResult(
                                processResult,
                                checkFolder,
                                movieFullPath
                            )
                        )
                        {
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
                        changedMoviesForUiReload = MergeChangedMoviesForUiReload(
                            changedMoviesForUiReload,
                            processResult.ChangedMovies
                        );
                        WriteWatchCheckProbeIfNeeded(
                            processResult,
                            movieFullPath,
                            snapshotTabIndex
                        );
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

                    // 端数の新規登録バッファを最後にまとめてDB反映する。
                    WatchPendingNewMovieGuardResult pendingFlushGuardResult =
                        await TryFlushPendingNewMoviesWithGuardsAsync(folderScanContext);
                    if (pendingFlushGuardResult.WasDroppedByStaleScope)
                    {
                        return;
                    }
                    if (pendingFlushGuardResult.WasStoppedByUiSuppression)
                    {
                        watchStoppedByUiSuppression = true;
                        break;
                    }
                    WatchPendingNewMovieFlushResult finalPendingMovieFlushResult =
                        pendingFlushGuardResult.FlushResult;

                    dbInsertTotalMs += finalPendingMovieFlushResult.DbInsertElapsedMs;
                    uiReflectTotalMs += finalPendingMovieFlushResult.UiReflectElapsedMs;
                    enqueueFlushTotalMs += finalPendingMovieFlushResult.EnqueueFlushElapsedMs;
                    addedByFolderCount += finalPendingMovieFlushResult.AddedByFolderCount;
                    enqueuedCount += finalPendingMovieFlushResult.EnqueuedCount;
                    changedMoviesForUiReload = MergeChangedMoviesForUiReload(
                        changedMoviesForUiReload,
                        finalPendingMovieFlushResult.ChangedMovies
                    );
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
                    changedMoviesForUiReload = MergeChangedMoviesForUiReload(
                        changedMoviesForUiReload,
                        recoveryFlushResult.ChangedMovies
                    );
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

                // ----- [4] バッファの残りを全てキューに流す -----
                // 100件未満の端数を最後に流し切る。
                WatchFinalQueueFlushResult finalQueueFlushResult = TryFlushFinalWatchFolderQueueWithGuards(
                    folderScanContext
                );

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

            if (
                TryAbortWatchScanForStaleScope(
                    snapshotDbFullPath,
                    snapshotWatchScanScopeStamp,
                    "before final reload"
                )
            )
            {
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

    }
}
