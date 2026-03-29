using System;
using System.IO;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly ThumbnailRescueWorkerLauncher _thumbnailRescueWorkerLauncher =
            CreateThumbnailRescueWorkerLauncher("default");
        private readonly ThumbnailRescueWorkerLauncher _thumbnailManualRescueWorkerLauncher1 =
            CreateThumbnailRescueWorkerLauncher("manual-1");
        private readonly ThumbnailRescueWorkerLauncher _thumbnailManualRescueWorkerLauncher2 =
            CreateThumbnailRescueWorkerLauncher("manual-2");

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
            bool useDedicatedManualWorkerSlot,
            out string slotLabel,
            out bool manualSlotsBusy
        )
        {
            manualSlotsBusy = false;
            if (!useDedicatedManualWorkerSlot)
            {
                slotLabel = "default-slot";
                return _thumbnailRescueWorkerLauncher;
            }

            if (!_thumbnailManualRescueWorkerLauncher1.IsBusy())
            {
                slotLabel = "manual-slot-1";
                return _thumbnailManualRescueWorkerLauncher1;
            }

            if (!_thumbnailManualRescueWorkerLauncher2.IsBusy())
            {
                slotLabel = "manual-slot-2";
                return _thumbnailManualRescueWorkerLauncher2;
            }

            manualSlotsBusy = true;
            slotLabel = "manual-slot";
            return null;
        }

        private bool TryStartThumbnailRescueWorker(
            bool useDedicatedManualWorkerSlot,
            string mainDbFullPath,
            string dbName,
            string thumbFolder,
            long requestedFailureId = 0
        )
        {
            ThumbnailRescueWorkerLauncher launcher = ResolveThumbnailRescueWorkerLauncher(
                useDedicatedManualWorkerSlot,
                out string slotLabel,
                out bool manualSlotsBusy
            );
            if (manualSlotsBusy)
            {
                HandleThumbnailRescueWorkerLog(
                    slotLabel,
                    "manual rescue slots are busy."
                );
                return false;
            }

            bool started = launcher.TryStartIfNeeded(
                mainDbFullPath,
                dbName,
                thumbFolder,
                requestedFailureId,
                message => HandleThumbnailRescueWorkerLog(slotLabel, message)
            );
            if (
                started
                && useDedicatedManualWorkerSlot
                && requestedFailureId > 0
                && slotLabel.StartsWith("manual-slot", StringComparison.Ordinal)
            )
            {
                RememberManualThumbnailRescueSlotRequest(slotLabel, requestedFailureId);
            }

            return started;
        }

        // 手動インデックス再構築だけは FailureDb へ積まず、manual slot で直接 worker を起動する。
        private ThumbnailDirectIndexRepairStartResult TryStartThumbnailDirectIndexRepairWorkerDetailed(
            string movieFullPath
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return ThumbnailDirectIndexRepairStartResult.Invalid;
            }

            ThumbnailRescueWorkerLauncher launcher = ResolveThumbnailRescueWorkerLauncher(
                useDedicatedManualWorkerSlot: true,
                out string slotLabel,
                out bool manualSlotsBusy
            );
            if (manualSlotsBusy)
            {
                HandleThumbnailRescueWorkerLog(slotLabel, "manual direct index repair slots are busy.");
                return ThumbnailDirectIndexRepairStartResult.Busy;
            }

            bool started = launcher.TryStartDirectIndexRepair(
                movieFullPath,
                message => HandleThumbnailRescueWorkerLog(slotLabel, message)
            );
            if (started && slotLabel.StartsWith("manual-slot", StringComparison.Ordinal))
            {
                RememberManualThumbnailDirectIndexRepairRequest(slotLabel, movieFullPath);
            }

            return started
                ? ThumbnailDirectIndexRepairStartResult.Started
                : ThumbnailDirectIndexRepairStartResult.Busy;
        }

        private bool TryStartThumbnailDirectIndexRepairWorker(string movieFullPath)
        {
            return TryStartThumbnailDirectIndexRepairWorkerDetailed(movieFullPath)
                == ThumbnailDirectIndexRepairStartResult.Started;
        }

        // 本体終了時は両slotのworkerを止めてから破棄し、別DB向けworkerが残存しないようにする。
        private void DisposeThumbnailRescueWorkerLaunchers()
        {
            CloseManualThumbnailRescueProgress();
            bool stoppedDefault = _thumbnailRescueWorkerLauncher.TryStopRunningWorker(
                message => HandleThumbnailRescueWorkerLog("default-slot", message)
            );
            bool stoppedManual1 = _thumbnailManualRescueWorkerLauncher1.TryStopRunningWorker(
                message => HandleThumbnailRescueWorkerLog("manual-slot-1", message)
            );
            bool stoppedManual2 = _thumbnailManualRescueWorkerLauncher2.TryStopRunningWorker(
                message => HandleThumbnailRescueWorkerLog("manual-slot-2", message)
            );
            if (stoppedDefault || stoppedManual1 || stoppedManual2)
            {
                string currentMainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-worker",
                    $"workers stopped by app shutdown: db='{currentMainDbFullPath}' stopped_default={stoppedDefault} stopped_manual1={stoppedManual1} stopped_manual2={stoppedManual2}"
                );
            }

            _thumbnailRescueWorkerLauncher.Dispose();
            _thumbnailManualRescueWorkerLauncher1.Dispose();
            _thumbnailManualRescueWorkerLauncher2.Dispose();
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
            bool stoppedManual1 = _thumbnailManualRescueWorkerLauncher1.TryStopRunningWorker(
                message => HandleThumbnailRescueWorkerLog("manual-slot-1", message)
            );
            bool stoppedManual2 = _thumbnailManualRescueWorkerLauncher2.TryStopRunningWorker(
                message => HandleThumbnailRescueWorkerLog("manual-slot-2", message)
            );

            if (!stoppedDefault && !stoppedManual1 && !stoppedManual2)
            {
                return;
            }

            DebugRuntimeLog.Write(
                "thumbnail-rescue-worker",
                $"workers stopped by db switch: from='{previousMainDbFullPath}' to='{nextMainDbFullPath}' stopped_default={stoppedDefault} stopped_manual1={stoppedManual1} stopped_manual2={stoppedManual2}"
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
