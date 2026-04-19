using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using IndigoMovieManager.Data;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.ViewModels;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // フォルダ走査で見つけた新規動画を、何件単位でサムネイルキューへ流すか。
        // 走査完了を待たずに段階投入することで、初動を早めつつI/O競合を抑える。
        private const int FolderScanEnqueueBatchSize = 100;

        private enum WatchCoordinatorGuardAction
        {
            Continue = 0,
            DeferByUiSuppression = 1,
            DropStaleScope = 2,
        }

        // watch 中の UI 仕事は、実行直前でも suppression を見直す。
        private static bool IsWatchWorkSuppressed(Func<bool> shouldSuppressWatchWork)
        {
            return shouldSuppressWatchWork?.Invoke() == true;
        }

        private static bool IsCurrentWatchCoordinatorScope(Func<bool> isCurrentWatchScanScope)
        {
            return isCurrentWatchScanScope?.Invoke() != false;
        }

        private static WatchCoordinatorGuardAction GetWatchCoordinatorGuardAction(
            Func<bool> isCurrentWatchScanScope,
            Func<bool> shouldSuppressWatchWork
        )
        {
            if (!IsCurrentWatchCoordinatorScope(isCurrentWatchScanScope))
            {
                return WatchCoordinatorGuardAction.DropStaleScope;
            }

            return IsWatchWorkSuppressed(shouldSuppressWatchWork)
                ? WatchCoordinatorGuardAction.DeferByUiSuppression
                : WatchCoordinatorGuardAction.Continue;
        }

        // watch のスコープ一致判定は、走査調停側で完結させる。
        internal static bool CanUseWatchScanScope(
            string currentDbFullPath,
            string snapshotDbFullPath,
            long requestScopeStamp,
            long currentScopeStamp
        )
        {
            if (requestScopeStamp < 1 || requestScopeStamp != currentScopeStamp)
            {
                return false;
            }

            return AreSameMainDbPath(currentDbFullPath, snapshotDbFullPath);
        }

        // DB保存と同じ粒度へ合わせ、watch比較で秒未満の揺れを誤検知しないようにする。
        private static string FormatWatchObservedFileDate(DateTime value)
        {
            DateTime trimmed = value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));
            return FormatDbDateTime(trimmed);
        }

        // DBの movie_size は KB 単位なので、watch 側も同じ単位へ揃えて比較する。
        private static long ToWatchObservedMovieSizeKb(long movieSizeBytes)
        {
            return Math.Max(0, movieSizeBytes / 1024);
        }

        private static WatchMovieObservedState CreateWatchObservedState(MovieInfo movie)
        {
            return new WatchMovieObservedState(
                FormatWatchObservedFileDate(movie.FileDate),
                ToWatchObservedMovieSizeKb(movie.MovieSize),
                movie.MovieLength
            );
        }

        internal static WatchMovieDirtyFields DetectExistingMovieDirtyFields(
            WatchMainDbMovieSnapshot snapshot,
            WatchMovieObservedState observedState
        )
        {
            WatchMovieDirtyFields dirtyFields = WatchMovieDirtyFields.None;
            if (
                !string.Equals(
                    snapshot.FileDateText ?? "",
                    observedState.FileDateText ?? "",
                    StringComparison.Ordinal
                )
            )
            {
                dirtyFields |= WatchMovieDirtyFields.FileDate;
            }

            if (snapshot.MovieSizeKb != observedState.MovieSizeKb)
            {
                dirtyFields |= WatchMovieDirtyFields.MovieSize;
            }

            if (
                observedState.MovieLengthSeconds.HasValue
                && snapshot.MovieLengthSeconds != observedState.MovieLengthSeconds.Value
            )
            {
                dirtyFields |= WatchMovieDirtyFields.MovieLength;
            }

            return dirtyFields;
        }

        private static async Task<(WatchMovieDirtyFields DirtyFields, WatchMovieObservedState? ObservedState)> TryBuildExistingMovieObservedStateAsync(
            string movieFullPath,
            WatchMainDbMovieSnapshot snapshot,
            bool allowMovieLengthProbe,
            Func<string, Task<WatchMovieObservedState?>> probeExistingMovieObservedStateAsync
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return (WatchMovieDirtyFields.None, null);
            }

            try
            {
                FileInfo file = new(movieFullPath);
                if (!file.Exists)
                {
                    return (WatchMovieDirtyFields.None, null);
                }

                WatchMovieObservedState currentObservedState = new(
                    FormatWatchObservedFileDate(file.LastWriteTime),
                    ToWatchObservedMovieSizeKb(file.Length),
                    null
                );

                WatchMovieDirtyFields cheapDirtyFields = DetectExistingMovieDirtyFields(
                    snapshot,
                    currentObservedState
                );

                bool shouldProbeMovieLength =
                    allowMovieLengthProbe
                    && (
                        snapshot.MovieLengthSeconds < 1
                        || (cheapDirtyFields
                            & (WatchMovieDirtyFields.FileDate | WatchMovieDirtyFields.MovieSize))
                            != WatchMovieDirtyFields.None
                    );
                if (shouldProbeMovieLength)
                {
                    WatchMovieObservedState? probedObservedState = null;
                    try
                    {
                        probedObservedState =
                            probeExistingMovieObservedStateAsync == null
                                ? null
                                : await probeExistingMovieObservedStateAsync(movieFullPath);
                    }
                    catch (Exception ex)
                    {
                        DebugRuntimeLog.Write(
                            "watch-check",
                            $"existing movie metadata probe skipped: movie='{movieFullPath}' reason={ex.GetType().Name}"
                        );
                    }

                    currentObservedState = MergeWatchMovieObservedState(
                            currentObservedState,
                            probedObservedState
                        )
                        ?? currentObservedState;
                }

                WatchMovieDirtyFields detectedDirtyFields = DetectExistingMovieDirtyFields(
                    snapshot,
                    currentObservedState
                );
                return detectedDirtyFields == WatchMovieDirtyFields.None
                    ? (WatchMovieDirtyFields.None, null)
                    : (detectedDirtyFields, currentObservedState);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"existing movie dirty detect skipped: movie='{movieFullPath}' reason={ex.GetType().Name}"
                );
                return (WatchMovieDirtyFields.None, null);
            }
        }

        internal static WatchMovieObservedState? MergeWatchMovieObservedState(
            WatchMovieObservedState? currentState,
            WatchMovieObservedState? incomingState
        )
        {
            if (!incomingState.HasValue)
            {
                return currentState;
            }

            if (!currentState.HasValue)
            {
                return incomingState;
            }

            WatchMovieObservedState current = currentState.Value;
            WatchMovieObservedState incoming = incomingState.Value;
            return new WatchMovieObservedState(
                string.IsNullOrWhiteSpace(incoming.FileDateText)
                    ? current.FileDateText
                    : incoming.FileDateText,
                incoming.MovieSizeKb > 0 ? incoming.MovieSizeKb : current.MovieSizeKb,
                incoming.MovieLengthSeconds ?? current.MovieLengthSeconds
            );
        }

        private static bool ShouldProbeExistingMovieObservedState(
            bool allowExistingMovieDirtyTracking,
            bool useIncrementalUiMode
        )
        {
            return allowExistingMovieDirtyTracking
                && useIncrementalUiMode;
        }

        private static async Task<WatchMovieObservedState?> ProbeExistingMovieObservedStateAsync(
            string movieFullPath
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return null;
            }

            try
            {
                MovieInfo movie = await Task.Run(() => new MovieInfo(movieFullPath, noHash: true));
                return CreateWatchObservedState(movie);
            }
            catch
            {
                return null;
            }
        }

        // append の直前で止め、左ドロワー表示中の差し込みを防ぐ。
        private async Task<WatchCoordinatorGuardAction> TryAppendMovieToViewIfWatchAllowedAsync(
            Func<bool> isCurrentWatchScanScope,
            Func<bool> shouldSuppressWatchWork,
            Func<string, string, Task> appendMovieToViewAsync,
            string snapshotDbFullPath,
            string moviePath
        )
        {
            WatchCoordinatorGuardAction guardAction = GetWatchCoordinatorGuardAction(
                isCurrentWatchScanScope,
                shouldSuppressWatchWork
            );
            if (guardAction != WatchCoordinatorGuardAction.Continue)
            {
                return guardAction;
            }

            Func<string, string, Task> appendAction =
                appendMovieToViewAsync ?? TryAppendMovieToViewByPathAsync;
            await appendAction(snapshotDbFullPath, moviePath);
            return WatchCoordinatorGuardAction.Continue;
        }

        // flush の直前でも止め、watch pass 中の1件/1batch漏れを防ぐ。
        private WatchCoordinatorGuardAction TryFlushPendingQueueItemsIfWatchAllowed(
            Func<bool> isCurrentWatchScanScope,
            Func<bool> shouldSuppressWatchWork,
            Action<List<QueueObj>, string> flushPendingQueueItemsAction,
            List<QueueObj> pendingItems,
            string folderPath
        )
        {
            if (pendingItems == null || pendingItems.Count < 1)
            {
                return WatchCoordinatorGuardAction.Continue;
            }

            WatchCoordinatorGuardAction guardAction = GetWatchCoordinatorGuardAction(
                isCurrentWatchScanScope,
                shouldSuppressWatchWork
            );
            if (guardAction != WatchCoordinatorGuardAction.Continue)
            {
                return guardAction;
            }

            Action<List<QueueObj>, string> flushAction =
                flushPendingQueueItemsAction ?? FlushPendingQueueItems;
            flushAction(pendingItems, folderPath);
            return WatchCoordinatorGuardAction.Continue;
        }

        // 走査中に溜めた新規動画を、DB反映・UI反映・サムネ投入まで一括で流す調停役。
        internal async Task<WatchPendingNewMovieFlushResult> FlushPendingNewMoviesAsync(
            WatchPendingNewMovieFlushContext context
        )
        {
            if (context?.PendingNewMovies == null || context.PendingNewMovies.Count < 1)
            {
                return WatchPendingNewMovieFlushResult.None;
            }

            WatchPendingNewMovieFlushResult result = new();
            List<MovieCore> moviesToInsert = context.PendingNewMovies
                .Select(x => (MovieCore)x.Movie)
                .ToList();

            if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
            {
                result.WasDroppedByStaleScope = true;
                return result;
            }

            Stopwatch stepStopwatch = Stopwatch.StartNew();
            Func<string, List<MovieCore>, Task<int>> insertMoviesBatchAsync =
                context.InsertMoviesBatchAsync ?? InsertMoviesToMainDbBatchAsync;
            int insertedCount = await insertMoviesBatchAsync(context.SnapshotDbFullPath, moviesToInsert);
            stepStopwatch.Stop();
            result.DbInsertElapsedMs += stepStopwatch.ElapsedMilliseconds;

            if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
            {
                result.WasDroppedByStaleScope = true;
                return result;
            }

            TryAdjustRegisteredMovieCount(context.SnapshotDbFullPath, insertedCount);

            foreach (PendingMovieRegistration pending in context.PendingNewMovies)
            {
                if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                {
                    result.WasDroppedByStaleScope = true;
                    return result;
                }

                context.ExistingMovieByPath[pending.MovieFullPath] = new WatchMainDbMovieSnapshot(
                    pending.Movie.MovieId,
                    pending.Movie.Hash ?? "",
                    FormatWatchObservedFileDate(pending.Movie.FileDate),
                    ToWatchObservedMovieSizeKb(pending.Movie.MovieSize),
                    pending.Movie.MovieLength
                );
                result.AddChangedMovie(
                    pending.MovieFullPath,
                    WatchMovieChangeKind.SourceInserted,
                    WatchMovieDirtyFields.MovieName
                        | WatchMovieDirtyFields.MoviePath
                        | WatchMovieDirtyFields.Kana
                        | WatchMovieDirtyFields.FileDate
                        | WatchMovieDirtyFields.MovieSize
                        | WatchMovieDirtyFields.RegistDate
                        | WatchMovieDirtyFields.MovieLength
                        | WatchMovieDirtyFields.Hash,
                    CreateWatchObservedState(pending.Movie)
                );
                bool shouldSuppressWatchWork = context.ShouldSuppressWatchWork?.Invoke() == true;
                bool shouldDeferCurrentMovie = shouldSuppressWatchWork;

                // 小規模時は1件ずつUIへ反映し、追加の体感を優先する。
                if (context.UseIncrementalUiMode && !shouldSuppressWatchWork)
                {
                    stepStopwatch.Restart();
                    WatchCoordinatorGuardAction appendGuardAction =
                        await TryAppendMovieToViewIfWatchAllowedAsync(
                        context.IsCurrentWatchScanScope,
                        context.ShouldSuppressWatchWork,
                        context.AppendMovieToViewAsync,
                        context.SnapshotDbFullPath,
                        pending.Movie.MoviePath
                    );
                    stepStopwatch.Stop();
                    if (appendGuardAction == WatchCoordinatorGuardAction.Continue)
                    {
                        result.UiReflectElapsedMs += stepStopwatch.ElapsedMilliseconds;
                    }
                    else if (appendGuardAction == WatchCoordinatorGuardAction.DropStaleScope)
                    {
                        result.WasDroppedByStaleScope = true;
                        return result;
                    }
                    else
                    {
                        shouldDeferCurrentMovie = true;
                    }
                }

                // DB反映が完了したら、仮表示から正式表示へ役割を切り替える。
                if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                {
                    result.WasDroppedByStaleScope = true;
                    return result;
                }

                Action<string> removePendingMoviePlaceholderAction =
                    context.RemovePendingMoviePlaceholderAction ?? RemovePendingMoviePlaceholder;
                removePendingMoviePlaceholderAction(pending.MovieFullPath);

                if (
                    shouldSuppressWatchWork
                    || !context.AllowMissingTabAutoEnqueue
                    || !context.AutoEnqueueTabIndex.HasValue
                )
                {
                    if (shouldDeferCurrentMovie)
                    {
                        if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                        {
                            result.WasDroppedByStaleScope = true;
                            return result;
                        }

                        result.AddDeferredMoviePath(
                            pending.MovieFullPath,
                            context.MarkWatchWorkDeferredWhileSuppressedAction,
                            "pending_movie_flush"
                        );
                    }

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
                    if (shouldDeferCurrentMovie)
                    {
                        if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                        {
                            result.WasDroppedByStaleScope = true;
                            return result;
                        }

                        result.AddDeferredMoviePath(
                            pending.MovieFullPath,
                            context.MarkWatchWorkDeferredWhileSuppressedAction,
                            "pending_movie_flush"
                        );
                    }

                    continue;
                }

                MissingThumbnailAutoEnqueueBlockReason pendingBlockReason =
                    ResolveMissingThumbnailAutoEnqueueBlockReason(
                        pending.MovieFullPath,
                        context.AutoEnqueueTabIndex.Value,
                        context.ExistingThumbnailFileNames,
                        context.OpenRescueRequestKeys
                    );
                if (pendingBlockReason != MissingThumbnailAutoEnqueueBlockReason.None)
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"skip enqueue by failure-state: tab={context.AutoEnqueueTabIndex.Value}, movie='{pending.MovieFullPath}', reason={DescribeMissingThumbnailAutoEnqueueBlockReason(pendingBlockReason)}"
                    );
                    if (shouldDeferCurrentMovie)
                    {
                        if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                        {
                            result.WasDroppedByStaleScope = true;
                            return result;
                        }

                        result.AddDeferredMoviePath(
                            pending.MovieFullPath,
                            context.MarkWatchWorkDeferredWhileSuppressedAction,
                            "pending_movie_flush"
                        );
                    }

                    continue;
                }

                DebugRuntimeLog.Write(
                    "watch-check",
                    $"enqueue by missing-tab-thumb: tab={context.AutoEnqueueTabIndex.Value}, movie='{pending.MovieFullPath}'"
                );

                QueueObj temp = new()
                {
                    MovieId = pending.Movie.MovieId,
                    MovieFullPath = pending.MovieFullPath,
                    Hash = pending.Movie.Hash,
                    Tabindex = context.AutoEnqueueTabIndex.Value,
                    Priority = ThumbnailQueuePriority.Normal,
                };
                context.AddFilesByFolder.Add(temp);
                result.AddedByFolderCount++;
                result.EnqueuedCount++;

                if (
                    context.UseIncrementalUiMode
                    || context.AddFilesByFolder.Count >= FolderScanEnqueueBatchSize
                )
                {
                    stepStopwatch.Restart();
                    WatchCoordinatorGuardAction flushGuardAction =
                        TryFlushPendingQueueItemsIfWatchAllowed(
                        context.IsCurrentWatchScanScope,
                        context.ShouldSuppressWatchWork,
                        context.FlushPendingQueueItemsAction,
                        context.AddFilesByFolder,
                        context.CheckFolder
                    );
                    stepStopwatch.Stop();
                    if (flushGuardAction == WatchCoordinatorGuardAction.Continue)
                    {
                        result.EnqueueFlushElapsedMs += stepStopwatch.ElapsedMilliseconds;
                        if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                        {
                            result.WasDroppedByStaleScope = true;
                            return result;
                        }

                        context.RefreshWatchVisibleMovieGate?.Invoke("pending_movie_flush");
                    }
                    else if (flushGuardAction == WatchCoordinatorGuardAction.DropStaleScope)
                    {
                        result.WasDroppedByStaleScope = true;
                        return result;
                    }
                    else
                    {
                        shouldDeferCurrentMovie = true;
                    }
                }

                if (shouldDeferCurrentMovie)
                {
                    if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                    {
                        result.WasDroppedByStaleScope = true;
                        return result;
                    }

                    result.AddDeferredMoviePath(
                        pending.MovieFullPath,
                        context.MarkWatchWorkDeferredWhileSuppressedAction,
                        "pending_movie_flush"
                    );
                }
            }

            context.PendingNewMovies.Clear();
            return result;
        }

        // 1件の走査結果について、DB存在確認からUI整合・サムネ投入までを調停する。
        internal async Task<WatchScannedMovieProcessResult> ProcessScannedMovieAsync(
            WatchScannedMovieContext context,
            string movieFullPath,
            string fileBody
        )
        {
            WatchScannedMovieProcessResult result = new();
            if (context == null || string.IsNullOrWhiteSpace(movieFullPath))
            {
                result.Outcome = "skip_invalid_path";
                return result;
            }

            if (string.IsNullOrWhiteSpace(fileBody))
            {
                result.Outcome = "skip_empty_body";
                return result;
            }

            Stopwatch stepStopwatch = Stopwatch.StartNew();
            bool existsInDb = context.ExistingMovieByPath.TryGetValue(
                movieFullPath,
                out WatchMainDbMovieSnapshot currentMovie
            );
            stepStopwatch.Stop();
            result.DbLookupElapsedMs = stepStopwatch.ElapsedMilliseconds;

            if (!existsInDb)
            {
                // 動画解析は重いためUIスレッドから外し、固まりを避ける。
                MovieInfo mvi;
                try
                {
                    stepStopwatch.Restart();
                    mvi = await Task.Run(() => new MovieInfo(movieFullPath));
                    stepStopwatch.Stop();
                    result.MovieInfoElapsedMs = stepStopwatch.ElapsedMilliseconds;
                }
                catch (Exception ex)
                {
                    stepStopwatch.Stop();
                    result.MovieInfoElapsedMs = stepStopwatch.ElapsedMilliseconds;
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan movie skipped: folder='{context.PendingMovieFlushContext?.CheckFolder ?? ""}' path='{movieFullPath}' reason='{ex.GetType().Name}: {ex.Message}'"
                    );
                    result.Outcome = "skip_movieinfo_exception";
                    return result;
                }

                if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                {
                    result.WasDroppedByStaleScope = true;
                    result.Outcome = "drop_stale_scope";
                    return result;
                }

                WatchCoordinatorGuardAction guardAction = GetWatchCoordinatorGuardAction(
                    context.IsCurrentWatchScanScope,
                    context.ShouldSuppressWatchWork
                );
                if (guardAction == WatchCoordinatorGuardAction.DropStaleScope)
                {
                    result.WasDroppedByStaleScope = true;
                    result.Outcome = "drop_stale_scope";
                    return result;
                }

                bool shouldSuppressWatchWork =
                    guardAction == WatchCoordinatorGuardAction.DeferByUiSuppression;

                // DB登録はループ内で直列実行せず、一定件数ごとにまとめて流す。
                context.PendingMovieFlushContext.PendingNewMovies.Add(
                    new PendingMovieRegistration(movieFullPath, fileBody, mvi)
                );
                if (!shouldSuppressWatchWork)
                {
                    AddOrUpdatePendingMoviePlaceholder(
                        movieFullPath,
                        fileBody,
                        context.SnapshotTabIndex,
                        PendingMoviePlaceholderStatus.Detected
                    );
                }
                result.HasFolderUpdate = true;

                if (
                    !shouldSuppressWatchWork
                    && (
                        context.UseIncrementalUiMode
                    || context.PendingMovieFlushContext.PendingNewMovies.Count
                        >= FolderScanEnqueueBatchSize
                    )
                )
                {
                    Stopwatch flushWaitStopwatch = Stopwatch.StartNew();
                    WatchPendingNewMovieFlushResult flushResult =
                        await FlushPendingNewMoviesAsync(context.PendingMovieFlushContext);
                    flushWaitStopwatch.Stop();
                    result.ApplyPendingFlush(flushResult);
                    result.FlushWaitElapsedMs = flushWaitStopwatch.ElapsedMilliseconds;
                    if (result.WasDroppedByStaleScope)
                    {
                        result.Outcome = "drop_stale_scope";
                        return result;
                    }
                }

                result.Outcome = shouldSuppressWatchWork
                    ? "pending_insert_suppressed"
                    : "pending_insert";
                return result;
            }

            // 既存DB登録済みは、辞書キャッシュからID/Hashを引いてキュー判定へ進む。
            long currentMovieId = currentMovie.MovieId;
            string currentHash = currentMovie.Hash;
            bool shouldDeferCurrentMovieBySuppression = false;
            bool shouldProbeExistingMovieObservedState = ShouldProbeExistingMovieObservedState(
                context.AllowExistingMovieDirtyTracking,
                context.UseIncrementalUiMode
            );
            (
                WatchMovieDirtyFields existingMovieDirtyFields,
                WatchMovieObservedState? existingMovieObservedState
            ) = await TryBuildExistingMovieObservedStateAsync(
                movieFullPath,
                currentMovie,
                shouldProbeExistingMovieObservedState,
                context.ProbeExistingMovieObservedStateAsync ?? ProbeExistingMovieObservedStateAsync
            );
            if (existingMovieDirtyFields != WatchMovieDirtyFields.None)
            {
                result.HasFolderUpdate = true;
                result.AddChangedMovie(
                    movieFullPath,
                    WatchMovieChangeKind.None,
                    existingMovieDirtyFields,
                    existingMovieObservedState
                );
                if (existingMovieObservedState.HasValue)
                {
                    context.ExistingMovieByPath[movieFullPath] = currentMovie with
                    {
                        FileDateText = existingMovieObservedState.Value.FileDateText,
                        MovieSizeKb = existingMovieObservedState.Value.MovieSizeKb,
                        MovieLengthSeconds =
                            existingMovieObservedState.Value.MovieLengthSeconds
                            ?? currentMovie.MovieLengthSeconds,
                    };
                }

                DebugRuntimeLog.Write(
                    "watch-check",
                    $"refresh existing-db-metadata: tab={context.SnapshotTabIndex}, movie='{movieFullPath}', dirty={existingMovieDirtyFields}"
                );
            }

            if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
            {
                result.WasDroppedByStaleScope = true;
                result.Outcome = "drop_stale_scope";
                return result;
            }

            MovieViewConsistencyDecision viewConsistency = EvaluateMovieViewConsistency(
                context.AllowViewConsistencyRepair,
                true,
                context.ExistingViewMoviePaths,
                context.SearchKeyword,
                context.DisplayedMoviePaths,
                movieFullPath
            );
            bool shouldRepairView = viewConsistency.ShouldRepairView;
            bool shouldRefreshDisplayedView = viewConsistency.ShouldRefreshDisplayedView;
            if (shouldRepairView)
            {
                result.HasFolderUpdate = true;
                result.AddChangedMovie(
                    movieFullPath,
                    WatchMovieChangeKind.ViewRepaired,
                    WatchMovieDirtyFields.None
                );
                context.ExistingViewMoviePaths.Add(movieFullPath);
                context.DisplayedMoviePaths.Add(movieFullPath);

                if (
                    context.UseIncrementalUiMode
                )
                {
                    bool shouldSuppressBeforeAppend =
                        context.ShouldSuppressWatchWork?.Invoke() == true;
                    if (shouldSuppressBeforeAppend)
                    {
                        shouldDeferCurrentMovieBySuppression = true;
                    }
                    else
                    {
                        stepStopwatch.Restart();
                        WatchCoordinatorGuardAction appendGuardAction =
                            await TryAppendMovieToViewIfWatchAllowedAsync(
                            context.IsCurrentWatchScanScope,
                            context.ShouldSuppressWatchWork,
                            context.AppendMovieToViewAsync,
                            context.SnapshotDbFullPath,
                            movieFullPath
                        );
                        stepStopwatch.Stop();
                        if (appendGuardAction == WatchCoordinatorGuardAction.Continue)
                        {
                            result.UiReflectElapsedMs += stepStopwatch.ElapsedMilliseconds;
                        }
                        else if (appendGuardAction == WatchCoordinatorGuardAction.DropStaleScope)
                        {
                            result.WasDroppedByStaleScope = true;
                            result.Outcome = "drop_stale_scope";
                            return result;
                        }
                        else
                        {
                            shouldDeferCurrentMovieBySuppression = true;
                        }
                    }
                }

                DebugRuntimeLog.Write(
                    "watch-check",
                    $"repair view by existing-db-movie: tab={context.SnapshotTabIndex}, movie='{movieFullPath}'"
                );
            }
            else if (shouldRefreshDisplayedView)
            {
                result.HasFolderUpdate = true;
                result.AddChangedMovie(
                    movieFullPath,
                    WatchMovieChangeKind.DisplayedViewRefresh,
                    WatchMovieDirtyFields.None
                );
                context.DisplayedMoviePaths.Add(movieFullPath);
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"refresh filtered-view by existing-db-movie: tab={context.SnapshotTabIndex}, movie='{movieFullPath}'"
                );
            }

            if (!context.AllowMissingTabAutoEnqueue || !context.AutoEnqueueTabIndex.HasValue)
            {
                if (shouldDeferCurrentMovieBySuppression)
                {
                    if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                    {
                        result.WasDroppedByStaleScope = true;
                        result.Outcome = "drop_stale_scope";
                        return result;
                    }

                    result.AddDeferredMoviePath(
                        movieFullPath,
                        context.PendingMovieFlushContext?.MarkWatchWorkDeferredWhileSuppressedAction,
                        "existing_movie"
                    );
                }

                result.Outcome = "skip_non_upper_tab";
                return result;
            }

            if (context.ShouldSuppressWatchWork?.Invoke() == true)
            {
                if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                {
                    result.WasDroppedByStaleScope = true;
                    result.Outcome = "drop_stale_scope";
                    return result;
                }

                result.AddDeferredMoviePath(
                    movieFullPath,
                    context.PendingMovieFlushContext?.MarkWatchWorkDeferredWhileSuppressedAction,
                    "existing_movie"
                );
                result.Outcome = "skip_enqueue_by_ui_suppression";
                return result;
            }

            // 結合したサムネイルのファイル名作成（存在チェック用）
            string saveThumbFileName = ThumbnailPathResolver.BuildThumbnailPath(
                context.ThumbnailOutPath,
                movieFullPath,
                currentHash
            );

            // 既にサムネ画像が存在しているなら作成処理はスキップ
            Stopwatch thumbExistsStopwatch = Stopwatch.StartNew();
            string saveThumbFileNameOnly = Path.GetFileName(saveThumbFileName) ?? "";
            bool thumbExists =
                !string.IsNullOrWhiteSpace(saveThumbFileNameOnly)
                && context.ExistingThumbnailFileNames.Contains(saveThumbFileNameOnly);
            thumbExistsStopwatch.Stop();
            result.ThumbExistsElapsedMs = thumbExistsStopwatch.ElapsedMilliseconds;
            if (thumbExists)
            {
                if (shouldDeferCurrentMovieBySuppression)
                {
                    if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                    {
                        result.WasDroppedByStaleScope = true;
                        result.Outcome = "drop_stale_scope";
                        return result;
                    }

                    result.AddDeferredMoviePath(
                        movieFullPath,
                        context.PendingMovieFlushContext?.MarkWatchWorkDeferredWhileSuppressedAction,
                        "existing_movie"
                    );
                }

                result.Outcome = "skip_existing_thumb";
                return result;
            }

            MissingThumbnailAutoEnqueueBlockReason blockReason =
                ResolveMissingThumbnailAutoEnqueueBlockReason(
                    movieFullPath,
                    context.AutoEnqueueTabIndex.Value,
                    context.ExistingThumbnailFileNames,
                    context.OpenRescueRequestKeys
                );
            if (blockReason != MissingThumbnailAutoEnqueueBlockReason.None)
            {
                if (shouldDeferCurrentMovieBySuppression)
                {
                    if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                    {
                        result.WasDroppedByStaleScope = true;
                        result.Outcome = "drop_stale_scope";
                        return result;
                    }

                    result.AddDeferredMoviePath(
                        movieFullPath,
                        context.PendingMovieFlushContext?.MarkWatchWorkDeferredWhileSuppressedAction,
                        "existing_movie"
                    );
                }

                result.Outcome =
                    $"skip_failure_state:{DescribeMissingThumbnailAutoEnqueueBlockReason(blockReason)}";
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"skip enqueue by failure-state: tab={context.AutoEnqueueTabIndex.Value}, movie='{movieFullPath}', reason={DescribeMissingThumbnailAutoEnqueueBlockReason(blockReason)}"
                );
                return result;
            }

            DebugRuntimeLog.Write(
                "watch-check",
                $"enqueue by missing-tab-thumb: tab={context.AutoEnqueueTabIndex.Value}, movie='{movieFullPath}'"
            );

            // サムネイル作成キュー用のオブジェクトを用意してバッファのリストへ積む。
            if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
            {
                result.WasDroppedByStaleScope = true;
                result.Outcome = "drop_stale_scope";
                return result;
            }

            QueueObj temp = new()
            {
                MovieId = currentMovieId,
                MovieFullPath = movieFullPath,
                Hash = currentHash,
                Tabindex = context.AutoEnqueueTabIndex.Value,
                Priority = ThumbnailQueuePriority.Normal,
            };
            context.PendingMovieFlushContext.AddFilesByFolder.Add(temp);
            result.AddedByFolderCount++;
            result.EnqueuedCount++;

            // 小規模時は1件ずつ即投入する。大規模時は100件単位で先行投入する。
            if (context.UseIncrementalUiMode)
            {
                stepStopwatch.Restart();
                WatchCoordinatorGuardAction flushGuardAction =
                    TryFlushPendingQueueItemsIfWatchAllowed(
                    context.IsCurrentWatchScanScope,
                    context.ShouldSuppressWatchWork,
                    context.PendingMovieFlushContext.FlushPendingQueueItemsAction,
                    context.PendingMovieFlushContext.AddFilesByFolder,
                    context.PendingMovieFlushContext.CheckFolder
                );
                stepStopwatch.Stop();
                if (flushGuardAction == WatchCoordinatorGuardAction.Continue)
                {
                    result.EnqueueFlushElapsedMs += stepStopwatch.ElapsedMilliseconds;
                    result.FlushWaitElapsedMs = stepStopwatch.ElapsedMilliseconds;
                    if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                    {
                        result.WasDroppedByStaleScope = true;
                        result.Outcome = "drop_stale_scope";
                        return result;
                    }

                    context.PendingMovieFlushContext.RefreshWatchVisibleMovieGate?.Invoke(
                        "incremental_flush"
                    );
                }
                else if (flushGuardAction == WatchCoordinatorGuardAction.DropStaleScope)
                {
                    result.WasDroppedByStaleScope = true;
                    result.Outcome = "drop_stale_scope";
                    return result;
                }
                else
                {
                    shouldDeferCurrentMovieBySuppression = true;
                }
            }
            else if (
                context.PendingMovieFlushContext.AddFilesByFolder.Count
                >= FolderScanEnqueueBatchSize
            )
            {
                stepStopwatch.Restart();
                WatchCoordinatorGuardAction flushGuardAction =
                    TryFlushPendingQueueItemsIfWatchAllowed(
                    context.IsCurrentWatchScanScope,
                    context.ShouldSuppressWatchWork,
                    context.PendingMovieFlushContext.FlushPendingQueueItemsAction,
                    context.PendingMovieFlushContext.AddFilesByFolder,
                    context.PendingMovieFlushContext.CheckFolder
                );
                stepStopwatch.Stop();
                if (flushGuardAction == WatchCoordinatorGuardAction.Continue)
                {
                    result.EnqueueFlushElapsedMs += stepStopwatch.ElapsedMilliseconds;
                    result.FlushWaitElapsedMs = stepStopwatch.ElapsedMilliseconds;
                    if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                    {
                        result.WasDroppedByStaleScope = true;
                        result.Outcome = "drop_stale_scope";
                        return result;
                    }

                    context.PendingMovieFlushContext.RefreshWatchVisibleMovieGate?.Invoke("batch_flush");
                }
                else if (flushGuardAction == WatchCoordinatorGuardAction.DropStaleScope)
                {
                    result.WasDroppedByStaleScope = true;
                    result.Outcome = "drop_stale_scope";
                    return result;
                }
                else
                {
                    shouldDeferCurrentMovieBySuppression = true;
                }
            }

            if (shouldDeferCurrentMovieBySuppression)
            {
                if (!IsCurrentWatchCoordinatorScope(context.IsCurrentWatchScanScope))
                {
                    result.WasDroppedByStaleScope = true;
                    result.Outcome = "drop_stale_scope";
                    return result;
                }

                result.AddDeferredMoviePath(
                    movieFullPath,
                    context.PendingMovieFlushContext?.MarkWatchWorkDeferredWhileSuppressedAction,
                    "existing_movie"
                );
            }

            result.Outcome = "enqueue_missing_thumb";
            return result;
        }

        // folder単位の事前判定をまとめ、CheckFolderAsync 側から細かい分岐を追い出す。
        private async Task<WatchFolderScanMovieResult> ProcessWatchFolderScanMovieAsync(
            WatchFolderScanContext context,
            string movieFullPath
        )
        {
            WatchFolderScanMovieResult result = new();
            Stopwatch totalStopwatch = Stopwatch.StartNew();

            try
            {
                if (context == null || string.IsNullOrWhiteSpace(movieFullPath))
                {
                    result.Outcome = "skip_invalid_path";
                    return result;
                }

                bool skipByVisibleOnlyGate = ShouldSkipWatchWorkByVisibleMovieGate(
                    context.RestrictWatchWorkToVisibleMovies,
                    context.VisibleMoviePaths,
                    movieFullPath
                );
                long zeroFileLength = 0;
                bool isZeroByteMovie =
                    !skipByVisibleOnlyGate && IsZeroByteMovieFile(movieFullPath, out zeroFileLength);
                string fileBody = Path.GetFileNameWithoutExtension(movieFullPath);

                WatchFolderMoviePreCheckDecision preCheckDecision =
                    EvaluateWatchFolderMoviePreCheck(
                        context.HasNotifiedFolderHit,
                        skipByVisibleOnlyGate,
                        isZeroByteMovie,
                        fileBody
                    );
                if (
                    TryHandleWatchFolderMoviePreCheck(
                        context,
                        movieFullPath,
                        zeroFileLength,
                        preCheckDecision,
                        result
                    )
                )
                {
                    return result;
                }

                WatchScannedMovieProcessResult processResult = await ProcessScannedMovieAsync(
                    context.ScannedMovieContext,
                    movieFullPath,
                    fileBody
                );
                result.ApplyProcessResult(processResult);
                return result;
            }
            finally
            {
                totalStopwatch.Stop();
                result.TotalElapsedMs = totalStopwatch.ElapsedMilliseconds;
            }
        }

        // first-hit と zero-byte の副作用をまとめ、1件処理の本体から分岐を追い出す。
        internal bool TryHandleWatchFolderMoviePreCheck(
            WatchFolderScanContext context,
            string movieFullPath,
            long zeroFileLength,
            WatchFolderMoviePreCheckDecision preCheckDecision,
            WatchFolderScanMovieResult result
        )
        {
            if (context == null || result == null)
            {
                return true;
            }

            if (preCheckDecision.ShouldNotifyFolderHit)
            {
                context.NotifyFolderFirstHit?.Invoke();
                context.HasNotifiedFolderHit = true;
            }

            if (preCheckDecision.ShouldContinueProcessing)
            {
                return false;
            }

            if (
                preCheckDecision.IsZeroByteMovie
                && context.AllowMissingTabAutoEnqueue
                && context.AutoEnqueueTabIndex.HasValue
            )
            {
                Action<string, int, string> createErrorMarkerAction =
                    context.CreateErrorMarkerForSkippedMovieAction ?? TryCreateErrorMarkerForSkippedMovie;
                createErrorMarkerAction(
                    movieFullPath,
                    context.AutoEnqueueTabIndex.Value,
                    "zero-byte movie(folder scan)"
                );
            }

            if (preCheckDecision.IsZeroByteMovie)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"skip zero-byte movie before queue: '{movieFullPath}' size={zeroFileLength}"
                );
            }

            result.Outcome = preCheckDecision.Outcome;
            return true;
        }

        // folder終端の端数キュー flush も coordinator 側へ寄せ、CheckFolderAsync は集計だけ持つ。
        internal WatchFinalQueueFlushResult FlushFinalWatchFolderQueue(WatchFolderScanContext context)
        {
            if (context?.ScannedMovieContext?.PendingMovieFlushContext?.AddFilesByFolder == null)
            {
                return WatchFinalQueueFlushResult.None;
            }

            if (IsWatchFolderScopeStale(context))
            {
                return new WatchFinalQueueFlushResult(0, false, true, false);
            }

            Stopwatch flushStopwatch = Stopwatch.StartNew();
            WatchCoordinatorGuardAction flushGuardAction = TryFlushPendingQueueItemsIfWatchAllowed(
                context.ScannedMovieContext.PendingMovieFlushContext.IsCurrentWatchScanScope,
                context.ScannedMovieContext.ShouldSuppressWatchWork,
                context.ScannedMovieContext.PendingMovieFlushContext.FlushPendingQueueItemsAction,
                context.ScannedMovieContext.PendingMovieFlushContext.AddFilesByFolder,
                context.ScannedMovieContext.PendingMovieFlushContext.CheckFolder
            );
            flushStopwatch.Stop();

            if (flushGuardAction == WatchCoordinatorGuardAction.Continue)
            {
                if (IsWatchFolderScopeStale(context))
                {
                    return new WatchFinalQueueFlushResult(
                        flushStopwatch.ElapsedMilliseconds,
                        false,
                        true,
                        false
                    );
                }

                context.ScannedMovieContext.PendingMovieFlushContext.RefreshWatchVisibleMovieGate?.Invoke(
                    "folder_final_flush"
                );
            }

            bool wasStoppedByUiSuppression = false;
            if (flushGuardAction == WatchCoordinatorGuardAction.DeferByUiSuppression)
            {
                wasStoppedByUiSuppression =
                    context.TryDeferWatchFolderWorkByUiSuppressionAction?.Invoke(
                        $"folder-final-queue:{context.ScannedMovieContext.PendingMovieFlushContext.CheckFolder}"
                    ) == true;
            }

            return new WatchFinalQueueFlushResult(
                flushStopwatch.ElapsedMilliseconds,
                flushGuardAction == WatchCoordinatorGuardAction.DeferByUiSuppression,
                flushGuardAction == WatchCoordinatorGuardAction.DropStaleScope,
                wasStoppedByUiSuppression
            );
        }

        // folder文脈から stale scope 判定の読み口を一本化し、Watcher 側へ生の closure を漏らさない。
        internal static bool IsWatchFolderScopeStale(WatchFolderScanContext context)
        {
            Func<bool> isCurrentWatchScanScope =
                context?.ScannedMovieContext?.IsCurrentWatchScanScope
                ?? context?.ScannedMovieContext?.PendingMovieFlushContext?.IsCurrentWatchScanScope;
            return !IsCurrentWatchCoordinatorScope(isCurrentWatchScanScope);
        }

        // folder走査に入る直前の suppression 再退避も coordinator 側へ寄せる。
        internal bool TryDeferWatchFolderPreprocess(
            WatchFolderScanContext context,
            IEnumerable<string> remainingScanPaths
        )
        {
            if (context == null)
            {
                return false;
            }

            string checkFolder =
                context.ScannedMovieContext?.PendingMovieFlushContext?.CheckFolder ?? "";
            return context.TryDeferWatchFolderPreprocessByUiSuppressionAction?.Invoke(
                    remainingScanPaths ?? [],
                    $"folder-preprocess:{checkFolder}"
                ) == true;
        }

        // folder走査中盤の suppression 再退避も coordinator 側へ寄せる。
        internal bool TryDeferWatchFolderMid(
            WatchFolderScanContext context,
            IEnumerable<string> remainingScanPaths
        )
        {
            if (context == null)
            {
                return false;
            }

            string checkFolder =
                context.ScannedMovieContext?.PendingMovieFlushContext?.CheckFolder ?? "";
            return context.TryDeferWatchFolderMidByUiSuppressionAction?.Invoke(
                    remainingScanPaths ?? [],
                    $"folder-mid:{checkFolder}"
                ) == true;
        }

        // visible-only gate と zero-byte / empty-body の順序を固定し、folder first-hit の条件もここで揃える。
        internal static WatchFolderMoviePreCheckDecision EvaluateWatchFolderMoviePreCheck(
            bool hasNotifiedFolderHit,
            bool skipByVisibleOnlyGate,
            bool isZeroByteMovie,
            string fileBody
        )
        {
            if (skipByVisibleOnlyGate)
            {
                return new WatchFolderMoviePreCheckDecision(
                    "skip_visible_only_gate",
                    false,
                    false,
                    false
                );
            }

            bool shouldNotifyFolderHit = !hasNotifiedFolderHit;
            if (isZeroByteMovie)
            {
                return new WatchFolderMoviePreCheckDecision(
                    "skip_zero_byte",
                    shouldNotifyFolderHit,
                    false,
                    true
                );
            }

            if (string.IsNullOrWhiteSpace(fileBody))
            {
                return new WatchFolderMoviePreCheckDecision(
                    "skip_empty_body",
                    shouldNotifyFolderHit,
                    false,
                    false
                );
            }

            return new WatchFolderMoviePreCheckDecision("continue", shouldNotifyFolderHit, true, false);
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

        // visible-only gate の更新判断とログ出しを、走査本体から切り離してまとめる。
        internal static (bool RestrictWatchWorkToVisibleMovies, int CurrentWatchQueueActiveCount)
            RefreshWatchVisibleMovieGate(
                bool isWatchMode,
                ISet<string> visibleMoviePaths,
                int threshold,
                int currentTabIndex,
                Func<int?> getCurrentQueueActiveCount,
                bool currentRestrictWatchWorkToVisibleMovies,
                int currentWatchQueueActiveCount,
                string reason
            )
        {
            if (!isWatchMode || visibleMoviePaths == null || visibleMoviePaths.Count < 1)
            {
                return (currentRestrictWatchWorkToVisibleMovies, currentWatchQueueActiveCount);
            }

            int? refreshedActiveCount = getCurrentQueueActiveCount?.Invoke();
            if (!refreshedActiveCount.HasValue)
            {
                return (currentRestrictWatchWorkToVisibleMovies, currentWatchQueueActiveCount);
            }

            bool nextRestrict = ShouldRestrictWatchWorkToVisibleMovies(
                isWatchMode,
                refreshedActiveCount.Value,
                threshold,
                currentTabIndex,
                visibleMoviePaths.Count
            );
            if (nextRestrict == currentRestrictWatchWorkToVisibleMovies)
            {
                return (currentRestrictWatchWorkToVisibleMovies, refreshedActiveCount.Value);
            }

            DebugRuntimeLog.Write(
                "watch-check",
                nextRestrict
                    ? $"watch visible-only gate enabled: active={refreshedActiveCount.Value} threshold={threshold} tab={currentTabIndex} visible={visibleMoviePaths.Count} reason={reason}"
                    : $"watch visible-only gate disabled: active={refreshedActiveCount.Value} threshold={threshold} tab={currentTabIndex} reason={reason}"
            );
            return (nextRestrict, refreshedActiveCount.Value);
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

        // flush に必要な依存だけを束ね、CheckFolderAsync 側の引数地獄を避ける。
        internal sealed class WatchPendingNewMovieFlushContext
        {
            public string SnapshotDbFullPath { get; set; } = "";
            public Dictionary<string, WatchMainDbMovieSnapshot> ExistingMovieByPath { get; set; }
            public List<PendingMovieRegistration> PendingNewMovies { get; set; }
            public bool UseIncrementalUiMode { get; set; }
            public bool AllowMissingTabAutoEnqueue { get; set; }
            public int? AutoEnqueueTabIndex { get; set; }
            public string ThumbnailOutPath { get; set; } = "";
            public HashSet<string> ExistingThumbnailFileNames { get; set; }
            public HashSet<string> OpenRescueRequestKeys { get; set; }
            public List<QueueObj> AddFilesByFolder { get; set; }
            public string CheckFolder { get; set; } = "";
            public Action<string> RefreshWatchVisibleMovieGate { get; set; }
            public Func<bool> ShouldSuppressWatchWork { get; set; }
            public Func<bool> IsCurrentWatchScanScope { get; set; }
            public Action<string> MarkWatchWorkDeferredWhileSuppressedAction { get; set; }
            public Func<string, List<MovieCore>, Task<int>> InsertMoviesBatchAsync { get; set; }
            public Func<string, string, Task> AppendMovieToViewAsync { get; set; }
            public Action<string> RemovePendingMoviePlaceholderAction { get; set; }
            public Action<List<QueueObj>, string> FlushPendingQueueItemsAction { get; set; }
        }

        [Flags]
        internal enum WatchMovieChangeKind
        {
            None = 0,
            SourceInserted = 1,
            ViewRepaired = 2,
            DisplayedViewRefresh = 4,
        }

        [Flags]
        internal enum WatchMovieDirtyFields
        {
            None = 0,
            LastDate = 1 << 0,
            FileDate = 1 << 1,
            Score = 1 << 2,
            ViewCount = 1 << 3,
            Kana = 1 << 4,
            MovieName = 1 << 5,
            MoviePath = 1 << 6,
            MovieSize = 1 << 7,
            RegistDate = 1 << 8,
            MovieLength = 1 << 9,
            Comment1 = 1 << 10,
            Comment2 = 1 << 11,
            Comment3 = 1 << 12,
            Tags = 1 << 13,
            Hash = 1 << 14,
            ThumbnailError = 1 << 15,
        }

        // watch で拾った cheap な観測値を保持し、DB再読込なしでも局所更新へ流す。
        internal readonly record struct WatchMovieObservedState(
            string FileDateText,
            long MovieSizeKb,
            long? MovieLengthSeconds = null
        );

        // watch で拾った changed path と変更種別を、後段の UI 判断へそのまま流す。
        internal readonly record struct WatchChangedMovie(
            string MoviePath,
            WatchMovieChangeKind ChangeKind,
            WatchMovieDirtyFields DirtyFields,
            WatchMovieObservedState? ObservedState = null
        );

        // 1回の flush で増えた件数と所要時間だけを返し、集計は呼び出し側で続ける。
        internal sealed class WatchPendingNewMovieFlushResult
        {
            public static WatchPendingNewMovieFlushResult None { get; } = new();

            public int AddedByFolderCount { get; set; }
            public int EnqueuedCount { get; set; }
            public long DbInsertElapsedMs { get; set; }
            public long UiReflectElapsedMs { get; set; }
            public long EnqueueFlushElapsedMs { get; set; }
            public bool WasDroppedByStaleScope { get; set; }
            public List<string> DeferredMoviePathsByUiSuppression { get; } = [];
            public List<WatchChangedMovie> ChangedMovies { get; } = [];

            public void AddDeferredMoviePath(
                string movieFullPath,
                Action<string> markDeferredAction,
                string trigger
            )
            {
                if (string.IsNullOrWhiteSpace(movieFullPath))
                {
                    return;
                }

                if (!DeferredMoviePathsByUiSuppression.Contains(movieFullPath, StringComparer.OrdinalIgnoreCase))
                {
                    DeferredMoviePathsByUiSuppression.Add(movieFullPath);
                }

                markDeferredAction?.Invoke(trigger);
            }

            public void AddChangedMovie(
                string movieFullPath,
                WatchMovieChangeKind changeKind,
                WatchMovieDirtyFields dirtyFields,
                WatchMovieObservedState? observedState = null
            )
            {
                if (
                    string.IsNullOrWhiteSpace(movieFullPath)
                    || (
                        changeKind == WatchMovieChangeKind.None
                        && dirtyFields == WatchMovieDirtyFields.None
                    )
                )
                {
                    return;
                }

                for (int index = 0; index < ChangedMovies.Count; index++)
                {
                    WatchChangedMovie current = ChangedMovies[index];
                    if (string.Equals(current.MoviePath, movieFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        ChangedMovies[index] = current with
                        {
                            ChangeKind = current.ChangeKind | changeKind,
                            DirtyFields = current.DirtyFields | dirtyFields,
                            ObservedState = MergeWatchMovieObservedState(
                                current.ObservedState,
                                observedState
                            ),
                        };
                        return;
                    }
                }

                ChangedMovies.Add(
                    new WatchChangedMovie(movieFullPath, changeKind, dirtyFields, observedState)
                );
            }
        }

        // 1件処理に必要な folder 単位の依存を束ね、分岐処理を外へ逃がす。
        internal sealed class WatchScannedMovieContext
        {
            public string SnapshotDbFullPath { get; set; } = "";
            public int SnapshotTabIndex { get; set; }
            public Dictionary<string, WatchMainDbMovieSnapshot> ExistingMovieByPath { get; set; }
            public HashSet<string> ExistingViewMoviePaths { get; set; }
            public HashSet<string> DisplayedMoviePaths { get; set; }
            public string SearchKeyword { get; set; } = "";
            public bool AllowViewConsistencyRepair { get; set; }
            public bool UseIncrementalUiMode { get; set; }
            public bool AllowExistingMovieDirtyTracking { get; set; }
            public bool AllowMissingTabAutoEnqueue { get; set; }
            public int? AutoEnqueueTabIndex { get; set; }
            public string ThumbnailOutPath { get; set; } = "";
            public HashSet<string> ExistingThumbnailFileNames { get; set; }
            public HashSet<string> OpenRescueRequestKeys { get; set; }
            public WatchPendingNewMovieFlushContext PendingMovieFlushContext { get; set; }
            public Func<bool> ShouldSuppressWatchWork { get; set; }
            public Func<bool> IsCurrentWatchScanScope { get; set; }
            public Func<string, string, Task> AppendMovieToViewAsync { get; set; }
            public Func<string, Task<WatchMovieObservedState?>> ProbeExistingMovieObservedStateAsync { get; set; }
        }

        // folder単位の前処理と終端処理に必要な依存だけを束ねる。
        internal sealed class WatchFolderScanContext
        {
            public bool RestrictWatchWorkToVisibleMovies { get; set; }
            public ISet<string> VisibleMoviePaths { get; set; }
            public bool HasNotifiedFolderHit { get; set; }
            public Action NotifyFolderFirstHit { get; set; }
            public Action<string, int, string> CreateErrorMarkerForSkippedMovieAction { get; set; }
            public bool AllowMissingTabAutoEnqueue { get; set; }
            public int? AutoEnqueueTabIndex { get; set; }
            public WatchScannedMovieContext ScannedMovieContext { get; set; }
            public Func<IEnumerable<string>, string, bool> TryDeferWatchFolderPreprocessByUiSuppressionAction { get; set; }
            public Func<IEnumerable<string>, string, bool> TryDeferWatchFolderMidByUiSuppressionAction { get; set; }
            public Func<string, bool> TryDeferWatchFolderWorkByUiSuppressionAction { get; set; }
        }

        // per-folder の事前判定結果を純粋値として返し、順序の回帰をテストで固定する。
        internal readonly record struct WatchFolderMoviePreCheckDecision(
            string Outcome,
            bool ShouldNotifyFolderHit,
            bool ShouldContinueProcessing,
            bool IsZeroByteMovie
        );

        // probe 用の outcome と、1件処理で増えた計測値だけを返す。
        internal class WatchScannedMovieProcessResult
        {
            public string Outcome { get; set; } = "";
            public bool HasFolderUpdate { get; set; }
            public int AddedByFolderCount { get; set; }
            public int EnqueuedCount { get; set; }
            public long DbLookupElapsedMs { get; set; }
            public long ThumbExistsElapsedMs { get; set; }
            public long MovieInfoElapsedMs { get; set; }
            public long FlushWaitElapsedMs { get; set; }
            public long DbInsertElapsedMs { get; set; }
            public long UiReflectElapsedMs { get; set; }
            public long EnqueueFlushElapsedMs { get; set; }
            public bool WasDroppedByStaleScope { get; set; }
            public List<string> DeferredMoviePathsByUiSuppression { get; } = [];
            public List<WatchChangedMovie> ChangedMovies { get; } = [];

            public void AddDeferredMoviePath(
                string movieFullPath,
                Action<string> markDeferredAction,
                string trigger
            )
            {
                if (string.IsNullOrWhiteSpace(movieFullPath))
                {
                    return;
                }

                if (!DeferredMoviePathsByUiSuppression.Contains(movieFullPath, StringComparer.OrdinalIgnoreCase))
                {
                    DeferredMoviePathsByUiSuppression.Add(movieFullPath);
                }

                markDeferredAction?.Invoke(trigger);
            }

            public void AddChangedMovie(
                string movieFullPath,
                WatchMovieChangeKind changeKind,
                WatchMovieDirtyFields dirtyFields,
                WatchMovieObservedState? observedState = null
            )
            {
                if (
                    string.IsNullOrWhiteSpace(movieFullPath)
                    || (
                        changeKind == WatchMovieChangeKind.None
                        && dirtyFields == WatchMovieDirtyFields.None
                    )
                )
                {
                    return;
                }

                for (int index = 0; index < ChangedMovies.Count; index++)
                {
                    WatchChangedMovie current = ChangedMovies[index];
                    if (string.Equals(current.MoviePath, movieFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        ChangedMovies[index] = current with
                        {
                            ChangeKind = current.ChangeKind | changeKind,
                            DirtyFields = current.DirtyFields | dirtyFields,
                            ObservedState = MergeWatchMovieObservedState(
                                current.ObservedState,
                                observedState
                            ),
                        };
                        return;
                    }
                }

                ChangedMovies.Add(
                    new WatchChangedMovie(movieFullPath, changeKind, dirtyFields, observedState)
                );
            }

            public void ApplyPendingFlush(WatchPendingNewMovieFlushResult flushResult)
            {
                if (flushResult == null)
                {
                    return;
                }

                DbInsertElapsedMs += flushResult.DbInsertElapsedMs;
                UiReflectElapsedMs += flushResult.UiReflectElapsedMs;
                EnqueueFlushElapsedMs += flushResult.EnqueueFlushElapsedMs;
                AddedByFolderCount += flushResult.AddedByFolderCount;
                EnqueuedCount += flushResult.EnqueuedCount;
                WasDroppedByStaleScope |= flushResult.WasDroppedByStaleScope;
                foreach (string movieFullPath in flushResult.DeferredMoviePathsByUiSuppression)
                {
                    AddDeferredMoviePath(movieFullPath, null, "");
                }
                foreach (WatchChangedMovie changedMovie in flushResult.ChangedMovies)
                {
                    AddChangedMovie(
                        changedMovie.MoviePath,
                        changedMovie.ChangeKind,
                        changedMovie.DirtyFields,
                        changedMovie.ObservedState
                    );
                }
            }

            public void ApplyProcessResult(WatchScannedMovieProcessResult processResult)
            {
                if (processResult == null)
                {
                    return;
                }

                Outcome = processResult.Outcome;
                HasFolderUpdate |= processResult.HasFolderUpdate;
                AddedByFolderCount += processResult.AddedByFolderCount;
                EnqueuedCount += processResult.EnqueuedCount;
                DbLookupElapsedMs += processResult.DbLookupElapsedMs;
                ThumbExistsElapsedMs += processResult.ThumbExistsElapsedMs;
                MovieInfoElapsedMs += processResult.MovieInfoElapsedMs;
                FlushWaitElapsedMs += processResult.FlushWaitElapsedMs;
                DbInsertElapsedMs += processResult.DbInsertElapsedMs;
                UiReflectElapsedMs += processResult.UiReflectElapsedMs;
                EnqueueFlushElapsedMs += processResult.EnqueueFlushElapsedMs;
                WasDroppedByStaleScope |= processResult.WasDroppedByStaleScope;
                foreach (string movieFullPath in processResult.DeferredMoviePathsByUiSuppression)
                {
                    AddDeferredMoviePath(movieFullPath, null, "");
                }
                foreach (WatchChangedMovie changedMovie in processResult.ChangedMovies)
                {
                    AddChangedMovie(
                        changedMovie.MoviePath,
                        changedMovie.ChangeKind,
                        changedMovie.DirtyFields,
                        changedMovie.ObservedState
                    );
                }
            }
        }

        // per-file total_ms を含め、probe 出力に必要な値を1つへ揃える。
        internal sealed class WatchFolderScanMovieResult : WatchScannedMovieProcessResult
        {
            public long TotalElapsedMs { get; set; }
        }

        internal readonly record struct WatchFinalQueueFlushResult(
            long ElapsedMs,
            bool WasDeferredBySuppression,
            bool WasDroppedByStaleScope,
            bool WasStoppedByUiSuppression
        )
        {
            public static WatchFinalQueueFlushResult None => new(0, false, false, false);
        }
    }
}
