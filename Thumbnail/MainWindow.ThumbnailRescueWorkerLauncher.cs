using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly ThumbnailRescueWorkerLauncher _thumbnailRescueWorkerLauncher = new();

        // 通常キューが空いた時だけ、FailureDb の pending_rescue を外部workerへ渡す。
        private Task TryStartExternalThumbnailRescueWorkerAsync(CancellationToken cts)
        {
            cts.ThrowIfCancellationRequested();
            if (!isThumbnailQueueInputEnabled)
            {
                return Task.CompletedTask;
            }

            if (TryGetCurrentQueueActiveCount(out int activeCount) && activeCount > 0)
            {
                return Task.CompletedTask;
            }

            string mainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            _ = _thumbnailRescueWorkerLauncher.TryStartIfNeeded(
                mainDbFullPath,
                message => DebugRuntimeLog.Write("thumbnail-rescue-worker", message)
            );
            return Task.CompletedTask;
        }
    }
}
