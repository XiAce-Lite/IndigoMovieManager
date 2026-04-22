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

                // 開始ログとトースト通知を同じ入口へ寄せる。
                HandleWatchFolderScanStart(checkFolder, mode);

                if (
                    TryHandleWatchFolderVisibleGate(
                        restrictWatchWorkToVisibleMovies,
                        visibleMoviePaths,
                        checkFolder,
                        sub,
                        currentWatchQueueActiveCount,
                        WatchVisibleOnlyQueueThreshold
                    )
                )
                {
                    continue;
                }

                try
                {
                    // ----- [2] 実際のフォルダ階層なめ (IOバウンド) を並列逃がし -----
                    // 重いファイル走査はUIスレッドを塞がないよう Task.Run(バックグラウンドスレッド) 上で実行する。
                    (
                        FolderScanWithStrategyResult scanStrategyResult,
                        FolderScanResult scanResult,
                        scanBackgroundElapsedMs
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
                        useIncrementalUiMode,
                        canUseQueryOnlyWatchReload
                    ) = HandleWatchScanStrategyAndUiReloadDiagnostics(
                        mode,
                        checkFolder,
                        scanStrategyResult.Strategy,
                        strategyDetailMessage,
                        scanResult.NewMoviePaths.Count,
                        IncrementalUiUpdateThreshold,
                        canUseQueryOnlyWatchReload
                    );

                    List<PendingMovieRegistration> pendingNewMovies = [];
                    void RefreshVisibleMovieGate(string reason)
                    {
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
                    }
                    (
                        WatchPendingNewMovieFlushContext pendingMovieFlushContext,
                        WatchScannedMovieContext scannedMovieContext,
                        folderScanContext
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
                        pendingNewMovies,
                        RefreshVisibleMovieGate
                    );

                    if (
                        TryPrepareWatchFolderMovieLoop(
                            folderScanContext,
                            checkFolder,
                            scanResult.NewMoviePaths,
                            out bool shouldBreakMovieLoopByUiSuppression
                        )
                    )
                    {
                        return;
                    }
                    if (shouldBreakMovieLoopByUiSuppression)
                    {
                        watchStoppedByUiSuppression = true;
                        break;
                    }

                    // movie loop の guard / 1件処理 / stale 打ち切り / 集計反映をまとめ、
                    // CheckFolderAsync 本体は流れだけを読めるようにする。
                    async Task<(
                        bool ShouldReturn,
                        bool ShouldBreakMovieLoopByUiSuppression
                    )> TryProcessWatchFolderMovieLoopAsync()
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
                                return (true, false);
                            }
                            if (shouldBreakCurrentMovieLoopByUiSuppression)
                            {
                                return (false, true);
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
                                return (true, false);
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
                                return (false, true);
                            }
                        }

                        return (false, false);
                    }

                    // ----- [3] 見つかった「新規ファイル」だけに対する処理 -----
                    (
                        bool shouldReturnByMovieLoop,
                        bool shouldBreakMovieLoopByCurrentUiSuppression
                    ) = await TryProcessWatchFolderMovieLoopAsync();
                    if (shouldReturnByMovieLoop)
                    {
                        return;
                    }
                    if (shouldBreakMovieLoopByCurrentUiSuppression)
                    {
                        watchStoppedByUiSuppression = true;
                        break;
                    }

                    if (watchStoppedByUiSuppression)
                    {
                        break;
                    }

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
                        return;
                    }
                    if (shouldBreakByUiSuppression)
                    {
                        watchStoppedByUiSuppression = true;
                        break;
                    }
                }
                catch (Exception e)
                {
                    canUseQueryOnlyWatchReload = false;
                    WriteWatchFolderFailure(checkFolder, e);

                    // ここまでに検出済みの新規動画は、可能な限りDBへ逃がして全損を避ける。
                    WatchPendingNewMovieFlushResult recoveryFlushResult =
                        await TryFlushPendingNewMoviesAfterFolderFailureAsync(
                            checkFolder,
                            folderScanContext
                        );
                    if (
                        TryHandleRecoveryFlushResult(
                            recoveryFlushResult,
                            snapshotDbFullPath,
                            snapshotWatchScanScopeStamp,
                            checkFolder,
                            sub,
                            folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext?.PendingNewMovies,
                            folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext?.AddFilesByFolder,
                            MergeWatchFolderDeferredWorkByUiSuppression,
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
                        watchStoppedByUiSuppression = true;
                    }

                    await HandleWatchFolderFailureTailAsync(checkFolder, e);
                }

                if (watchStoppedByUiSuppression)
                {
                    break;
                }

                // final queue flush と scan end ログをまとめ、フォルダ単位の終端処理を読みやすくする。
                async Task<bool> TryFinalizeWatchFolderAsync()
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
                        return true;
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
                    return false;
                }

                if (await TryFinalizeWatchFolderAsync())
                {
                    watchStoppedByUiSuppression = true;
                    break;
                }
            }

            if (
                TryAbortWatchScanBeforeFinalReload(
                    watchStoppedByUiSuppression,
                    mode,
                    snapshotDbFullPath,
                    snapshotWatchScanScopeStamp
                )
            )
            {
                return;
            }

            //stack : ファイル名を外部から変更したときに、エクステンションのファイル名が追従してなかった。強制チェックで反応はした。
            //再クリックで表示はリロードしたので、内部は変わってる。リフレッシュも漏れてる可能性あり。
            //と言うかですね。これは外部からのリネームでも、アプリでのリネームでも同じで。クリックすりゃ反映する（そりゃそうだ）

            if (
                await CompleteWatchRunAsync(
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
