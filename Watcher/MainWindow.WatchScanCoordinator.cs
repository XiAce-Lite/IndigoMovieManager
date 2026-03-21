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
        // UI 霑･・ｶ隲ｷ荵昶・ watch 騾包ｽｨ郢ｧ・ｭ郢晢ｽ｣郢昴・縺咏ｹ晢ｽ･邵ｺ・ｮ陋ｻ譎・ｄ郢ｧ・ｹ郢晉ｿｫ繝｣郢晏干縺咏ｹ晢ｽｧ郢昴・繝ｨ郢ｧ蛛ｵ・・ｸｺ阮吶定ｬ繝ｻ竏ｴ郢ｧ荵敖繝ｻ        private async Task InitializeWatchScanCoordinatorContextAsync(
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

        // visible-only gate 邵ｺ・ｯ watch 鬯ｮ蛟ｩ・ｲ・ｰ髣包ｽｷ隴弱ｅ笆｡邵ｺ螟ｧ譟醍ｸｺ荵昶雷邵ｲ窶冩lder 陷・ｽｦ騾・・・ｸ・ｭ邵ｺ・ｮ flush 陟募ｾ娯・郢ｧ繧・・髫ｧ遨ゑｽｾ・｡邵ｺ蜷ｶ・狗ｸｲ繝ｻ        private void RefreshWatchVisibleMovieGate(
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

        // folder 陷雁・ｽｽ髦ｪ繝ｻ陷・ｽｦ騾・・謔ｽ闖ｴ阮呻ｽ定崕繝ｻ・邵ｲ・敬eckFolderAsync 邵ｺ・ｯ陷茨ｽ･陷ｿ・｣邵ｺ・ｨ驍ｨ繧・ｽｺ繝ｻ繝ｻ騾・・笆｡邵ｺ莉｣竏郁汞繝ｻ笳狗ｹｧ荵敖繝ｻ        private async Task ProcessWatchFolderAsync(
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

        // 隴・ｽｰ髫穂ｸ櫁劒騾包ｽｻ邵ｺ・ｯ batch 騾具ｽｻ鬪ｭ・ｲ邵ｺ・ｨ queue flush 郢ｧ繝ｻcoordinator 邵ｺ荵晢ｽ臥ｸｺ・ｾ邵ｺ・ｨ郢ｧ竏壺ｻ陋ｻ・ｶ陟包ｽ｡邵ｺ蜷ｶ・狗ｸｲ繝ｻ        private async Task FlushPendingNewMoviesAsync(
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

        // 1闔会ｽｶ邵ｺ譁絶・邵ｺ・ｮ陋ｻ繝ｻ・ｲ闊鯉ｽ堤ｸｺ阮呻ｼ・ｸｺ・ｸ陝・・笳狗ｸｲ縲絞sible-only / deferred batch / UI髯ｬ諛茨ｽｭ・｣邵ｺ・ｮ鬯・・・ｺ荳奇ｽ定摎・ｺ陞ｳ螢ｹ笘・ｹｧ荵敖繝ｻ        private async Task ProcessScannedMovieAsync(
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

        // small 郢晢ｽ｢郢晢ｽｼ郢晏ｳｨ縲堤ｸｺ・ｯ陷奇ｽｳ隴弱ｅﾂ・忖lk 郢晢ｽ｢郢晢ｽｼ郢晏ｳｨ縲堤ｸｺ・ｯ deferred batch 邵ｺ蜉ｱ窶ｳ邵ｺ繝ｻﾂ・､陋ｻ・ｰ鬩墓鱒縲堤ｸｺ・ｰ邵ｺ繝ｻflush 邵ｺ蜷ｶ・狗ｸｲ繝ｻ        internal static bool ShouldFlushWatchPendingMovieBatch(
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

        // queue 陋幢ｽｴ郢ｧ繧・・邵ｺ莨懶ｽ｢繝ｻ髦懃ｸｺ・ｧ flush 邵ｺ蜉ｱﾂ縲絞sible-only 邵ｺ・ｨ deferred batch 邵ｺ・ｮ鬯・・・ｺ荳奇ｽ定ｬ繝ｻ竏ｴ郢ｧ荵敖繝ｻ        internal static bool ShouldFlushWatchEnqueueBatch(
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

        // 陷茨ｽ･陷ｿ・｣邵ｺ・ｮ visible-only gate 邵ｺ・ｯ邵ｺ阮呻ｼ・ｸｺ・ｧ驕擾ｽｭ驍ｨ・｡邵ｺ蜉ｱﾂ竏晢ｽｾ譴ｧ・ｮ・ｵ邵ｺ・ｮ work 郢ｧ螳夲ｽｵ・ｷ邵ｺ阮呻ｼ・ｸｺ・ｪ邵ｺ繝ｻﾂ繝ｻ        internal static WatchScanEntryDecision EvaluateWatchScanEntryDecision(
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

        // 隴・ｽｰ髫穂ｸ櫁劒騾包ｽｻ邵ｺ・ｯ visible-only 郢ｧ蟶敖螟絶с邵ｺ蜉ｱ笳・募ｾ後堤ｸｺ・ｰ邵ｺ繝ｻdeferred batch 陋ｻ・､陞ｳ螢ｹ竏磯ｨｾ・ｲ郢ｧ竏夲ｽ狗ｸｲ繝ｻ        internal static WatchNewMovieFlowDecision EvaluateWatchNewMovieFlowDecision(
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

        // 隴・ｽｰ髫穂ｸ櫁劒騾包ｽｻ邵ｺ・ｮ陋ｻ繝ｻ・ｲ莉吶・邵ｺ・ｯ邵ｲ縲絞sible-only 驕擾ｽｭ驍ｨ・｡邵ｺ荵晢ｽ・batch 陋ｻ・､陞ｳ螢ｹ竏ｪ邵ｺ・ｧ郢ｧ蜑・ｽｸﾂ邵ｺ・､邵ｺ・ｮ decision 邵ｺ・ｧ陜暦ｽｺ陞ｳ螢ｹ笘・ｹｧ荵敖繝ｻ        internal static WatchNewMovieScanDecision EvaluateWatchNewMovieScanDecision(
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

        // 隴鯉ｽ｢陝・ｼ懆劒騾包ｽｻ邵ｺ・ｯ UI 髯ｬ諛茨ｽｭ・｣邵ｺ・ｮ suppress / repair 郢ｧ雋槭・邵ｺ・ｫ雎趣ｽｺ郢ｧ竏堋竏壺落邵ｺ・ｮ陟募ｾ後・queue batch 郢ｧ雋槫ｴ玖楜螢ｹ笘・ｹｧ荵敖繝ｻ        internal static WatchExistingMovieFlowDecision EvaluateWatchExistingMovieFlowDecision(
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

        // 隴鯉ｽ｢陝・ｼ懆劒騾包ｽｻ邵ｺ・ｮ陋ｻ繝ｻ・ｲ莉吶・郢ｧ繧・縲絞sible-only 邵ｺ・ｨ UI 髯ｬ諛茨ｽｭ・｣/queue 邵ｺ・ｮ鬯・・・ｺ荳奇ｽ堤ｸｺ・ｾ邵ｺ・ｨ郢ｧ竏壺ｻ陜暦ｽｺ陞ｳ螢ｹ笘・ｹｧ荵敖繝ｻ        internal static WatchExistingMovieScanDecision EvaluateWatchExistingMovieScanDecision(
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

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // --- 蟶ｯ1: WatchScanCoordinator 蜀榊・/菫晉蕗蛻ｶ蠕｡繧ｬ繝ｼ繝臥ｾ､ ---
        private enum WatchCoordinatorGuardAction { Continue=0, DeferByUiSuppression=1, DropStaleScope=2 }
        private static bool IsWatchWorkSuppressed(Func<bool> shouldSuppressWatchWork) => shouldSuppressWatchWork?.Invoke() == true;
        private static bool IsCurrentWatchCoordinatorScope(Func<bool> isCurrentWatchScanScope) => isCurrentWatchScanScope?.Invoke() != false;
        private static WatchCoordinatorGuardAction GetWatchCoordinatorGuardAction(Func<bool> isCurrentWatchScanScope, Func<bool> shouldSuppressWatchWork)
            => !IsCurrentWatchCoordinatorScope(isCurrentWatchScanScope) ? WatchCoordinatorGuardAction.DropStaleScope
             : IsWatchWorkSuppressed(shouldSuppressWatchWork) ? WatchCoordinatorGuardAction.DeferByUiSuppression
             : WatchCoordinatorGuardAction.Continue;

        private async System.Threading.Tasks.Task<WatchCoordinatorGuardAction> TryAppendMovieToViewIfWatchAllowedAsync(
            Func<bool> isCurrentWatchScanScope,
            Func<bool> shouldSuppressWatchWork,
            Func<string, string, System.Threading.Tasks.Task> appendMovieToViewAsync,
            string snapshotDbFullPath,
            string moviePath)
        {
            var guard = GetWatchCoordinatorGuardAction(isCurrentWatchScanScope, shouldSuppressWatchWork);
            if (guard != WatchCoordinatorGuardAction.Continue) return guard;
            var append = appendMovieToViewAsync ?? TryAppendMovieToViewByPathAsync;
            await append(snapshotDbFullPath, moviePath);
            return WatchCoordinatorGuardAction.Continue;
        }

        private WatchCoordinatorGuardAction TryFlushPendingQueueItemsIfWatchAllowed(
            Func<bool> isCurrentWatchScanScope,
            Func<bool> shouldSuppressWatchWork,
            System.Action<System.Collections.Generic.List<IndigoMovieManager.Thumbnail.QueueObj>, string> flushPendingQueueItemsAction,
            System.Collections.Generic.List<IndigoMovieManager.Thumbnail.QueueObj> pendingItems,
            string folderPath)
        {
            if (pendingItems == null || pendingItems.Count < 1) return WatchCoordinatorGuardAction.Continue;
            var guard = GetWatchCoordinatorGuardAction(isCurrentWatchScanScope, shouldSuppressWatchWork);
            if (guard != WatchCoordinatorGuardAction.Continue) return guard;
            var flush = flushPendingQueueItemsAction ?? FlushPendingQueueItems; flush(pendingItems, folderPath);
            return WatchCoordinatorGuardAction.Continue;
        }

        internal static WatchFolderMoviePreCheckDecision EvaluateWatchFolderMoviePreCheck(bool hasNotifiedFolderHit, bool skipByVisibleOnlyGate, bool isZeroByteMovie, string fileBody)
        {
            if (skipByVisibleOnlyGate) return new WatchFolderMoviePreCheckDecision("skip_visible_only_gate", false, false, false);
            bool shouldNotifyFolderHit = !hasNotifiedFolderHit;
            if (isZeroByteMovie) return new WatchFolderMoviePreCheckDecision("skip_zero_byte", shouldNotifyFolderHit, false, true);
            if (string.IsNullOrWhiteSpace(fileBody)) return new WatchFolderMoviePreCheckDecision("skip_empty_body", shouldNotifyFolderHit, false, false);
            return new WatchFolderMoviePreCheckDecision("continue", shouldNotifyFolderHit, true, false);
        }

        internal async System.Threading.Tasks.Task<WatchPendingNewMovieFlushResult> FlushPendingNewMoviesAsync(WatchPendingNewMovieFlushContext context)
        {
            if (context?.PendingNewMovies == null || context.PendingNewMovies.Count < 1) return WatchPendingNewMovieFlushResult.None;
            var result = new WatchPendingNewMovieFlushResult();
            var moviesToInsert = context.PendingNewMovies.ConvertAll(x => (IndigoMovieManager.Data.MovieCore)x.Movie);
            if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope)) { result.WasDroppedByStaleScope = true; return result; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var insert = context.InsertMoviesBatchAsync ?? InsertMoviesToMainDbBatchAsync; var inserted = await insert(context.SnapshotDbFullPath, moviesToInsert); sw.Stop(); result.DbInsertElapsedMs = sw.ElapsedMilliseconds;
            foreach (var p in context.PendingNewMovies){
                if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope)) { result.WasDroppedByStaleScope = true; break; }
                var guard = GetWatchCoordinatorGuardAction(context.IsCurrentWatchScanScope, context.ShouldSuppressWatchWork);
                if (guard == WatchCoordinatorGuardAction.DropStaleScope){ result.WasDroppedByStaleScope = true; break; }
                if (guard == WatchCoordinatorGuardAction.DeferByUiSuppression){ result.AddDeferredMoviePath(p.MovieFullPath, context.MarkWatchWorkDeferredWhileSuppressedAction, "pending_movie_flush"); continue; }
                var uiSw = System.Diagnostics.Stopwatch.StartNew(); var append = context.AppendMovieToViewAsync ?? TryAppendMovieToViewByPathAsync; await append(context.SnapshotDbFullPath, p.MovieFullPath); uiSw.Stop(); result.UiReflectElapsedMs += uiSw.ElapsedMilliseconds;
            }
            if (context.AddFilesByFolder?.Count > 0){ var fl = System.Diagnostics.Stopwatch.StartNew(); var g = TryFlushPendingQueueItemsIfWatchAllowed(context.IsCurrentWatchScanScope, context.ShouldSuppressWatchWork, context.FlushPendingQueueItemsAction, context.AddFilesByFolder, context.CheckFolder); fl.Stop(); if (g==WatchCoordinatorGuardAction.Continue){ context.RefreshWatchVisibleMovieGate?.Invoke("pending_movie_flush"); } result.EnqueueFlushElapsedMs += fl.ElapsedMilliseconds; }
            context.PendingNewMovies.Clear(); return result;
        }

        internal async System.Threading.Tasks.Task<WatchScannedMovieProcessResult> ProcessScannedMovieAsync(WatchScannedMovieContext context, string movieFullPath, string fileBody)
        {
            var result = new WatchScannedMovieProcessResult(); if (context==null || string.IsNullOrWhiteSpace(movieFullPath)){ result.Outcome="skip_invalid_path"; return result; } if (string.IsNullOrWhiteSpace(fileBody)){ result.Outcome="skip_empty_body"; return result; }
            var dbSw = System.Diagnostics.Stopwatch.StartNew(); bool exists = context.ExistingMovieByPath.TryGetValue(movieFullPath, out var cur); dbSw.Stop(); result.DbLookupElapsedMs = dbSw.ElapsedMilliseconds;
            if (!exists){ var infoSw = System.Diagnostics.Stopwatch.StartNew(); var mvi = await System.Threading.Tasks.Task.Run(()=> new IndigoMovieManager.Thumbnail.MovieInfo(movieFullPath)); infoSw.Stop(); result.MovieInfoElapsedMs = infoSw.ElapsedMilliseconds; if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope)){ result.WasDroppedByStaleScope=true; result.Outcome="drop_stale_scope"; return result; } var guard = GetWatchCoordinatorGuardAction(context.IsCurrentWatchScanScope, context.ShouldSuppressWatchWork); if (guard==WatchCoordinatorGuardAction.DropStaleScope){ result.WasDroppedByStaleScope=true; result.Outcome="drop_stale_scope"; return result; } bool suppressed = guard==WatchCoordinatorGuardAction.DeferByUiSuppression; context.PendingMovieFlushContext.PendingNewMovies.Add(new PendingMovieRegistration(movieFullPath, fileBody, mvi)); if(!suppressed){ AddOrUpdatePendingMoviePlaceholder(movieFullPath, fileBody, context.SnapshotTabIndex, PendingMoviePlaceholderStatus.Detected);} result.HasFolderUpdate = true; if(!suppressed && (context.UseIncrementalUiMode || context.PendingMovieFlushContext.PendingNewMovies.Count >= FolderScanEnqueueBatchSize)){ var w=System.Diagnostics.Stopwatch.StartNew(); var fr = await FlushPendingNewMoviesAsync(context.PendingMovieFlushContext); w.Stop(); result.ApplyPendingFlush(fr); result.FlushWaitElapsedMs = w.ElapsedMilliseconds; if(result.WasDroppedByStaleScope){ result.Outcome="drop_stale_scope"; return result; } } result.Outcome = suppressed?"pending_insert_suppressed":"pending_insert"; return result; }
            if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope)){ result.WasDroppedByStaleScope=true; result.Outcome="drop_stale_scope"; return result; }
            var vc = EvaluateMovieViewConsistency(context.AllowViewConsistencyRepair, true, context.ExistingViewMoviePaths, context.SearchKeyword, context.DisplayedMoviePaths, movieFullPath);
            if (vc.ShouldRepairView){ var guard = GetWatchCoordinatorGuardAction(context.IsCurrentWatchScanScope, context.ShouldSuppressWatchWork); if(guard==WatchCoordinatorGuardAction.DropStaleScope){ result.WasDroppedByStaleScope=true; result.Outcome="drop_stale_scope"; return result; } if(guard==WatchCoordinatorGuardAction.DeferByUiSuppression){ result.AddDeferredMoviePath(movieFullPath, context.PendingMovieFlushContext?.MarkWatchWorkDeferredWhileSuppressedAction, "existing_movie_repair"); result.Outcome="skip_non_upper_tab"; return result; } var uiSw = System.Diagnostics.Stopwatch.StartNew(); var append = context.AppendMovieToViewAsync ?? TryAppendMovieToViewByPathAsync; await append(context.SnapshotDbFullPath, movieFullPath); uiSw.Stop(); result.UiReflectElapsedMs += uiSw.ElapsedMilliseconds; result.Outcome = "repaired"; return result; }
            result.Outcome = vc.ShouldRefreshDisplayedView?"refresh_displayed":"skip_non_upper_tab"; return result;
        }

        internal WatchFinalQueueFlushResult FlushFinalWatchFolderQueue(WatchFolderScanContext context)
        {
            if (context?.ScannedMovieContext?.PendingMovieFlushContext?.AddFilesByFolder == null) return WatchFinalQueueFlushResult.None;
            if (!IsCurrentWatchCoordinatorScope(context.ScannedMovieContext.PendingMovieFlushContext.IsCurrentWatchScanScope)) return new WatchFinalQueueFlushResult(0,false,true);
            var sw=System.Diagnostics.Stopwatch.StartNew(); var g = TryFlushPendingQueueItemsIfWatchAllowed(context.ScannedMovieContext.PendingMovieFlushContext.IsCurrentWatchScanScope, context.ScannedMovieContext.ShouldSuppressWatchWork, context.ScannedMovieContext.PendingMovieFlushContext.FlushPendingQueueItemsAction, context.ScannedMovieContext.PendingMovieFlushContext.AddFilesByFolder, context.ScannedMovieContext.PendingMovieFlushContext.CheckFolder); sw.Stop(); if(g==WatchCoordinatorGuardAction.Continue){ if(!IsCurrentWatchCoordinatorScope(context.ScannedMovieContext.PendingMovieFlushContext.IsCurrentWatchScanScope)) return new WatchFinalQueueFlushResult(sw.ElapsedMilliseconds,false,true); context.ScannedMovieContext.PendingMovieFlushContext.RefreshWatchVisibleMovieGate?.Invoke("folder_final_flush"); }
            return new WatchFinalQueueFlushResult(sw.ElapsedMilliseconds, g==WatchCoordinatorGuardAction.DeferByUiSuppression, g==WatchCoordinatorGuardAction.DropStaleScope);
        }

        internal static string BuildDeferredWatchScanScopeKey(string dbFullPath, string watchFolder, bool includeSubfolders)
        {
            // 帯1: 監視スコープキーの生成
            // - DBパス/監視フォルダはフルパス化し、末尾の区切り(\\,/)を安全に除去
            // - ドライブ直下(E:\\ 等)は末尾区切りを保持して正規化
            // - 比較はWindows想定で大小無視のため小文字化
            static string Normalize(string path)
            {
                // 空は空として扱う
                string raw = (path ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(raw)) return string.Empty;

                try
                {
                    // まずフルパス化
                    string full = System.IO.Path.GetFullPath(raw);
                    string root = System.IO.Path.GetPathRoot(full) ?? string.Empty;

                    // ルート(E:\\ 等)はそのまま、それ以外は末尾セパレータを落とす
                    if (full.Equals(root, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return full.ToLowerInvariant();
                    }

                    return full.TrimEnd('\','/').ToLowerInvariant();
                }
                catch
                {
                    // フルパス化失敗時は安全側: 末尾セパレータ除去 + 小文字
                    return raw.TrimEnd('\','/').ToLowerInvariant();
                }
            }

            string db = Normalize(dbFullPath);
            string folder = Normalize(watchFolder);
            return $"{db}|{folder}|{includeSubfolders}";
        }
        internal static System.DateTime? MergeDeferredWatchScanCursorUtc(System.DateTime? existingCursorUtc, System.DateTime? observedCursorUtc)
        { if(observedCursorUtc==null) return existingCursorUtc; if(existingCursorUtc==null) return observedCursorUtc; return observedCursorUtc>existingCursorUtc?observedCursorUtc:existingCursorUtc; }
        internal static bool CanUseWatchScanScope(string currentDbFullPath, string snapshotDbFullPath, long requestScopeStamp, long currentScopeStamp)
        { bool sameDb = string.Equals(currentDbFullPath??string.Empty, snapshotDbFullPath??string.Empty, System.StringComparison.OrdinalIgnoreCase); return sameDb && requestScopeStamp==currentScopeStamp; }

        private sealed class DeferredWatchScanState{ public System.Collections.Generic.Queue<string> PendingPaths {get;} = new(); public System.DateTime? LastSyncUtc {get; set;} }
        private readonly object _deferredWatchScanSync = new object();
        private readonly System.Collections.Generic.Dictionary<string, DeferredWatchScanState> _deferredWatchScanStateByScope = new(System.StringComparer.OrdinalIgnoreCase);
        private long _watchScanScopeStamp;

        private void ReplaceDeferredWatchScanBatch(string snapshotDbFullPath, long requestScopeStamp, string watchFolder, bool includeSubfolders, System.Collections.Generic.IEnumerable<string> pendingPaths, System.DateTime? lastSyncUtc)
        {
            if (!CanUseWatchScanScope(MainVM?.DbInfo?.DBFullPath ?? string.Empty, snapshotDbFullPath, requestScopeStamp, _watchScanScopeStamp)) return;
            string scopeKey = BuildDeferredWatchScanScopeKey(snapshotDbFullPath, watchFolder, includeSubfolders);
            lock(_deferredWatchScanSync){ if(!_deferredWatchScanStateByScope.TryGetValue(scopeKey, out var state)){ state=new DeferredWatchScanState(); _deferredWatchScanStateByScope[scopeKey]=state; } state.PendingPaths.Clear(); if(pendingPaths != null){ foreach(var p in pendingPaths){ if(!string.IsNullOrWhiteSpace(p)) state.PendingPaths.Enqueue(p);} } state.LastSyncUtc = lastSyncUtc; }
        }

        private void SaveEverythingLastSyncUtc(string snapshotDbFullPath, long requestScopeStamp, string watchFolder, bool includeSubfolders, System.DateTime lastSyncUtc)
        {
            // 帯1: stale scope は即終了
            if (!CanUseWatchScanScope(MainVM?.DbInfo?.DBFullPath ?? string.Empty, snapshotDbFullPath, requestScopeStamp, _watchScanScopeStamp)) return;

            // 帯1: systemテーブルが無いDBでは例外で監視を止めない(WhiteBrowser DBは変更しない)
            using var connection = new System.Data.SQLite.SQLiteConnection($"Data Source={snapshotDbFullPath}");
            connection.Open();

            // systemテーブル存在確認
            using (var exists = connection.CreateCommand())
            {
                exists.CommandText = "select 1 from sqlite_master where type='table' and name='system' limit 1";
                var ok = exists.ExecuteScalar() != null;
                if (!ok)
                {
                    DebugRuntimeLog.Write("watch-check", "system table missing; last_sync save skipped.");
                    return; // 無ければ安全にskip
                }
            }

            // upsert保存（キーは正規化済みスコープを採用）
            using var cmd = connection.CreateCommand();
            string key = $"EverythingLastSyncUtc:{BuildDeferredWatchScanScopeKey(snapshotDbFullPath, watchFolder, includeSubfolders)}";
            cmd.CommandText = "insert into system(attr,value) values(@a,@v) on conflict(attr) do update set value=excluded.value";
            cmd.Parameters.AddWithValue("@a", key);
            cmd.Parameters.AddWithValue("@v", lastSyncUtc.ToUniversalTime().ToString("o"));
            cmd.ExecuteNonQuery();

            // 旧キーも同値へ寄せ、既存保存値との互換を保つ。
            try
            {
                string legacyAttr = BuildEverythingLastSyncAttr(watchFolder, includeSubfolders);
                DB.SQLite.UpsertSystemTable(
                    snapshotDbFullPath,
                    legacyAttr,
                    lastSyncUtc.ToUniversalTime().ToString("o")
                );
            }
            catch
            {
                // 互換保存失敗は監視中断より軽いため握りつぶす。
            }
        }

        internal sealed class WatchPendingNewMovieFlushContext
        {
            public string SnapshotDbFullPath { get; set; } = string.Empty;
            public System.Collections.Generic.Dictionary<string, WatchMainDbMovieSnapshot> ExistingMovieByPath { get; set; }
            public System.Collections.Generic.List<PendingMovieRegistration> PendingNewMovies { get; set; }
            public bool UseIncrementalUiMode { get; set; }
            public bool AllowMissingTabAutoEnqueue { get; set; }
            public int? AutoEnqueueTabIndex { get; set; }
            public string ThumbnailOutPath { get; set; } = string.Empty;
            public System.Collections.Generic.HashSet<string> ExistingThumbnailFileNames { get; set; }
            public System.Collections.Generic.HashSet<string> OpenRescueRequestKeys { get; set; }
            public System.Collections.Generic.List<IndigoMovieManager.Thumbnail.QueueObj> AddFilesByFolder { get; set; }
            public string CheckFolder { get; set; } = string.Empty;
            public System.Action<string> RefreshWatchVisibleMovieGate { get; set; }
            public System.Func<bool> ShouldSuppressWatchWork { get; set; }
            public System.Func<bool> IsCurrentWatchScanScope { get; set; }
            public System.Action<string> MarkWatchWorkDeferredWhileSuppressedAction { get; set; }
            public System.Func<string, System.Collections.Generic.List<IndigoMovieManager.Data.MovieCore>, System.Threading.Tasks.Task<int>> InsertMoviesBatchAsync { get; set; }
            public System.Func<string, string, System.Threading.Tasks.Task> AppendMovieToViewAsync { get; set; }
            public System.Action<string> RemovePendingMoviePlaceholderAction { get; set; }
            public System.Action<System.Collections.Generic.List<IndigoMovieManager.Thumbnail.QueueObj>, string> FlushPendingQueueItemsAction { get; set; }
        }

        internal sealed class WatchPendingNewMovieFlushResult
        {
            public static WatchPendingNewMovieFlushResult None { get; } = new();
            public int AddedByFolderCount { get; set; }
            public int EnqueuedCount { get; set; }
            public long DbInsertElapsedMs { get; set; }
            public long UiReflectElapsedMs { get; set; }
            public long EnqueueFlushElapsedMs { get; set; }
            public bool WasDroppedByStaleScope { get; set; }
            public System.Collections.Generic.List<string> DeferredMoviePathsByUiSuppression { get; } = new();
            public void AddDeferredMoviePath(string movieFullPath, System.Action<string> markDeferredAction, string trigger){ if(string.IsNullOrWhiteSpace(movieFullPath)) return; if(!DeferredMoviePathsByUiSuppression.Exists(x=>string.Equals(x, movieFullPath, System.StringComparison.OrdinalIgnoreCase))) DeferredMoviePathsByUiSuppression.Add(movieFullPath); markDeferredAction?.Invoke(trigger); }        }

        internal sealed class WatchScannedMovieContext
        {
            public string SnapshotDbFullPath { get; set; } = string.Empty;
            public int SnapshotTabIndex { get; set; }
            public System.Collections.Generic.Dictionary<string, WatchMainDbMovieSnapshot> ExistingMovieByPath { get; set; }
            public System.Collections.Generic.HashSet<string> ExistingViewMoviePaths { get; set; }
            public System.Collections.Generic.HashSet<string> DisplayedMoviePaths { get; set; }
            public string SearchKeyword { get; set; } = string.Empty;
            public bool AllowViewConsistencyRepair { get; set; }
            public bool UseIncrementalUiMode { get; set; }
            public bool AllowMissingTabAutoEnqueue { get; set; }
            public int? AutoEnqueueTabIndex { get; set; }
            public string ThumbnailOutPath { get; set; } = string.Empty;
            public System.Collections.Generic.HashSet<string> ExistingThumbnailFileNames { get; set; }
            public System.Collections.Generic.HashSet<string> OpenRescueRequestKeys { get; set; }
            public WatchPendingNewMovieFlushContext PendingMovieFlushContext { get; set; }
            public System.Func<bool> ShouldSuppressWatchWork { get; set; }
            public System.Func<bool> IsCurrentWatchScanScope { get; set; }
            public System.Func<string, string, System.Threading.Tasks.Task> AppendMovieToViewAsync { get; set; }
        }

        internal sealed class WatchFolderScanContext
        {
            public bool RestrictWatchWorkToVisibleMovies { get; set; }
            public System.Collections.Generic.ISet<string> VisibleMoviePaths { get; set; }
            public bool HasNotifiedFolderHit { get; set; }
            public System.Action NotifyFolderFirstHit { get; set; }
            public bool AllowMissingTabAutoEnqueue { get; set; }
            public int? AutoEnqueueTabIndex { get; set; }
            public WatchScannedMovieContext ScannedMovieContext { get; set; }
        }

        internal class WatchScannedMovieProcessResult
        {
            public string Outcome { get; set; } = string.Empty; public bool HasFolderUpdate { get; set; } public int AddedByFolderCount { get; set; } public int EnqueuedCount { get; set; } public long DbLookupElapsedMs { get; set; } public long ThumbExistsElapsedMs { get; set; } public long MovieInfoElapsedMs { get; set; } public long FlushWaitElapsedMs { get; set; } public long DbInsertElapsedMs { get; set; } public long UiReflectElapsedMs { get; set; } public long EnqueueFlushElapsedMs { get; set; } public bool WasDroppedByStaleScope { get; set; }
            public System.Collections.Generic.List<string> DeferredMoviePathsByUiSuppression { get; } = new();
            public void AddDeferredMoviePath(string movieFullPath, System.Action<string> markDeferredAction, string trigger){ if(string.IsNullOrWhiteSpace(movieFullPath)) return; if(!DeferredMoviePathsByUiSuppression.Exists(x=>string.Equals(x, movieFullPath, System.StringComparison.OrdinalIgnoreCase))) DeferredMoviePathsByUiSuppression.Add(movieFullPath); markDeferredAction?.Invoke(trigger); }
            public void ApplyPendingFlush(WatchPendingNewMovieFlushResult f){ if(f==null) return; DbInsertElapsedMs+=f.DbInsertElapsedMs; UiReflectElapsedMs+=f.UiReflectElapsedMs; EnqueueFlushElapsedMs+=f.EnqueueFlushElapsedMs; AddedByFolderCount+=f.AddedByFolderCount; EnqueuedCount+=f.EnqueuedCount; WasDroppedByStaleScope|=f.WasDroppedByStaleScope; foreach(var p in f.DeferredMoviePathsByUiSuppression){ AddDeferredMoviePath(p, null, string.Empty);} }
        }

        internal sealed class WatchFolderScanMovieResult : WatchScannedMovieProcessResult { public long TotalElapsedMs { get; set; } }
        internal readonly record struct WatchFinalQueueFlushResult(long ElapsedMs, bool WasDeferredBySuppression, bool WasDroppedByStaleScope){ public static WatchFinalQueueFlushResult None => new(0,false,false);}    }
}
