using System.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private long ReadCurrentWatchScanScopeStamp()
        {
            return Interlocked.Read(ref _watchScanScopeStamp);
        }

        private bool IsCurrentWatchScanScope(string snapshotDbFullPath, long requestScopeStamp)
        {
            return CanUseWatchScanScope(
                MainVM?.DbInfo?.DBFullPath ?? "",
                snapshotDbFullPath,
                requestScopeStamp,
                ReadCurrentWatchScanScopeStamp()
            );
        }

        private bool TryAbortWatchScanForStaleScope(
            string snapshotDbFullPath,
            long requestScopeStamp,
            string phase,
            bool includeCurrentDb = false
        )
        {
            if (IsCurrentWatchScanScope(snapshotDbFullPath, requestScopeStamp))
            {
                return false;
            }

            string currentDb = MainVM?.DbInfo?.DBFullPath ?? "";
            string currentDbSuffix = includeCurrentDb ? $" current_db='{currentDb}'" : "";
            string phasePrefix = string.IsNullOrWhiteSpace(phase) ? "" : $"{phase}: ";
            DebugRuntimeLog.Write(
                "watch-check",
                $"abort scan {phasePrefix}stale scope. snapshot_db='{snapshotDbFullPath}'{currentDbSuffix}"
            );
            return true;
        }

        // folder走査後の suppression / stale 打ち切りをまとめ、Watcher 側の末尾分岐を薄くする。
        private bool TryAbortWatchScanBeforeFinalReload(
            bool watchStoppedByUiSuppression,
            CheckMode mode,
            string snapshotDbFullPath,
            long requestScopeStamp
        )
        {
            if (watchStoppedByUiSuppression)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan stopped by ui suppression: mode={mode} db='{snapshotDbFullPath}'"
                );
                return true;
            }

            return TryAbortWatchScanForStaleScope(
                snapshotDbFullPath,
                requestScopeStamp,
                "before final reload"
            );
        }
    }
}
