using System.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // DB切替や shutdown で watch scope を進め、旧passをまとめて stale 化する。
        private long _watchScanScopeStamp = 1;

        private long InvalidateWatchScanScope(string reason)
        {
            CancelDeferredWatchUiReload($"scope-invalidated:{reason}");
            long nextStamp = Interlocked.Increment(ref _watchScanScopeStamp);
            DebugRuntimeLog.Write(
                "watch-check",
                $"watch scan scope invalidated: stamp={nextStamp} reason={reason}"
            );
            return nextStamp;
        }

        // manual 起点は scope を気にせず、watch 起点だけ現在スコープかを判定する。
        private bool IsCurrentOrManualWatchScope(
            CheckMode mode,
            string snapshotDbFullPath,
            long snapshotWatchScanScopeStamp
        )
        {
            return mode != CheckMode.Watch
                || IsCurrentWatchScanScope(snapshotDbFullPath, snapshotWatchScanScopeStamp);
        }
    }
}
