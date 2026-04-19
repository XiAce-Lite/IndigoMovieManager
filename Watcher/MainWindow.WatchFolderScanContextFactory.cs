using System;
using System.Collections.Generic;
using IndigoMovieManager.Data;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
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
                AllowViewConsistencyRepair = allowViewConsistencyRepair,
                UseIncrementalUiMode = useIncrementalUiMode,
                AllowExistingMovieDirtyTracking =
                    canUseQueryOnlyWatchReload
                    && mode == CheckMode.Watch
                    && string.Equals(
                        scanStrategy,
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
                    || IsCurrentWatchScanScope(snapshotDbFullPath, snapshotWatchScanScopeStamp),
                AppendMovieToViewAsync = TryAppendMovieToViewByPathAsync,
            };
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
    }
}
