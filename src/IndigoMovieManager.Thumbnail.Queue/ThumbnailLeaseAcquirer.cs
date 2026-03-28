using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// QueueDB からの lease 取得と lane 順整列をまとめて扱う。
    /// </summary>
    public static class ThumbnailLeaseAcquirer
    {
        public static List<QueueDbLeaseItem> AcquireLeasedItems(
            QueueDbService queueDbService,
            string ownerInstanceId,
            int leaseBatchSize,
            int leaseMinutes,
            Func<int?> preferredTabIndexResolver,
            Func<IReadOnlyList<string>> preferredMoviePathKeysResolver,
            Action<string> log,
            ThumbnailQueuePriority? minimumPriority = null
        )
        {
            int? preferredTabIndex = null;
            IReadOnlyList<string> preferredMoviePathKeys = null;
            if (preferredTabIndexResolver != null)
            {
                try
                {
                    int? resolved = preferredTabIndexResolver();
                    preferredTabIndex =
                        (resolved.HasValue && resolved.Value >= 0) ? resolved : null;
                }
                catch (Exception ex)
                {
                    log?.Invoke($"preferred tab resolver failed: {ex.Message}");
                }
            }

            if (preferredMoviePathKeysResolver != null)
            {
                try
                {
                    preferredMoviePathKeys = preferredMoviePathKeysResolver();
                }
                catch (Exception ex)
                {
                    log?.Invoke($"preferred movie path resolver failed: {ex.Message}");
                }
            }

            List<QueueDbLeaseItem> leasedItems = queueDbService.GetPendingAndLease(
                ownerInstanceId,
                leaseBatchSize,
                TimeSpan.FromMinutes(leaseMinutes),
                DateTime.UtcNow,
                preferredTabIndex,
                preferredMoviePathKeys,
                minimumPriority
            );
            SortLeasedItemsByLane(leasedItems);
            long leaseTotal = ThumbnailQueueMetrics.RecordLeaseAcquired(leasedItems.Count);
            if (leasedItems.Count > 0)
            {
                log?.Invoke($"consumer lease: acquired={leasedItems.Count} total={leaseTotal}");
            }

            return leasedItems;
        }

        // バッチ先頭へ通常動画を寄せ、巨大動画を後段へ回す。
        // これにより巨大動画の貼り付きで通常キュー全体が鈍るのを避ける。
        public static void SortLeasedItemsByLane(List<QueueDbLeaseItem> leasedItems)
        {
            if (leasedItems == null || leasedItems.Count < 2)
            {
                return;
            }

            leasedItems.Sort((left, right) =>
            {
                int leftPriority = (int)ThumbnailQueuePriorityHelper.Normalize(
                    left?.Priority ?? ThumbnailQueuePriority.Normal
                );
                int rightPriority = (int)ThumbnailQueuePriorityHelper.Normalize(
                    right?.Priority ?? ThumbnailQueuePriority.Normal
                );
                int priorityDiff = rightPriority - leftPriority;
                if (priorityDiff != 0)
                {
                    return priorityDiff;
                }

                int leftBucketRank = Math.Max(0, left?.LeaseBucketRank ?? 0);
                int rightBucketRank = Math.Max(0, right?.LeaseBucketRank ?? 0);
                int bucketDiff = leftBucketRank - rightBucketRank;
                if (bucketDiff != 0)
                {
                    return bucketDiff;
                }

                ThumbnailExecutionLane leftLane = ThumbnailLaneClassifier.ResolveLane(
                    left?.MovieSizeBytes ?? 0
                );
                ThumbnailExecutionLane rightLane = ThumbnailLaneClassifier.ResolveLane(
                    right?.MovieSizeBytes ?? 0
                );
                int rankDiff = ThumbnailLaneClassifier.ResolveRank(leftLane)
                    - ThumbnailLaneClassifier.ResolveRank(rightLane);
                if (rankDiff != 0)
                {
                    return rankDiff;
                }

                long leftSize = Math.Max(0, left?.MovieSizeBytes ?? 0);
                long rightSize = Math.Max(0, right?.MovieSizeBytes ?? 0);
                int sizeDiff = leftSize.CompareTo(rightSize);
                if (sizeDiff != 0)
                {
                    return sizeDiff;
                }

                int leftLeaseOrder = Math.Max(0, left?.LeaseOrder ?? 0);
                int rightLeaseOrder = Math.Max(0, right?.LeaseOrder ?? 0);
                return leftLeaseOrder.CompareTo(rightLeaseOrder);
            });
        }
    }
}
