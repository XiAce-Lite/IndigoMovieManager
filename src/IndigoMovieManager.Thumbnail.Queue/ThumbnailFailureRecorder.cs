using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Queue失敗時の状態更新とFailureDb追記をまとめて扱う。
    /// </summary>
    public static class ThumbnailFailureRecorder
    {
        private const int DefaultMaxAttemptCount = 1;
        private static readonly ConcurrentDictionary<string, ThumbnailFailureDbService> FailureDbServiceCache =
            new(StringComparer.OrdinalIgnoreCase);

        public static void HandleFailedItem(
            QueueDbService queueDbService,
            QueueDbLeaseItem leasedItem,
            string ownerInstanceId,
            Exception ex,
            string laneName,
            Action<string> log
        )
        {
            bool exceeded = leasedItem.AttemptCount + 1 >= DefaultMaxAttemptCount;
            bool missingFile =
                string.IsNullOrWhiteSpace(leasedItem.MoviePath)
                || !Path.Exists(leasedItem.MoviePath);
            bool retryable = !exceeded && !missingFile;
            ThumbnailQueueStatus nextStatus = retryable
                ? ThumbnailQueueStatus.Pending
                : ThumbnailQueueStatus.Failed;
            long failedTotal = ThumbnailQueueMetrics.RecordFailed();

            int updated = queueDbService.UpdateStatus(
                leasedItem.QueueId,
                ownerInstanceId,
                nextStatus,
                DateTime.UtcNow,
                ex.Message,
                incrementAttemptCount: retryable
            );

            if (updated < 1)
            {
                log?.Invoke(
                    $"consumer status skipped: queue_id={leasedItem.QueueId} next={nextStatus} message={ex.Message}"
                );
                return;
            }

            if (failedTotal <= 20 || failedTotal % 50 == 0)
            {
                log?.Invoke(
                    $"consumer failed: queue_id={leasedItem.QueueId} next={nextStatus} "
                        + $"retryable={retryable} failed_total={failedTotal}"
                );
            }

            if (nextStatus == ThumbnailQueueStatus.Failed)
            {
                TryAppendTerminalFailureRecord(
                    queueDbService,
                    leasedItem,
                    ownerInstanceId,
                    ex,
                    laneName,
                    log
                );
            }
        }

        // terminal failure は救済workerへ渡す親行だけを記録する。
        private static void TryAppendTerminalFailureRecord(
            QueueDbService queueDbService,
            QueueDbLeaseItem leasedItem,
            string ownerInstanceId,
            Exception ex,
            string laneName,
            Action<string> log
        )
        {
            try
            {
                ThumbnailFailureDbService failureDbService = FailureDbServiceCache.GetOrAdd(
                    queueDbService.MainDbFullPath,
                    key => new ThumbnailFailureDbService(key)
                );
                DateTime nowUtc = DateTime.UtcNow;
                string moviePath = leasedItem?.MoviePath ?? "";
                string lane = ThumbnailRescueHandoffPolicy.NormalizeMainLaneName(laneName);
                string handoffType = ThumbnailRescueHandoffPolicy.ResolveHandoffType(ex);
                ThumbnailFailureKind failureKind =
                    ThumbnailRescueHandoffPolicy.ResolveFailureKind(ex, moviePath);

                long failureId = failureDbService.AppendFailureRecord(
                    new ThumbnailFailureRecord
                    {
                        MainDbFullPath = queueDbService.MainDbFullPath,
                        MainDbPathHash = queueDbService.MainDbPathHash,
                        MoviePath = moviePath,
                        MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = leasedItem?.TabIndex ?? 0,
                        Lane = lane,
                        AttemptGroupId = "",
                        AttemptNo = Math.Max(1, (leasedItem?.AttemptCount ?? 0) + 1),
                        Status = "pending_rescue",
                        LeaseOwner = "",
                        Engine = "",
                        FailureKind = failureKind,
                        FailureReason = ex?.Message ?? "",
                        ElapsedMs = 0,
                        SourcePath = moviePath,
                        RepairApplied = false,
                        ResultSignature = "unknown",
                        ExtraJson = BuildFailureExtraJson(
                            leasedItem,
                            ex,
                            ownerInstanceId,
                            handoffType,
                            failureKind
                        ),
                        CreatedAtUtc = nowUtc,
                        UpdatedAtUtc = nowUtc,
                    }
                );
                log?.Invoke(
                    $"failuredb append: queue_id={leasedItem?.QueueId} failure_id={failureId} status=pending_rescue handoff={handoffType} failure_kind={failureKind} lane={lane}"
                );
            }
            catch (Exception appendEx)
            {
                log?.Invoke(
                    $"failuredb append failed: queue_id={leasedItem?.QueueId} message={appendEx.Message}"
                );
            }
        }

        private static string BuildFailureExtraJson(
            QueueDbLeaseItem leasedItem,
            Exception ex,
            string ownerInstanceId,
            string handoffType,
            ThumbnailFailureKind failureKind
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    QueueId = leasedItem?.QueueId ?? 0,
                    QueueAttemptCount = leasedItem?.AttemptCount ?? 0,
                    QueueStatus = ThumbnailQueueStatus.Failed.ToString(),
                    ExceptionType = ex?.GetType().FullName ?? "",
                    OwnerInstanceId = leasedItem?.OwnerInstanceId ?? "",
                    FailureRecordCreatedBy = ownerInstanceId ?? "",
                    HandoffType = handoffType ?? "",
                    FailureKind = failureKind.ToString(),
                    WorkerRole = "normal",
                }
            );
        }
    }
}
