namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// キュー処理中の進捗表示と通知コールバックをまとめて扱う。
    /// </summary>
    internal sealed class ThumbnailQueueProgressPublisher
    {
        private readonly object progressLock = new();
        private readonly Action<int, int, int, int> progressSnapshot;
        private readonly IThumbnailQueueProgressPresenter progressPresenter;
        private readonly Action<QueueObj> onJobStarted;
        private readonly Action<QueueObj> onJobCompleted;
        private readonly Action<string> log;
        private IThumbnailQueueProgressHandle progressHandle =
            NoOpThumbnailQueueProgressHandle.Instance;

        public ThumbnailQueueProgressPublisher(
            Action<int, int, int, int> progressSnapshot,
            IThumbnailQueueProgressPresenter progressPresenter,
            Action<QueueObj> onJobStarted,
            Action<QueueObj> onJobCompleted,
            Action<string> log
        )
        {
            this.progressSnapshot = progressSnapshot;
            this.progressPresenter = progressPresenter ?? NoOpThumbnailQueueProgressPresenter.Instance;
            this.onJobStarted = onJobStarted;
            this.onJobCompleted = onJobCompleted;
            this.log = log ?? (_ => { });
        }

        public void Open(string title)
        {
            IThumbnailQueueProgressHandle nextHandle = NoOpThumbnailQueueProgressHandle.Instance;
            try
            {
                nextHandle =
                    progressPresenter.Show(title) ?? NoOpThumbnailQueueProgressHandle.Instance;
            }
            catch (Exception ex)
            {
                log($"consumer progress open failed: {ex.Message}");
            }

            lock (progressLock)
            {
                progressHandle = nextHandle;
            }

            log("consumer progress opened.");
        }

        public void Close()
        {
            lock (progressLock)
            {
                progressHandle.Dispose();
                progressHandle = NoOpThumbnailQueueProgressHandle.Instance;
            }

            log("consumer progress closed.");
        }

        public void ReportSnapshot(
            int completedCount,
            int totalCount,
            int currentParallelism,
            int configuredParallelism
        )
        {
            if (progressSnapshot == null)
            {
                return;
            }

            try
            {
                progressSnapshot(
                    Math.Max(0, completedCount),
                    Math.Max(0, totalCount),
                    Math.Max(0, currentParallelism),
                    Math.Max(0, configuredParallelism)
                );
            }
            catch
            {
                // 進捗通知失敗はキュー処理本体を止めない。
            }
        }

        public void NotifyJobStarted(QueueObj queueObj)
        {
            NotifyJobCallback(onJobStarted, queueObj);
        }

        public void NotifyJobCompleted(QueueObj queueObj)
        {
            NotifyJobCallback(onJobCompleted, queueObj);
        }

        public void ReportJobCompleted(
            int tabIndex,
            string moviePath,
            int doneInSession,
            int sessionTotalCount,
            int currentParallelism,
            int configuredParallelism
        )
        {
            string reportTitle =
                $"{GetTabProgressTitle(tabIndex)} ({doneInSession}/{sessionTotalCount})";
            string message = moviePath ?? "";
            int safeSessionTotalCount = sessionTotalCount < 1 ? 1 : sessionTotalCount;
            double totalProgress = (double)doneInSession * 100d / safeSessionTotalCount;
            if (totalProgress > 100d)
            {
                totalProgress = 100d;
            }

            lock (progressLock)
            {
                progressHandle.Report(totalProgress, message, reportTitle, false);
            }

            ReportSnapshot(
                doneInSession,
                sessionTotalCount,
                currentParallelism,
                configuredParallelism
            );
        }

        private static void NotifyJobCallback(Action<QueueObj> callback, QueueObj queueObj)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(queueObj);
            }
            catch
            {
                // UI通知失敗でジョブ処理を止めない。
            }
        }

        private static string GetTabProgressTitle(int tabIndex)
        {
            return tabIndex switch
            {
                0 => "サムネイル作成中(Small)",
                1 => "サムネイル作成中(Big)",
                2 => "サムネイル作成中(Grid)",
                3 => "サムネイル作成中(List)",
                4 => "サムネイル作成中(Big10)",
                _ => "サムネイル作成中",
            };
        }
    }
}
