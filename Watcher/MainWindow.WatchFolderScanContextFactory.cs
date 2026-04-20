using System;
using System.Collections.Generic;
using IndigoMovieManager.Data;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // watch 1周の開始時点で必要な UI/DB スナップショットをまとめて取る。
        private (
            string SnapshotDbFullPath,
            string SnapshotThumbFolder,
            string SnapshotDbName,
            int SnapshotTabIndex,
            int? AutoEnqueueTabIndex,
            bool AllowMissingTabAutoEnqueue,
            long SnapshotWatchScanScopeStamp,
            bool CanUseQueryOnlyWatchReload
        ) BuildWatchRunSnapshot(CheckMode mode)
        {
            string snapshotDbFullPath = MainVM.DbInfo.DBFullPath;
            string snapshotThumbFolder = MainVM.DbInfo.ThumbFolder;
            string snapshotDbName = MainVM.DbInfo.DBName;
            int snapshotTabIndex = MainVM.DbInfo.CurrentTabIndex;
            int? autoEnqueueTabIndex = ResolveWatchMissingThumbnailTabIndex(snapshotTabIndex);
            bool allowMissingTabAutoEnqueue = autoEnqueueTabIndex.HasValue;
            long snapshotWatchScanScopeStamp = ReadCurrentWatchScanScopeStamp();
            bool canUseQueryOnlyWatchReload =
                mode == CheckMode.Watch && !IsStartupFeedPartialActive;
            return (
                snapshotDbFullPath,
                snapshotThumbFolder,
                snapshotDbName,
                snapshotTabIndex,
                autoEnqueueTabIndex,
                allowMissingTabAutoEnqueue,
                snapshotWatchScanScopeStamp,
                canUseQueryOnlyWatchReload
            );
        }

        // 表示中一覧まわりの状態を先に固め、watch 本流では参照だけにする。
        private async Task<(
            HashSet<string> ExistingViewMoviePaths,
            HashSet<string> DisplayedMoviePaths,
            string SearchKeyword,
            HashSet<string> VisibleMoviePaths,
            bool AllowViewConsistencyRepair
        )> BuildWatchViewSnapshotAsync()
        {
            HashSet<string> existingViewMoviePaths = await BuildCurrentViewMoviePathLookupAsync();
            (
                HashSet<string> displayedMoviePaths,
                string searchKeyword
            ) = await BuildCurrentDisplayedMovieStateAsync();
            HashSet<string> visibleMoviePaths = await BuildCurrentVisibleMoviePathLookupAsync();
            bool allowViewConsistencyRepair = !IsStartupFeedPartialActive;
            if (!allowViewConsistencyRepair)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    "view repair deferred: startup feed partial active."
                );
            }

            return (
                existingViewMoviePaths,
                displayedMoviePaths,
                searchKeyword,
                visibleMoviePaths,
                allowViewConsistencyRepair
            );
        }

        // 走査中1件ずつ処理するための共有文脈をまとめて組み立てる。
        private WatchScannedMovieContext CreateWatchScannedMovieContext(
            string snapshotDbFullPath,
            int snapshotTabIndex,
            Dictionary<string, WatchMainDbMovieSnapshot> existingMovieByPath,
            HashSet<string> existingViewMoviePaths,
            HashSet<string> displayedMoviePaths,
            string searchKeyword,
            bool allowViewConsistencyRepair,
            bool useIncrementalUiMode,
            bool canUseQueryOnlyWatchReload,
            CheckMode mode,
            string scanStrategy,
            bool hasIncrementalCursor,
            bool allowMissingTabAutoEnqueue,
            int? autoEnqueueTabIndex,
            string thumbnailOutPath,
            HashSet<string> existingThumbnailFileNames,
            HashSet<string> openRescueRequestKeys,
            WatchPendingNewMovieFlushContext pendingMovieFlushContext,
            long snapshotWatchScanScopeStamp
        )
        {
            return new WatchScannedMovieContext
            {
                SnapshotDbFullPath = snapshotDbFullPath,
                SnapshotTabIndex = snapshotTabIndex,
                ExistingMovieByPath = existingMovieByPath,
                ExistingViewMoviePaths = existingViewMoviePaths,
                DisplayedMoviePaths = displayedMoviePaths,
                SearchKeyword = searchKeyword,
                // 最終的に full reload する周回では、途中の view repair を抑えて無駄な差分反映を避ける。
                AllowViewConsistencyRepair = ResolveAllowViewConsistencyRepair(
                    allowViewConsistencyRepair,
                    useIncrementalUiMode
                ),
                UseIncrementalUiMode = useIncrementalUiMode,
                AllowExistingMovieDirtyTracking = ShouldAllowExistingMovieDirtyTracking(
                    canUseQueryOnlyWatchReload,
                    mode == CheckMode.Watch,
                    scanStrategy,
                    hasIncrementalCursor
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
                    || IsCurrentWatchScanScope(snapshotDbFullPath, snapshotWatchScanScopeStamp),
                AppendMovieToViewAsync = TryAppendMovieToViewByPathAsync,
            };
        }

        // pending flush 用の依存をまとめ、走査入口の object initializer を薄くする。
        private WatchPendingNewMovieFlushContext CreateWatchPendingNewMovieFlushContext(
            string snapshotDbFullPath,
            Dictionary<string, WatchMainDbMovieSnapshot> existingMovieByPath,
            List<PendingMovieRegistration> pendingNewMovies,
            bool useIncrementalUiMode,
            bool allowMissingTabAutoEnqueue,
            int? autoEnqueueTabIndex,
            string thumbnailOutPath,
            HashSet<string> existingThumbnailFileNames,
            HashSet<string> openRescueRequestKeys,
            List<QueueObj> addFilesByFolder,
            string checkFolder,
            Action<string> refreshWatchVisibleMovieGate,
            Func<bool> shouldSuppressWatchWork,
            Func<bool> isCurrentWatchScanScope
        )
        {
            return new WatchPendingNewMovieFlushContext
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
                RefreshWatchVisibleMovieGate = refreshWatchVisibleMovieGate,
                ShouldSuppressWatchWork = shouldSuppressWatchWork,
                IsCurrentWatchScanScope = isCurrentWatchScanScope,
                MarkWatchWorkDeferredWhileSuppressedAction =
                    MarkWatchWorkDeferredWhileSuppressed,
                InsertMoviesBatchAsync = InsertMoviesToMainDbBatchAsync,
                AppendMovieToViewAsync = TryAppendMovieToViewByPathAsync,
                RemovePendingMoviePlaceholderAction = RemovePendingMoviePlaceholder,
                FlushPendingQueueItemsAction = FlushPendingQueueItems,
            };
        }

        // missing thumbnail 救済に必要な周辺状態を先にまとめて作り、走査入口を薄くする。
        private async Task<(
            string ThumbnailOutPath,
            HashSet<string> ExistingThumbnailFileNames,
            ThumbnailFailureDbService FailureDbService,
            HashSet<string> OpenRescueRequestKeys
        )> BuildWatchMissingThumbnailSetupAsync(
            bool allowMissingTabAutoEnqueue,
            int snapshotTabIndex,
            int? autoEnqueueTabIndex,
            string snapshotDbName,
            string snapshotThumbFolder
        )
        {
            if (!allowMissingTabAutoEnqueue)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-tab-thumb auto enqueue suppressed: current_tab={snapshotTabIndex}"
                );
                return (
                    "",
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    null,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                );
            }

            string thumbnailOutPath = ResolveThumbnailOutPath(
                autoEnqueueTabIndex!.Value,
                snapshotDbName,
                snapshotThumbFolder
            );
            HashSet<string> existingThumbnailFileNames = await Task.Run(() =>
                BuildThumbnailFileNameLookup(thumbnailOutPath)
            );
            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            HashSet<string> openRescueRequestKeys =
                failureDbService?.GetOpenRescueRequestKeys()
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return (
                thumbnailOutPath,
                existingThumbnailFileNames,
                failureDbService,
                openRescueRequestKeys
            );
        }

        // 1フォルダ走査全体の文脈を組み立て、途中停止や再退避の判断を渡す。
        private WatchFolderScanContext CreateWatchFolderScanContext(
            CheckMode mode,
            string snapshotDbFullPath,
            long snapshotWatchScanScopeStamp,
            bool sub,
            bool restrictWatchWorkToVisibleMovies,
            HashSet<string> visibleMoviePaths,
            bool allowMissingTabAutoEnqueue,
            int? autoEnqueueTabIndex,
            WatchScannedMovieContext scannedMovieContext,
            string checkFolder
        )
        {
            WatchFolderScanContext folderScanContext = new WatchFolderScanContext
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
                NotifyFolderFirstHit = () => BuildNotifyFolderFirstHitAction(checkFolder),
            };
            folderScanContext.TryDeferWatchFolderPreprocessByUiSuppressionAction =
                folderScanContext.TryDeferWatchFolderPreprocessByUiSuppression;
            folderScanContext.TryDeferWatchFolderMidByUiSuppressionAction =
                folderScanContext.TryDeferWatchFolderMidByUiSuppression;
            folderScanContext.TryDeferWatchFolderWorkByUiSuppressionAction =
                folderScanContext.TryDeferWatchFolderWorkByUiSuppression;
            return folderScanContext;
        }

        // 1フォルダ走査で使う3つの context をまとめて作り、Watcher 側の入口初期化を薄くする。
        private (
            WatchPendingNewMovieFlushContext PendingMovieFlushContext,
            WatchScannedMovieContext ScannedMovieContext,
            WatchFolderScanContext FolderScanContext
        ) BuildWatchFolderScanRuntimeContexts(
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
            List<PendingMovieRegistration> pendingNewMovies,
            Action<string> refreshWatchVisibleMovieGate,
            Func<bool> shouldSuppressWatchWork,
            Func<bool> isCurrentWatchScanScope
        )
        {
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
                    refreshWatchVisibleMovieGate,
                    shouldSuppressWatchWork,
                    isCurrentWatchScanScope
                );
            WatchScannedMovieContext scannedMovieContext = CreateWatchScannedMovieContext(
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
                scanStrategy,
                hasIncrementalCursor,
                allowMissingTabAutoEnqueue,
                autoEnqueueTabIndex,
                thumbnailOutPath,
                existingThumbnailFileNames,
                openRescueRequestKeys,
                pendingMovieFlushContext,
                snapshotWatchScanScopeStamp
            );
            WatchFolderScanContext folderScanContext = CreateWatchFolderScanContext(
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
            return (pendingMovieFlushContext, scannedMovieContext, folderScanContext);
        }
    }
}
