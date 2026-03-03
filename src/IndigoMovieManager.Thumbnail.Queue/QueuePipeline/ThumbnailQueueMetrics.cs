using System.Threading;

namespace IndigoMovieManager.Thumbnail.QueuePipeline
{
    // Producer/Persister/Consumerで共通利用するキューメトリクス集計。
    // Interlockedで更新し、複数スレッドからの加算でも値を壊さない。
    public static class ThumbnailQueueMetrics
    {
        private static long _totalEnqueueAccepted;
        private static long _totalUpsertSubmitted;
        private static long _totalDbAffected;
        private static long _totalDbInserted;
        private static long _totalDbUpdated;
        private static long _totalDbSkippedProcessing;
        private static long _totalLeaseAcquired;
        private static long _totalFailed;

        // 受理した投入数を加算し、更新後の累計を返す。
        public static long RecordEnqueueAccepted(int count = 1)
        {
            return Interlocked.Add(ref _totalEnqueueAccepted, count);
        }

        // QueueDBへUpsert投入した件数を加算し、更新後の累計を返す。
        // 注意: 実際に行変更された件数ではなく、Upsertへ渡した件数を表す。
        internal static long RecordUpsertSubmitted(int count)
        {
            return Interlocked.Add(ref _totalUpsertSubmitted, count);
        }

        // QueueDBへ実際に反映された件数を加算し、更新後の累計を返す。
        internal static long RecordDbAffected(int count)
        {
            return Interlocked.Add(ref _totalDbAffected, count);
        }

        // QueueDBの新規INSERT件数を加算し、更新後の累計を返す。
        internal static long RecordDbInserted(int count)
        {
            return Interlocked.Add(ref _totalDbInserted, count);
        }

        // QueueDBの既存UPDATE件数を加算し、更新後の累計を返す。
        internal static long RecordDbUpdated(int count)
        {
            return Interlocked.Add(ref _totalDbUpdated, count);
        }

        // Processing保護で未反映になった件数を加算し、更新後の累計を返す。
        internal static long RecordDbSkippedProcessing(int count)
        {
            return Interlocked.Add(ref _totalDbSkippedProcessing, count);
        }

        // Consumerがリース取得した件数を加算し、更新後の累計を返す。
        internal static long RecordLeaseAcquired(int count)
        {
            return Interlocked.Add(ref _totalLeaseAcquired, count);
        }

        // 失敗件数を加算し、更新後の累計を返す。
        internal static long RecordFailed(int count = 1)
        {
            return Interlocked.Add(ref _totalFailed, count);
        }

        // 現時点の累計をログ向け文字列で返す。
        internal static string CreateSummary()
        {
            long enqueue = Volatile.Read(ref _totalEnqueueAccepted);
            long upsertSubmitted = Volatile.Read(ref _totalUpsertSubmitted);
            long dbAffected = Volatile.Read(ref _totalDbAffected);
            long dbInserted = Volatile.Read(ref _totalDbInserted);
            long dbUpdated = Volatile.Read(ref _totalDbUpdated);
            long dbSkippedProcessing = Volatile.Read(ref _totalDbSkippedProcessing);
            long leased = Volatile.Read(ref _totalLeaseAcquired);
            long failed = Volatile.Read(ref _totalFailed);
            return $"enqueue_total={enqueue} upsert_submitted_total={upsertSubmitted} " +
                $"db_affected_total={dbAffected} db_inserted_total={dbInserted} " +
                $"db_updated_total={dbUpdated} db_skipped_processing_total={dbSkippedProcessing} " +
                $"lease_total={leased} failed_total={failed}";
        }
    }
}
