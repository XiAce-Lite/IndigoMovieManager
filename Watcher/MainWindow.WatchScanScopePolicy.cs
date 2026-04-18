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
    }
}
