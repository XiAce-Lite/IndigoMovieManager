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
                message => HandleThumbnailRescueWorkerLog(slotLabel, message)
            );
        }

        // 本体終了時は両slotのworkerを止めてから破棄し、別DB向けworkerが残存しないようにする。
        private void DisposeThumbnailRescueWorkerLaunchers()
        {
            CloseManualThumbnailRescueProgress();
            bool stoppedDefault = _thumbnailRescueWorkerLauncher.TryStopRunningWorker(
                message => HandleThumbnailRescueWorkerLog("default-slot", message)
            );
            bool stoppedManual = _thumbnailManualRescueWorkerLauncher.TryStopRunningWorker(
                message => HandleThumbnailRescueWorkerLog("manual-slot", message)
            );
            if (stoppedDefault || stoppedManual)
            {
                string currentMainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-worker",
                    $"workers stopped by app shutdown: db='{currentMainDbFullPath}' stopped_default={stoppedDefault} stopped_manual={stoppedManual}"
                );
            }

            _thumbnailRescueWorkerLauncher.Dispose();
            _thumbnailManualRescueWorkerLauncher.Dispose();
        }

        // DBを切り替える時だけ旧DB用workerを止め、他DBの救済が残り続ける状態を防ぐ。
        private void StopThumbnailRescueWorkersForDbSwitch(
            string previousMainDbFullPath,
            string nextMainDbFullPath
        )
        {
            if (string.IsNullOrWhiteSpace(previousMainDbFullPath))
            {
                return;
            }

            if (
                string.Equals(
                    previousMainDbFullPath,
                    nextMainDbFullPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            CloseManualThumbnailRescueProgress();
            bool stoppedDefault = _thumbnailRescueWorkerLauncher.TryStopRunningWorker(
                message => HandleThumbnailRescueWorkerLog("default-slot", message)
            );
            bool stoppedManual = _thumbnailManualRescueWorkerLauncher.TryStopRunningWorker(
                message => HandleThumbnailRescueWorkerLog("manual-slot", message)
            );

            if (!stoppedDefault && !stoppedManual)
            {
                return;
            }

            DebugRuntimeLog.Write(
                "thumbnail-rescue-worker",
                $"workers stopped by db switch: from='{previousMainDbFullPath}' to='{nextMainDbFullPath}' stopped_default={stoppedDefault} stopped_manual={stoppedManual}"
            );
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
