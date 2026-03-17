using System.Threading;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// セッション進捗と現在バッチ件数をまとめて持つ。
    /// </summary>
    internal sealed class ThumbnailQueueBatchState
    {
        private int _sessionCompletedCount;
        private int _sessionTotalCount;
        private int _batchCompletedCount;
        private int _batchFailedCount;

        public int SessionCompletedCount => Volatile.Read(ref _sessionCompletedCount);
        public int SessionTotalCount => Volatile.Read(ref _sessionTotalCount);
        public int BatchCompletedCount => Volatile.Read(ref _batchCompletedCount);
        public int BatchFailedCount => Volatile.Read(ref _batchFailedCount);

        public void BeginBatch(int activeCountAtBatchStart, int leasedItemCount)
        {
            Interlocked.Exchange(ref _batchCompletedCount, 0);
            Interlocked.Exchange(ref _batchFailedCount, 0);

            int estimatedTotal = SessionCompletedCount + Math.Max(0, activeCountAtBatchStart);
            if (estimatedTotal > _sessionTotalCount)
            {
                _sessionTotalCount = estimatedTotal;
            }

            if (_sessionTotalCount < 1)
            {
                _sessionTotalCount = SessionCompletedCount + Math.Max(0, leasedItemCount);
            }
        }

        public int MarkJobCompleted()
        {
            Interlocked.Increment(ref _batchCompletedCount);
            return Interlocked.Increment(ref _sessionCompletedCount);
        }

        public void MarkJobFailed()
        {
            Interlocked.Increment(ref _batchFailedCount);
        }
    }
}
