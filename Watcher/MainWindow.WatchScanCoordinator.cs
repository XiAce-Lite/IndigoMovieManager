using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IndigoMovieManager.Data;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // UI 状態と watch 用キャッシュの初期スナップショットをここで揃える。
        private async Task InitializeWatchScanCoordinatorContextAsync(
            WatchScanCoordinatorContext context
        )
        {
            WatchScanUiSnapshot uiSnapshot = await context.UiBridge.CaptureSnapshotAsync();

            context.ExistingMovieByPath = await Task.Run(
                () => BuildExistingMovieSnapshotByPath(context.SnapshotDbFullPath)
            );
            context.ExistingViewMoviePaths = uiSnapshot.ExistingViewMoviePaths;
            context.DisplayedMoviePaths = uiSnapshot.DisplayedMoviePaths;
            context.SearchKeyword = uiSnapshot.SearchKeyword;
            context.VisibleMoviePaths = uiSnapshot.VisibleMoviePaths;
            RefreshWatchVisibleMovieGate(context, "initial");

            if (!context.AllowMissingTabAutoEnqueue)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-tab-thumb auto enqueue suppressed: current_tab={context.SnapshotTabIndex}"
                );
            }

            context.ThumbnailOutPath = context.AllowMissingTabAutoEnqueue
                ? ResolveThumbnailOutPath(
                    context.AutoEnqueueTabIndex!.Value,
                    context.SnapshotDbName,
                    context.SnapshotThumbFolder
                )
                : "";
            context.ExistingThumbnailFileNames = context.AllowMissingTabAutoEnqueue
                ? await Task.Run(() => BuildThumbnailFileNameLookup(context.ThumbnailOutPath))
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            context.OpenRescueRequestKeys = context.AllowMissingTabAutoEnqueue
                ? ResolveCurrentThumbnailFailureDbService()?.GetOpenRescueRequestKeys()
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            context.AllowViewConsistencyRepair = uiSnapshot.AllowViewConsistencyRepair;
            if (!context.AllowViewConsistencyRepair)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    "view repair deferred: startup feed partial active."
                );
            }
        }

        private bool HasWatchScanDbSwitched(WatchScanCoordinatorContext context)
        {
            return !string.Equals(
                MainVM.DbInfo.DBFullPath,
                context.SnapshotDbFullPath,
                StringComparison.OrdinalIgnoreCase
            );
        }

        // visible-only gate は watch 高負荷時だけ効かせ、folder 処理中の flush 後にも再評価する。
        private void RefreshWatchVisibleMovieGate(
            WatchScanCoordinatorContext context,
            string reason
        )
        {
            if (context.Mode != CheckMode.Watch || context.VisibleMoviePaths.Count < 1)
            {
                return;
            }

            if (!TryGetCurrentQueueActiveCount(out int refreshedActiveCount))
            {
                return;
            }

            context.CurrentWatchQueueActiveCount = refreshedActiveCount;
            bool nextRestrict = ShouldRestrictWatchWorkToVisibleMovies(
                isWatchMode: true,
                activeQueueCount: context.CurrentWatchQueueActiveCount,
                threshold: WatchVisibleOnlyQueueThreshold,
                currentTabIndex: context.SnapshotTabIndex,
                visibleMovieCount: context.VisibleMoviePaths.Count
            );
            if (nextRestrict == context.RestrictWatchWorkToVisibleMovies)
            {
                return;
            }

            context.RestrictWatchWorkToVisibleMovies = nextRestrict;
            DebugRuntimeLog.Write(
                "watch-check",
                nextRestrict
                    ? $"watch visible-only gate enabled: active={context.CurrentWatchQueueActiveCount} threshold={WatchVisibleOnlyQueueThreshold} tab={context.SnapshotTabIndex} visible={context.VisibleMoviePaths.Count} reason={reason}"
                    : $"watch visible-only gate disabled: active={context.CurrentWatchQueueActiveCount} threshold={WatchVisibleOnlyQueueThreshold} tab={context.SnapshotTabIndex} reason={reason}"
            );
        }

        // folder 単位の処理本体を分け、CheckFolderAsync は入口と終了処理だけへ寄せる。
        private async Task ProcessWatchFolderAsync(
            WatchScanCoordinatorContext context,
            DataRow row,
            string checkExt
        )
        {
            string checkFolder = row["dir"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(checkFolder) || !Path.Exists(checkFolder))
            {
                return;
            }

            context.CheckedFolderCount++;
            DebugRuntimeLog.Write(
                "watch-check",
                $"scan start: folder='{checkFolder}' mode={context.Mode}"
            );

            WatchFolderScanState state = new(checkFolder);
            context.UiBridge.TryShowFolderMonitoringProgress(checkFolder);

            bool sub = ((long)row["sub"] == 1);
            if (
                ShouldSkipWatchFolderByVisibleMovieGate(
                    context.RestrictWatchWorkToVisibleMovies,
                    context.VisibleMoviePaths,
                    checkFolder,
                    sub
                )
            )
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan skipped by visible-only gate: folder='{checkFolder}' active={context.CurrentWatchQueueActiveCount} threshold={WatchVisibleOnlyQueueThreshold} visible={context.VisibleMoviePaths.Count}"
                );
                return;
            }

            try
            {
                Stopwatch scanBackgroundStopwatch = Stopwatch.StartNew();
                FolderScanWithStrategyResult scanStrategyResult = await Task.Run(() =>
                    ScanFolderWithStrategyInBackground(context.Mode, checkFolder, sub, checkExt)
                );
                FolderScanResult scanResult = scanStrategyResult.ScanResult;
                scanBackgroundStopwatch.Stop();
                state.ScanBackgroundElapsedMs = scanBackgroundStopwatch.ElapsedMilliseconds;

                (string strategyDetailCode, string strategyDetailMessage) =
                    DescribeEverythingDetail(scanStrategyResult.Detail);
                string strategyDetailCategory = FileIndexReasonTable.ToCategory(
                    scanStrategyResult.Detail
                );
                string strategyDetailAxis = FileIndexReasonTable.ToLogAxis(scanStrategyResult.Detail);
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan strategy: category={strategyDetailAxis} folder='{checkFolder}' strategy={scanStrategyResult.Strategy} detail_category={strategyDetailCategory} detail_code={strategyDetailCode} detail_message={strategyDetailMessage} scanned={scanResult.ScannedCount}"
                );

                if (
                    !context.RestrictWatchWorkToVisibleMovies
                    && ShouldRunWatchFolderFullReconcile(
                        context.Mode == CheckMode.Watch,
                        scanStrategyResult.Strategy,
                        scanResult.NewMoviePaths.Count
                    )
                )
                {
                    string reconcileScopeKey = BuildWatchFolderFullReconcileScopeKey(
                        context.SnapshotDbFullPath,
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
                                checkFolder,
                                sub,
                                checkExt
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
                    context.UiBridge.TryShowEverythingModeNotice(strategyDetailMessage);
                }
                else if (
                    scanStrategyResult.Strategy == FileIndexStrategies.Filesystem
                )
                {
                    context.UiBridge.TryShowEverythingFallbackNotice(strategyDetailMessage);
                }

                state.UseIncrementalUiMode =
                    scanResult.NewMoviePaths.Count <= IncrementalUiUpdateThreshold;
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan mode: folder='{checkFolder}' new={scanResult.NewMoviePaths.Count} mode={(state.UseIncrementalUiMode ? "small" : "bulk")} threshold={IncrementalUiUpdateThreshold}"
                );

                foreach (string movieFullPath in scanResult.NewMoviePaths)
                {
                    await ProcessScannedMovieAsync(context, state, movieFullPath);
                }

                await FlushPendingNewMoviesAsync(context, state);

                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan file summary: folder='{checkFolder}' scanned={scanResult.ScannedCount} new={scanResult.NewMoviePaths.Count}"
                );
            }
            catch (Exception ex)
            {
                ClearPendingMoviePlaceholdersByFolder(checkFolder);
                if (ex is IOException)
                {
                    await Task.Delay(1000);
                }
            }

            Stopwatch finalFlushStopwatch = Stopwatch.StartNew();
            FlushPendingQueueItems(state.AddFilesByFolder, checkFolder);
            finalFlushStopwatch.Stop();
            state.EnqueueFlushTotalMs += finalFlushStopwatch.ElapsedMilliseconds;
            RefreshWatchVisibleMovieGate(context, "folder_final_flush");
            DebugRuntimeLog.Write(
                "watch-check",
                $"scan end: folder='{checkFolder}' added={state.AddedByFolderCount} "
                    + $"mode={(state.UseIncrementalUiMode ? "small" : "bulk")} "
                    + $"scan_bg_ms={state.ScanBackgroundElapsedMs} movieinfo_ms={state.MovieInfoTotalMs} db_lookup_ms={state.DbLookupTotalMs} "
                    + $"db_insert_ms={state.DbInsertTotalMs} ui_reflect_ms={state.UiReflectTotalMs} "
                    + $"enqueue_flush_ms={state.EnqueueFlushTotalMs}"
            );
            await Task.Delay(100);
        }

        // 新規動画は batch 登録と queue flush を coordinator からまとめて制御する。
        private async Task FlushPendingNewMoviesAsync(
            WatchScanCoordinatorContext context,
            WatchFolderScanState state
        )
        {
            if (state.PendingNewMovies.Count < 1)
            {
                return;
            }

            List<MovieCore> moviesToInsert = state.PendingNewMovies.Select(x => (MovieCore)x.Movie).ToList();

            Stopwatch stepStopwatch = Stopwatch.StartNew();
            int insertedCount = await InsertMoviesToMainDbBatchAsync(
                context.SnapshotDbFullPath,
                moviesToInsert
            );
            stepStopwatch.Stop();
            state.DbInsertTotalMs += stepStopwatch.ElapsedMilliseconds;
            TryAdjustRegisteredMovieCount(context.SnapshotDbFullPath, insertedCount);

            foreach (PendingMovieRegistration pending in state.PendingNewMovies)
            {
                context.ExistingMovieByPath[pending.MovieFullPath] = new WatchMainDbMovieSnapshot(
                    pending.Movie.MovieId,
                    pending.Movie.Hash ?? ""
                );

                if (state.UseIncrementalUiMode)
                {
                    stepStopwatch.Restart();
                    await context.UiBridge.AppendMovieToViewByPathAsync(
                        context.SnapshotDbFullPath,
                        pending.Movie.MoviePath
                    );
                    stepStopwatch.Stop();
                    state.UiReflectTotalMs += stepStopwatch.ElapsedMilliseconds;
                }

                RemovePendingMoviePlaceholder(pending.MovieFullPath);

                if (!context.AllowMissingTabAutoEnqueue)
                {
                    continue;
                }

                string saveThumbFileName = ThumbnailPathResolver.BuildThumbnailPath(
                    context.ThumbnailOutPath,
                    pending.MovieFullPath,
                    pending.Movie.Hash
                );
                string saveThumbFileNameOnly = Path.GetFileName(saveThumbFileName) ?? "";
                if (
                    !string.IsNullOrWhiteSpace(saveThumbFileNameOnly)
                    && context.ExistingThumbnailFileNames.Contains(saveThumbFileNameOnly)
                )
                {
                    continue;
                }

                MissingThumbnailAutoEnqueueBlockReason pendingBlockReason =
                    ResolveMissingThumbnailAutoEnqueueBlockReason(
                        pending.MovieFullPath,
                        context.AutoEnqueueTabIndex!.Value,
                        context.ExistingThumbnailFileNames,
                        context.OpenRescueRequestKeys
                    );
                if (pendingBlockReason != MissingThumbnailAutoEnqueueBlockReason.None)
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"skip enqueue by failure-state: tab={context.AutoEnqueueTabIndex.Value}, movie='{pending.MovieFullPath}', reason={DescribeMissingThumbnailAutoEnqueueBlockReason(pendingBlockReason)}"
                    );
                    continue;
                }

                DebugRuntimeLog.Write(
                    "watch-check",
                    $"enqueue by missing-tab-thumb: tab={context.AutoEnqueueTabIndex.Value}, movie='{pending.MovieFullPath}'"
                );

                state.AddFilesByFolder.Add(
                    new QueueObj
                    {
                        MovieId = pending.Movie.MovieId,
                        MovieFullPath = pending.MovieFullPath,
                        Hash = pending.Movie.Hash,
                        Tabindex = context.AutoEnqueueTabIndex.Value,
                        Priority = ThumbnailQueuePriority.Normal,
                    }
                );
                state.AddedByFolderCount++;
                context.EnqueuedCount++;

                if (
                    ShouldFlushWatchEnqueueBatch(
                        state.UseIncrementalUiMode,
                        state.AddFilesByFolder.Count,
                        FolderScanEnqueueBatchSize
                    )
                )
                {
                    stepStopwatch.Restart();
                    FlushPendingQueueItems(state.AddFilesByFolder, state.CheckFolder);
                    stepStopwatch.Stop();
                    state.EnqueueFlushTotalMs += stepStopwatch.ElapsedMilliseconds;
                    RefreshWatchVisibleMovieGate(context, "pending_movie_flush");
                }
            }

            state.PendingNewMovies.Clear();
        }

        // 1件ごとの分岐をここへ寄せ、visible-only / deferred batch / UI補正の順序を固定する。
        private async Task ProcessScannedMovieAsync(
            WatchScanCoordinatorContext context,
            WatchFolderScanState state,
            string movieFullPath
        )
        {
            Stopwatch perFileStopwatch = Stopwatch.StartNew();
            long perFileDbLookupMs = 0;
            long perFileThumbExistsMs = 0;
            long perFileMovieInfoMs = 0;
            long perFileFlushWaitMs = 0;

            WatchScanEntryDecision entryDecision = EvaluateWatchScanEntryDecision(
                context.RestrictWatchWorkToVisibleMovies,
                context.VisibleMoviePaths,
                movieFullPath
            );
            if (entryDecision.ShouldSkipByVisibleOnlyGate)
            {
                perFileStopwatch.Stop();
                WriteWatchCheckProbeIfNeeded(
                    context.SnapshotTabIndex,
                    movieFullPath,
                    "skip_visible_only_gate",
                    perFileDbLookupMs,
                    perFileThumbExistsMs,
                    perFileMovieInfoMs,
                    perFileFlushWaitMs,
                    perFileStopwatch.ElapsedMilliseconds
                );
                return;
            }

            if (!state.HasHitInFolder)
            {
                context.UiBridge.TryShowFolderHit(state.CheckFolder);
                state.HasHitInFolder = true;
            }

            if (IsZeroByteMovieFile(movieFullPath, out long zeroFileLength))
            {
                if (context.AllowMissingTabAutoEnqueue)
                {
                    TryCreateErrorMarkerForSkippedMovie(
                        movieFullPath,
                        context.AutoEnqueueTabIndex!.Value,
                        "zero-byte movie(folder scan)"
                    );
                }

                DebugRuntimeLog.Write(
                    "watch-check",
                    $"skip zero-byte movie before queue: '{movieFullPath}' size={zeroFileLength}"
                );
                perFileStopwatch.Stop();
                WriteWatchCheckProbeIfNeeded(
                    context.SnapshotTabIndex,
                    movieFullPath,
                    "skip_zero_byte",
                    perFileDbLookupMs,
                    perFileThumbExistsMs,
                    perFileMovieInfoMs,
                    perFileFlushWaitMs,
                    perFileStopwatch.ElapsedMilliseconds
                );
                return;
            }

            string fileBody = Path.GetFileNameWithoutExtension(movieFullPath) ?? "";
            if (string.IsNullOrWhiteSpace(fileBody))
            {
                return;
            }

            Stopwatch stepStopwatch = Stopwatch.StartNew();
            bool existsInDb = context.ExistingMovieByPath.TryGetValue(
                movieFullPath,
                out WatchMainDbMovieSnapshot currentMovie
            );
            stepStopwatch.Stop();
            state.DbLookupTotalMs += stepStopwatch.ElapsedMilliseconds;
            perFileDbLookupMs = stepStopwatch.ElapsedMilliseconds;

            if (!existsInDb)
            {
                stepStopwatch.Restart();
                MovieInfo mvi = await Task.Run(() => new MovieInfo(movieFullPath));
                stepStopwatch.Stop();
                state.MovieInfoTotalMs += stepStopwatch.ElapsedMilliseconds;
                perFileMovieInfoMs = stepStopwatch.ElapsedMilliseconds;

                state.PendingNewMovies.Add(new PendingMovieRegistration(movieFullPath, fileBody, mvi));
                AddOrUpdatePendingMoviePlaceholder(
                    movieFullPath,
                    fileBody,
                    context.SnapshotTabIndex,
                    PendingMoviePlaceholderStatus.Detected
                );
                context.HasAnyFolderUpdate = true;

                WatchNewMovieScanDecision newMovieDecision = EvaluateWatchNewMovieScanDecision(
                    context.RestrictWatchWorkToVisibleMovies,
                    context.VisibleMoviePaths,
                    movieFullPath,
                    state.UseIncrementalUiMode,
                    state.PendingNewMovies.Count,
                    FolderScanEnqueueBatchSize
                );
                if (newMovieDecision.ShouldFlushPendingMovieBatch)
                {
                    Stopwatch flushWaitStopwatch = Stopwatch.StartNew();
                    await FlushPendingNewMoviesAsync(context, state);
                    flushWaitStopwatch.Stop();
                    perFileFlushWaitMs = flushWaitStopwatch.ElapsedMilliseconds;
                }

                perFileStopwatch.Stop();
                WriteWatchCheckProbeIfNeeded(
                    context.SnapshotTabIndex,
                    movieFullPath,
                    "pending_insert",
                    perFileDbLookupMs,
                    perFileThumbExistsMs,
                    perFileMovieInfoMs,
                    perFileFlushWaitMs,
                    perFileStopwatch.ElapsedMilliseconds
                );
                return;
            }

            WatchExistingMovieScanDecision existingMovieDecision =
                EvaluateWatchExistingMovieScanDecision(
                    context.RestrictWatchWorkToVisibleMovies,
                    context.VisibleMoviePaths,
                    movieFullPath,
                    context.AllowViewConsistencyRepair,
                    context.ExistingViewMoviePaths,
                    context.SearchKeyword,
                    context.DisplayedMoviePaths,
                    state.UseIncrementalUiMode,
                    state.AddFilesByFolder.Count + 1,
                    FolderScanEnqueueBatchSize
                );
            MovieViewConsistencyDecision viewConsistency = existingMovieDecision.ViewConsistency;
            if (viewConsistency.ShouldRepairView)
            {
                context.HasAnyFolderUpdate = true;
                context.ExistingViewMoviePaths.Add(movieFullPath);
                context.DisplayedMoviePaths.Add(movieFullPath);

                if (state.UseIncrementalUiMode)
                {
                    stepStopwatch.Restart();
                    await context.UiBridge.AppendMovieToViewByPathAsync(
                        context.SnapshotDbFullPath,
                        movieFullPath
                    );
                    stepStopwatch.Stop();
                    state.UiReflectTotalMs += stepStopwatch.ElapsedMilliseconds;
                }

                DebugRuntimeLog.Write(
                    "watch-check",
                    $"repair view by existing-db-movie: tab={context.SnapshotTabIndex}, movie='{movieFullPath}'"
                );
            }
            else if (viewConsistency.ShouldRefreshDisplayedView)
            {
                context.HasAnyFolderUpdate = true;
                context.DisplayedMoviePaths.Add(movieFullPath);
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"refresh filtered-view by existing-db-movie: tab={context.SnapshotTabIndex}, movie='{movieFullPath}'"
                );
            }

            if (!context.AllowMissingTabAutoEnqueue)
            {
                perFileStopwatch.Stop();
                WriteWatchCheckProbeIfNeeded(
                    context.SnapshotTabIndex,
                    movieFullPath,
                    "skip_non_upper_tab",
                    perFileDbLookupMs,
                    perFileThumbExistsMs,
                    perFileMovieInfoMs,
                    perFileFlushWaitMs,
                    perFileStopwatch.ElapsedMilliseconds
                );
                return;
            }

            string saveThumbFileName = ThumbnailPathResolver.BuildThumbnailPath(
                context.ThumbnailOutPath,
                movieFullPath,
                currentMovie.Hash
            );

            Stopwatch thumbExistsStopwatch = Stopwatch.StartNew();
            string saveThumbFileNameOnly = Path.GetFileName(saveThumbFileName) ?? "";
            bool thumbExists =
                !string.IsNullOrWhiteSpace(saveThumbFileNameOnly)
                && context.ExistingThumbnailFileNames.Contains(saveThumbFileNameOnly);
            thumbExistsStopwatch.Stop();
            perFileThumbExistsMs = thumbExistsStopwatch.ElapsedMilliseconds;
            if (thumbExists)
            {
                perFileStopwatch.Stop();
                WriteWatchCheckProbeIfNeeded(
                    context.SnapshotTabIndex,
                    movieFullPath,
                    "skip_existing_thumb",
                    perFileDbLookupMs,
                    perFileThumbExistsMs,
                    perFileMovieInfoMs,
                    perFileFlushWaitMs,
                    perFileStopwatch.ElapsedMilliseconds
                );
                return;
            }

            MissingThumbnailAutoEnqueueBlockReason blockReason =
                ResolveMissingThumbnailAutoEnqueueBlockReason(
                    movieFullPath,
                    context.AutoEnqueueTabIndex!.Value,
                    context.ExistingThumbnailFileNames,
                    context.OpenRescueRequestKeys
                );
            if (blockReason != MissingThumbnailAutoEnqueueBlockReason.None)
            {
                perFileStopwatch.Stop();
                WriteWatchCheckProbeIfNeeded(
                    context.SnapshotTabIndex,
                    movieFullPath,
                    $"skip_failure_state:{DescribeMissingThumbnailAutoEnqueueBlockReason(blockReason)}",
                    perFileDbLookupMs,
                    perFileThumbExistsMs,
                    perFileMovieInfoMs,
                    perFileFlushWaitMs,
                    perFileStopwatch.ElapsedMilliseconds
                );
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"skip enqueue by failure-state: tab={context.AutoEnqueueTabIndex.Value}, movie='{movieFullPath}', reason={DescribeMissingThumbnailAutoEnqueueBlockReason(blockReason)}"
                );
                return;
            }

            DebugRuntimeLog.Write(
                "watch-check",
                $"enqueue by missing-tab-thumb: tab={context.AutoEnqueueTabIndex.Value}, movie='{movieFullPath}'"
            );

            state.AddFilesByFolder.Add(
                new QueueObj
                {
                    MovieId = currentMovie.MovieId,
                    MovieFullPath = movieFullPath,
                    Hash = currentMovie.Hash,
                    Tabindex = context.AutoEnqueueTabIndex.Value,
                    Priority = ThumbnailQueuePriority.Normal,
                }
            );
            state.AddedByFolderCount++;
            context.EnqueuedCount++;

            if (existingMovieDecision.ShouldFlushEnqueueBatch)
            {
                stepStopwatch.Restart();
                FlushPendingQueueItems(state.AddFilesByFolder, state.CheckFolder);
                stepStopwatch.Stop();
                state.EnqueueFlushTotalMs += stepStopwatch.ElapsedMilliseconds;
                perFileFlushWaitMs = stepStopwatch.ElapsedMilliseconds;
                RefreshWatchVisibleMovieGate(
                    context,
                    state.UseIncrementalUiMode ? "incremental_flush" : "batch_flush"
                );
            }

            perFileStopwatch.Stop();
            WriteWatchCheckProbeIfNeeded(
                context.SnapshotTabIndex,
                movieFullPath,
                "enqueue_missing_thumb",
                perFileDbLookupMs,
                perFileThumbExistsMs,
                perFileMovieInfoMs,
                perFileFlushWaitMs,
                perFileStopwatch.ElapsedMilliseconds
            );
        }

        private static void WriteWatchCheckProbeIfNeeded(
            int snapshotTabIndex,
            string movieFullPath,
            string outcome,
            long dbLookupMs,
            long thumbExistsMs,
            long movieInfoMs,
            long flushWaitMs,
            long totalMs
        )
        {
            bool isTarget = IsWatchCheckProbeTargetMovie(movieFullPath);
            if (!isTarget && totalMs < WatchCheckProbeSlowThresholdMs)
            {
                return;
            }

            DebugRuntimeLog.Write(
                "watch-check-probe",
                $"tab={snapshotTabIndex} outcome={outcome} total_ms={totalMs} "
                    + $"db_lookup_ms={dbLookupMs} thumb_exists_ms={thumbExistsMs} "
                    + $"movieinfo_ms={movieInfoMs} flush_wait_ms={flushWaitMs} path='{movieFullPath}'"
            );
        }

        // small モードでは即時、bulk モードでは deferred batch しきい値到達でだけ flush する。
        internal static bool ShouldFlushWatchPendingMovieBatch(
            bool useIncrementalUiMode,
            int pendingMovieCount,
            int batchSize
        )
        {
            if (pendingMovieCount < 1)
            {
                return false;
            }

            int safeBatchSize = Math.Max(1, batchSize);
            return useIncrementalUiMode || pendingMovieCount >= safeBatchSize;
        }

        // queue 側も同じ境界で flush し、visible-only と deferred batch の順序を揃える。
        internal static bool ShouldFlushWatchEnqueueBatch(
            bool useIncrementalUiMode,
            int pendingQueueCount,
            int batchSize
        )
        {
            if (pendingQueueCount < 1)
            {
                return false;
            }

            int safeBatchSize = Math.Max(1, batchSize);
            return useIncrementalUiMode || pendingQueueCount >= safeBatchSize;
        }

        // 入口の visible-only gate はここで短絡し、後段の work を起こさない。
        internal static WatchScanEntryDecision EvaluateWatchScanEntryDecision(
            bool restrictToVisibleMovies,
            ISet<string> visibleMoviePaths,
            string movieFullPath
        )
        {
            bool shouldSkip = ShouldSkipWatchWorkByVisibleMovieGate(
                restrictToVisibleMovies,
                visibleMoviePaths,
                movieFullPath
            );
            return new WatchScanEntryDecision(
                shouldSkip,
                shouldSkip
                    ? [WatchScanFlowStage.VisibleOnlyGateSkipped]
                    : [WatchScanFlowStage.VisibleOnlyGatePassed]
            );
        }

        // 新規動画は visible-only を通過した後でだけ deferred batch 判定へ進める。
        internal static WatchNewMovieFlowDecision EvaluateWatchNewMovieFlowDecision(
            bool useIncrementalUiMode,
            int pendingMovieCount,
            int batchSize
        )
        {
            bool shouldFlush = ShouldFlushWatchPendingMovieBatch(
                useIncrementalUiMode,
                pendingMovieCount,
                batchSize
            );
            return new WatchNewMovieFlowDecision(
                shouldFlush,
                shouldFlush
                    ? [WatchScanFlowStage.PendingMovieBatchFlushed]
                    : [WatchScanFlowStage.PendingMovieBatchDeferred]
            );
        }

        // 新規動画の分岐列は、visible-only 短絡から batch 判定までを一つの decision で固定する。
        internal static WatchNewMovieScanDecision EvaluateWatchNewMovieScanDecision(
            bool restrictToVisibleMovies,
            ISet<string> visibleMoviePaths,
            string movieFullPath,
            bool useIncrementalUiMode,
            int pendingMovieCount,
            int batchSize
        )
        {
            WatchScanEntryDecision entryDecision = EvaluateWatchScanEntryDecision(
                restrictToVisibleMovies,
                visibleMoviePaths,
                movieFullPath
            );
            if (entryDecision.ShouldSkipByVisibleOnlyGate)
            {
                return new WatchNewMovieScanDecision(
                    ShouldSkipByVisibleOnlyGate: true,
                    ShouldFlushPendingMovieBatch: false,
                    Stages: [.. entryDecision.Stages]
                );
            }

            WatchNewMovieFlowDecision flowDecision = EvaluateWatchNewMovieFlowDecision(
                useIncrementalUiMode,
                pendingMovieCount,
                batchSize
            );
            return new WatchNewMovieScanDecision(
                ShouldSkipByVisibleOnlyGate: false,
                ShouldFlushPendingMovieBatch: flowDecision.ShouldFlushPendingMovieBatch,
                Stages: [.. entryDecision.Stages, .. flowDecision.Stages]
            );
        }

        // 既存動画は UI 補正の suppress / repair を先に決め、その後で queue batch を固定する。
        internal static WatchExistingMovieFlowDecision EvaluateWatchExistingMovieFlowDecision(
            bool allowViewConsistencyRepair,
            ISet<string> existingViewMoviePaths,
            string searchKeyword,
            ISet<string> displayedMoviePaths,
            string movieFullPath,
            bool useIncrementalUiMode,
            int pendingQueueCount,
            int batchSize
        )
        {
            List<WatchScanFlowStage> stages = [];
            bool isViewRepairSuppressed =
                !allowViewConsistencyRepair
                && ShouldRepairExistingMovieView(existingViewMoviePaths, movieFullPath);
            MovieViewConsistencyDecision viewConsistency = EvaluateMovieViewConsistency(
                allowViewConsistencyRepair,
                existsInDb: true,
                existingViewMoviePaths,
                searchKeyword,
                displayedMoviePaths,
                movieFullPath
            );
            if (isViewRepairSuppressed)
            {
                stages.Add(WatchScanFlowStage.UiRepairSuppressed);
            }
            else if (viewConsistency.ShouldRepairView)
            {
                stages.Add(WatchScanFlowStage.UiRepairRequested);
            }
            else if (viewConsistency.ShouldRefreshDisplayedView)
            {
                stages.Add(WatchScanFlowStage.DisplayedViewRefreshRequested);
            }

            bool shouldFlush = ShouldFlushWatchEnqueueBatch(
                useIncrementalUiMode,
                pendingQueueCount,
                batchSize
            );
            stages.Add(
                shouldFlush
                    ? WatchScanFlowStage.EnqueueBatchFlushed
                    : WatchScanFlowStage.EnqueueBatchDeferred
            );
            return new WatchExistingMovieFlowDecision(
                isViewRepairSuppressed,
                viewConsistency,
                shouldFlush,
                [.. stages]
            );
        }

        // 既存動画の分岐列も、visible-only と UI 補正/queue の順序をまとめて固定する。
        internal static WatchExistingMovieScanDecision EvaluateWatchExistingMovieScanDecision(
            bool restrictToVisibleMovies,
            ISet<string> visibleMoviePaths,
            string movieFullPath,
            bool allowViewConsistencyRepair,
            ISet<string> existingViewMoviePaths,
            string searchKeyword,
            ISet<string> displayedMoviePaths,
            bool useIncrementalUiMode,
            int pendingQueueCount,
            int batchSize
        )
        {
            WatchScanEntryDecision entryDecision = EvaluateWatchScanEntryDecision(
                restrictToVisibleMovies,
                visibleMoviePaths,
                movieFullPath
            );
            if (entryDecision.ShouldSkipByVisibleOnlyGate)
            {
                return new WatchExistingMovieScanDecision(
                    ShouldSkipByVisibleOnlyGate: true,
                    IsViewRepairSuppressed: false,
                    ViewConsistency: MovieViewConsistencyDecision.None,
                    ShouldFlushEnqueueBatch: false,
                    Stages: [.. entryDecision.Stages]
                );
            }

            WatchExistingMovieFlowDecision flowDecision = EvaluateWatchExistingMovieFlowDecision(
                allowViewConsistencyRepair,
                existingViewMoviePaths,
                searchKeyword,
                displayedMoviePaths,
                movieFullPath,
                useIncrementalUiMode,
                pendingQueueCount,
                batchSize
            );
            return new WatchExistingMovieScanDecision(
                ShouldSkipByVisibleOnlyGate: false,
                IsViewRepairSuppressed: flowDecision.IsViewRepairSuppressed,
                ViewConsistency: flowDecision.ViewConsistency,
                ShouldFlushEnqueueBatch: flowDecision.ShouldFlushEnqueueBatch,
                Stages: [.. entryDecision.Stages, .. flowDecision.Stages]
            );
        }

        private sealed class WatchScanCoordinatorContext
        {
            public WatchScanCoordinatorContext(
                CheckMode mode,
                string snapshotDbFullPath,
                string snapshotThumbFolder,
                string snapshotDbName,
                int snapshotTabIndex,
                int? autoEnqueueTabIndex,
                WatchScanUiBridge uiBridge
            )
            {
                Mode = mode;
                SnapshotDbFullPath = snapshotDbFullPath ?? "";
                SnapshotThumbFolder = snapshotThumbFolder ?? "";
                SnapshotDbName = snapshotDbName ?? "";
                SnapshotTabIndex = snapshotTabIndex;
                AutoEnqueueTabIndex = autoEnqueueTabIndex;
                UiBridge = uiBridge ?? throw new ArgumentNullException(nameof(uiBridge));
            }

            public CheckMode Mode { get; }
            public string SnapshotDbFullPath { get; }
            public string SnapshotThumbFolder { get; }
            public string SnapshotDbName { get; }
            public int SnapshotTabIndex { get; }
            public int? AutoEnqueueTabIndex { get; }
            public WatchScanUiBridge UiBridge { get; }
            public bool AllowMissingTabAutoEnqueue => AutoEnqueueTabIndex.HasValue;
            public string ThumbnailOutPath { get; set; } = "";
            public Dictionary<string, WatchMainDbMovieSnapshot> ExistingMovieByPath { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ExistingViewMoviePaths { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> DisplayedMoviePaths { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public string SearchKeyword { get; set; } = "";
            public HashSet<string> VisibleMoviePaths { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ExistingThumbnailFileNames { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> OpenRescueRequestKeys { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public bool AllowViewConsistencyRepair { get; set; }
            public bool RestrictWatchWorkToVisibleMovies { get; set; }
            public int CurrentWatchQueueActiveCount { get; set; }
            public int CheckedFolderCount { get; set; }
            public int EnqueuedCount { get; set; }
            public bool HasAnyFolderUpdate { get; set; }
        }

        internal enum WatchScanFlowStage
        {
            VisibleOnlyGatePassed,
            VisibleOnlyGateSkipped,
            PendingMovieBatchDeferred,
            PendingMovieBatchFlushed,
            UiRepairSuppressed,
            UiRepairRequested,
            DisplayedViewRefreshRequested,
            EnqueueBatchDeferred,
            EnqueueBatchFlushed,
        }

        internal readonly record struct WatchScanEntryDecision(
            bool ShouldSkipByVisibleOnlyGate,
            WatchScanFlowStage[] Stages
        );

        internal readonly record struct WatchNewMovieFlowDecision(
            bool ShouldFlushPendingMovieBatch,
            WatchScanFlowStage[] Stages
        );

        internal readonly record struct WatchNewMovieScanDecision(
            bool ShouldSkipByVisibleOnlyGate,
            bool ShouldFlushPendingMovieBatch,
            WatchScanFlowStage[] Stages
        );

        internal readonly record struct WatchExistingMovieFlowDecision(
            bool IsViewRepairSuppressed,
            MovieViewConsistencyDecision ViewConsistency,
            bool ShouldFlushEnqueueBatch,
            WatchScanFlowStage[] Stages
        );

        internal readonly record struct WatchExistingMovieScanDecision(
            bool ShouldSkipByVisibleOnlyGate,
            bool IsViewRepairSuppressed,
            MovieViewConsistencyDecision ViewConsistency,
            bool ShouldFlushEnqueueBatch,
            WatchScanFlowStage[] Stages
        );

        private sealed class WatchFolderScanState
        {
            public WatchFolderScanState(string checkFolder)
            {
                CheckFolder = checkFolder ?? "";
            }

            public string CheckFolder { get; }
            public List<QueueObj> AddFilesByFolder { get; } = [];
            public List<PendingMovieRegistration> PendingNewMovies { get; } = [];
            public int AddedByFolderCount { get; set; }
            public bool UseIncrementalUiMode { get; set; }
            public long ScanBackgroundElapsedMs { get; set; }
            public long MovieInfoTotalMs { get; set; }
            public long DbLookupTotalMs { get; set; }
            public long DbInsertTotalMs { get; set; }
            public long UiReflectTotalMs { get; set; }
            public long EnqueueFlushTotalMs { get; set; }
            public bool HasHitInFolder { get; set; }
        }
    }
}
