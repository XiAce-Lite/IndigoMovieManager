using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly ThumbnailRescueWorkerLauncher _thumbnailRescueWorkerLauncher =
            CreateThumbnailRescueWorkerLauncher();

        // launcher が app 固有の path policy を直接読まないよう、host 側でまとめて渡す。
        private static ThumbnailRescueWorkerLauncher CreateThumbnailRescueWorkerLauncher()
        {
            ThumbnailRescueWorkerLaunchSettings launchSettings =
                ThumbnailRescueWorkerLaunchSettingsFactory.CreateDefault(
                sessionRootDirectoryPath: AppLocalDataPaths.RescueWorkerSessionsPath,
                logDirectoryPath: AppLocalDataPaths.LogsPath,
                failureDbDirectoryPath: AppLocalDataPaths.FailureDbPath,
                hostBaseDirectory: AppContext.BaseDirectory
            );
            return new ThumbnailRescueWorkerLauncher(launchSettings);
        }

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
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            string thumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            _ = _thumbnailRescueWorkerLauncher.TryStartIfNeeded(
                mainDbFullPath,
                dbName,
                thumbFolder,
                message => DebugRuntimeLog.Write("thumbnail-rescue-worker", message)
            );
            return Task.CompletedTask;
        }
    }
}
