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
        private static readonly string[] ThumbnailErrorPlaceholderFileNames =
        [
            "errorSmall.jpg",
            "errorBig.jpg",
            "errorGrid.jpg",
            "errorList.jpg",
        ];

        // 明示救済要求は FailureDb へ記録し、実処理は外部 worker に委譲する。
        private bool TryEnqueueThumbnailRescueJob(
            QueueObj queueObj,
            bool requiresIdle,
            string reason
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

            string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(
                rescueQueueObj.MovieFullPath
            );
            string panelSize = ThumbnailRescueTraceLog.BuildPanelSizeLabel(
                rescueQueueObj.Tabindex,
                MainVM?.DbInfo?.DBName ?? "",
                MainVM?.DbInfo?.ThumbFolder ?? ""
            );
            if (failureDbService.HasOpenRescueRequest(moviePathKey, rescueQueueObj.Tabindex))
            {
                _ = TryStartThumbnailRescueWorkerForRequest(requiresIdle, "already-pending");
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-request",
                    $"enqueue skipped duplicated: path='{rescueQueueObj.MovieFullPath}' tab={rescueQueueObj.Tabindex} reason={reason}"
                );
                ThumbnailRescueTraceLog.Write(
                    source: "main",
                    action: "request_enqueued",
                    result: "duplicate",
                    moviePath: rescueQueueObj.MovieFullPath,
                    tabIndex: rescueQueueObj.Tabindex,
                    panelSize: panelSize,
                    phase: "manual_rescue_request",
                    reason: reason ?? ""
                );
                return false;
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
                ExtraJson = BuildThumbnailRescueRequestExtraJson(reason, requiresIdle),
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
            };

            long failureId = failureDbService.AppendFailureRecord(record);
            bool launchRequested = TryStartThumbnailRescueWorkerForRequest(requiresIdle, reason);
            RequestThumbnailErrorSnapshotRefresh();
            RequestThumbnailProgressSnapshotRefresh();

            DebugRuntimeLog.Write(
                "thumbnail-rescue-request",
                $"enqueue accepted: failure_id={failureId} path='{rescueQueueObj.MovieFullPath}' tab={rescueQueueObj.Tabindex} idle_only={requiresIdle} launch_requested={launchRequested} reason={reason}"
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

        // UI 上の ERROR 画像起点でも、入口ごとに即時実行か待機付きかを切り替えて worker へ渡す。
        private bool TryEnqueueThumbnailDisplayErrorRescueJob(
            QueueObj queueObj,
            string reason,
            bool requiresIdle = true
        )
        {
            if (queueObj == null)
            {
                return false;
            }

            string currentDbName = MainVM?.DbInfo?.DBName ?? "";
            string currentThumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            TabInfo targetTabInfo = new(queueObj.Tabindex, currentDbName, currentThumbFolder);
            TryDeleteThumbnailErrorMarker(targetTabInfo.OutPath, queueObj.MovieFullPath);

            return TryEnqueueThumbnailRescueJob(
                queueObj,
                requiresIdle: requiresIdle,
                reason: reason
            );
        }

        // requiresIdle が true の要求だけ、通常キューが空くまで worker 起動を待たせる。
        private bool TryStartThumbnailRescueWorkerForRequest(bool requiresIdle, string reason)
        {
            if (requiresIdle && TryGetCurrentQueueActiveCount(out int activeCount) && activeCount > 0)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-request",
                    $"worker launch deferred: active={activeCount} reason={reason}"
                );
                return false;
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
            bool requiresIdle
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    phase = "manual_rescue_request",
                    reason = reason ?? "",
                    requires_idle = requiresIdle,
                }
            );
        }

        // 組み込みの error 代替画像だけを検出し、通常のパス名に含まれる error 文字列とは分離する。
        internal static bool IsThumbnailErrorPlaceholderPath(string thumbPath)
        {
            if (string.IsNullOrWhiteSpace(thumbPath))
            {
                return false;
            }

            string fileName = Path.GetFileName(thumbPath.Trim());
            for (int i = 0; i < ThumbnailErrorPlaceholderFileNames.Length; i++)
            {
                if (
                    string.Equals(
                        fileName,
                        ThumbnailErrorPlaceholderFileNames[i],
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            return false;
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
