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
                string lane = ResolveFailureLaneName(leasedItem);

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
                        FailureKind = ResolveFailureKind(ex, moviePath),
                        FailureReason = ex?.Message ?? "",
                        ElapsedMs = 0,
                        SourcePath = moviePath,
                        RepairApplied = false,
                        ResultSignature = "unknown",
                        ExtraJson = BuildFailureExtraJson(leasedItem, ex, ownerInstanceId),
                        CreatedAtUtc = nowUtc,
                        UpdatedAtUtc = nowUtc,
                    }
                );
                log?.Invoke(
                    $"failuredb append: queue_id={leasedItem?.QueueId} failure_id={failureId} status=pending_rescue lane={lane}"
                );
            }
            catch (Exception appendEx)
            {
                log?.Invoke(
                    $"failuredb append failed: queue_id={leasedItem?.QueueId} message={appendEx.Message}"
                );
            }
        }

        private static string ResolveFailureLaneName(QueueDbLeaseItem leasedItem)
        {
            // terminal failure の lane 記録は Phase 4 で normal/slow の2値へ固定する。
            ThumbnailExecutionLane lane = ThumbnailLaneClassifier.ResolveLane(
                leasedItem?.MovieSizeBytes ?? 0
            );
            return lane switch
            {
                ThumbnailExecutionLane.Slow => "slow",
                _ => "normal",
            };
        }

        private static ThumbnailFailureKind ResolveFailureKind(Exception ex, string moviePath)
        {
            if (ex is TimeoutException)
            {
                return ThumbnailFailureKind.HangSuspected;
            }

            if (ex is FileNotFoundException)
            {
                return ThumbnailFailureKind.FileMissing;
            }

            if (!string.IsNullOrWhiteSpace(moviePath))
            {
                try
                {
                    if (File.Exists(moviePath))
                    {
                        if (new FileInfo(moviePath).Length <= 0)
                        {
                            return ThumbnailFailureKind.ZeroByteFile;
                        }
                    }
                    else
                    {
                        return ThumbnailFailureKind.FileMissing;
                    }
                }
                catch
                {
                    // ファイル状態の追加判定に失敗しても文言判定へ進む。
                }
            }

            string normalized = (ex?.Message ?? "").Trim().ToLowerInvariant();
            if (normalized.Contains("drm"))
            {
                return ThumbnailFailureKind.DrmProtected;
            }
            if (normalized.Contains("unsupported codec"))
            {
                return ThumbnailFailureKind.UnsupportedCodec;
            }
            if (
                normalized.Contains("moov atom not found")
                || normalized.Contains("invalid data found")
                || normalized.Contains("find stream info failed")
                || normalized.Contains("avformat_open_input failed")
                || normalized.Contains("avformat_find_stream_info failed")
            )
            {
                return ThumbnailFailureKind.IndexCorruption;
            }
            if (
                normalized.Contains("video stream is missing")
                || normalized.Contains("no video stream")
                || normalized.Contains("video stream not found")
            )
            {
                return ThumbnailFailureKind.NoVideoStream;
            }
            if (normalized.Contains("no frames decoded"))
            {
                return ThumbnailFailureKind.TransientDecodeFailure;
            }
            if (
                normalized.Contains("being used by another process")
                || normalized.Contains("file is locked")
                || normalized.Contains("locked")
            )
            {
                return ThumbnailFailureKind.FileLocked;
            }

            return ThumbnailFailureKind.Unknown;
        }

        private static string BuildFailureExtraJson(
            QueueDbLeaseItem leasedItem,
            Exception ex,
            string ownerInstanceId
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
                    WorkerRole = "normal",
                }
            );
        }
    }
}
