using System.Threading;
using IndigoMovieManager;

namespace IndigoMovieManager.Startup
{
    /// <summary>
    /// 起動時の first-page 要求を採番し、古い要求を確実に捨てるための小さな司令塔。
    /// sidecar 導入前でも使い回せるよう、責務はキャンセル管理だけに絞る。
    /// </summary>
    internal sealed class StartupLoadCoordinator
    {
        private int _currentRevision;
        private CancellationTokenSource _currentCts = new();

        public StartupLoadSession StartNewSession()
        {
            CancellationTokenSource nextCts = new();
            CancellationTokenSource oldCts = Interlocked.Exchange(ref _currentCts, nextCts);
            try
            {
                oldCts.Cancel();
            }
            catch
            {
                // 旧セッション破棄時は、起動継続を優先する。
            }
            finally
            {
                oldCts.Dispose();
            }

            int revision = Interlocked.Increment(ref _currentRevision);
            return new StartupLoadSession(revision, nextCts.Token);
        }

        public bool IsCurrent(int revision)
        {
            return revision == Volatile.Read(ref _currentRevision)
                && !_currentCts.IsCancellationRequested;
        }

        public void CancelCurrent()
        {
            CancellationTokenSource cts = _currentCts;
            try
            {
                cts.Cancel();
            }
            catch
            {
                // キャンセル失敗でも次の要求は継続させる。
            }
        }
    }

    internal readonly record struct StartupLoadSession(
        int Revision,
        CancellationToken CancellationToken
    );

    internal readonly record struct StartupFeedRequest(
        string DbPath,
        string SortId,
        string SearchKeyword,
        int FirstPageSize,
        int AppendPageSize
    );

    internal readonly record struct StartupFeedPage(
        MovieRecords[] Items,
        int ApproximateTotalCount,
        bool HasMore,
        string SourceKind,
        int PageIndex
    );
}
