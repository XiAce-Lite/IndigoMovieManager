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
            if (TryDeferWatchStart(mode))
            {
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
            (
                string snapshotDbFullPath,
                string snapshotThumbFolder,
                string snapshotDbName,
                int snapshotTabIndex,
                int? autoEnqueueTabIndex,
                bool allowMissingTabAutoEnqueue,
                long snapshotWatchScanScopeStamp,
                bool canUseQueryOnlyWatchReload
            ) = BuildWatchRunSnapshot(mode);

            WriteWatchCheckTaskStart(mode, snapshotDbFullPath);

            // 呼び出し元（OpenDatafile等UIスレッド）をすぐ返すため、最初に非同期コンテキストへ切り替える。
            await Task.Yield();

            // ----- [1] 既存DB/表示状態のスナップショット -----
            // movieテーブルを1回だけ読み、以降の存在確認は辞書参照で高速化する。
            Dictionary<string, WatchMainDbMovieSnapshot> existingMovieByPath = await Task.Run(() =>
                BuildExistingMovieSnapshotByPath(snapshotDbFullPath)
            );
            (
                HashSet<string> existingViewMoviePaths,
                HashSet<string> displayedMoviePaths,
                string searchKeyword,
                HashSet<string> visibleMoviePaths,
                bool allowViewConsistencyRepair
            ) = await BuildWatchViewSnapshotAsync();
            (
                bool restrictWatchWorkToVisibleMovies,
                int currentWatchQueueActiveCount
            ) = InitializeWatchVisibleMovieGate(
                    mode == CheckMode.Watch,
                    visibleMoviePaths,
                    WatchVisibleOnlyQueueThreshold,
                    snapshotTabIndex,
                    () =>
                        TryGetCurrentQueueActiveCount(out int refreshedActiveCount)
                            ? refreshedActiveCount
                            : (int?)null
                );
            (
                string thumbnailOutPath,
                HashSet<string> existingThumbnailFileNames,
                ThumbnailFailureDbService failureDbService,
                HashSet<string> openRescueRequestKeys
            ) = await BuildWatchMissingThumbnailSetupAsync(
                allowMissingTabAutoEnqueue,
                snapshotTabIndex,
                autoEnqueueTabIndex,
                snapshotDbName,
                snapshotThumbFolder
            );

            // モードに応じた監視設定の取得（自動更新対象のみか、全対象か）
            if (!TryLoadWatchTableForModeOrWriteFailure(mode, snapshotDbFullPath))
            {
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

                (string checkFolder, bool sub) = ResolveWatchFolderTarget(row);

                //存在しない監視フォルダは読み飛ばし。
                if (!Path.Exists(checkFolder))
                {
                    continue;
                }
                checkedFolderCount++;

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

                if (
                    TryStartWatchFolderScan(
                        mode,
                        restrictWatchWorkToVisibleMovies,
                        visibleMoviePaths,
                        checkFolder,
                        sub,
                        currentWatchQueueActiveCount
                    )
                )
                {
                    continue;
                }

                try
                {
                    (
                        FolderScanWithStrategyResult scanStrategyResult,
                        FolderScanResult scanResult,
                        useIncrementalUiMode,
                        canUseQueryOnlyWatchReload,
                        scanBackgroundElapsedMs
                    ) = await ExecuteWatchFolderScanPipelineAsync(
                        restrictWatchWorkToVisibleMovies,
                        mode,
                        snapshotDbFullPath,
                        snapshotWatchScanScopeStamp,
                        checkFolder,
                        sub,
                        checkExt,
                        visibleMoviePaths,
                        canUseQueryOnlyWatchReload
                    );

                    (
                        List<PendingMovieRegistration> pendingNewMovies,
                        WatchPendingNewMovieFlushContext pendingMovieFlushContext,
                        WatchScannedMovieContext scannedMovieContext,
                        folderScanContext
                    ) = CreateWatchFolderRuntimeContexts(
                        mode,
                        snapshotDbFullPath,
                        snapshotTabIndex,
                        snapshotWatchScanScopeStamp,
                        existingMovieByPath,
                        existingViewMoviePaths,
                        displayedMoviePaths,
                        searchKeyword,
                        allowViewConsistencyRepair,
                        useIncrementalUiMode,
                        canUseQueryOnlyWatchReload,
                        scanStrategyResult.Strategy,
                        scanStrategyResult.HasIncrementalCursor,
                        allowMissingTabAutoEnqueue,
                        autoEnqueueTabIndex,
                        thumbnailOutPath,
                        existingThumbnailFileNames,
                        openRescueRequestKeys,
                        addFilesByFolder,
                        checkFolder,
                        sub,
                        restrictWatchWorkToVisibleMovies,
                        visibleMoviePaths,
                        currentWatchQueueActiveCount
                    );

                    if (
                        TryPrepareWatchFolderMovieLoop(
                            folderScanContext,
                            checkFolder,
                            scanResult.NewMoviePaths,
                            ref watchStoppedByUiSuppression,
                            out bool shouldBreakByMovieLoopPreparation
                        )
                    )
                    {
                        return;
                    }
                    if (shouldBreakByMovieLoopPreparation)
                    {
                        break;
                    }

                    // movie loop の guard / 1件処理 / stale 打ち切り / 集計反映をまとめ、
                    // CheckFolderAsync 本体は流れだけを読めるようにする。
                    async Task<WatchLoopDecision> TryProcessWatchFolderMovieLoopAsync()
                    {
                        for (int movieIndex = 0; movieIndex < scanResult.NewMoviePaths.Count; movieIndex++)
                        {
                            if (
                                TryAdvanceWatchFolderMovieLoop(
                                    folderScanContext,
                                    checkFolder,
                                    scanResult.NewMoviePaths.Skip(movieIndex),
                                    out bool shouldBreakCurrentMovieLoopByUiSuppression
                                )
                            )
                            {
                                return new WatchLoopDecision(true, false);
                            }
                            if (shouldBreakCurrentMovieLoopByUiSuppression)
                            {
                                return new WatchLoopDecision(false, true);
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
                                return new WatchLoopDecision(true, false);
                            }

                            if (
                                TryHandleWatchProcessResultWithProbe(
                                    processResult,
                                    movieFullPath,
                                    snapshotTabIndex,
                                    snapshotDbFullPath,
                                    snapshotWatchScanScopeStamp,
                                    checkFolder,
                                    sub,
                                    scanResult.NewMoviePaths,
                                    movieIndex,
                                    pendingNewMovies,
                                    addFilesByFolder,
                                    ref dbLookupTotalMs,
                                    ref movieInfoTotalMs,
                                    ref dbInsertTotalMs,
                                    ref uiReflectTotalMs,
                                    ref enqueueFlushTotalMs,
                                    ref addedByFolderCount,
                                    ref enqueuedCount,
                                    ref FolderCheckflg,
                                    ref changedMoviesForUiReload
                                )
                            )
                            {
                                return new WatchLoopDecision(false, true);
                            }
                        }

                        return new WatchLoopDecision(false, false);
                    }

                    // ----- [3] 見つかった「新規ファイル」だけに対する処理 -----
                    WatchLoopDecision movieLoopDecision =
                        await TryProcessWatchFolderMovieLoopAsync();
                    if (
                        TryApplyWatchFolderMovieLoopDecision(
                            movieLoopDecision,
                            ref watchStoppedByUiSuppression,
                            out bool shouldBreakByMovieLoop
                        )
                    )
                    {
                        return;
                    }
                    if (shouldBreakByMovieLoop)
                    {
                        break;
                    }

                    // pending flush 実行と戻り値判定をまとめ、終盤の if 連鎖を減らす。
                    async Task<WatchLoopDecision> TryFlushWatchPendingMoviesAsync()
                    {
                        // 端数の新規登録バッファを最後にまとめてDB反映する。
                        WatchPendingNewMovieGuardResult pendingFlushGuardResult =
                            await TryFlushPendingNewMoviesWithGuardsAsync(folderScanContext);
                        if (
                            TryHandlePendingFlushSequence(
                                pendingFlushGuardResult,
                                snapshotDbFullPath,
                                snapshotWatchScanScopeStamp,
                                checkFolder,
                                sub,
                                scanResult.ScannedCount,
                                scanResult.NewMoviePaths.Count,
                                pendingNewMovies,
                                addFilesByFolder,
                                MergeWatchFolderDeferredWorkByUiSuppression,
                                ref dbInsertTotalMs,
                                ref uiReflectTotalMs,
                                ref enqueueFlushTotalMs,
                                ref addedByFolderCount,
                                ref enqueuedCount,
                                ref changedMoviesForUiReload,
                                out bool shouldBreakByUiSuppression
                            )
                        )
                        {
                            return new WatchLoopDecision(true, false);
                        }

                        return new WatchLoopDecision(false, shouldBreakByUiSuppression);
                    }

                    WatchLoopDecision pendingFlushDecision =
                        await TryFlushWatchPendingMoviesAsync();
                    if (
                        TryHandleWatchLoopDecisionWithBreak(
                            pendingFlushDecision,
                            ref watchStoppedByUiSuppression,
                            out bool shouldBreakByPendingFlush
                        )
                    )
                    {
                        return;
                    }
                    if (shouldBreakByPendingFlush)
                    {
                        break;
                    }
                }
                catch (Exception e)
                {
                    // 失敗ログと recovery flush 開始までは helper に寄せ、catch 本体は結果反映に集中させる。
                    WatchPendingNewMovieFlushResult recoveryFlushResult =
                        await RunWatchFolderFailureRecoveryAsync(checkFolder, e, folderScanContext);
                    watchStoppedByUiSuppression = TryApplyWatchFolderFailureRecoveryResult(
                        recoveryFlushResult,
                        snapshotDbFullPath,
                        snapshotWatchScanScopeStamp,
                        checkFolder,
                        sub,
                        folderScanContext,
                        ref dbInsertTotalMs,
                        ref uiReflectTotalMs,
                        ref enqueueFlushTotalMs,
                        ref addedByFolderCount,
                        ref enqueuedCount,
                        ref FolderCheckflg,
                        ref changedMoviesForUiReload
                    );

                    await HandleWatchFolderFailureTailAsync(checkFolder, e);
                    canUseQueryOnlyWatchReload = false;
                }

                if (watchStoppedByUiSuppression)
                {
                    break;
                }

                (
                    WatchLoopDecision finalQueueFlushDecision,
                    enqueueFlushTotalMs
                ) = await TryCompleteWatchFolderAsync(
                    folderScanContext,
                    checkFolder,
                    addedByFolderCount,
                    useIncrementalUiMode,
                    scanBackgroundElapsedMs,
                    movieInfoTotalMs,
                    dbLookupTotalMs,
                    dbInsertTotalMs,
                    uiReflectTotalMs,
                    enqueueFlushTotalMs
                );
                if (
                    TryHandleWatchLoopDecisionWithBreak(
                        finalQueueFlushDecision,
                        ref watchStoppedByUiSuppression,
                        out bool shouldBreakByFinalQueueFlush
                    )
                )
                {
                    return;
                }
                if (shouldBreakByFinalQueueFlush)
                {
                    break;
                }
            }

            if (
                await FinishWatchRunAsync(
                    watchStoppedByUiSuppression,
                    FolderCheckflg,
                    mode,
                    snapshotDbFullPath,
                    snapshotDbName,
                    snapshotThumbFolder,
                    snapshotTabIndex,
                    canUseQueryOnlyWatchReload,
                    changedMoviesForUiReload,
                    snapshotWatchScanScopeStamp,
                    checkedFolderCount,
                    enqueuedCount,
                    sw
                )
            )
            {
                return;
            }
        }

        // WatchLoopDecision の return / break 判定を 1 入口へ寄せ、呼び出し側の 2 段 if を減らす。
        private static bool TryHandleWatchLoopDecisionWithBreak(
            WatchLoopDecision decision,
            ref bool watchStoppedByUiSuppression,
            out bool shouldBreakByUiSuppression
        )
        {
            if (TryHandleWatchLoopDecision(decision, ref watchStoppedByUiSuppression))
            {
                shouldBreakByUiSuppression = false;
                return true;
            }

            shouldBreakByUiSuppression = watchStoppedByUiSuppression;
            return false;
        }

        // movie loop 入口の準備判定を 1 入口へ寄せ、Watcher 本体の中盤を読みやすくする。
        private bool TryPrepareWatchFolderMovieLoop(
            WatchFolderScanContext folderScanContext,
            string checkFolder,
            List<string> newMoviePaths,
            ref bool watchStoppedByUiSuppression,
            out bool shouldBreakByUiSuppression
        )
        {
            WatchLoopDecision movieLoopPreparation =
                ResolveWatchFolderMovieLoopPreparation(
                    folderScanContext,
                    checkFolder,
                    newMoviePaths
                );
            return TryHandleWatchLoopDecisionWithBreak(
                movieLoopPreparation,
                ref watchStoppedByUiSuppression,
                out shouldBreakByUiSuppression
            );
        }

        // movie loop の decision 適用を 1 入口へ寄せ、中盤の見通しを揃える。
        private bool TryApplyWatchFolderMovieLoopDecision(
            WatchLoopDecision movieLoopDecision,
            ref bool watchStoppedByUiSuppression,
            out bool shouldBreakByUiSuppression
        )
        {
            return TryHandleWatchLoopDecisionWithBreak(
                movieLoopDecision,
                ref watchStoppedByUiSuppression,
                out shouldBreakByUiSuppression
            );
        }

        // 1フォルダ走査で使う context 初期化を 1 入口へ寄せ、Watcher 本体は流れを追いやすくする。
        private (
            List<PendingMovieRegistration> PendingNewMovies,
            WatchPendingNewMovieFlushContext PendingMovieFlushContext,
            WatchScannedMovieContext ScannedMovieContext,
            WatchFolderScanContext FolderScanContext
        ) CreateWatchFolderRuntimeContexts(
            CheckMode mode,
            string snapshotDbFullPath,
            int snapshotTabIndex,
            long snapshotWatchScanScopeStamp,
            Dictionary<string, WatchMainDbMovieSnapshot> existingMovieByPath,
            HashSet<string> existingViewMoviePaths,
            HashSet<string> displayedMoviePaths,
            string searchKeyword,
            bool allowViewConsistencyRepair,
            bool useIncrementalUiMode,
            bool canUseQueryOnlyWatchReload,
            string scanStrategy,
            bool hasIncrementalCursor,
            bool allowMissingTabAutoEnqueue,
            int? autoEnqueueTabIndex,
            string thumbnailOutPath,
            HashSet<string> existingThumbnailFileNames,
            HashSet<string> openRescueRequestKeys,
            List<QueueObj> addFilesByFolder,
            string checkFolder,
            bool sub,
            bool restrictWatchWorkToVisibleMovies,
            HashSet<string> visibleMoviePaths,
            int currentWatchQueueActiveCount
        )
        {
            List<PendingMovieRegistration> pendingNewMovies = [];

            Action<string> refreshVisibleMovieGate =
                CreateRefreshWatchVisibleMovieGateAction(
                    mode,
                    visibleMoviePaths,
                    snapshotTabIndex,
                    () => (restrictWatchWorkToVisibleMovies, currentWatchQueueActiveCount),
                    (nextRestrictWatchWorkToVisibleMovies, nextCurrentWatchQueueActiveCount) =>
                    {
                        restrictWatchWorkToVisibleMovies =
                            nextRestrictWatchWorkToVisibleMovies;
                        currentWatchQueueActiveCount = nextCurrentWatchQueueActiveCount;
                    }
                );

            (
                WatchPendingNewMovieFlushContext pendingMovieFlushContext,
                WatchScannedMovieContext scannedMovieContext,
                WatchFolderScanContext folderScanContext
            ) = BuildWatchFolderScanRuntimeContexts(
                mode,
                snapshotDbFullPath,
                snapshotTabIndex,
                snapshotWatchScanScopeStamp,
                existingMovieByPath,
                existingViewMoviePaths,
                displayedMoviePaths,
                searchKeyword,
                allowViewConsistencyRepair,
                useIncrementalUiMode,
                canUseQueryOnlyWatchReload,
                scanStrategy,
                hasIncrementalCursor,
                allowMissingTabAutoEnqueue,
                autoEnqueueTabIndex,
                thumbnailOutPath,
                existingThumbnailFileNames,
                openRescueRequestKeys,
                addFilesByFolder,
                checkFolder,
                sub,
                restrictWatchWorkToVisibleMovies,
                visibleMoviePaths,
                pendingNewMovies,
                refreshVisibleMovieGate
            );

            return (
                pendingNewMovies,
                pendingMovieFlushContext,
                scannedMovieContext,
                folderScanContext
            );
        }

        // visible gate の再評価 callback を 1 か所で作り、context 初期化側のローカル関数を減らす。
        private Action<string> CreateRefreshWatchVisibleMovieGateAction(
            CheckMode mode,
            HashSet<string> visibleMoviePaths,
            int snapshotTabIndex,
            Func<(bool RestrictWatchWorkToVisibleMovies, int CurrentWatchQueueActiveCount)> getState,
            Action<bool, int> setState
        )
        {
            return reason =>
            {
                (bool restrictWatchWorkToVisibleMovies, int currentWatchQueueActiveCount) =
                    getState();
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
                        reason
                    );
                setState(restrictWatchWorkToVisibleMovies, currentWatchQueueActiveCount);
            };
        }

        // background scan から strategy detail / reconcile / scan mode 診断までを 1 入口にまとめ、
        // Watcher 本体はフォルダ走査フローだけを追えるようにする。
        private async Task<(
            FolderScanWithStrategyResult ScanStrategyResult,
            FolderScanResult ScanResult,
            bool UseIncrementalUiMode,
            bool CanUseQueryOnlyWatchReload,
            long ScanBackgroundElapsedMs
        )> ExecuteWatchFolderScanPipelineAsync(
            bool restrictWatchWorkToVisibleMovies,
            CheckMode mode,
            string snapshotDbFullPath,
            long snapshotWatchScanScopeStamp,
            string checkFolder,
            bool sub,
            string checkExt,
            HashSet<string> visibleMoviePaths,
            bool canUseQueryOnlyWatchReload
        )
        {
            // 重いファイル走査は UI スレッドを塞がないよう、バックグラウンド側で完了させる。
            (
                FolderScanWithStrategyResult scanStrategyResult,
                FolderScanResult scanResult,
                long scanBackgroundElapsedMs
            ) = await RunWatchFolderBackgroundScanAsync(
                mode,
                snapshotDbFullPath,
                snapshotWatchScanScopeStamp,
                checkFolder,
                sub,
                checkExt,
                restrictWatchWorkToVisibleMovies,
                visibleMoviePaths
            );

            string strategyDetailCode;
            string strategyDetailMessage;
            string strategyDetailCategory;
            string strategyDetailAxis;
            (
                scanStrategyResult,
                scanResult,
                strategyDetailCode,
                strategyDetailMessage,
                strategyDetailCategory,
                strategyDetailAxis
            ) = await ResolveStrategyDetailAndApplyWatchFolderFullReconcileAsync(
                restrictWatchWorkToVisibleMovies,
                mode,
                scanStrategyResult,
                scanResult,
                checkFolder,
                snapshotDbFullPath,
                sub,
                snapshotWatchScanScopeStamp,
                checkExt
            );

            (
                bool useIncrementalUiMode,
                bool nextCanUseQueryOnlyWatchReload
            ) = HandleWatchScanStrategyAndUiReloadDiagnostics(
                mode,
                checkFolder,
                scanStrategyResult.Strategy,
                strategyDetailMessage,
                scanResult.NewMoviePaths.Count,
                IncrementalUiUpdateThreshold,
                canUseQueryOnlyWatchReload
            );

            return (
                scanStrategyResult,
                scanResult,
                useIncrementalUiMode,
                nextCanUseQueryOnlyWatchReload,
                scanBackgroundElapsedMs
            );
        }

        // フォルダ走査の開始通知と visible gate をまとめ、入口の continue 条件を 1 か所へ寄せる。
        private bool TryStartWatchFolderScan(
            CheckMode mode,
            bool restrictWatchWorkToVisibleMovies,
            HashSet<string> visibleMoviePaths,
            string checkFolder,
            bool sub,
            int currentWatchQueueActiveCount
        )
        {
            // 開始ログとトースト通知を同じ入口へ寄せる。
            HandleWatchFolderScanStart(checkFolder, mode);

            return TryHandleWatchFolderVisibleGate(
                restrictWatchWorkToVisibleMovies,
                visibleMoviePaths,
                checkFolder,
                sub,
                currentWatchQueueActiveCount,
                WatchVisibleOnlyQueueThreshold
            );
        }

        // final queue flush と scan end ログをまとめ、フォルダ単位の終端処理を読みやすくする。
        private async Task<(
            WatchLoopDecision Decision,
            long UpdatedEnqueueFlushTotalMs
        )> TryCompleteWatchFolderAsync(
            WatchFolderScanContext folderScanContext,
            string checkFolder,
            int addedByFolderCount,
            bool useIncrementalUiMode,
            long scanBackgroundElapsedMs,
            long movieInfoTotalMs,
            long dbLookupTotalMs,
            long dbInsertTotalMs,
            long uiReflectTotalMs,
            long enqueueFlushTotalMs
        )
        {
            // ----- [4] バッファの残りを全てキューに流す -----
            // 100件未満の端数を最後に流し切る。
            WatchFinalQueueFlushResult finalQueueFlushResult =
                TryFlushFinalWatchFolderQueueWithGuards(folderScanContext);
            if (
                TryHandleFinalQueueFlushResult(
                    finalQueueFlushResult,
                    ref enqueueFlushTotalMs
                )
            )
            {
                return (new WatchLoopDecision(false, true), enqueueFlushTotalMs);
            }

            await WriteWatchFolderScanEndAsync(
                checkFolder,
                addedByFolderCount,
                useIncrementalUiMode,
                scanBackgroundElapsedMs,
                movieInfoTotalMs,
                dbLookupTotalMs,
                dbInsertTotalMs,
                uiReflectTotalMs,
                enqueueFlushTotalMs
            );
            return (new WatchLoopDecision(false, false), enqueueFlushTotalMs);
        }

        // 走査ループ後の abort 判定と完了処理をまとめ、Watcher 本体の末尾を素直にする。
        private async Task<bool> FinishWatchRunAsync(
            bool watchStoppedByUiSuppression,
            bool folderCheckFlag,
            CheckMode mode,
            string snapshotDbFullPath,
            string snapshotDbName,
            string snapshotThumbFolder,
            int snapshotTabIndex,
            bool canUseQueryOnlyWatchReload,
            List<WatchChangedMovie> changedMoviesForUiReload,
            long snapshotWatchScanScopeStamp,
            int checkedFolderCount,
            int enqueuedCount,
            Stopwatch sw
        )
        {
            if (
                TryAbortWatchScanBeforeFinalReload(
                    watchStoppedByUiSuppression,
                    mode,
                    snapshotDbFullPath,
                    snapshotWatchScanScopeStamp
                )
            )
            {
                return true;
            }

            // stack : ファイル名を外部から変更したときに、エクステンションのファイル名が追従してなかった。強制チェックで反応はした。
            // 再クリックで表示はリロードしたので、内部は変わってる。リフレッシュも漏れてる可能性あり。
            // と言うかですね。これは外部からのリネームでも、アプリでのリネームでも同じで。クリックすりゃ反映する（そりゃそうだ）
            return await CompleteWatchRunAsync(
                folderCheckFlag,
                mode,
                snapshotDbFullPath,
                snapshotDbName,
                snapshotThumbFolder,
                snapshotTabIndex,
                canUseQueryOnlyWatchReload,
                changedMoviesForUiReload,
                snapshotWatchScanScopeStamp,
                checkedFolderCount,
                enqueuedCount,
                sw
            );
        }

        // 最終 UI 反映から rescue / poll 記録 / end ログまでを 1 入口へまとめ、走査全体の締めを読みやすくする。
        private async Task<bool> CompleteWatchRunAsync(
            bool folderCheckFlag,
            CheckMode mode,
            string snapshotDbFullPath,
            string snapshotDbName,
            string snapshotThumbFolder,
            int snapshotTabIndex,
            bool canUseQueryOnlyWatchReload,
            List<WatchChangedMovie> changedMoviesForUiReload,
            long snapshotWatchScanScopeStamp,
            int checkedFolderCount,
            int enqueuedCount,
            Stopwatch sw
        )
        {
            // ----- [5] 走査全体を通していずれかのフォルダで変化があったらUI一覧を再描画 -----
            HandleFolderCheckUiReloadAfterChanges(
                folderCheckFlag,
                mode,
                snapshotDbFullPath,
                canUseQueryOnlyWatchReload,
                changedMoviesForUiReload
            );

            return await FinalizeWatchRunAsync(
                mode,
                snapshotDbFullPath,
                snapshotDbName,
                snapshotThumbFolder,
                snapshotTabIndex,
                snapshotWatchScanScopeStamp,
                checkedFolderCount,
                enqueuedCount,
                folderCheckFlag,
                changedMoviesForUiReload?.Count ?? 0,
                sw
            );
        }

        // 最終リロード後の rescue / poll 記録 / end ログをまとめ、Watcher 本体の末尾を薄くする。
        private async Task<bool> FinalizeWatchRunAsync(
            CheckMode mode,
            string snapshotDbFullPath,
            string snapshotDbName,
            string snapshotThumbFolder,
            int snapshotTabIndex,
            long snapshotWatchScanScopeStamp,
            int checkedFolderCount,
            int enqueuedCount,
            bool folderCheckFlag,
            int changedMovieCount,
            Stopwatch sw
        )
        {
            // Watch/Manual時は、削除されたサムネイルの取りこぼし救済を低頻度で実行する。
            if (
                TryHandleMissingThumbnailRescueEntrySuppression(
                    mode,
                    snapshotDbFullPath,
                    mode == CheckMode.Watch
                )
            )
            {
                return true;
            }

            await TryRunMissingThumbnailRescueAsync(
                mode,
                snapshotDbFullPath,
                snapshotDbName,
                snapshotThumbFolder,
                snapshotTabIndex,
                snapshotWatchScanScopeStamp
            );

            RecordWatchUpdateCountForPollIfNeeded(
                mode,
                folderCheckFlag,
                enqueuedCount,
                changedMovieCount
            );

            sw.Stop();
            WriteWatchCheckTaskEnd(
                mode,
                checkedFolderCount,
                enqueuedCount,
                folderCheckFlag,
                sw.ElapsedMilliseconds
            );
            return false;
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
