using System.IO;
using System.Text.Json;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private static readonly string[] ThumbnailRescueRepairExtensions =
        [
            ".mp4",
            ".mkv",
            ".avi",
            ".wmv",
            ".asf",
            ".divx",
        ];
        private static readonly string[] ThumbnailRescueRepairErrorKeywords =
        [
            "invalid data found",
            "moov atom not found",
            "video stream is missing",
            "no frames decoded",
            "find stream info failed",
            "stream info failed",
            "failed to open input",
            "avformat_open_input failed",
            "avformat_find_stream_info failed",
        ];
        private static readonly TimeSpan ThumbnailVisibleErrorPreferredDuration = TimeSpan.FromSeconds(
            45
        );

        // 明示救済要求は FailureDb へ記録し、実処理は外部 worker に委譲する。
        private bool TryEnqueueThumbnailRescueJob(
            QueueObj queueObj,
            bool requiresIdle,
            string reason,
            DateTime? priorityUntilUtc = null
        )
        {
            if (queueObj == null || string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return false;
            }

            if (!isThumbnailQueueInputEnabled)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-request",
                    "enqueue skipped: input disabled."
                );
                return false;
            }

            QueueObj rescueQueueObj = CloneQueueObj(queueObj);
            if (TryGetMovieFileLength(rescueQueueObj.MovieFullPath, out long fileLength))
            {
                rescueQueueObj.MovieSizeBytes = fileLength;
            }

            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            if (failureDbService == null)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-request",
                    "enqueue skipped: failure db unavailable."
                );
                return false;
            }

            if (
                TrySkipThumbnailRescueRequestBecauseSuccessExists(
                    rescueQueueObj,
                    failureDbService,
                    reason
                )
            )
            {
                return false;
            }

            int recoveredStaleCount = failureDbService.RecoverExpiredProcessingToPendingRescue(
                DateTime.UtcNow
            );
            if (recoveredStaleCount > 0)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-request",
                    $"stale rescue recovered before enqueue: count={recoveredStaleCount}"
                );
                RequestThumbnailErrorSnapshotRefresh();
                RequestThumbnailProgressSnapshotRefresh();
            }

            string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(
                rescueQueueObj.MovieFullPath
            );
            ThumbnailQueuePriority requestPriority = ThumbnailQueuePriorityHelper.Normalize(
                rescueQueueObj.Priority
            );
            string rescueRequestExtraJson = BuildThumbnailRescueRequestExtraJson(
                reason,
                requiresIdle,
                requestPriority,
                priorityUntilUtc
            );
            string panelSize = ThumbnailRescueTraceLog.BuildPanelSizeLabel(
                rescueQueueObj.Tabindex,
                MainVM?.DbInfo?.DBName ?? "",
                MainVM?.DbInfo?.ThumbFolder ?? ""
            );
            if (failureDbService.HasOpenRescueRequest(moviePathKey, rescueQueueObj.Tabindex))
            {
                int promotedCount = failureDbService.PromotePendingRescueRequest(
                    moviePathKey,
                    rescueQueueObj.Tabindex,
                    requestPriority,
                    DateTime.UtcNow,
                    extraJson: rescueRequestExtraJson,
                    priorityUntilUtc: priorityUntilUtc
                );
                _ = TryStartThumbnailRescueWorkerForRequest(
                    requiresIdle,
                    requestPriority,
                    "already-pending"
                );
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-request",
                    promotedCount > 0
                        ? $"enqueue promoted existing request: path='{rescueQueueObj.MovieFullPath}' tab={rescueQueueObj.Tabindex} priority={requestPriority} reason={reason}"
                        : $"enqueue skipped duplicated: path='{rescueQueueObj.MovieFullPath}' tab={rescueQueueObj.Tabindex} priority={requestPriority} reason={reason}"
                );
                ThumbnailRescueTraceLog.Write(
                    source: "main",
                    action: "request_enqueued",
                    result: promotedCount > 0 ? "promoted" : "duplicate",
                    moviePath: rescueQueueObj.MovieFullPath,
                    tabIndex: rescueQueueObj.Tabindex,
                    panelSize: panelSize,
                    phase: "manual_rescue_request",
                    reason: reason ?? ""
                );
                return promotedCount > 0;
            }

            DateTime nowUtc = DateTime.UtcNow;
            ThumbnailFailureRecord record = new()
            {
                MainDbFullPath = failureDbService.MainDbFullPath,
                MainDbPathHash = failureDbService.MainDbPathHash,
                MoviePath = rescueQueueObj.MovieFullPath,
                MoviePathKey = moviePathKey,
                TabIndex = rescueQueueObj.Tabindex,
                Lane = ResolveThumbnailRescueLaneName(rescueQueueObj),
                AttemptGroupId = "",
                AttemptNo = 1,
                Status = "pending_rescue",
                LeaseOwner = "",
                LeaseUntilUtc = "",
                Engine = "",
                FailureKind = ThumbnailFailureKind.Unknown,
                FailureReason = reason ?? "",
                ElapsedMs = 0,
                SourcePath = rescueQueueObj.MovieFullPath,
                OutputThumbPath = "",
                RepairApplied = false,
                ResultSignature = "manual-rescue-request",
                ExtraJson = rescueRequestExtraJson,
                Priority = requestPriority,
                PriorityUntilUtc = BuildPriorityUntilUtcText(requestPriority, priorityUntilUtc),
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
            };

            long failureId = failureDbService.AppendFailureRecord(record);
            bool launchRequested = TryStartThumbnailRescueWorkerForRequest(
                requiresIdle,
                requestPriority,
                reason
            );
            RequestThumbnailErrorSnapshotRefresh();
            RequestThumbnailProgressSnapshotRefresh();

            DebugRuntimeLog.Write(
                "thumbnail-rescue-request",
                $"enqueue accepted: failure_id={failureId} path='{rescueQueueObj.MovieFullPath}' tab={rescueQueueObj.Tabindex} priority={requestPriority} idle_only={requiresIdle} launch_requested={launchRequested} reason={reason}"
            );
            ThumbnailRescueTraceLog.Write(
                source: "main",
                action: "request_enqueued",
                result: "accepted",
                failureId: failureId,
                moviePath: rescueQueueObj.MovieFullPath,
                tabIndex: rescueQueueObj.Tabindex,
                panelSize: panelSize,
                phase: "manual_rescue_request",
                reason:
                    $"reason={reason ?? ""}; idle_only={requiresIdle}; launch_requested={launchRequested}"
            );
            return true;
        }

        // 既に正常jpgがある個体を再救済すると無駄な pending_rescue が増えるため、入口で即座に掃除する。
        private bool TrySkipThumbnailRescueRequestBecauseSuccessExists(
            QueueObj rescueQueueObj,
            ThumbnailFailureDbService failureDbService,
            string reason
        )
        {
            if (rescueQueueObj == null || failureDbService == null)
            {
                return false;
            }

            string dbName = MainVM?.DbInfo?.DBName ?? "";
            string thumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            string thumbOutPath = ResolveThumbnailOutPath(
                rescueQueueObj.Tabindex,
                dbName,
                thumbFolder
            );
            if (
                ShouldCreateErrorMarkerForSkippedMovie(
                    thumbOutPath,
                    rescueQueueObj.MovieFullPath,
                    out string existingSuccessThumbnailPath
                )
            )
            {
                return false;
            }

            string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(
                rescueQueueObj.MovieFullPath
            );
            int deletedFailureCount = failureDbService.DeleteMainFailureRecords(
            [
                (moviePathKey, rescueQueueObj.Tabindex),
            ]
            );
            bool deletedMarker = TryDeleteThumbnailErrorMarker(
                thumbOutPath,
                rescueQueueObj.MovieFullPath
            );

            DebugRuntimeLog.Write(
                "thumbnail-rescue-request",
                $"enqueue skipped: success thumbnail already exists. movie='{rescueQueueObj.MovieFullPath}' tab={rescueQueueObj.Tabindex} reason={reason} success='{existingSuccessThumbnailPath}' deleted_failure={deletedFailureCount} deleted_marker={deletedMarker}"
            );
            ThumbnailRescueTraceLog.Write(
                source: "main",
                action: "request_enqueued",
                result: "skipped_existing_success",
                moviePath: rescueQueueObj.MovieFullPath,
                tabIndex: rescueQueueObj.Tabindex,
                panelSize: ThumbnailRescueTraceLog.BuildPanelSizeLabel(
                    rescueQueueObj.Tabindex,
                    dbName,
                    thumbFolder
                ),
                phase: "manual_rescue_request",
                reason: $"reason={reason ?? ""}; success={existingSuccessThumbnailPath}"
            );

            RequestThumbnailErrorSnapshotRefresh();
            RequestThumbnailProgressSnapshotRefresh();
            return true;
        }

        // UI 上の ERROR 画像起点でも、入口ごとに即時実行か待機付きかを切り替えて worker へ渡す。
        private bool TryEnqueueThumbnailDisplayErrorRescueJob(
            QueueObj queueObj,
            string reason,
            bool requiresIdle = true,
            DateTime? priorityUntilUtc = null
        )
        {
            if (queueObj == null)
            {
                return false;
            }

            string currentDbName = MainVM?.DbInfo?.DBName ?? "";
            string currentThumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            TryDeleteThumbnailErrorMarker(
                ResolveThumbnailOutPath(queueObj.Tabindex, currentDbName, currentThumbFolder),
                queueObj.MovieFullPath
            );

            return TryEnqueueThumbnailRescueJob(
                queueObj,
                requiresIdle: requiresIdle,
                reason: reason,
                priorityUntilUtc: priorityUntilUtc
            );
        }

        // 優先 rescue は、通常キュー稼働中でも起動待機を越えられるようにする。
        private bool TryStartThumbnailRescueWorkerForRequest(
            bool requiresIdle,
            ThumbnailQueuePriority priority,
            string reason
        )
        {
            ThumbnailQueuePriority normalizedPriority = ThumbnailQueuePriorityHelper.Normalize(
                priority
            );
            bool hasActiveCount = TryGetCurrentQueueActiveCount(out int activeCount);
            if (
                hasActiveCount
                && ShouldDeferThumbnailRescueWorkerLaunch(
                    requiresIdle,
                    normalizedPriority,
                    activeCount
                )
            )
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-request",
                    $"worker launch deferred: active={activeCount} priority={normalizedPriority} reason={reason}"
                );
                return false;
            }

            if (
                hasActiveCount
                && activeCount > 0
                && requiresIdle
                && ThumbnailQueuePriorityHelper.IsPreferred(normalizedPriority)
            )
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-request",
                    $"worker launch prioritized: active={activeCount} priority={normalizedPriority} reason={reason}"
                );
            }

            string mainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            string thumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            return _thumbnailRescueWorkerLauncher.TryStartIfNeeded(
                mainDbFullPath,
                dbName,
                thumbFolder,
                message => DebugRuntimeLog.Write("thumbnail-rescue-worker", message)
            );
        }

        // 通常救済だけ idle 待ちを守り、優先救済は開始判定を前へ出す。
        internal static bool ShouldDeferThumbnailRescueWorkerLaunch(
            bool requiresIdle,
            ThumbnailQueuePriority priority,
            int activeCount
        )
        {
            if (!requiresIdle || activeCount < 1)
            {
                return false;
            }

            return !ThumbnailQueuePriorityHelper.IsPreferred(priority);
        }

        private static string ResolveThumbnailRescueLaneName(QueueObj queueObj)
        {
            int configuredSlowLaneMinGb =
                IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb;
            int slowLaneMinGb = Math.Clamp(configuredSlowLaneMinGb, 1, 1024);
            long slowLaneMinBytes = slowLaneMinGb * 1024L * 1024L * 1024L;
            long movieSizeBytes = queueObj?.MovieSizeBytes ?? 0;
            return movieSizeBytes >= slowLaneMinBytes ? "slow" : "normal";
        }

        private static string BuildThumbnailRescueRequestExtraJson(
            string reason,
            bool requiresIdle,
            ThumbnailQueuePriority priority,
            DateTime? priorityUntilUtc
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    phase = "manual_rescue_request",
                    reason = reason ?? "",
                    requires_idle = requiresIdle,
                    priority = ThumbnailQueuePriorityHelper.IsPreferred(priority)
                        ? "preferred"
                        : "normal",
                    priority_until_utc = BuildPriorityUntilUtcText(priority, priorityUntilUtc),
                    launch_wait_policy = BuildThumbnailRescueLaunchWaitPolicy(
                        priority,
                        requiresIdle
                    ),
                }
            );
        }

        // 進捗タブでも読めるよう、救済要求の開始待機ポリシーを文字列で残す。
        private static string BuildThumbnailRescueLaunchWaitPolicy(
            ThumbnailQueuePriority priority,
            bool requiresIdle
        )
        {
            if (!requiresIdle)
            {
                return "immediate";
            }

            return ThumbnailQueuePriorityHelper.IsPreferred(priority)
                ? "preferred-bypass"
                : "wait-idle";
        }

        private static string BuildPriorityUntilUtcText(
            ThumbnailQueuePriority priority,
            DateTime? priorityUntilUtc
        )
        {
            if (
                !ThumbnailQueuePriorityHelper.IsPreferred(priority)
                || !priorityUntilUtc.HasValue
            )
            {
                return "";
            }

            return priorityUntilUtc.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }

        // 組み込みの error 代替画像だけを検出し、通常のパス名に含まれる error 文字列とは分離する。
        internal static bool IsThumbnailErrorPlaceholderPath(string thumbPath)
        {
            return ThumbnailErrorPlaceholderHelper.IsPlaceholderPath(thumbPath);
        }

        // 明示救済前に stale な失敗固定マーカーだけを消し、再救済を妨げないようにする。
        private bool TryDeleteThumbnailErrorMarker(string thumbOutPath, string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(thumbOutPath) || string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            try
            {
                string errorMarkerPath = Thumbnail.ThumbnailPathResolver.BuildErrorMarkerPath(
                    thumbOutPath,
                    movieFullPath
                );
                if (!Path.Exists(errorMarkerPath))
                {
                    return false;
                }

                File.Delete(errorMarkerPath);
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-request",
                    $"deleted stale error marker: '{errorMarkerPath}'"
                );
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-request",
                    $"delete error marker failed: movie='{movieFullPath}' reason='{ex.Message}'"
                );
                return false;
            }
        }

        // repair の対象拡張子判定だけは共有 helper として残す。
        internal static bool CanTryThumbnailIndexRepair(string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            string extension = Path.GetExtension(movieFullPath ?? "");
            return ThumbnailRescueRepairExtensions.Contains(
                extension,
                StringComparer.OrdinalIgnoreCase
            );
        }

        // 失敗文言ベースの repair 候補判定は worker と同じ粗い規則を共有する。
        internal static bool ShouldTryThumbnailIndexRepair(
            string movieFullPath,
            string failureReason
        )
        {
            if (!CanTryThumbnailIndexRepair(movieFullPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(failureReason))
            {
                return false;
            }

            string normalizedReason = failureReason.Trim().ToLowerInvariant();
            for (int i = 0; i < ThumbnailRescueRepairErrorKeywords.Length; i++)
            {
                if (normalizedReason.Contains(ThumbnailRescueRepairErrorKeywords[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
