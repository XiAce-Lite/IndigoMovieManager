using System.IO;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly ThumbnailRescueWorkerLauncher _thumbnailRescueWorkerLauncher =
            CreateThumbnailRescueWorkerLauncher("default");
        private readonly ThumbnailRescueWorkerLauncher _thumbnailManualRescueWorkerLauncher =
            CreateThumbnailRescueWorkerLauncher("manual");

        // 常駐起動枠と明示救済枠で session を分け、右クリック救済だけ別枠で即時起動できるようにする。
        private static ThumbnailRescueWorkerLauncher CreateThumbnailRescueWorkerLauncher(
            string slotName
        )
        {
            string normalizedSlotName = string.IsNullOrWhiteSpace(slotName)
                ? "default"
                : slotName.Trim();
            ThumbnailRescueWorkerLaunchSettings launchSettings =
                ThumbnailRescueWorkerLaunchSettingsFactory.CreateDefault(
                sessionRootDirectoryPath: Path.Combine(
                    AppLocalDataPaths.RescueWorkerSessionsPath,
                    normalizedSlotName
                ),
                logDirectoryPath: AppLocalDataPaths.LogsPath,
                failureDbDirectoryPath: AppLocalDataPaths.FailureDbPath,
                hostBaseDirectory: AppContext.BaseDirectory
            );
            return new ThumbnailRescueWorkerLauncher(launchSettings);
        }

        private ThumbnailRescueWorkerLauncher ResolveThumbnailRescueWorkerLauncher(
            bool useDedicatedManualWorkerSlot
        )
        {
            return useDedicatedManualWorkerSlot
                ? _thumbnailManualRescueWorkerLauncher
                : _thumbnailRescueWorkerLauncher;
        }

        private bool TryStartThumbnailRescueWorker(
            bool useDedicatedManualWorkerSlot,
            string mainDbFullPath,
            string dbName,
            string thumbFolder
        )
        {
            string slotLabel = useDedicatedManualWorkerSlot ? "manual-slot" : "default-slot";
            return ResolveThumbnailRescueWorkerLauncher(useDedicatedManualWorkerSlot).TryStartIfNeeded(
                mainDbFullPath,
                dbName,
                thumbFolder,
                message => DebugRuntimeLog.Write("thumbnail-rescue-worker", $"{slotLabel}: {message}")
            );
        }

        // 閉じ際は両slotの process handle 参照を外し、session 状態だけを外部 worker へ委ねる。
        private void DisposeThumbnailRescueWorkerLaunchers()
        {
            _thumbnailRescueWorkerLauncher.Dispose();
            _thumbnailManualRescueWorkerLauncher.Dispose();
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
            _ = TryStartThumbnailRescueWorker(
                useDedicatedManualWorkerSlot: false,
                mainDbFullPath,
                dbName,
                thumbFolder
            );
            return Task.CompletedTask;
        }
    }
}
