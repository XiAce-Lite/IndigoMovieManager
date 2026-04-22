using System;
using System.Threading.Tasks;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private static WatchPendingNewMovieFlushContext GetWatchPendingNewMovieFlushContext(
            WatchFolderScanContext folderScanContext
        )
        {
            return folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext;
        }

        // folder failure 時の先頭回復手順を 1 入口へ寄せ、Watcher 側の catch を薄くする。
        private async Task<WatchPendingNewMovieFlushResult> RunWatchFolderFailureRecoveryAsync(
            string checkFolder,
            Exception exception,
            WatchFolderScanContext folderScanContext
        )
        {
            WriteWatchFolderFailure(checkFolder, exception);
            return await TryFlushPendingNewMoviesAfterFolderFailureAsync(
                checkFolder,
                folderScanContext
            );
        }

        // folder単位の例外でも、途中まで積めた新規動画だけはDBへ逃がして全損を避ける。
        private async Task<WatchPendingNewMovieFlushResult> TryFlushPendingNewMoviesAfterFolderFailureAsync(
            string checkFolder,
            WatchFolderScanContext folderScanContext
        )
        {
            WatchPendingNewMovieFlushContext pendingContext =
                GetWatchPendingNewMovieFlushContext(folderScanContext);
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
    }
}
