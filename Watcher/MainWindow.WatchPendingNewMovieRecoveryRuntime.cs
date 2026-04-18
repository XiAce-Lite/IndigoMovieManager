using System;
using System.Threading.Tasks;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // folder単位の例外でも、途中まで積めた新規動画だけはDBへ逃がして全損を避ける。
        private async Task<WatchPendingNewMovieFlushResult> TryFlushPendingNewMoviesAfterFolderFailureAsync(
            string checkFolder,
            WatchFolderScanContext folderScanContext
        )
        {
            WatchPendingNewMovieFlushContext pendingContext =
                folderScanContext?.ScannedMovieContext?.PendingMovieFlushContext;
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
