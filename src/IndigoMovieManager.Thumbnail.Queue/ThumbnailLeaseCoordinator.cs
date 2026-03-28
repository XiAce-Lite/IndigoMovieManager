using System.Runtime.CompilerServices;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 取得済みleaseの消費と継ぎ足しをまとめ、処理側へ順に流す。
    /// </summary>
    internal static class ThumbnailLeaseCoordinator
    {
        public static async IAsyncEnumerable<QueueDbLeaseItem> EnumerateLeasedItemsAsync(
            QueueDbService queueDbService,
            string ownerInstanceId,
            IReadOnlyList<QueueDbLeaseItem> initialItems,
            int leaseBatchSize,
            int leaseMinutes,
            Func<int?> preferredTabIndexResolver,
            Func<IReadOnlyList<string>> preferredMoviePathKeysResolver,
            Action<string> log,
            [EnumeratorCancellation] CancellationToken cts
        )
        {
            LinkedList<QueueDbLeaseItem> buffer = new();
            ThumbnailLeaseBuffer.AppendLeaseItems(buffer, initialItems);
            DateTime lastPreferredProbeUtc = DateTime.MinValue;

            while (!cts.IsCancellationRequested)
            {
                if (buffer.Count < 1)
                {
                    List<QueueDbLeaseItem> nextItems = ThumbnailLeaseAcquirer.AcquireLeasedItems(
                        queueDbService,
                        ownerInstanceId,
                        leaseBatchSize,
                        leaseMinutes,
                        preferredTabIndexResolver,
                        preferredMoviePathKeysResolver,
                        log
                    );
                    if (nextItems.Count > 0)
                    {
                        ThumbnailLeaseBuffer.AppendLeaseItems(buffer, nextItems);
                        lastPreferredProbeUtc = DateTime.MinValue;
                    }
                    else
                    {
                        int activeCount = queueDbService.GetActiveQueueCount(ownerInstanceId);
                        if (activeCount < 1)
                        {
                            yield break;
                        }

                        // 実行中ジョブが残っている間は短い間隔で再取得を試みる。
                        await Task.Delay(250, cts).ConfigureAwait(false);
                        continue;
                    }
                }

                if (
                    ThumbnailLeaseBuffer.ShouldProbePreferredLease(buffer, lastPreferredProbeUtc)
                    && ThumbnailLeaseBuffer.TryFrontInsertPreferredLeaseItems(
                        queueDbService,
                        ownerInstanceId,
                        leaseMinutes,
                        preferredTabIndexResolver,
                        preferredMoviePathKeysResolver,
                        log,
                        buffer
                    )
                )
                {
                    lastPreferredProbeUtc = DateTime.UtcNow;
                }
                else if (
                    ThumbnailLeaseBuffer.ShouldProbePreferredLease(buffer, lastPreferredProbeUtc)
                )
                {
                    lastPreferredProbeUtc = DateTime.UtcNow;
                }

                if (buffer.Count < 1)
                {
                    continue;
                }

                LinkedListNode<QueueDbLeaseItem> headNode = buffer.First;
                if (headNode == null)
                {
                    continue;
                }

                buffer.RemoveFirst();
                if (ThumbnailQueuePriorityHelper.IsPreferred(headNode.Value.Priority))
                {
                    lastPreferredProbeUtc = DateTime.MinValue;
                }

                yield return headNode.Value;
            }
        }
    }
}
