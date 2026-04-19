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
            string sql = ResolveWatchFolderQuerySql(mode);
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

                (string checkFolder, bool sub) = ResolveWatchFolderTarget(row);

                //存在しない監視フォルダは読み飛ばし。
                if (!Path.Exists(checkFolder))
                {
                    continue;
                }
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
                ShowFolderScanStartNoticeIfNeeded(checkFolder);

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
                    (
                        string strategyDetailCode,
                        string strategyDetailMessage,
                        string strategyDetailCategory,
                        string strategyDetailAxis
                    ) = ResolveWatchScanStrategyDetail(scanStrategyResult.Detail);
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
                                strategyDetailMessage,
                                strategyDetailCategory,
                                strategyDetailAxis
                            ) = ResolveWatchScanStrategyDetail(scanStrategyResult.Detail);
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

                    (
                        useIncrementalUiMode,
                        canUseQueryOnlyWatchReload,
                        bool wasDowngradedToFull
                    ) = ResolveWatchScanUiReloadMode(
                        mode,
                        scanResult.NewMoviePaths.Count,
                        IncrementalUiUpdateThreshold,
                        canUseQueryOnlyWatchReload
                    );
                    if (wasDowngradedToFull)
                    {
                        DebugRuntimeLog.Write(
                            "watch-check",
                            $"watch final reload downgraded to full: folder='{checkFolder}' reason=bulk-watch-batch new={scanResult.NewMoviePaths.Count}"
                        );
                    }
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan mode: folder='{checkFolder}' new={scanResult.NewMoviePaths.Count} mode={(useIncrementalUiMode ? "small" : "bulk")} threshold={IncrementalUiUpdateThreshold}"
                    );

                    List<PendingMovieRegistration> pendingNewMovies = [];
                    WatchPendingNewMovieFlushContext pendingMovieFlushContext =
                        CreateWatchPendingNewMovieFlushContext(
                            snapshotDbFullPath,
                            existingMovieByPath,
                            pendingNewMovies,
                            useIncrementalUiMode,
                            allowMissingTabAutoEnqueue,
                            autoEnqueueTabIndex,
                            thumbnailOutPath,
                            existingThumbnailFileNames,
                            openRescueRequestKeys,
                            addFilesByFolder,
                            checkFolder,
                            reason =>
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
                            () =>
                                ShouldSuppressWatchWorkByUi(
                                    IsWatchSuppressedByUi(),
                                    mode == CheckMode.Watch
                                ),
                            () =>
                                mode != CheckMode.Watch
                                || IsCurrentWatchScanScope(
                                    snapshotDbFullPath,
                                    snapshotWatchScanScopeStamp
                                )
                        );
                    WatchScannedMovieContext scannedMovieContext =
                        CreateWatchScannedMovieContext(
                            snapshotDbFullPath,
                            snapshotTabIndex,
                            existingMovieByPath,
                            existingViewMoviePaths,
                            displayedMoviePaths,
                            searchKeyword,
                            allowViewConsistencyRepair,
                            useIncrementalUiMode,
                            canUseQueryOnlyWatchReload,
                            mode,
                            scanStrategyResult.Strategy,
                            allowMissingTabAutoEnqueue,
                            autoEnqueueTabIndex,
                            thumbnailOutPath,
                            existingThumbnailFileNames,
                            openRescueRequestKeys,
                            pendingMovieFlushContext,
                            snapshotWatchScanScopeStamp
                        );
                    folderScanContext = CreateWatchFolderScanContext(
                        mode,
                        snapshotDbFullPath,
                        snapshotWatchScanScopeStamp,
                        sub,
                        restrictWatchWorkToVisibleMovies,
                        visibleMoviePaths,
                        allowMissingTabAutoEnqueue,
                        autoEnqueueTabIndex,
                        scannedMovieContext,
                        checkFolder
                    );

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

                        ApplyWatchProcessResultWithProbe(
                            processResult,
                            movieFullPath,
                            snapshotTabIndex,
                            ref dbLookupTotalMs,
                            ref movieInfoTotalMs,
                            ref dbInsertTotalMs,
                            ref uiReflectTotalMs,
                            ref enqueueFlushTotalMs,
                            ref addedByFolderCount,
                            ref enqueuedCount,
                            ref FolderCheckflg,
                            ref changedMoviesForUiReload
                        );
                        if (processResult.DeferredMoviePathsByUiSuppression.Count > 0)
                        {
                            if (
                                TryApplyDeferredPathsFromProcessResult(
                                    processResult,
                                    snapshotDbFullPath,
                                    snapshotWatchScanScopeStamp,
                                    checkFolder,
                                    sub,
                                    scanResult.NewMoviePaths.Skip(movieIndex + 1),
                                    pendingNewMovies,
                                    addFilesByFolder,
                                    MergeWatchFolderDeferredWorkByUiSuppression
                                )
                            )
                            {
                                watchStoppedByUiSuppression = true;
                                break;
                            }
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

                    ApplyWatchPendingMovieFlushResult(
                        finalPendingMovieFlushResult,
                        ref dbInsertTotalMs,
                        ref uiReflectTotalMs,
                        ref enqueueFlushTotalMs,
                        ref addedByFolderCount,
                        ref enqueuedCount,
                        ref changedMoviesForUiReload
                    );
                    if (
                        TryApplyDeferredPathsFromFlushResult(
                            finalPendingMovieFlushResult,
                            snapshotDbFullPath,
                            snapshotWatchScanScopeStamp,
                            checkFolder,
                            sub,
                            [],
                            pendingNewMovies,
                            addFilesByFolder,
                            MergeWatchFolderDeferredWorkByUiSuppression
                        )
                    )
                    {
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
                    ApplyWatchPendingMovieFlushResult(
                        recoveryFlushResult,
                        ref dbInsertTotalMs,
                        ref uiReflectTotalMs,
                        ref enqueueFlushTotalMs,
                        ref addedByFolderCount,
                        ref enqueuedCount,
                        ref changedMoviesForUiReload
                    );
                    FolderCheckflg |= recoveryFlushResult.AddedByFolderCount > 0;
                    if (
                        TryApplyDeferredPathsFromFlushResult(
                            recoveryFlushResult,
                            snapshotDbFullPath,
                            snapshotWatchScanScopeStamp,
                            checkFolder,
                            sub,
                            [],
                            folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext?.PendingNewMovies,
                            folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext?.AddFilesByFolder,
                            MergeWatchFolderDeferredWorkByUiSuppression
                        )
                    )
                    {
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

            if (
                TryResolveWatchUpdateCountForPoll(
                    mode,
                    FolderCheckflg,
                    enqueuedCount,
                    changedMoviesForUiReload?.Count ?? 0,
                    out int watchUpdateCount
                )
            )
            {
                RecordEverythingWatchPollResult(watchUpdateCount);
            }

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
