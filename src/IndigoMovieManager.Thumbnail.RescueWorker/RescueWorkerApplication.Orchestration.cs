using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
        // RunAsync は mode 判定だけ持ち、本線の lease / heartbeat / 完了処理はここへ寄せる。
        private static async Task<int> RunMainRescueAsync(
            string mainDbFullPath,
            string thumbFolderOverride,
            string logDirectoryPath,
            string failureDbDirectoryPath,
            long requestedFailureId
        )
        {
            ThumbnailQueueHostPathPolicy.Configure(
                failureDbDirectoryPath: failureDbDirectoryPath,
                logDirectoryPath: logDirectoryPath
            );
            if (!string.IsNullOrWhiteSpace(logDirectoryPath))
            {
                ThumbnailRescueTraceLog.ConfigureLogDirectory(logDirectoryPath);
            }
            if (!File.Exists(mainDbFullPath))
            {
                Console.Error.WriteLine($"main db not found: {mainDbFullPath}");
                return 2;
            }
            ThumbnailFailureDbService failureDbService = new(mainDbFullPath);
            string leaseOwner = $"rescue-{Environment.ProcessId}-{Guid.NewGuid():N}";
            DateTime nowUtc = DateTime.UtcNow;
            ThumbnailFailureRecord leasedRecord = requestedFailureId > 0
                ? failureDbService.GetPendingRescueAndLeaseById(
                    requestedFailureId,
                    leaseOwner,
                    TimeSpan.FromMinutes(LeaseMinutes),
                    nowUtc
                )
                : failureDbService.GetPendingRescueAndLease(
                    leaseOwner,
                    TimeSpan.FromMinutes(LeaseMinutes),
                    nowUtc
                );
            if (leasedRecord == null)
            {
                Console.WriteLine("rescue queue empty");
                return 0;
            }
            Console.WriteLine(
                $"rescue leased: failure_id={leasedRecord.FailureId} movie='{leasedRecord.MoviePath}' priority={leasedRecord.Priority}"
            );
            WriteRescueTrace(
                leasedRecord,
                dbName: "",
                thumbFolder: "",
                action: "worker_leased",
                result: "leased"
            );
            Console.WriteLine(
                $"rescue timeout config: engine_sec={ResolveEngineAttemptTimeout().TotalSeconds:0} opencv_sec={ResolveEngineAttemptTimeout("opencv").TotalSeconds:0} probe_sec={ResolveRepairProbeTimeout().TotalSeconds:0} repair_sec={ResolveRepairTimeout().TotalSeconds:0}"
            );
            using CancellationTokenSource heartbeatCts = new();
            Task heartbeatTask = RunLeaseHeartbeatAsync(
                failureDbService,
                leasedRecord.FailureId,
                leaseOwner,
                heartbeatCts.Token
            );
            try
            {
                await ProcessLeasedRecordAsync(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        thumbFolderOverride,
                        logDirectoryPath
                    )
                    .ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"rescue worker failed: {ex.Message}");
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "gave_up",
                    DateTime.UtcNow,
                    extraJson: BuildTerminalExtraJson("worker_exception", "", false, ex.Message),
                    clearLease: true,
                    failureKind: ResolveFailureKind(ex, leasedRecord.MoviePath),
                    failureReason: ex.Message
                );
                return 1;
            }
            finally
            {
                heartbeatCts.Cancel();
                try
                {
                    await heartbeatTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // heartbeat停止時のキャンセルは正常系として握る。
                }
            }
        }
        private static async Task RunLeaseHeartbeatAsync(
            ThumbnailFailureDbService failureDbService,
            long failureId,
            string leaseOwner,
            CancellationToken cts
        )
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(LeaseHeartbeatSeconds));
            while (await timer.WaitForNextTickAsync(cts).ConfigureAwait(false))
            {
                DateTime nowUtc = DateTime.UtcNow;
                DateTime leaseUntilUtc = nowUtc.AddMinutes(LeaseMinutes);
                failureDbService.ExtendLease(
                    failureId,
                    leaseOwner,
                    leaseUntilUtc,
                    nowUtc
                );
                Console.WriteLine(
                    $"lease heartbeat: failure_id={failureId} lease_until_utc={leaseUntilUtc:O}"
                );
            }
        }
    }
}
