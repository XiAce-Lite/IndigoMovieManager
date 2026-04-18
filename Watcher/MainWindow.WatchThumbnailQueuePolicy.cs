using System.Collections.Generic;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // QueueDBのアクティブ件数を安全に取得する。取得不能時はfalseを返して救済判定を継続する。
        private bool TryGetCurrentQueueActiveCount(out int activeCount)
        {
            activeCount = 0;
            try
            {
                var queueDbService = ResolveCurrentQueueDbService();
                if (queueDbService == null)
                {
                    return false;
                }

                activeCount = queueDbService.GetActiveQueueCount(thumbnailQueueOwnerInstanceId);
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-thumb rescue queue count failed: {ex.Message}"
                );
                return false;
            }
        }

        // 100件たまるごとにサムネイルキューへ流すための共通処理。
        private void FlushPendingQueueItems(List<QueueObj> pendingItems, string folderPath)
        {
            if (pendingItems.Count < 1)
            {
                return;
            }

            bool bypassDebounce = string.Equals(
                folderPath,
                "RescueMissingThumbnails",
                StringComparison.Ordinal
            );
            int flushedCount = 0;
            foreach (QueueObj pending in pendingItems)
            {
                if (TryEnqueueThumbnailJob(pending, bypassDebounce))
                {
                    flushedCount++;
                }
            }
            DebugRuntimeLog.Write(
                "watch-check",
                $"enqueue batch: folder='{folderPath}' requested={pendingItems.Count} flushed={flushedCount}"
            );
            pendingItems.Clear();
        }
    }
}
