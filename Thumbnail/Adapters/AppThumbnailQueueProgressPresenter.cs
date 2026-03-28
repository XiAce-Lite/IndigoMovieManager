using Notification.Wpf;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Queue通知ポートを既存の Notification.Wpf へ橋渡しする。
    /// </summary>
    internal sealed class AppThumbnailQueueProgressPresenter : IThumbnailQueueProgressPresenter
    {
        private readonly NotificationManager notificationManager = new();

        public IThumbnailQueueProgressHandle Show(string title)
        {
            try
            {
                var progress = notificationManager.ShowProgressBar(
                    title,
                    false,
                    true,
                    "ProgressArea",
                    false,
                    2,
                    ""
                );
                return new AppThumbnailQueueProgressHandle(progress);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "queue-consumer",
                    $"progress presenter open failed: {ex.Message}"
                );
                return NoOpThumbnailQueueProgressHandle.Instance;
            }
        }

        private sealed class AppThumbnailQueueProgressHandle : IThumbnailQueueProgressHandle
        {
            private readonly dynamic progress;
            private bool disposed;

            public AppThumbnailQueueProgressHandle(object progress)
            {
                this.progress = progress;
            }

            public void Report(
                double progressPercent,
                string message,
                string title,
                bool isIndeterminate
            )
            {
                if (disposed)
                {
                    return;
                }

                try
                {
                    progress.Report((progressPercent, message, title, isIndeterminate));
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "queue-consumer",
                        $"progress presenter report failed: {ex.Message}"
                    );
                }
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;

                try
                {
                    progress.Dispose();
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "queue-consumer",
                        $"progress presenter dispose failed: {ex.Message}"
                    );
                }
            }
        }
    }
}
