using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 取得済み lease のバッファ運用と preferred 差し込みをまとめる。
    /// </summary>
    public static class ThumbnailLeaseBuffer
    {
        private const int PreferredLeaseProbeIntervalMs = 150;

        public static void AppendLeaseItems(
            LinkedList<QueueDbLeaseItem> buffer,
            IReadOnlyList<QueueDbLeaseItem> items
        )
        {
            if (buffer == null || items == null || items.Count < 1)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                buffer.AddLast(items[i]);
            }
        }

        public static bool TryFrontInsertPreferredLeaseItems(
            QueueDbService queueDbService,
            string ownerInstanceId,
            int leaseMinutes,
            Func<int?> preferredTabIndexResolver,
            Func<IReadOnlyList<string>> preferredMoviePathKeysResolver,
            Action<string> log,
            LinkedList<QueueDbLeaseItem> buffer
        )
        {
            if (queueDbService == null || buffer == null)
            {
                return false;
            }

            List<QueueDbLeaseItem> preferredItems = ThumbnailLeaseAcquirer.AcquireLeasedItems(
                queueDbService,
                ownerInstanceId,
                leaseBatchSize: 1,
                leaseMinutes,
                preferredTabIndexResolver,
                preferredMoviePathKeysResolver,
                log,
                minimumPriority: ThumbnailQueuePriority.Preferred
            );
            if (preferredItems.Count < 1)
            {
                return false;
            }

            for (int i = preferredItems.Count - 1; i >= 0; i--)
            {
                buffer.AddFirst(preferredItems[i]);
            }

            log?.Invoke($"consumer preferred inserted: acquired={preferredItems.Count}");
            return true;
        }

        public static bool ShouldProbePreferredLease(
            LinkedList<QueueDbLeaseItem> buffer,
            DateTime lastPreferredProbeUtc
        )
        {
            if (buffer == null || buffer.Count < 1)
            {
                return false;
            }

            QueueDbLeaseItem head = buffer.First?.Value;
            if (head == null || ThumbnailQueuePriorityHelper.IsPreferred(head.Priority))
            {
                return false;
            }

            if (lastPreferredProbeUtc == DateTime.MinValue)
            {
                return true;
            }

            return (DateTime.UtcNow - lastPreferredProbeUtc).TotalMilliseconds
                >= PreferredLeaseProbeIntervalMs;
        }
    }
}
