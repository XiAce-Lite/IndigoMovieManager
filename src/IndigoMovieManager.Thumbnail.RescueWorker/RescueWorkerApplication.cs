using System.Drawing;
using System.Drawing.Imaging;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    // 救済exeは1回起動で1本だけ掴み、最後までやり切る。
    internal sealed class RescueWorkerApplication
    {
        private const string AttemptChildModeArg = "--attempt-child";
        private const int LeaseMinutes = 5;
        private const int LeaseHeartbeatSeconds = 60;
        private const string EngineAttemptTimeoutSecEnvName = "IMM_THUMB_RESCUE_ENGINE_TIMEOUT_SEC";
        private const string OpenCvAttemptTimeoutSecEnvName =
            "IMM_THUMB_RESCUE_OPENCV_TIMEOUT_SEC";
        private const string RepairProbeTimeoutSecEnvName =
            "IMM_THUMB_RESCUE_REPAIR_PROBE_TIMEOUT_SEC";
        private const string RepairTimeoutSecEnvName = "IMM_THUMB_RESCUE_REPAIR_TIMEOUT_SEC";
        private const int DefaultEngineAttemptTimeoutSec = 120;
        private const int DefaultOpenCvAttemptTimeoutSec = 300;
        private const int DefaultRepairProbeTimeoutSec = 45;
        private const int DefaultRepairTimeoutSec = 300;
        private const int ExperimentalFinalSeekRescueTimeoutSec = 300;
        private const int ExperimentalFinalSeekSampleCount = 12;
        private const int ExperimentalFinalSeekScaleWidth = 320;
        private const double NearBlackThumbnailLumaThreshold = 2d;
        private const int NearBlackThumbnailSampleStep = 4;
        private const long UltraShortMaxMovieSizeBytes = 4L * 1024L * 1024L;
        private const double UltraShortDecimalRetryDurationThresholdSec = 1d;
        private const double UltraShortDecimalRetryFallbackDurationSec = 0.2d;
        private const double LongDurationNearBlackVirtualRetryThresholdSec = 2d * 60d * 60d;
        private const string DecimalNearBlackRetryEngineId = "black-retry-decimal-ffmpeg";
        private const string ExperimentalFinalSeekRescueEngineId = "final-seek-ffmpeg";
        private const string FixedRouteId = "fixed";
        private const string UnclassifiedSymptomClass = "unclassified";
        private const string LongNoFramesRouteId = "route-long-no-frames";
        private const string UltraShortNoFramesRouteId = "route-ultra-short-no-frames";
        private const string NearBlackOrOldFrameRouteId = "route-near-black-or-old-frame";
        private const string CorruptOrPartialRouteId = "route-corrupt-or-partial";
        private const string LongNoFramesSymptomClass = "long-no-frames";
        private const string UltraShortNoFramesSymptomClass = "ultra-short-no-frames";
        private const string NearBlackOrOldFrameSymptomClass = "near-black-or-old-frame";
        private const string CorruptOrPartialSymptomClass = "corrupt-or-partial";
        private static readonly string[] FixedDirectEngineOrder =
        [
            "ffmpeg1pass",
            "ffmediatoolkit",
            "autogen",
            "opencv",
        ];
        private static readonly string[] LongNoFramesDirectEngineOrder = ["ffmpeg1pass", "ffmediatoolkit"];
        private static readonly string[] LongNoFramesRepairEngineOrder =
        [
            "ffmpeg1pass",
            "ffmediatoolkit",
            "autogen",
            "opencv",
        ];
        private static readonly string[] UltraShortDirectEngineOrder =
        [
            "autogen",
            "ffmpeg1pass",
            "ffmediatoolkit",
            "opencv",
        ];
        private static readonly string[] NearBlackDirectEngineOrder =
        [
            "autogen",
            "ffmpeg1pass",
            "ffmediatoolkit",
        ];
        private static readonly string[] CorruptOrPartialDirectEngineOrder = ["ffmpeg1pass"];
        private static readonly string[] CorruptOrPartialRepairEngineOrder =
        [
            "ffmpeg1pass",
            "ffmediatoolkit",
            "autogen",
            "opencv",
        ];
        private static readonly string[] RepairExtensions =
        [
            ".mp4",
            ".mkv",
            ".avi",
            ".wmv",
            ".asf",
            ".divx",
        ];
        private static readonly string[] ForcedRepairAfterProbeNegativeExtensions =
        [
            ".avi",
            ".wmv",
            ".asf",
        ];
        private static readonly string[] RepairErrorKeywords =
        [
            "invalid data found",
            "moov atom not found",
            "video stream is missing",
            "frame decode failed",
            "no frames decoded",
            "find stream info failed",
            "stream info failed",
            "failed to open input",
            "avformat_open_input failed",
            "avformat_find_stream_info failed",
        ];
        private static readonly string[] CorruptionClassificationKeywords =
        [
            "invalid data found",
            "moov atom not found",
            "frame decode failed",
            "find stream info failed",
            "stream info failed",
            "partial file",
            "corrupt",
            "broken index",
        ];
        private static readonly string[] NearBlackClassificationKeywords =
        [
            "near-black",
            "near black",
            "black frame",
            "too dark",
            "dark frame",
            "old frame",
        ];
        private static readonly string[] LongNoFramesClassificationKeywords =
        [
            "no frames decoded",
            "ffmpeg one-pass failed",
            "thumbnail normal lane timeout",
            "engine attempt timeout",
        ];
        private static readonly string[] LongNoFramesRepairKeywords =
        [
            "no frames decoded",
            "ffmpeg one-pass failed",
            "thumbnail normal lane timeout",
            "engine attempt timeout",
        ];
        private static readonly double[] NearBlackRetryRatios = [0.10d, 0.35d, 0.65d, 0.85d];
        private static readonly double[] UltraShortNearBlackRetryRatios =
            [0.10d, 0.25d, 0.50d, 0.75d, 0.90d];
        private static readonly double[] LongDurationVirtualDurationDivisors = [2d, 3d, 4d];

        public async Task<int> RunAsync(string[] args)
        {
            string initialLogDirectoryPath = ResolveLogDirectoryPathFromArgs(args);
            string initialFailureDbDirectoryPath = ResolveFailureDbDirectoryPathFromArgs(args);
            ThumbnailQueueHostPathPolicy.Configure(
                failureDbDirectoryPath: initialFailureDbDirectoryPath,
                logDirectoryPath: initialLogDirectoryPath
            );
            if (!string.IsNullOrWhiteSpace(initialLogDirectoryPath))
            {
                // host 側から渡された log dir を使い、worker が app 固有 path policy を持たないようにする。
                ThumbnailRescueTraceLog.ConfigureLogDirectory(initialLogDirectoryPath);
            }

            if (HasArgument(args, AttemptChildModeArg))
            {
                if (!TryParseIsolatedAttemptArguments(args, out IsolatedEngineAttemptRequest attemptRequest))
                {
                    Console.Error.WriteLine(
                        "usage: IndigoMovieManager.Thumbnail.RescueWorker --attempt-child --engine <id> --movie <path> --db-name <name> --thumb-folder <path> --tab-index <index> --movie-size-bytes <size> --result-json <path> [--source-movie <path>] [--log-dir <path>]"
                    );
                    return 2;
                }

                return await RunIsolatedAttemptChildAsync(attemptRequest).ConfigureAwait(false);
            }

            if (
                !TryParseArguments(
                    args,
                    out string mainDbFullPath,
                    out string thumbFolderOverride,
                    out string logDirectoryPath,
                    out string failureDbDirectoryPath
                )
            )
            {
                Console.Error.WriteLine(
                    "usage: IndigoMovieManager.Thumbnail.RescueWorker --main-db <path> [--thumb-folder <path>] [--log-dir <path>] [--failure-db-dir <path>]"
                );
                return 2;
            }

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
            ThumbnailFailureRecord leasedRecord = failureDbService.GetPendingRescueAndLease(
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

        private static async Task ProcessLeasedRecordAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            string thumbFolderOverride,
            string logDirectoryPath
        )
        {
            string moviePath = leasedRecord.MoviePath ?? "";
            if (string.IsNullOrWhiteSpace(moviePath) || !File.Exists(moviePath))
            {
                Console.WriteLine(
                    $"rescue skipped: failure_id={leasedRecord.FailureId} reason='movie file not found'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    dbName: "",
                    thumbFolder: "",
                    action: "terminal",
                    result: "skipped",
                    phase: "missing_movie",
                    failureKind: ThumbnailFailureKind.FileMissing,
                    reason: "movie file not found"
                );
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "skipped",
                    DateTime.UtcNow,
                    extraJson: BuildTerminalExtraJson("missing_movie", "", false, "movie file not found"),
                    clearLease: true,
                    failureKind: ThumbnailFailureKind.FileMissing,
                    failureReason: "movie file not found"
                );
                return;
            }

            MainDbContext mainDbContext = ResolveMainDbContext(
                leasedRecord.MainDbFullPath,
                thumbFolderOverride
            );
            if (
                TryFindExistingSuccessThumbnailPath(
                    mainDbContext.ThumbFolder,
                    leasedRecord.TabIndex,
                    moviePath,
                    out string existingSuccessThumbnailPath
                )
            )
            {
                DeleteStaleErrorMarker(mainDbContext.ThumbFolder, leasedRecord.TabIndex, moviePath);
                Console.WriteLine(
                    $"rescue skipped: failure_id={leasedRecord.FailureId} reason='success thumbnail already exists'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "terminal",
                    result: "skipped",
                    phase: "existing_success",
                    outputPath: existingSuccessThumbnailPath,
                    reason: "success thumbnail already exists"
                );
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "skipped",
                    DateTime.UtcNow,
                    outputThumbPath: existingSuccessThumbnailPath,
                    resultSignature: "skipped:existing_success",
                    extraJson: BuildTerminalExtraJson(
                        "existing_success",
                        "",
                        false,
                        "success thumbnail already exists"
                    ),
                    clearLease: true,
                    failureReason: "success thumbnail already exists"
                );
                return;
            }

            VideoIndexRepairService repairService = new();
            VideoIndexProbeResult containerProbeResult = await TryProbeContainerAsync(
                    repairService,
                    leasedRecord,
                    mainDbContext,
                    moviePath
                )
                .ConfigureAwait(false);
            if (IsDefinitiveNoVideoStreamProbeResult(containerProbeResult))
            {
                string noVideoReason = BuildNoVideoStreamProbeReason(containerProbeResult);
                Console.WriteLine(
                    $"rescue gave up: failure_id={leasedRecord.FailureId} phase=container_probe reason='{noVideoReason}'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "terminal",
                    result: "gave_up",
                    phase: "container_probe",
                    failureKind: ThumbnailFailureKind.NoVideoStream,
                    reason: noVideoReason
                );
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "gave_up",
                    DateTime.UtcNow,
                    extraJson: BuildTerminalExtraJson(
                        "container_probe",
                        "",
                        false,
                        noVideoReason
                    ),
                    clearLease: true,
                    failureKind: ThumbnailFailureKind.NoVideoStream,
                    failureReason: noVideoReason
                );
                return;
            }

            DeleteStaleErrorMarker(mainDbContext.ThumbFolder, leasedRecord.TabIndex, moviePath);

            QueueObj queueObj = new()
            {
                MovieFullPath = moviePath,
                MovieSizeBytes = TryGetMovieFileLength(moviePath),
                Tabindex = leasedRecord.TabIndex,
            };
            // 親失敗の軽量情報だけで route を切り、分からない個体だけ fixed へ逃がす。
            string symptomClass = ClassifyRescueSymptom(
                leasedRecord.FailureKind,
                leasedRecord.FailureReason,
                queueObj.MovieSizeBytes,
                moviePath
            );
            RescueExecutionPlan rescuePlan = BuildRescuePlan(symptomClass);
            Console.WriteLine(
                $"rescue plan selected: failure_id={leasedRecord.FailureId} route={rescuePlan.RouteId} symptom={rescuePlan.SymptomClass} direct={string.Join(">", rescuePlan.DirectEngineOrder)} repair={rescuePlan.UseRepairAfterDirect}"
            );
            WriteRescueTrace(
                leasedRecord,
                mainDbContext.DbName,
                mainDbContext.ThumbFolder,
                action: "plan_selected",
                result: "selected",
                routeId: rescuePlan.RouteId,
                symptomClass: rescuePlan.SymptomClass,
                phase: "direct"
            );

            IThumbnailCreationService thumbnailCreationService =
                RescueWorkerThumbnailCreationServiceFactory.Create(logDirectoryPath);
            int nextAttemptNo = Math.Max(leasedRecord.AttemptNo + 1, 2);
            UpdateProgressSnapshot(
                failureDbService,
                leasedRecord,
                leaseOwner,
                phase: "direct_start",
                engineId: "",
                repairApplied: false,
                detail: "",
                attemptNo: nextAttemptNo,
                routeId: rescuePlan.RouteId,
                symptomClass: rescuePlan.SymptomClass,
                sourceMovieFullPath: moviePath,
                currentFailureKind: ThumbnailFailureKind.None,
                currentFailureReason: ""
            );

            RescueAttemptResult directResult = await RunEngineAttemptsAsync(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    queueObj,
                    thumbnailCreationService,
                    mainDbContext,
                    repairApplied: false,
                    rescuePlan: rescuePlan,
                    engineOrder: rescuePlan.DirectEngineOrder,
                    sourceMovieFullPathOverride: null,
                    nextAttemptNo: nextAttemptNo,
                    logDirectoryPath: logDirectoryPath
                )
                .ConfigureAwait(false);
            rescuePlan = directResult.EffectiveRescuePlan;
            nextAttemptNo = directResult.NextAttemptNo;
            ThumbnailFailureKind repairTriggerFailureKind = directResult.LastFailureKind;
            string repairTriggerFailureReason = directResult.LastFailureReason;
            if (directResult.IsSuccess)
            {
                Console.WriteLine(
                    $"rescue succeeded: failure_id={leasedRecord.FailureId} phase=direct engine={directResult.EngineId} output='{directResult.OutputThumbPath}'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "terminal",
                    result: "rescued",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: "direct",
                    engine: directResult.EngineId,
                    outputPath: directResult.OutputThumbPath
                );
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "rescued",
                    DateTime.UtcNow,
                    outputThumbPath: directResult.OutputThumbPath,
                    resultSignature: $"rescued:{directResult.EngineId}",
                    extraJson: BuildTerminalExtraJson(
                        "direct",
                        directResult.EngineId,
                        false,
                        "",
                        rescuePlan.RouteId,
                        rescuePlan.SymptomClass
                    ),
                    clearLease: true
                );
                return;
            }

            RescueExecutionPlan postDirectPlan = TryPromoteAfterDirectExhausted(
                rescuePlan,
                directResult.LastFailureKind,
                directResult.LastFailureReason,
                queueObj.MovieSizeBytes,
                queueObj.MovieFullPath
            );
            if (
                !string.Equals(postDirectPlan.RouteId, rescuePlan.RouteId, StringComparison.Ordinal)
            )
            {
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "plan_promoted",
                    result: "promoted",
                    routeId: postDirectPlan.RouteId,
                    symptomClass: postDirectPlan.SymptomClass,
                    phase: "direct_exhausted",
                    failureKind: directResult.LastFailureKind,
                    reason:
                        $"from={rescuePlan.RouteId}; failure={directResult.LastFailureReason}"
                );
                rescuePlan = postDirectPlan;
                directResult.EffectiveRescuePlan = rescuePlan;
            }

            if (!rescuePlan.UseRepairAfterDirect)
            {
                if (
                    await TryCompleteWithExperimentalFinalSeekRescueAsync(
                            failureDbService,
                            leasedRecord,
                            leaseOwner,
                            queueObj,
                            mainDbContext,
                            rescuePlan,
                            moviePath,
                            repairApplied: false,
                            triggerPhase: "route_exhausted",
                            triggerFailureKind: directResult.LastFailureKind,
                            triggerFailureReason: directResult.LastFailureReason,
                            attemptNo: directResult.NextAttemptNo
                        )
                        .ConfigureAwait(false)
                )
                {
                    return;
                }

                Console.WriteLine(
                    $"rescue gave up: failure_id={leasedRecord.FailureId} phase=route_exhausted reason='{directResult.LastFailureReason}' route={rescuePlan.RouteId}"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "terminal",
                    result: "gave_up",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: "route_exhausted",
                    failureKind: directResult.LastFailureKind,
                    reason: directResult.LastFailureReason
                );
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "gave_up",
                    DateTime.UtcNow,
                    extraJson: BuildTerminalExtraJson(
                        "route_exhausted",
                        "",
                        false,
                        directResult.LastFailureReason,
                        rescuePlan.RouteId,
                        rescuePlan.SymptomClass
                    ),
                    clearLease: true,
                    failureKind: directResult.LastFailureKind,
                    failureReason: directResult.LastFailureReason
                );
                return;
            }

            if (
                !ShouldEnterRepairPath(
                    rescuePlan.RouteId,
                    moviePath,
                    directResult.LastFailureKind,
                    directResult.LastFailureReason
                )
            )
            {
                if (
                    await TryCompleteWithExperimentalFinalSeekRescueAsync(
                            failureDbService,
                            leasedRecord,
                            leaseOwner,
                            queueObj,
                            mainDbContext,
                            rescuePlan,
                            moviePath,
                            repairApplied: false,
                            triggerPhase: "direct_exhausted",
                            triggerFailureKind: directResult.LastFailureKind,
                            triggerFailureReason: directResult.LastFailureReason,
                            attemptNo: directResult.NextAttemptNo
                        )
                        .ConfigureAwait(false)
                )
                {
                    return;
                }

                Console.WriteLine(
                    $"rescue gave up: failure_id={leasedRecord.FailureId} phase=direct_exhausted reason='{directResult.LastFailureReason}'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "terminal",
                    result: "gave_up",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: "direct_exhausted",
                    failureKind: directResult.LastFailureKind,
                    reason: directResult.LastFailureReason
                );
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "gave_up",
                    DateTime.UtcNow,
                    extraJson: BuildTerminalExtraJson(
                        "direct_exhausted",
                        "",
                        false,
                        directResult.LastFailureReason,
                        rescuePlan.RouteId,
                        rescuePlan.SymptomClass
                    ),
                    clearLease: true,
                    failureKind: directResult.LastFailureKind,
                    failureReason: directResult.LastFailureReason
                );
                return;
            }

            string repairedMoviePath = "";
            try
            {
                TimeSpan repairProbeTimeout = ResolveRepairProbeTimeout();
                TimeSpan repairTimeout = ResolveRepairTimeout();
                UpdateProgressSnapshot(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    phase: "repair_probe",
                    engineId: "",
                    repairApplied: true,
                    detail: "",
                    attemptNo: nextAttemptNo,
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    sourceMovieFullPath: moviePath,
                    currentFailureKind: directResult.LastFailureKind,
                    currentFailureReason: directResult.LastFailureReason
                );
                Console.WriteLine(
                    $"repair probe start: failure_id={leasedRecord.FailureId} timeout_sec={repairProbeTimeout.TotalSeconds:0} movie='{moviePath}'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "repair_probe",
                    result: "start",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: "repair_probe",
                    failureKind: directResult.LastFailureKind,
                    reason: directResult.LastFailureReason
                );
                VideoIndexProbeResult probeResult = await RunWithTimeoutAsync(
                        cts => repairService.ProbeAsync(moviePath, cts),
                        repairProbeTimeout,
                        $"repair probe timeout: failure_id={leasedRecord.FailureId}"
                    )
                    .ConfigureAwait(false);
                Console.WriteLine(
                    $"repair probe end: failure_id={leasedRecord.FailureId} detected={probeResult.IsIndexCorruptionDetected} reason='{probeResult.DetectionReason}'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "repair_probe",
                    result: probeResult.IsIndexCorruptionDetected ? "positive" : "negative",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: "repair_probe",
                    failureKind: directResult.LastFailureKind,
                    reason: probeResult.DetectionReason
                );
                if (!probeResult.IsIndexCorruptionDetected)
                {
                    bool continueToForcedRepairAfterNegativeProbe = false;
                    RescueExecutionPlan probeNegativePlan = TryPromoteAfterRepairProbeNegative(
                        rescuePlan,
                        directResult.LastFailureKind,
                        directResult.LastFailureReason,
                        queueObj.MovieSizeBytes,
                        queueObj.MovieFullPath
                    );
                    IReadOnlyList<string> remainingEngineOrder = BuildRemainingEngineOrder(
                        probeNegativePlan.RepairEngineOrder,
                        directResult.AttemptedEngines
                    );
                    if (
                        ShouldContinueAfterRepairProbeNegative(
                            probeNegativePlan.RouteId,
                            directResult.LastFailureKind,
                            directResult.LastFailureReason
                        )
                        && remainingEngineOrder.Count > 0
                    )
                    {
                        if (
                            !string.Equals(
                                probeNegativePlan.RouteId,
                                rescuePlan.RouteId,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            WriteRescueTrace(
                                leasedRecord,
                                mainDbContext.DbName,
                                mainDbContext.ThumbFolder,
                                action: "plan_promoted",
                                result: "promoted",
                                routeId: probeNegativePlan.RouteId,
                                symptomClass: probeNegativePlan.SymptomClass,
                                phase: "repair_probe_negative",
                                failureKind: directResult.LastFailureKind,
                                reason:
                                    $"from={rescuePlan.RouteId}; trigger=repair_probe_negative; failure={directResult.LastFailureReason}"
                            );
                        }

                        rescuePlan = probeNegativePlan;
                        Console.WriteLine(
                            $"repair probe negative fallback: failure_id={leasedRecord.FailureId} route={rescuePlan.RouteId} engines={string.Join(">", remainingEngineOrder)}"
                        );
                        WriteRescueTrace(
                            leasedRecord,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            action: "repair_probe",
                            result: "fallback_continue",
                            routeId: rescuePlan.RouteId,
                            symptomClass: rescuePlan.SymptomClass,
                            phase: "repair_probe_negative",
                            failureKind: directResult.LastFailureKind,
                            reason:
                                $"probe={probeResult.DetectionReason}; next={string.Join(">", remainingEngineOrder)}"
                        );
                        UpdateProgressSnapshot(
                            failureDbService,
                            leasedRecord,
                            leaseOwner,
                            phase: "repair_probe_negative_fallback",
                            engineId: "",
                            repairApplied: false,
                            detail: string.Join(">", remainingEngineOrder),
                            attemptNo: directResult.NextAttemptNo,
                            routeId: rescuePlan.RouteId,
                            symptomClass: rescuePlan.SymptomClass,
                            sourceMovieFullPath: moviePath,
                            currentFailureKind: directResult.LastFailureKind,
                            currentFailureReason: directResult.LastFailureReason
                        );

                        RescueAttemptResult postProbeResult = await RunEngineAttemptsAsync(
                                failureDbService,
                                leasedRecord,
                                leaseOwner,
                                queueObj,
                                thumbnailCreationService,
                                mainDbContext,
                                repairApplied: false,
                                rescuePlan: rescuePlan,
                                engineOrder: remainingEngineOrder,
                                sourceMovieFullPathOverride: null,
                                nextAttemptNo: directResult.NextAttemptNo,
                                preserveProvidedEngineOrder: true,
                                logDirectoryPath: logDirectoryPath
                            )
                            .ConfigureAwait(false);
                        rescuePlan = postProbeResult.EffectiveRescuePlan;
                        if (postProbeResult.IsSuccess)
                        {
                            Console.WriteLine(
                                $"rescue succeeded: failure_id={leasedRecord.FailureId} phase=probe_negative_fallback engine={postProbeResult.EngineId} output='{postProbeResult.OutputThumbPath}'"
                            );
                            WriteRescueTrace(
                                leasedRecord,
                                mainDbContext.DbName,
                                mainDbContext.ThumbFolder,
                                action: "terminal",
                                result: "rescued",
                                routeId: rescuePlan.RouteId,
                                symptomClass: rescuePlan.SymptomClass,
                                phase: "probe_negative_fallback",
                                engine: postProbeResult.EngineId,
                                outputPath: postProbeResult.OutputThumbPath
                            );
                            _ = failureDbService.UpdateFailureStatus(
                                leasedRecord.FailureId,
                                leaseOwner,
                                "rescued",
                                DateTime.UtcNow,
                                outputThumbPath: postProbeResult.OutputThumbPath,
                                resultSignature: $"rescued:{postProbeResult.EngineId}",
                                extraJson: BuildTerminalExtraJson(
                                    "probe_negative_fallback",
                                    postProbeResult.EngineId,
                                    false,
                                    probeResult.DetectionReason,
                                    rescuePlan.RouteId,
                                    rescuePlan.SymptomClass
                                ),
                                clearLease: true
                            );
                            return;
                        }

                        Console.WriteLine(
                            $"rescue gave up: failure_id={leasedRecord.FailureId} phase=probe_negative_fallback_exhausted reason='{postProbeResult.LastFailureReason}'"
                        );
                        if (
                            ShouldForceRepairAfterProbeNegativeExhausted(
                                rescuePlan.RouteId,
                                queueObj.MovieFullPath,
                                directResult.LastFailureKind,
                                postProbeResult.LastFailureKind,
                                postProbeResult.LastFailureReason
                            )
                        )
                        {
                            Console.WriteLine(
                                $"repair probe negative forced repair: failure_id={leasedRecord.FailureId} route={rescuePlan.RouteId} reason='{postProbeResult.LastFailureReason}'"
                            );
                            WriteRescueTrace(
                                leasedRecord,
                                mainDbContext.DbName,
                                mainDbContext.ThumbFolder,
                                action: "repair_probe",
                                result: "force_repair",
                                routeId: rescuePlan.RouteId,
                                symptomClass: rescuePlan.SymptomClass,
                                phase: "repair_probe_negative",
                                failureKind: postProbeResult.LastFailureKind,
                                reason: postProbeResult.LastFailureReason
                            );
                            UpdateProgressSnapshot(
                                failureDbService,
                                leasedRecord,
                                leaseOwner,
                                phase: "repair_probe_negative_force_repair",
                                engineId: "",
                                repairApplied: true,
                                detail: postProbeResult.LastFailureReason,
                                attemptNo: postProbeResult.NextAttemptNo,
                                routeId: rescuePlan.RouteId,
                                symptomClass: rescuePlan.SymptomClass,
                                sourceMovieFullPath: moviePath,
                                currentFailureKind: postProbeResult.LastFailureKind,
                                currentFailureReason: postProbeResult.LastFailureReason
                            );
                            repairTriggerFailureKind = postProbeResult.LastFailureKind;
                            repairTriggerFailureReason = postProbeResult.LastFailureReason;
                            nextAttemptNo = postProbeResult.NextAttemptNo;
                            continueToForcedRepairAfterNegativeProbe = true;
                        }
                        else
                        {
                            if (
                                await TryCompleteWithExperimentalFinalSeekRescueAsync(
                                        failureDbService,
                                        leasedRecord,
                                        leaseOwner,
                                        queueObj,
                                        mainDbContext,
                                        rescuePlan,
                                        moviePath,
                                        repairApplied: false,
                                        triggerPhase: "probe_negative_fallback_exhausted",
                                        triggerFailureKind: postProbeResult.LastFailureKind,
                                        triggerFailureReason: postProbeResult.LastFailureReason,
                                        attemptNo: postProbeResult.NextAttemptNo
                                    )
                                    .ConfigureAwait(false)
                            )
                            {
                                return;
                            }

                            WriteRescueTrace(
                                leasedRecord,
                                mainDbContext.DbName,
                                mainDbContext.ThumbFolder,
                                action: "terminal",
                                result: "gave_up",
                                routeId: rescuePlan.RouteId,
                                symptomClass: rescuePlan.SymptomClass,
                                phase: "probe_negative_fallback_exhausted",
                                failureKind: postProbeResult.LastFailureKind,
                                reason: postProbeResult.LastFailureReason
                            );
                            _ = failureDbService.UpdateFailureStatus(
                                leasedRecord.FailureId,
                                leaseOwner,
                                "gave_up",
                                DateTime.UtcNow,
                                extraJson: BuildTerminalExtraJson(
                                    "probe_negative_fallback_exhausted",
                                    "",
                                    false,
                                    postProbeResult.LastFailureReason,
                                    rescuePlan.RouteId,
                                    rescuePlan.SymptomClass
                                ),
                                clearLease: true,
                                failureKind: postProbeResult.LastFailureKind,
                                failureReason: postProbeResult.LastFailureReason
                            );
                            return;
                        }
                    }

                    if (!continueToForcedRepairAfterNegativeProbe)
                    {
                        if (
                            ShouldForceRepairAfterProbeNegative(
                                rescuePlan.RouteId,
                                queueObj.MovieFullPath,
                                directResult.LastFailureKind,
                                directResult.LastFailureReason
                            )
                        )
                        {
                            Console.WriteLine(
                                $"repair probe negative forced repair: failure_id={leasedRecord.FailureId} route={rescuePlan.RouteId} reason='{directResult.LastFailureReason}'"
                            );
                            WriteRescueTrace(
                                leasedRecord,
                                mainDbContext.DbName,
                                mainDbContext.ThumbFolder,
                                action: "repair_probe",
                                result: "force_repair",
                                routeId: rescuePlan.RouteId,
                                symptomClass: rescuePlan.SymptomClass,
                                phase: "repair_probe_negative",
                                failureKind: directResult.LastFailureKind,
                                reason: directResult.LastFailureReason
                            );
                            UpdateProgressSnapshot(
                                failureDbService,
                                leasedRecord,
                                leaseOwner,
                                phase: "repair_probe_negative_force_repair",
                                engineId: "",
                                repairApplied: true,
                                detail: directResult.LastFailureReason,
                                attemptNo: nextAttemptNo,
                                routeId: rescuePlan.RouteId,
                                symptomClass: rescuePlan.SymptomClass,
                                sourceMovieFullPath: moviePath,
                                currentFailureKind: directResult.LastFailureKind,
                                currentFailureReason: directResult.LastFailureReason
                            );
                            repairTriggerFailureKind = directResult.LastFailureKind;
                            repairTriggerFailureReason = directResult.LastFailureReason;
                            continueToForcedRepairAfterNegativeProbe = true;
                        }
                    }

                    if (!continueToForcedRepairAfterNegativeProbe)
                    {
                        if (
                            await TryCompleteWithExperimentalFinalSeekRescueAsync(
                                    failureDbService,
                                    leasedRecord,
                                    leaseOwner,
                                    queueObj,
                                    mainDbContext,
                                    rescuePlan,
                                    moviePath,
                                    repairApplied: false,
                                    triggerPhase: "repair_probe_negative",
                                    triggerFailureKind: directResult.LastFailureKind,
                                    triggerFailureReason: probeResult.DetectionReason,
                                    attemptNo: nextAttemptNo
                                )
                                .ConfigureAwait(false)
                        )
                        {
                            return;
                        }

                        Console.WriteLine(
                            $"rescue gave up: failure_id={leasedRecord.FailureId} phase=repair_probe_negative reason='{probeResult.DetectionReason}'"
                        );
                        WriteRescueTrace(
                            leasedRecord,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            action: "terminal",
                            result: "gave_up",
                            routeId: rescuePlan.RouteId,
                            symptomClass: rescuePlan.SymptomClass,
                            phase: "repair_probe_negative",
                            failureKind: directResult.LastFailureKind,
                            reason: probeResult.DetectionReason
                        );
                        _ = failureDbService.UpdateFailureStatus(
                            leasedRecord.FailureId,
                            leaseOwner,
                            "gave_up",
                            DateTime.UtcNow,
                            extraJson: BuildTerminalExtraJson(
                                "repair_probe_negative",
                                "",
                                true,
                                probeResult.DetectionReason,
                                rescuePlan.RouteId,
                                rescuePlan.SymptomClass
                            ),
                            clearLease: true,
                            failureKind: directResult.LastFailureKind,
                            failureReason: directResult.LastFailureReason
                        );
                        return;
                    }
                }

                repairedMoviePath = BuildRepairOutputPath(moviePath);
                UpdateProgressSnapshot(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    phase: "repair_execute",
                    engineId: "",
                    repairApplied: true,
                    detail: repairedMoviePath,
                    attemptNo: nextAttemptNo,
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    sourceMovieFullPath: moviePath,
                    currentFailureKind: repairTriggerFailureKind,
                    currentFailureReason: repairTriggerFailureReason
                );
                Console.WriteLine(
                    $"repair start: failure_id={leasedRecord.FailureId} timeout_sec={repairTimeout.TotalSeconds:0} output='{repairedMoviePath}'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "repair_execute",
                    result: "start",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: "repair_execute",
                    reason: repairedMoviePath
                );
                VideoIndexRepairResult repairResult = await RunWithTimeoutAsync(
                        cts => repairService.RepairAsync(moviePath, repairedMoviePath, cts),
                        repairTimeout,
                        $"repair timeout: failure_id={leasedRecord.FailureId}"
                    )
                    .ConfigureAwait(false);
                Console.WriteLine(
                    $"repair end: failure_id={leasedRecord.FailureId} success={repairResult.IsSuccess} output='{repairResult.OutputPath}' reason='{repairResult.ErrorMessage}'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "repair_execute",
                    result: repairResult.IsSuccess ? "success" : "failed",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: "repair_execute",
                    reason: repairResult.ErrorMessage,
                    outputPath: repairResult.OutputPath
                );
                if (!repairResult.IsSuccess || !File.Exists(repairResult.OutputPath))
                {
                    if (
                        await TryCompleteWithExperimentalFinalSeekRescueAsync(
                                failureDbService,
                                leasedRecord,
                                leaseOwner,
                                queueObj,
                                mainDbContext,
                                rescuePlan,
                                moviePath,
                                repairApplied: true,
                                triggerPhase: "repair_failed",
                                triggerFailureKind: repairTriggerFailureKind,
                                triggerFailureReason: repairResult.ErrorMessage,
                                attemptNo: nextAttemptNo
                            )
                            .ConfigureAwait(false)
                    )
                    {
                        return;
                    }

                    Console.WriteLine(
                        $"rescue gave up: failure_id={leasedRecord.FailureId} phase=repair_failed reason='{repairResult.ErrorMessage}'"
                    );
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "terminal",
                        result: "gave_up",
                        routeId: rescuePlan.RouteId,
                        symptomClass: rescuePlan.SymptomClass,
                        phase: "repair_failed",
                        failureKind: repairTriggerFailureKind,
                        reason: repairResult.ErrorMessage
                    );
                    _ = failureDbService.UpdateFailureStatus(
                        leasedRecord.FailureId,
                        leaseOwner,
                        "gave_up",
                        DateTime.UtcNow,
                        extraJson: BuildTerminalExtraJson(
                            "repair_failed",
                            "",
                            true,
                            repairResult.ErrorMessage,
                            rescuePlan.RouteId,
                            rescuePlan.SymptomClass
                        ),
                        clearLease: true,
                        failureKind: repairTriggerFailureKind,
                        failureReason: repairResult.ErrorMessage
                    );
                    return;
                }

                RescueAttemptResult repairedResult = await RunEngineAttemptsAsync(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        queueObj,
                        thumbnailCreationService,
                        mainDbContext,
                        repairApplied: true,
                        rescuePlan: rescuePlan,
                        engineOrder: rescuePlan.RepairEngineOrder,
                        sourceMovieFullPathOverride: repairResult.OutputPath,
                        nextAttemptNo: nextAttemptNo,
                        preserveProvidedEngineOrder: true,
                        logDirectoryPath: logDirectoryPath
                    )
                    .ConfigureAwait(false);
                rescuePlan = repairedResult.EffectiveRescuePlan;
                if (repairedResult.IsSuccess)
                {
                    Console.WriteLine(
                        $"rescue succeeded: failure_id={leasedRecord.FailureId} phase=repair_rescue engine={repairedResult.EngineId} output='{repairedResult.OutputThumbPath}'"
                    );
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "terminal",
                        result: "rescued",
                        routeId: rescuePlan.RouteId,
                        symptomClass: rescuePlan.SymptomClass,
                        phase: "repair_rescue",
                        engine: repairedResult.EngineId,
                        outputPath: repairedResult.OutputThumbPath
                    );
                    _ = failureDbService.UpdateFailureStatus(
                        leasedRecord.FailureId,
                        leaseOwner,
                        "rescued",
                        DateTime.UtcNow,
                        outputThumbPath: repairedResult.OutputThumbPath,
                        resultSignature: $"rescued:{repairedResult.EngineId}",
                        extraJson: BuildTerminalExtraJson(
                            "repair_rescue",
                            repairedResult.EngineId,
                            true,
                            "",
                            rescuePlan.RouteId,
                            rescuePlan.SymptomClass
                        ),
                        clearLease: true
                    );
                    return;
                }

                Console.WriteLine(
                    $"rescue gave up: failure_id={leasedRecord.FailureId} phase=repair_exhausted reason='{repairedResult.LastFailureReason}'"
                );
                if (
                    await TryCompleteWithExperimentalFinalSeekRescueAsync(
                            failureDbService,
                            leasedRecord,
                            leaseOwner,
                            queueObj,
                            mainDbContext,
                            rescuePlan,
                            repairResult.OutputPath,
                            repairApplied: true,
                            triggerPhase: "repair_exhausted",
                            triggerFailureKind: repairedResult.LastFailureKind,
                            triggerFailureReason: repairedResult.LastFailureReason,
                            attemptNo: repairedResult.NextAttemptNo
                        )
                        .ConfigureAwait(false)
                )
                {
                    return;
                }
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "terminal",
                    result: "gave_up",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: "repair_exhausted",
                    failureKind: repairedResult.LastFailureKind,
                    reason: repairedResult.LastFailureReason
                );
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "gave_up",
                    DateTime.UtcNow,
                    extraJson: BuildTerminalExtraJson(
                        "repair_exhausted",
                        "",
                        true,
                        repairedResult.LastFailureReason,
                        rescuePlan.RouteId,
                        rescuePlan.SymptomClass
                    ),
                    clearLease: true,
                    failureKind: repairedResult.LastFailureKind,
                    failureReason: repairedResult.LastFailureReason
                );
            }
            finally
            {
                TryDeleteFileQuietly(repairedMoviePath);
            }
        }

        // 実験用の最終救済。通常 route を全部使い切った後だけ、前進 decode で拾えるコマを最後に探す。
        private static async Task<bool> TryCompleteWithExperimentalFinalSeekRescueAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            MainDbContext mainDbContext,
            RescueExecutionPlan rescuePlan,
            string sourceMovieFullPath,
            bool repairApplied,
            string triggerPhase,
            ThumbnailFailureKind triggerFailureKind,
            string triggerFailureReason,
            int attemptNo
        )
        {
            if (!ShouldRunExperimentalFinalSeekRescue(queueObj))
            {
                return false;
            }

            Stopwatch sw = Stopwatch.StartNew();
            UpdateProgressSnapshot(
                failureDbService,
                leasedRecord,
                leaseOwner,
                phase: "experimental_final_seek",
                engineId: ExperimentalFinalSeekRescueEngineId,
                repairApplied: repairApplied,
                detail: $"trigger={triggerPhase}",
                attemptNo: attemptNo,
                routeId: rescuePlan.RouteId,
                symptomClass: rescuePlan.SymptomClass,
                sourceMovieFullPath: sourceMovieFullPath,
                currentFailureKind: triggerFailureKind,
                currentFailureReason: triggerFailureReason
            );
            WriteRescueTrace(
                leasedRecord,
                mainDbContext.DbName,
                mainDbContext.ThumbFolder,
                action: "experimental_final_seek",
                result: "start",
                routeId: rescuePlan.RouteId,
                symptomClass: rescuePlan.SymptomClass,
                phase: "experimental_final_seek",
                engine: ExperimentalFinalSeekRescueEngineId,
                failureKind: triggerFailureKind,
                reason: $"trigger={triggerPhase}; failure={triggerFailureReason}"
            );

            ThumbnailCreateResult finalSeekResult =
                await RunExperimentalFinalSeekRescueAsync(
                        queueObj,
                        mainDbContext,
                        sourceMovieFullPath
                    )
                    .ConfigureAwait(false);
            sw.Stop();

            if (
                finalSeekResult.IsSuccess
                && !string.IsNullOrWhiteSpace(finalSeekResult.SaveThumbFileName)
                && File.Exists(finalSeekResult.SaveThumbFileName)
            )
            {
                Console.WriteLine(
                    $"rescue succeeded: failure_id={leasedRecord.FailureId} phase=experimental_final_seek engine={ExperimentalFinalSeekRescueEngineId} output='{finalSeekResult.SaveThumbFileName}'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "experimental_final_seek",
                    result: "success",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: "experimental_final_seek",
                    engine: ExperimentalFinalSeekRescueEngineId,
                    elapsedMs: sw.ElapsedMilliseconds,
                    reason: $"trigger={triggerPhase}",
                    outputPath: finalSeekResult.SaveThumbFileName
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "terminal",
                    result: "rescued",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: "experimental_final_seek",
                    engine: ExperimentalFinalSeekRescueEngineId,
                    outputPath: finalSeekResult.SaveThumbFileName
                );
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "rescued",
                    DateTime.UtcNow,
                    outputThumbPath: finalSeekResult.SaveThumbFileName,
                    resultSignature: $"rescued:{ExperimentalFinalSeekRescueEngineId}",
                    extraJson: BuildTerminalExtraJson(
                        "experimental_final_seek",
                        ExperimentalFinalSeekRescueEngineId,
                        repairApplied,
                        $"trigger={triggerPhase}; failure={triggerFailureReason}",
                        rescuePlan.RouteId,
                        rescuePlan.SymptomClass
                    ),
                    clearLease: true
                );
                return true;
            }

            string finalFailureReason =
                finalSeekResult.ErrorMessage ?? "experimental final seek rescue failed";
            ThumbnailFailureKind finalFailureKind = ResolveFailureKind(
                null,
                queueObj?.MovieFullPath ?? "",
                finalFailureReason
            );
            WriteRescueTrace(
                leasedRecord,
                mainDbContext.DbName,
                mainDbContext.ThumbFolder,
                action: "experimental_final_seek",
                result: "failed",
                routeId: rescuePlan.RouteId,
                symptomClass: rescuePlan.SymptomClass,
                phase: "experimental_final_seek",
                engine: ExperimentalFinalSeekRescueEngineId,
                elapsedMs: sw.ElapsedMilliseconds,
                failureKind: finalFailureKind,
                reason: $"{finalFailureReason}; trigger={triggerPhase}"
            );
            AppendRescueAttemptRecord(
                failureDbService,
                leasedRecord,
                leaseOwner,
                ExperimentalFinalSeekRescueEngineId,
                finalFailureKind,
                finalFailureReason,
                sw.ElapsedMilliseconds,
                rescuePlan.RouteId,
                rescuePlan.SymptomClass,
                repairApplied,
                sourceMovieFullPath,
                attemptNo
            );
            return false;
        }

        internal static bool ShouldRunExperimentalFinalSeekRescue(QueueObj queueObj)
        {
            return ThumbnailLaneClassifier.ResolveLane(queueObj) == ThumbnailExecutionLane.Slow;
        }

        // 超巨大 AV1 を含む seek 重い個体でも、最後は先頭から低fpsで前進 decode して拾えるコマを探す。
        private static async Task<ThumbnailCreateResult> RunExperimentalFinalSeekRescueAsync(
            QueueObj queueObj,
            MainDbContext mainDbContext,
            string sourceMovieFullPath
        )
        {
            if (string.IsNullOrWhiteSpace(sourceMovieFullPath) || !File.Exists(sourceMovieFullPath))
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = "",
                    IsSuccess = false,
                    ErrorMessage = "experimental final seek source movie not found",
                    ProcessEngineId = ExperimentalFinalSeekRescueEngineId,
                };
            }

            double? durationSec = TryProbeDurationSecWithFfprobe(sourceMovieFullPath);
            if (!durationSec.HasValue || durationSec.Value <= 0d)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = "",
                    IsSuccess = false,
                    ErrorMessage = "experimental final seek duration probe failed",
                    ProcessEngineId = ExperimentalFinalSeekRescueEngineId,
                };
            }

            IReadOnlyList<double> captureSecs = BuildExperimentalFinalSeekCaptureSeconds(
                durationSec.Value,
                ExperimentalFinalSeekSampleCount
            );
            if (captureSecs.Count < 1)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = "",
                    DurationSec = durationSec,
                    IsSuccess = false,
                    ErrorMessage = "experimental final seek produced no capture points",
                    ProcessEngineId = ExperimentalFinalSeekRescueEngineId,
                };
            }

            string saveThumbFileName = ResolveThumbnailOutputPath(queueObj, mainDbContext);
            if (string.IsNullOrWhiteSpace(saveThumbFileName))
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = "",
                    DurationSec = durationSec,
                    IsSuccess = false,
                    ErrorMessage = "experimental final seek output path could not be resolved",
                    ProcessEngineId = ExperimentalFinalSeekRescueEngineId,
                };
            }

            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "IndigoMovieManager_fork_workthree",
                "thumbnail-final-seek"
            );
            string tempSessionDir = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempSessionDir);

            try
            {
                (
                    bool ok,
                    List<UltraShortFrameCandidate> candidates,
                    string errorMessage
                ) = await ExtractExperimentalFinalSeekCandidatesAsync(
                        sourceMovieFullPath,
                        captureSecs,
                        TimeSpan.FromSeconds(ExperimentalFinalSeekRescueTimeoutSec),
                        tempSessionDir
                    )
                    .ConfigureAwait(false);
                if (!ok || candidates.Count < 1)
                {
                    return new ThumbnailCreateResult
                    {
                        SaveThumbFileName = saveThumbFileName,
                        DurationSec = durationSec,
                        IsSuccess = false,
                        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
                            ? "experimental final seek produced no usable frames"
                            : errorMessage,
                        ProcessEngineId = ExperimentalFinalSeekRescueEngineId,
                    };
                }

                return ComposeExperimentalFinalSeekCandidates(
                    queueObj,
                    mainDbContext,
                    durationSec,
                    saveThumbFileName,
                    candidates
                );
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempSessionDir))
                    {
                        Directory.Delete(tempSessionDir, recursive: true);
                    }
                }
                catch
                {
                    // 実験用一時ディレクトリの掃除失敗では止めない。
                }
            }
        }

        // 失敗時も試行をappendし、後から比較できるようにする。
        private static async Task<RescueAttemptResult> RunEngineAttemptsAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            IThumbnailCreationService thumbnailCreationService,
            MainDbContext mainDbContext,
            bool repairApplied,
            RescueExecutionPlan rescuePlan,
            IReadOnlyList<string> engineOrder,
            string sourceMovieFullPathOverride,
            int nextAttemptNo,
            bool preserveProvidedEngineOrder = false,
            string logDirectoryPath = ""
        )
        {
            RescueExecutionPlan effectiveRescuePlan = rescuePlan;
            IReadOnlyList<string> effectiveEngineOrder = engineOrder;
            List<string> attemptedEngines = new();
            RescueAttemptResult result = new()
            {
                NextAttemptNo = nextAttemptNo,
                LastFailureKind = ThumbnailFailureKind.Unknown,
                EffectiveRescuePlan = rescuePlan,
            };
            for (int i = 0; i < effectiveEngineOrder.Count; i++)
            {
                string engineId = effectiveEngineOrder[i];
                TimeSpan engineAttemptTimeout = ResolveEngineAttemptTimeout(engineId);
                Stopwatch sw = Stopwatch.StartNew();
                string previousEngineEnv = Environment.GetEnvironmentVariable(
                    ThumbnailEnvConfig.ThumbEngine
                );
                string sourceMovieFullPath = string.IsNullOrWhiteSpace(sourceMovieFullPathOverride)
                    ? leasedRecord.MoviePath
                    : sourceMovieFullPathOverride;

                try
                {
                    UpdateProgressSnapshot(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        phase: repairApplied ? "repair_engine_attempt" : "direct_engine_attempt",
                        engineId: engineId,
                        repairApplied: repairApplied,
                        detail: "",
                        attemptNo: nextAttemptNo,
                        routeId: effectiveRescuePlan.RouteId,
                        symptomClass: effectiveRescuePlan.SymptomClass,
                        sourceMovieFullPath: sourceMovieFullPath,
                        currentFailureKind: ThumbnailFailureKind.None,
                        currentFailureReason: ""
                    );
                    Console.WriteLine(
                        $"engine attempt start: failure_id={leasedRecord.FailureId} engine={engineId} timeout_sec={engineAttemptTimeout.TotalSeconds:0} repair={repairApplied} source='{sourceMovieFullPath}'"
                    );
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "engine_attempt",
                        result: "start",
                        routeId: effectiveRescuePlan.RouteId,
                        symptomClass: effectiveRescuePlan.SymptomClass,
                        phase: repairApplied ? "repair_engine_attempt" : "direct_engine_attempt",
                        engine: engineId,
                        reason: sourceMovieFullPath
                    );
                ThumbnailCreateResult createResult = await RunCreateThumbAttemptAsync(
                        thumbnailCreationService,
                        queueObj,
                        mainDbContext,
                        engineId,
                            sourceMovieFullPathOverride,
                        engineAttemptTimeout,
                        $"engine attempt timeout: failure_id={leasedRecord.FailureId} engine={engineId}",
                        thumbInfoOverride: null,
                        logDirectoryPath: logDirectoryPath,
                        traceId: TryExtractTraceId(leasedRecord?.ExtraJson)
                    )
                    .ConfigureAwait(false);
                    attemptedEngines.Add(engineId);

                    if (IsFailurePlaceholderSuccess(createResult))
                    {
                        string placeholderReason =
                            $"failure placeholder created: {createResult.ProcessEngineId}";
                        TryDeleteFileQuietly(createResult.SaveThumbFileName);
                        createResult = new ThumbnailCreateResult
                        {
                            SaveThumbFileName = "",
                            DurationSec = createResult.DurationSec,
                            IsSuccess = false,
                            ErrorMessage = placeholderReason,
                            PreviewFrame = createResult.PreviewFrame,
                            ProcessEngineId = createResult.ProcessEngineId,
                        };
                    }

                    bool isSuccess =
                        createResult != null
                        && createResult.IsSuccess
                        && !string.IsNullOrWhiteSpace(createResult.SaveThumbFileName)
                        && File.Exists(createResult.SaveThumbFileName);
                    string nearBlackReason = "";
                    bool shouldRunNearBlackRetry = false;
                    if (
                        isSuccess
                        && TryRejectNearBlackOutput(
                            createResult.SaveThumbFileName,
                            out nearBlackReason
                        )
                    )
                    {
                        isSuccess = false;
                        createResult = new ThumbnailCreateResult
                        {
                            SaveThumbFileName = createResult.SaveThumbFileName,
                            DurationSec = createResult.DurationSec,
                            IsSuccess = false,
                            ErrorMessage = nearBlackReason,
                            PreviewFrame = createResult.PreviewFrame,
                        };
                        shouldRunNearBlackRetry = true;
                        Console.WriteLine(
                            $"engine attempt rejected: failure_id={leasedRecord.FailureId} engine={engineId} reason='{nearBlackReason}'"
                        );
                        WriteRescueTrace(
                            leasedRecord,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            action: "black_retry",
                            result: "rejected",
                            routeId: effectiveRescuePlan.RouteId,
                            symptomClass: effectiveRescuePlan.SymptomClass,
                            phase: repairApplied ? "repair_engine_attempt" : "direct_engine_attempt",
                            engine: engineId,
                            reason: nearBlackReason
                        );
                    }
                    else if (IsNearBlackFailureReason(createResult?.ErrorMessage))
                    {
                        nearBlackReason = createResult.ErrorMessage;
                        shouldRunNearBlackRetry = true;
                    }

                    if (shouldRunNearBlackRetry)
                    {
                        createResult = await RunNearBlackRetryAttemptsAsync(
                                failureDbService,
                                leasedRecord,
                                leaseOwner,
                                queueObj,
                                thumbnailCreationService,
                                mainDbContext,
                                engineId,
                                sourceMovieFullPathOverride,
                                engineAttemptTimeout,
                                repairApplied,
                                effectiveRescuePlan,
                                nextAttemptNo,
                                createResult?.DurationSec,
                                nearBlackReason,
                                logDirectoryPath
                            )
                            .ConfigureAwait(false);
                        isSuccess =
                            createResult != null
                            && createResult.IsSuccess
                            && !string.IsNullOrWhiteSpace(createResult.SaveThumbFileName)
                            && File.Exists(createResult.SaveThumbFileName);

                        if (
                            !isSuccess
                            && ShouldRunAutogenVirtualDurationRetry(
                                effectiveRescuePlan,
                                engineId,
                                createResult?.DurationSec
                            )
                        )
                        {
                            createResult = await RunAutogenVirtualDurationRetryAttemptsAsync(
                                    failureDbService,
                                    leasedRecord,
                                    leaseOwner,
                                    queueObj,
                                    thumbnailCreationService,
                                    mainDbContext,
                                    sourceMovieFullPathOverride,
                                    engineAttemptTimeout,
                                    repairApplied,
                                    effectiveRescuePlan,
                                    nextAttemptNo,
                                    createResult?.DurationSec,
                                    createResult?.ErrorMessage ?? nearBlackReason,
                                    logDirectoryPath
                                )
                                .ConfigureAwait(false);
                            isSuccess =
                                createResult != null
                                && createResult.IsSuccess
                                && !string.IsNullOrWhiteSpace(createResult.SaveThumbFileName)
                                && File.Exists(createResult.SaveThumbFileName);
                        }
                    }
                    sw.Stop();

                    if (isSuccess)
                    {
                        string succeededEngineId = ResolveSucceededEngineId(engineId, createResult);
                        Console.WriteLine(
                            $"engine attempt success: failure_id={leasedRecord.FailureId} engine={succeededEngineId} elapsed_ms={sw.ElapsedMilliseconds} output='{createResult.SaveThumbFileName}'"
                        );
                        WriteRescueTrace(
                            leasedRecord,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            action: "engine_attempt",
                            result: "success",
                            routeId: effectiveRescuePlan.RouteId,
                            symptomClass: effectiveRescuePlan.SymptomClass,
                            phase: repairApplied ? "repair_engine_attempt" : "direct_engine_attempt",
                            engine: succeededEngineId,
                            elapsedMs: sw.ElapsedMilliseconds,
                            outputPath: createResult.SaveThumbFileName
                        );
                        result.IsSuccess = true;
                        result.EngineId = succeededEngineId;
                        result.OutputThumbPath = createResult.SaveThumbFileName;
                        result.NextAttemptNo = nextAttemptNo;
                        result.EffectiveRescuePlan = effectiveRescuePlan;
                        result.AttemptedEngines = attemptedEngines.ToArray();
                        return result;
                    }

                    string failureReason = createResult?.ErrorMessage ?? "thumbnail create failed";
                    result.LastFailureReason = failureReason;
                    result.LastFailureKind = ResolveFailureKind(null, queueObj.MovieFullPath, failureReason);
                    Console.WriteLine(
                        $"engine attempt failed: failure_id={leasedRecord.FailureId} engine={engineId} elapsed_ms={sw.ElapsedMilliseconds} kind={result.LastFailureKind} reason='{failureReason}'"
                    );
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "engine_attempt",
                        result: "failed",
                        routeId: effectiveRescuePlan.RouteId,
                        symptomClass: effectiveRescuePlan.SymptomClass,
                        phase: repairApplied ? "repair_engine_attempt" : "direct_engine_attempt",
                        engine: engineId,
                        elapsedMs: sw.ElapsedMilliseconds,
                        failureKind: result.LastFailureKind,
                        reason: failureReason
                    );
                    UpdateProgressSnapshot(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        phase: repairApplied ? "repair_engine_failed" : "direct_engine_failed",
                        engineId: engineId,
                        repairApplied: repairApplied,
                        detail: "",
                        attemptNo: nextAttemptNo,
                        routeId: effectiveRescuePlan.RouteId,
                        symptomClass: effectiveRescuePlan.SymptomClass,
                        sourceMovieFullPath: sourceMovieFullPath,
                        currentFailureKind: result.LastFailureKind,
                        currentFailureReason: failureReason
                    );
                    AppendRescueAttemptRecord(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        engineId,
                        result.LastFailureKind,
                        failureReason,
                        sw.ElapsedMilliseconds,
                        effectiveRescuePlan.RouteId,
                        effectiveRescuePlan.SymptomClass,
                        repairApplied,
                        sourceMovieFullPathOverride,
                        nextAttemptNo
                    );
                    RescueExecutionPlan promotedPlan = TryPromoteRescuePlan(
                        effectiveRescuePlan,
                        attemptedEngines,
                        result.LastFailureKind,
                        failureReason,
                        queueObj.MovieSizeBytes,
                        queueObj.MovieFullPath,
                        repairApplied
                    );
                    if (
                        !string.Equals(
                            promotedPlan.RouteId,
                            effectiveRescuePlan.RouteId,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        WriteRescueTrace(
                            leasedRecord,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            action: "plan_promoted",
                            result: "promoted",
                            routeId: promotedPlan.RouteId,
                            symptomClass: promotedPlan.SymptomClass,
                            phase: repairApplied ? "repair_engine_attempt" : "direct_engine_attempt",
                            engine: engineId,
                            failureKind: result.LastFailureKind,
                            reason:
                                $"from={effectiveRescuePlan.RouteId}; attempted={string.Join(">", attemptedEngines)}; failure={failureReason}"
                        );
                    }
                    effectiveRescuePlan = promotedPlan;
                    effectiveEngineOrder = ResolveEffectiveEngineOrderAfterPromotion(
                        effectiveEngineOrder,
                        effectiveRescuePlan,
                        preserveProvidedEngineOrder
                    );
                    result.EffectiveRescuePlan = effectiveRescuePlan;
                    nextAttemptNo++;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    attemptedEngines.Add(engineId);
                    result.LastFailureReason = ex.Message;
                    result.LastFailureKind = ResolveFailureKind(ex, queueObj.MovieFullPath);
                    Console.WriteLine(
                        $"engine attempt exception: failure_id={leasedRecord.FailureId} engine={engineId} elapsed_ms={sw.ElapsedMilliseconds} kind={result.LastFailureKind} reason='{ex.Message}'"
                    );
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "engine_attempt",
                        result: "exception",
                        routeId: effectiveRescuePlan.RouteId,
                        symptomClass: effectiveRescuePlan.SymptomClass,
                        phase: repairApplied ? "repair_engine_attempt" : "direct_engine_attempt",
                        engine: engineId,
                        elapsedMs: sw.ElapsedMilliseconds,
                        failureKind: result.LastFailureKind,
                        reason: ex.Message
                    );
                    UpdateProgressSnapshot(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        phase: repairApplied ? "repair_engine_exception" : "direct_engine_exception",
                        engineId: engineId,
                        repairApplied: repairApplied,
                        detail: "",
                        attemptNo: nextAttemptNo,
                        routeId: effectiveRescuePlan.RouteId,
                        symptomClass: effectiveRescuePlan.SymptomClass,
                        sourceMovieFullPath: sourceMovieFullPath,
                        currentFailureKind: result.LastFailureKind,
                        currentFailureReason: ex.Message
                    );
                    AppendRescueAttemptRecord(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        engineId,
                        result.LastFailureKind,
                        ex.Message,
                        sw.ElapsedMilliseconds,
                        effectiveRescuePlan.RouteId,
                        effectiveRescuePlan.SymptomClass,
                        repairApplied,
                        sourceMovieFullPathOverride,
                        nextAttemptNo
                    );
                    RescueExecutionPlan promotedPlan = TryPromoteRescuePlan(
                        effectiveRescuePlan,
                        attemptedEngines,
                        result.LastFailureKind,
                        ex.Message,
                        queueObj.MovieSizeBytes,
                        queueObj.MovieFullPath,
                        repairApplied
                    );
                    if (
                        !string.Equals(
                            promotedPlan.RouteId,
                            effectiveRescuePlan.RouteId,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        WriteRescueTrace(
                            leasedRecord,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            action: "plan_promoted",
                            result: "promoted",
                            routeId: promotedPlan.RouteId,
                            symptomClass: promotedPlan.SymptomClass,
                            phase: repairApplied ? "repair_engine_attempt" : "direct_engine_attempt",
                            engine: engineId,
                            failureKind: result.LastFailureKind,
                            reason:
                                $"from={effectiveRescuePlan.RouteId}; attempted={string.Join(">", attemptedEngines)}; failure={ex.Message}"
                        );
                    }
                    effectiveRescuePlan = promotedPlan;
                    effectiveEngineOrder = ResolveEffectiveEngineOrderAfterPromotion(
                        effectiveEngineOrder,
                        effectiveRescuePlan,
                        preserveProvidedEngineOrder
                    );
                    result.EffectiveRescuePlan = effectiveRescuePlan;
                    nextAttemptNo++;
                }
                finally
                {
                    Environment.SetEnvironmentVariable(ThumbnailEnvConfig.ThumbEngine, previousEngineEnv);
                }
            }

            result.NextAttemptNo = nextAttemptNo;
            result.EffectiveRescuePlan = effectiveRescuePlan;
            result.AttemptedEngines = attemptedEngines.ToArray();
            return result;
        }

        // 黒フレームを拾った時だけ、同じengineのまま別時刻で再取得して最後の一押しを狙う。
        private static async Task<ThumbnailCreateResult> RunNearBlackRetryAttemptsAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            IThumbnailCreationService thumbnailCreationService,
            MainDbContext mainDbContext,
            string engineId,
            string sourceMovieFullPathOverride,
            TimeSpan timeout,
            bool repairApplied,
            RescueExecutionPlan rescuePlan,
            int attemptNo,
            double? durationSec,
            string initialFailureReason,
            string logDirectoryPath
        )
        {
            string sourceMovieFullPath = string.IsNullOrWhiteSpace(sourceMovieFullPathOverride)
                ? leasedRecord.MoviePath
                : sourceMovieFullPathOverride;
            double? resolvedDurationSec = ResolveNearBlackRetryDurationSec(
                durationSec,
                queueObj?.MovieSizeBytes ?? 0,
                sourceMovieFullPath
            );
            IReadOnlyList<ThumbInfo> retryThumbInfos = BuildNearBlackRetryThumbInfos(
                queueObj?.Tabindex ?? 0,
                mainDbContext.DbName,
                mainDbContext.ThumbFolder,
                resolvedDurationSec
            );
            if (retryThumbInfos.Count < 1)
            {
                return await RunUltraShortNearBlackRetryAttemptsAsync(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        queueObj,
                        mainDbContext,
                        engineId,
                        sourceMovieFullPathOverride,
                        timeout,
                        repairApplied,
                        rescuePlan,
                        attemptNo,
                        resolvedDurationSec,
                        initialFailureReason
                    )
                    .ConfigureAwait(false);
            }

            string phase = repairApplied ? "repair_black_retry" : "direct_black_retry";
            string lastFailureReason = initialFailureReason ?? "near-black thumbnail rejected";
            string lastSaveThumbFileName = "";

            for (int i = 0; i < retryThumbInfos.Count; i++)
            {
                ThumbInfo retryThumbInfo = retryThumbInfos[i];
                string thumbSecLabel = BuildThumbSecLabel(retryThumbInfo);
                UpdateProgressSnapshot(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    phase: phase,
                    engineId: engineId,
                    repairApplied: repairApplied,
                    detail: $"retry={i + 1}/{retryThumbInfos.Count}; secs={thumbSecLabel}",
                    attemptNo: attemptNo,
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    sourceMovieFullPath: sourceMovieFullPath,
                    currentFailureKind: ThumbnailFailureKind.Unknown,
                    currentFailureReason: lastFailureReason
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "black_retry",
                    result: "start",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: phase,
                    engine: engineId,
                    reason: $"retry={i + 1}/{retryThumbInfos.Count}; secs={thumbSecLabel}"
                );

                ThumbnailCreateResult retryResult = await RunCreateThumbAttemptAsync(
                        thumbnailCreationService,
                        queueObj,
                        mainDbContext,
                        engineId,
                        sourceMovieFullPathOverride,
                        timeout,
                        $"engine attempt timeout: failure_id={leasedRecord.FailureId} engine={engineId}",
                        retryThumbInfo,
                        logDirectoryPath,
                        TryExtractTraceId(leasedRecord?.ExtraJson)
                    )
                    .ConfigureAwait(false);
                lastSaveThumbFileName = retryResult?.SaveThumbFileName ?? lastSaveThumbFileName;

                bool retrySuccess =
                    retryResult != null
                    && retryResult.IsSuccess
                    && !string.IsNullOrWhiteSpace(retryResult.SaveThumbFileName)
                    && File.Exists(retryResult.SaveThumbFileName);
                if (
                    retrySuccess
                    && TryRejectNearBlackOutput(
                        retryResult.SaveThumbFileName,
                        out string retryNearBlackReason
                    )
                )
                {
                    lastFailureReason = retryNearBlackReason;
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "black_retry",
                        result: "rejected",
                        routeId: rescuePlan.RouteId,
                        symptomClass: rescuePlan.SymptomClass,
                        phase: phase,
                        engine: engineId,
                        reason: $"{retryNearBlackReason}; secs={thumbSecLabel}"
                    );
                    continue;
                }

                if (retrySuccess)
                {
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "black_retry",
                        result: "success",
                        routeId: rescuePlan.RouteId,
                        symptomClass: rescuePlan.SymptomClass,
                        phase: phase,
                        engine: engineId,
                        reason: $"secs={thumbSecLabel}",
                        outputPath: retryResult.SaveThumbFileName
                    );
                    return retryResult;
                }

                lastFailureReason = retryResult?.ErrorMessage ?? "thumbnail create failed";
                ThumbnailFailureKind retryFailureKind = ResolveFailureKind(
                    null,
                    queueObj?.MovieFullPath ?? "",
                    lastFailureReason
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "black_retry",
                    result: "failed",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: phase,
                    engine: engineId,
                    failureKind: retryFailureKind,
                    reason: $"{lastFailureReason}; secs={thumbSecLabel}"
                );
            }

            return new ThumbnailCreateResult
            {
                SaveThumbFileName = lastSaveThumbFileName,
                DurationSec = resolvedDurationSec,
                IsSuccess = false,
                ErrorMessage = lastFailureReason,
            };
        }

        // 1秒未満の near-black 個体は整数秒候補を作れないため、救済workerだけ小数秒 ffmpeg 1枚抜きへ逃がす。
        private static async Task<ThumbnailCreateResult> RunUltraShortNearBlackRetryAttemptsAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            MainDbContext mainDbContext,
            string engineId,
            string sourceMovieFullPathOverride,
            TimeSpan timeout,
            bool repairApplied,
            RescueExecutionPlan rescuePlan,
            int attemptNo,
            double? durationSec,
            string initialFailureReason
        )
        {
            IReadOnlyList<double> retryCaptureSecs = BuildUltraShortNearBlackRetryCaptureSeconds(
                durationSec
            );
            if (retryCaptureSecs.Count < 1)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = "",
                    DurationSec = durationSec,
                    IsSuccess = false,
                    ErrorMessage = initialFailureReason ?? "near-black thumbnail rejected",
                };
            }

            string sourceMovieFullPath = string.IsNullOrWhiteSpace(sourceMovieFullPathOverride)
                ? leasedRecord.MoviePath
                : sourceMovieFullPathOverride;
            string saveThumbFileName = ResolveThumbnailOutputPath(queueObj, mainDbContext);
            if (string.IsNullOrWhiteSpace(saveThumbFileName))
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = "",
                    DurationSec = durationSec,
                    IsSuccess = false,
                    ErrorMessage = "thumbnail output path could not be resolved",
                    ProcessEngineId = DecimalNearBlackRetryEngineId,
                };
            }

            string phase = repairApplied ? "repair_black_retry_decimal" : "direct_black_retry_decimal";
            string lastFailureReason = initialFailureReason ?? "near-black thumbnail rejected";
            List<UltraShortFrameCandidate> candidates = [];

            try
            {
                for (int i = 0; i < retryCaptureSecs.Count; i++)
                {
                    double captureSec = retryCaptureSecs[i];
                    string captureSecLabel = captureSec.ToString("0.###", CultureInfo.InvariantCulture);
                    UpdateProgressSnapshot(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        phase: phase,
                        engineId: engineId,
                        repairApplied: repairApplied,
                        detail: $"retry={i + 1}/{retryCaptureSecs.Count}; secs={captureSecLabel}; mode=decimal",
                        attemptNo: attemptNo,
                        routeId: rescuePlan.RouteId,
                        symptomClass: rescuePlan.SymptomClass,
                        sourceMovieFullPath: sourceMovieFullPath,
                        currentFailureKind: ThumbnailFailureKind.Unknown,
                        currentFailureReason: lastFailureReason
                    );
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "black_retry",
                        result: "start",
                        routeId: rescuePlan.RouteId,
                        symptomClass: rescuePlan.SymptomClass,
                        phase: phase,
                        engine: DecimalNearBlackRetryEngineId,
                        reason: $"retry={i + 1}/{retryCaptureSecs.Count}; secs={captureSecLabel}; mode=decimal"
                    );

                    (
                        bool extracted,
                        UltraShortFrameCandidate candidate,
                        string errorMessage
                    ) = await ExtractUltraShortDecimalNearBlackRetryCandidateAsync(
                            sourceMovieFullPath,
                            captureSec,
                            timeout
                        )
                        .ConfigureAwait(false);
                    if (!extracted)
                    {
                        lastFailureReason = string.IsNullOrWhiteSpace(errorMessage)
                            ? "decimal near-black retry failed"
                            : errorMessage;
                        ThumbnailFailureKind retryFailureKind = ResolveFailureKind(
                            null,
                            queueObj?.MovieFullPath ?? "",
                            lastFailureReason
                        );
                        WriteRescueTrace(
                            leasedRecord,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            action: "black_retry",
                            result: "failed",
                            routeId: rescuePlan.RouteId,
                            symptomClass: rescuePlan.SymptomClass,
                            phase: phase,
                            engine: DecimalNearBlackRetryEngineId,
                            failureKind: retryFailureKind,
                            reason: $"{lastFailureReason}; secs={captureSecLabel}; mode=decimal"
                        );
                        continue;
                    }

                    if (
                        TryRejectNearBlackOutput(candidate.ImagePath, out string retryNearBlackReason)
                    )
                    {
                        lastFailureReason = retryNearBlackReason;
                        WriteRescueTrace(
                            leasedRecord,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            action: "black_retry",
                            result: "rejected",
                            routeId: rescuePlan.RouteId,
                            symptomClass: rescuePlan.SymptomClass,
                            phase: phase,
                            engine: DecimalNearBlackRetryEngineId,
                            reason: $"{retryNearBlackReason}; secs={captureSecLabel}; mode=decimal"
                        );
                        continue;
                    }

                    candidates.Add(candidate);
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "black_retry",
                        result: "candidate",
                        routeId: rescuePlan.RouteId,
                        symptomClass: rescuePlan.SymptomClass,
                        phase: phase,
                        engine: DecimalNearBlackRetryEngineId,
                        reason:
                            $"secs={captureSecLabel}; mode=decimal; score={candidate.Score:0.##}; luma={candidate.AverageLuma:0.##}; sat={candidate.AverageSaturation:0.##}",
                        outputPath: candidate.ImagePath
                    );
                }

                if (candidates.Count > 0)
                {
                    ThumbnailCreateResult composedResult = ComposeUltraShortRetryCandidates(
                        queueObj,
                        mainDbContext,
                        durationSec,
                        saveThumbFileName,
                        candidates
                    );
                    if (composedResult.IsSuccess)
                    {
                        string selectedSecs = string.Join(
                            ",",
                            SelectUltraShortRetryCandidates(
                                candidates,
                                Math.Max(1, ResolveLayoutProfile(queueObj?.Tabindex ?? 0).DivCount)
                            ).Select(x => x.CaptureSec.ToString("0.###", CultureInfo.InvariantCulture))
                        );
                        WriteRescueTrace(
                            leasedRecord,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            action: "black_retry",
                            result: "success",
                            routeId: rescuePlan.RouteId,
                            symptomClass: rescuePlan.SymptomClass,
                            phase: phase,
                            engine: DecimalNearBlackRetryEngineId,
                            reason: $"secs={selectedSecs}; mode=decimal-multi; candidates={candidates.Count}",
                            outputPath: composedResult.SaveThumbFileName
                        );
                        return composedResult;
                    }

                    lastFailureReason = composedResult.ErrorMessage ?? lastFailureReason;
                }
            }
            finally
            {
                foreach (UltraShortFrameCandidate candidate in candidates)
                {
                    TryDeleteFileQuietly(candidate.ImagePath);
                }
            }

            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = false,
                ErrorMessage = lastFailureReason,
                ProcessEngineId = DecimalNearBlackRetryEngineId,
            };
        }

        // 超短尺 near-black は候補を全部抜いてから選ぶため、ここでは一時フレームだけを返す。
        private static async Task<(
            bool IsSuccess,
            UltraShortFrameCandidate Candidate,
            string ErrorMessage
        )> ExtractUltraShortDecimalNearBlackRetryCandidateAsync(
            string sourceMovieFullPath,
            double captureSec,
            TimeSpan timeout
        )
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "IndigoMovieManager_fork_workthree",
                "thumbnail-black-retry-decimal"
            );
            Directory.CreateDirectory(tempRoot);

            string tempFramePath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.jpg");
            try
            {
                (bool ok, string errorMessage) = await ExtractSingleFrameJpegWithFfmpegAsync(
                        sourceMovieFullPath,
                        captureSec,
                        tempFramePath,
                        timeout
                    )
                    .ConfigureAwait(false);
                if (!ok || !File.Exists(tempFramePath))
                {
                    TryDeleteFileQuietly(tempFramePath);
                    return (
                        false,
                        default,
                        string.IsNullOrWhiteSpace(errorMessage)
                            ? "decimal near-black retry failed"
                            : errorMessage
                    );
                }

                using Bitmap sourceBitmap = new(tempFramePath);
                double score = CalculateFrameVisualScore(
                    sourceBitmap,
                    out double averageLuma,
                    out double averageSaturation,
                    out double lumaStdDev
                );
                return (
                    true,
                    new UltraShortFrameCandidate(
                        tempFramePath,
                        captureSec,
                        score,
                        averageLuma,
                        averageSaturation,
                        lumaStdDev
                    ),
                    ""
                );
            }
            catch (Exception ex)
            {
                TryDeleteFileQuietly(tempFramePath);
                return (false, default, ex.Message ?? "decimal near-black retry failed");
            }
        }

        // 長尺 near-black は autogen の候補分布だけ前方へ圧縮して、明るい帯が前半に偏る個体を追加探索する。
        private static async Task<ThumbnailCreateResult> RunAutogenVirtualDurationRetryAttemptsAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            IThumbnailCreationService thumbnailCreationService,
            MainDbContext mainDbContext,
            string sourceMovieFullPathOverride,
            TimeSpan timeout,
            bool repairApplied,
            RescueExecutionPlan rescuePlan,
            int attemptNo,
            double? durationSec,
            string initialFailureReason,
            string logDirectoryPath
        )
        {
            IReadOnlyList<AutogenVirtualDurationRetryPlan> retryPlans =
                BuildAutogenVirtualDurationRetryPlans(queueObj?.Tabindex ?? 0, durationSec);
            if (retryPlans.Count < 1)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = "",
                    DurationSec = durationSec,
                    IsSuccess = false,
                    ErrorMessage = initialFailureReason ?? "near-black thumbnail rejected",
                    ProcessEngineId = "autogen",
                };
            }

            string sourceMovieFullPath = string.IsNullOrWhiteSpace(sourceMovieFullPathOverride)
                ? leasedRecord.MoviePath
                : sourceMovieFullPathOverride;
            string phase = repairApplied
                ? "repair_autogen_virtual_duration"
                : "direct_autogen_virtual_duration";
            string lastFailureReason = initialFailureReason ?? "near-black thumbnail rejected";
            string lastSaveThumbFileName = "";

            for (int i = 0; i < retryPlans.Count; i++)
            {
                AutogenVirtualDurationRetryPlan retryPlan = retryPlans[i];
                string thumbSecLabel = BuildThumbSecLabel(retryPlan.ThumbInfo);
                string retryLabel =
                    $"retry={i + 1}/{retryPlans.Count}; divisor=1/{retryPlan.DurationDivisor:0.###}; virtual_duration_sec={retryPlan.VirtualDurationSec:0.###}; secs={thumbSecLabel}";

                UpdateProgressSnapshot(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    phase: phase,
                    engineId: "autogen",
                    repairApplied: repairApplied,
                    detail: retryLabel,
                    attemptNo: attemptNo,
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    sourceMovieFullPath: sourceMovieFullPath,
                    currentFailureKind: ThumbnailFailureKind.Unknown,
                    currentFailureReason: lastFailureReason
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "autogen_virtual_duration",
                    result: "start",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: phase,
                    engine: "autogen",
                    reason: retryLabel
                );

                ThumbnailCreateResult retryResult = await RunCreateThumbAttemptAsync(
                        thumbnailCreationService,
                        queueObj,
                        mainDbContext,
                        "autogen",
                        sourceMovieFullPathOverride,
                        timeout,
                        $"engine attempt timeout: failure_id={leasedRecord.FailureId} engine=autogen",
                        retryPlan.ThumbInfo,
                        logDirectoryPath,
                        TryExtractTraceId(leasedRecord?.ExtraJson)
                    )
                    .ConfigureAwait(false);
                lastSaveThumbFileName = retryResult?.SaveThumbFileName ?? lastSaveThumbFileName;

                if (IsFailurePlaceholderSuccess(retryResult))
                {
                    string placeholderReason =
                        $"failure placeholder created: {retryResult.ProcessEngineId}";
                    TryDeleteFileQuietly(retryResult.SaveThumbFileName);
                    retryResult = new ThumbnailCreateResult
                    {
                        SaveThumbFileName = "",
                        DurationSec = retryResult.DurationSec,
                        IsSuccess = false,
                        ErrorMessage = placeholderReason,
                        PreviewFrame = retryResult.PreviewFrame,
                        ProcessEngineId = retryResult.ProcessEngineId,
                    };
                }

                bool retrySuccess =
                    retryResult != null
                    && retryResult.IsSuccess
                    && !string.IsNullOrWhiteSpace(retryResult.SaveThumbFileName)
                    && File.Exists(retryResult.SaveThumbFileName);
                if (
                    retrySuccess
                    && TryRejectNearBlackOutput(
                        retryResult.SaveThumbFileName,
                        out string retryNearBlackReason
                    )
                )
                {
                    lastFailureReason = retryNearBlackReason;
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "autogen_virtual_duration",
                        result: "rejected",
                        routeId: rescuePlan.RouteId,
                        symptomClass: rescuePlan.SymptomClass,
                        phase: phase,
                        engine: "autogen",
                        reason: $"{retryNearBlackReason}; {retryLabel}"
                    );
                    continue;
                }

                if (retrySuccess)
                {
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "autogen_virtual_duration",
                        result: "success",
                        routeId: rescuePlan.RouteId,
                        symptomClass: rescuePlan.SymptomClass,
                        phase: phase,
                        engine: "autogen",
                        reason: retryLabel,
                        outputPath: retryResult.SaveThumbFileName
                    );
                    return retryResult;
                }

                lastFailureReason = retryResult?.ErrorMessage ?? "thumbnail create failed";
                ThumbnailFailureKind retryFailureKind = ResolveFailureKind(
                    null,
                    queueObj?.MovieFullPath ?? "",
                    lastFailureReason
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "autogen_virtual_duration",
                    result: "failed",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: phase,
                    engine: "autogen",
                    failureKind: retryFailureKind,
                    reason: $"{lastFailureReason}; {retryLabel}"
                );
            }

            return new ThumbnailCreateResult
            {
                SaveThumbFileName = lastSaveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = false,
                ErrorMessage = lastFailureReason,
                ProcessEngineId = "autogen",
            };
        }

        // 超短尺は候補を全部見てから、鮮やかで明るいものを panel 数ぶん並べて保存する。
        private static ThumbnailCreateResult ComposeUltraShortRetryCandidates(
            QueueObj queueObj,
            MainDbContext mainDbContext,
            double? durationSec,
            string saveThumbFileName,
            IReadOnlyList<UltraShortFrameCandidate> candidates
        )
        {
            if (candidates == null || candidates.Count < 1)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = saveThumbFileName ?? "",
                    DurationSec = durationSec,
                    IsSuccess = false,
                    ErrorMessage = "decimal near-black retry produced no usable frames",
                    ProcessEngineId = DecimalNearBlackRetryEngineId,
                };
            }

            ThumbnailLayoutProfile layoutProfile = ResolveLayoutProfile(queueObj?.Tabindex ?? 0);
            IReadOnlyList<UltraShortFrameCandidate> selectedCandidates = SelectUltraShortRetryCandidates(
                candidates,
                Math.Max(1, layoutProfile.DivCount)
            );
            if (selectedCandidates.Count < 1)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = saveThumbFileName ?? "",
                    DurationSec = durationSec,
                    IsSuccess = false,
                    ErrorMessage = "decimal near-black retry selection failed",
                    ProcessEngineId = DecimalNearBlackRetryEngineId,
                };
            }

            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "IndigoMovieManager_fork_workthree",
                "thumbnail-black-retry-decimal"
            );
            Directory.CreateDirectory(tempRoot);
            string tempOutputPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.jpg");

            try
            {
                using Bitmap finalBitmap = BuildMultiFrameBitmap(selectedCandidates, layoutProfile);
                ThumbInfo thumbInfo = BuildExplicitThumbInfo(
                    layoutProfile,
                    BuildUltraShortCompositeCaptureSecs(selectedCandidates, layoutProfile.DivCount)
                );
                SaveBitmapWithThumbInfo(finalBitmap, thumbInfo, tempOutputPath);

                Directory.CreateDirectory(Path.GetDirectoryName(saveThumbFileName) ?? tempRoot);
                if (File.Exists(saveThumbFileName))
                {
                    File.Delete(saveThumbFileName);
                }
                File.Move(tempOutputPath, saveThumbFileName);
                DeleteStaleErrorMarker(
                    mainDbContext.ThumbFolder,
                    queueObj?.Tabindex ?? 0,
                    queueObj?.MovieFullPath ?? ""
                );

                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = saveThumbFileName,
                    DurationSec = durationSec,
                    IsSuccess = true,
                    ProcessEngineId = DecimalNearBlackRetryEngineId,
                };
            }
            catch (Exception ex)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = saveThumbFileName ?? "",
                    DurationSec = durationSec,
                    IsSuccess = false,
                    ErrorMessage = ex.Message ?? "decimal near-black retry compose failed",
                    ProcessEngineId = DecimalNearBlackRetryEngineId,
                };
            }
            finally
            {
                TryDeleteFileQuietly(tempOutputPath);
            }
        }

        // 最終救済の前進 decode 候補から、使えるコマだけで通常 jpg を組み立てる。
        private static ThumbnailCreateResult ComposeExperimentalFinalSeekCandidates(
            QueueObj queueObj,
            MainDbContext mainDbContext,
            double? durationSec,
            string saveThumbFileName,
            IReadOnlyList<UltraShortFrameCandidate> candidates
        )
        {
            if (candidates == null || candidates.Count < 1)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = saveThumbFileName ?? "",
                    DurationSec = durationSec,
                    IsSuccess = false,
                    ErrorMessage = "experimental final seek produced no usable frames",
                    ProcessEngineId = ExperimentalFinalSeekRescueEngineId,
                };
            }

            ThumbnailLayoutProfile layoutProfile = ResolveLayoutProfile(queueObj?.Tabindex ?? 0);
            IReadOnlyList<UltraShortFrameCandidate> selectedCandidates = SelectUltraShortRetryCandidates(
                candidates,
                Math.Max(1, layoutProfile.DivCount)
            );
            if (selectedCandidates.Count < 1)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = saveThumbFileName ?? "",
                    DurationSec = durationSec,
                    IsSuccess = false,
                    ErrorMessage = "experimental final seek selection failed",
                    ProcessEngineId = ExperimentalFinalSeekRescueEngineId,
                };
            }

            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "IndigoMovieManager_fork_workthree",
                "thumbnail-final-seek"
            );
            Directory.CreateDirectory(tempRoot);
            string tempOutputPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.jpg");

            try
            {
                using Bitmap finalBitmap = BuildMultiFrameBitmap(selectedCandidates, layoutProfile);
                ThumbInfo thumbInfo = BuildExplicitThumbInfo(
                    layoutProfile,
                    BuildUltraShortCompositeCaptureSecs(selectedCandidates, layoutProfile.DivCount)
                );
                SaveBitmapWithThumbInfo(finalBitmap, thumbInfo, tempOutputPath);

                Directory.CreateDirectory(Path.GetDirectoryName(saveThumbFileName) ?? tempRoot);
                if (File.Exists(saveThumbFileName))
                {
                    File.Delete(saveThumbFileName);
                }

                File.Move(tempOutputPath, saveThumbFileName);
                DeleteStaleErrorMarker(
                    mainDbContext.ThumbFolder,
                    queueObj?.Tabindex ?? 0,
                    queueObj?.MovieFullPath ?? ""
                );

                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = saveThumbFileName,
                    DurationSec = durationSec,
                    IsSuccess = true,
                    ProcessEngineId = ExperimentalFinalSeekRescueEngineId,
                };
            }
            catch (Exception ex)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = saveThumbFileName ?? "",
                    DurationSec = durationSec,
                    IsSuccess = false,
                    ErrorMessage = ex.Message ?? "experimental final seek compose failed",
                    ProcessEngineId = ExperimentalFinalSeekRescueEngineId,
                };
            }
            finally
            {
                TryDeleteFileQuietly(tempOutputPath);
            }
        }

        // fallback で明示した残り順は崩さず、通常 direct phase だけ route 昇格後の順へ差し替える。
        internal static IReadOnlyList<string> ResolveEffectiveEngineOrderAfterPromotion(
            IReadOnlyList<string> currentEngineOrder,
            RescueExecutionPlan promotedPlan,
            bool preserveProvidedEngineOrder
        )
        {
            if (preserveProvidedEngineOrder)
            {
                return currentEngineOrder ?? [];
            }

            return promotedPlan.DirectEngineOrder ?? [];
        }

        private static async Task<ThumbnailCreateResult> RunCreateThumbAttemptAsync(
            IThumbnailCreationService thumbnailCreationService,
            QueueObj queueObj,
            MainDbContext mainDbContext,
            string engineId,
            string sourceMovieFullPathOverride,
            TimeSpan timeout,
            string timeoutMessage,
            ThumbInfo thumbInfoOverride,
            string logDirectoryPath,
            string traceId = ""
        )
        {
            if (ShouldUseIsolatedChildProcess(engineId))
            {
                return await RunIsolatedEngineAttemptInChildProcessAsync(
                        queueObj,
                        mainDbContext,
                        engineId,
                        sourceMovieFullPathOverride,
                        timeout,
                        timeoutMessage,
                        thumbInfoOverride,
                        logDirectoryPath,
                        traceId
                    )
                    .ConfigureAwait(false);
            }

            // エンジン切替はプロセス環境変数を使うため、このworkerは1プロセス1動画前提で動かす。
            Environment.SetEnvironmentVariable(ThumbnailEnvConfig.ThumbEngine, engineId);
            return await RunWithTimeoutAsync(
                    cts =>
                        thumbnailCreationService.CreateThumbAsync(
                            new ThumbnailCreateArgs
                            {
                                QueueObj = queueObj,
                                DbName = mainDbContext.DbName,
                                ThumbFolder = mainDbContext.ThumbFolder,
                                IsResizeThumb = false,
                                IsManual = false,
                                SourceMovieFullPathOverride = sourceMovieFullPathOverride,
                                TraceId = traceId,
                                ThumbInfoOverride = thumbInfoOverride,
                            },
                            cts
                        ),
                    timeout,
                    timeoutMessage
                )
                .ConfigureAwait(false);
        }

        // OpenCV のように token 非対応で掴みっぱなしになる engine は子プロセスへ隔離する。
        internal static bool ShouldUseIsolatedChildProcess(string engineId)
        {
            return string.Equals(engineId, "opencv", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<ThumbnailCreateResult> RunIsolatedEngineAttemptInChildProcessAsync(
            QueueObj queueObj,
            MainDbContext mainDbContext,
            string engineId,
            string sourceMovieFullPathOverride,
            TimeSpan timeout,
            string timeoutMessage,
            ThumbInfo thumbInfoOverride,
            string logDirectoryPath,
            string traceId
        )
        {
            string currentExePath = ResolveCurrentExecutablePath();
            string resultJsonPath = Path.Combine(
                Path.GetTempPath(),
                "IndigoMovieManager_fork_workthree",
                "thumbnail-rescue-attempt",
                $"{Guid.NewGuid():N}.json"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(resultJsonPath) ?? Path.GetTempPath());

            IsolatedEngineAttemptRequest request = new(
                engineId,
                queueObj?.MovieFullPath ?? "",
                string.IsNullOrWhiteSpace(sourceMovieFullPathOverride)
                    ? queueObj?.MovieFullPath ?? ""
                    : sourceMovieFullPathOverride,
                mainDbContext.DbName,
                mainDbContext.ThumbFolder,
                queueObj?.Tabindex ?? 0,
                Math.Max(0, queueObj?.MovieSizeBytes ?? 0),
                BuildThumbSecCsv(thumbInfoOverride),
                resultJsonPath,
                logDirectoryPath,
                traceId
            );

            ProcessStartInfo startInfo = new()
            {
                FileName = currentExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };

            foreach (string argument in BuildIsolatedAttemptArguments(request))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            using CancellationTokenSource waitCts = new();
            waitCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (waitCts.IsCancellationRequested)
            {
                TryKillProcessTree(process);
                await process.WaitForExitAsync().ConfigureAwait(false);
                _ = await stdoutTask.ConfigureAwait(false);
                _ = await stderrTask.ConfigureAwait(false);
                TryDeleteFileQuietly(resultJsonPath);
                throw new TimeoutException($"{timeoutMessage}, timeout_sec={timeout.TotalSeconds:0}");
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            IsolatedEngineAttemptResultPayload payload = TryReadIsolatedAttemptResult(resultJsonPath);
            TryDeleteFileQuietly(resultJsonPath);

            if (payload != null)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = payload.SaveThumbFileName ?? "",
                    DurationSec = payload.DurationSec,
                    IsSuccess = payload.IsSuccess,
                    ErrorMessage = payload.ErrorMessage ?? "",
                };
            }

            return new ThumbnailCreateResult
            {
                SaveThumbFileName = "",
                DurationSec = null,
                IsSuccess = false,
                ErrorMessage = BuildIsolatedAttemptFailureMessage(
                    process.ExitCode,
                    stdout,
                    stderr,
                    engineId
                ),
            };
        }

        private static string ResolveCurrentExecutablePath()
        {
            string processPath = Environment.ProcessPath ?? "";
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                return processPath;
            }

            using Process currentProcess = Process.GetCurrentProcess();
            string fallbackPath = currentProcess.MainModule?.FileName ?? "";
            if (!string.IsNullOrWhiteSpace(fallbackPath))
            {
                return fallbackPath;
            }

            throw new InvalidOperationException("rescue worker executable path could not be resolved");
        }

        internal static IReadOnlyList<string> BuildIsolatedAttemptArguments(
            IsolatedEngineAttemptRequest request
        )
        {
            List<string> args =
            [
                AttemptChildModeArg,
                "--engine",
                request.EngineId ?? "",
                "--movie",
                request.MoviePath ?? "",
                "--source-movie",
                request.SourceMoviePath ?? "",
                "--db-name",
                request.DbName ?? "",
                "--thumb-folder",
                request.ThumbFolder ?? "",
                "--tab-index",
                request.TabIndex.ToString(),
                "--movie-size-bytes",
                Math.Max(0, request.MovieSizeBytes).ToString(),
                "--result-json",
                request.ResultJsonPath ?? "",
            ];

            if (!string.IsNullOrWhiteSpace(request.ThumbSecCsv))
            {
                args.Insert(args.Count - 2, "--thumb-sec-csv");
                args.Insert(args.Count - 2, request.ThumbSecCsv ?? "");
            }

            if (!string.IsNullOrWhiteSpace(request.LogDirectoryPath))
            {
                args.Insert(args.Count - 2, "--log-dir");
                args.Insert(args.Count - 2, request.LogDirectoryPath ?? "");
            }

            if (!string.IsNullOrWhiteSpace(request.TraceId))
            {
                args.Insert(args.Count - 2, "--trace-id");
                args.Insert(args.Count - 2, request.TraceId ?? "");
            }

            return args;
        }

        internal static IReadOnlyList<ThumbInfo> BuildNearBlackRetryThumbInfos(
            int tabIndex,
            string dbName,
            string thumbFolder,
            double? durationSec
        )
        {
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                return [];
            }

            int safeMaxCaptureSec = ResolveSafeMaxCaptureSec(durationSec.Value);
            if (safeMaxCaptureSec < 1)
            {
                return [];
            }

            ThumbnailLayoutProfile layoutProfile = ResolveLayoutProfile(tabIndex);
            HashSet<int> uniqueSeconds = new();
            List<ThumbInfo> retryThumbInfos = new();
            foreach (double ratio in NearBlackRetryRatios)
            {
                int captureSec = (int)Math.Floor(durationSec.Value * ratio);
                captureSec = Math.Max(1, Math.Min(safeMaxCaptureSec, captureSec));
                if (!uniqueSeconds.Add(captureSec))
                {
                    continue;
                }

                retryThumbInfos.Add(BuildUniformThumbInfo(layoutProfile, captureSec));
            }

            return retryThumbInfos;
        }

        internal static IReadOnlyList<double> BuildUltraShortNearBlackRetryCaptureSeconds(
            double? durationSec
        )
        {
            if (
                !durationSec.HasValue
                || durationSec.Value <= 0
                || durationSec.Value >= UltraShortDecimalRetryDurationThresholdSec
            )
            {
                return [];
            }

            double safeEnd = Math.Max(0.001d, durationSec.Value - 0.001d);
            HashSet<string> uniqueSeconds = new(StringComparer.Ordinal);
            List<double> captureSecs = [];
            foreach (double ratio in UltraShortNearBlackRetryRatios)
            {
                double captureSec = Math.Clamp(durationSec.Value * ratio, 0.001d, safeEnd);
                captureSec = Math.Round(captureSec, 3, MidpointRounding.AwayFromZero);
                string key = captureSec.ToString("0.000", CultureInfo.InvariantCulture);
                if (!uniqueSeconds.Add(key))
                {
                    continue;
                }

                captureSecs.Add(captureSec);
            }

            return captureSecs;
        }

        internal static bool ShouldRunAutogenVirtualDurationRetry(
            RescueExecutionPlan rescuePlan,
            string engineId,
            double? durationSec
        )
        {
            return string.Equals(engineId, "autogen", StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    rescuePlan.RouteId,
                    NearBlackOrOldFrameRouteId,
                    StringComparison.Ordinal
                )
                && durationSec.HasValue
                && durationSec.Value >= LongDurationNearBlackVirtualRetryThresholdSec;
        }

        internal static IReadOnlyList<AutogenVirtualDurationRetryPlan> BuildAutogenVirtualDurationRetryPlans(
            int tabIndex,
            double? durationSec
        )
        {
            if (
                !durationSec.HasValue
                || durationSec.Value < LongDurationNearBlackVirtualRetryThresholdSec
            )
            {
                return [];
            }

            ThumbnailLayoutProfile layoutProfile = ResolveLayoutProfile(tabIndex);
            List<AutogenVirtualDurationRetryPlan> result = [];
            HashSet<string> uniqueThumbSecCsv = new(StringComparer.Ordinal);

            foreach (double divisor in LongDurationVirtualDurationDivisors)
            {
                if (divisor <= 0d)
                {
                    continue;
                }

                double virtualDurationSec = durationSec.Value / divisor;
                ThumbInfo thumbInfo = BuildAutoThumbInfoForVirtualDuration(
                    layoutProfile,
                    virtualDurationSec
                );
                string thumbSecCsv = BuildThumbSecCsv(thumbInfo);
                if (string.IsNullOrWhiteSpace(thumbSecCsv) || !uniqueThumbSecCsv.Add(thumbSecCsv))
                {
                    continue;
                }

                result.Add(
                    new AutogenVirtualDurationRetryPlan(divisor, virtualDurationSec, thumbInfo)
                );
            }

            return result;
        }

        internal static IReadOnlyList<UltraShortFrameCandidate> SelectUltraShortRetryCandidates(
            IReadOnlyList<UltraShortFrameCandidate> candidates,
            int panelCount
        )
        {
            if (candidates == null || candidates.Count < 1)
            {
                return [];
            }

            int safePanelCount = Math.Max(1, panelCount);
            return candidates
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.AverageSaturation)
                .ThenByDescending(x => x.LumaStdDev)
                .ThenBy(x => x.CaptureSec)
                .Take(safePanelCount)
                .OrderBy(x => x.CaptureSec)
                .ToArray();
        }

        internal static double? ResolveNearBlackRetryDurationSec(
            double? durationSec,
            long movieSizeBytes,
            string moviePath,
            Func<string, double?> durationProbe = null
        )
        {
            if (durationSec.HasValue && durationSec.Value > 0)
            {
                return durationSec;
            }

            Func<string, double?> effectiveProbe = durationProbe ?? TryProbeDurationSecWithFfprobe;
            double? probedDurationSec = effectiveProbe(moviePath);
            if (probedDurationSec.HasValue && probedDurationSec.Value > 0)
            {
                return probedDurationSec;
            }

            if (IsLikelyUltraShort(movieSizeBytes, moviePath))
            {
                return UltraShortDecimalRetryFallbackDurationSec;
            }

            return durationSec;
        }

        internal static double CalculateFrameVisualScore(
            Bitmap source,
            out double averageLuma,
            out double averageSaturation,
            out double lumaStdDev
        )
        {
            averageLuma = 0d;
            averageSaturation = 0d;
            lumaStdDev = 0d;
            if (source == null || source.Width < 1 || source.Height < 1)
            {
                return 0d;
            }

            long count = 0;
            double lumaSum = 0d;
            double lumaSqSum = 0d;
            double saturationSum = 0d;
            for (int y = 0; y < source.Height; y += NearBlackThumbnailSampleStep)
            {
                for (int x = 0; x < source.Width; x += NearBlackThumbnailSampleStep)
                {
                    Color pixel = source.GetPixel(x, y);
                    double red = pixel.R;
                    double green = pixel.G;
                    double blue = pixel.B;
                    double luma = (0.2126d * red) + (0.7152d * green) + (0.0722d * blue);
                    double max = Math.Max(red, Math.Max(green, blue));
                    double min = Math.Min(red, Math.Min(green, blue));
                    double saturation = max <= 0d ? 0d : ((max - min) / max) * 255d;

                    count++;
                    lumaSum += luma;
                    lumaSqSum += luma * luma;
                    saturationSum += saturation;
                }
            }

            if (count < 1)
            {
                return 0d;
            }

            averageLuma = lumaSum / count;
            averageSaturation = saturationSum / count;
            double variance = Math.Max(0d, (lumaSqSum / count) - (averageLuma * averageLuma));
            lumaStdDev = Math.Sqrt(variance);

            // 明るさだけでなく、彩度とコントラストも足して「映えるコマ」を優先する。
            return (averageLuma * 0.35d) + (averageSaturation * 1.50d) + (lumaStdDev * 0.75d);
        }

        internal static string BuildThumbSecCsv(ThumbInfo thumbInfo)
        {
            if (thumbInfo?.ThumbSec == null || thumbInfo.ThumbSec.Count < 1)
            {
                return "";
            }

            return string.Join(",", thumbInfo.ThumbSec.Select(x => x.ToString()));
        }

        internal static ThumbInfo BuildThumbInfoFromCsv(
            int tabIndex,
            string dbName,
            string thumbFolder,
            string thumbSecCsv
        )
        {
            if (string.IsNullOrWhiteSpace(thumbSecCsv))
            {
                return null;
            }

            List<int> captureSecs = new();
            foreach (
                string part in thumbSecCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            )
            {
                if (int.TryParse(part.Trim(), out int captureSec))
                {
                    captureSecs.Add(Math.Max(0, captureSec));
                }
            }

            if (captureSecs.Count < 1)
            {
                return null;
            }

            return BuildExplicitThumbInfo(ResolveLayoutProfile(tabIndex), captureSecs);
        }

        private static ThumbInfo BuildUniformThumbInfo(
            ThumbnailLayoutProfile layoutProfile,
            int captureSec
        )
        {
            int thumbCount = Math.Max(1, layoutProfile?.DivCount ?? 1);
            int[] captureSecs = Enumerable.Repeat(Math.Max(0, captureSec), thumbCount).ToArray();
            return BuildExplicitThumbInfo(layoutProfile, captureSecs);
        }

        // autogen の通常候補計算と同じ割り方で、救済worker だけ仮想時間の panel 秒を組み立てる。
        private static ThumbInfo BuildAutoThumbInfoForVirtualDuration(
            ThumbnailLayoutProfile layoutProfile,
            double virtualDurationSec
        )
        {
            int thumbCount = Math.Max(1, layoutProfile?.DivCount ?? 1);
            int divideSec = 1;
            int maxCaptureSec = ResolveSafeMaxCaptureSec(virtualDurationSec);
            if (virtualDurationSec > 0d)
            {
                divideSec = (int)(virtualDurationSec / (thumbCount + 1));
                if (divideSec < 1)
                {
                    divideSec = 1;
                }
            }

            List<int> captureSecs = [];
            for (int i = 1; i < thumbCount + 1; i++)
            {
                int captureSec = i * divideSec;
                if (captureSec > maxCaptureSec)
                {
                    captureSec = maxCaptureSec;
                }

                captureSecs.Add(Math.Max(0, captureSec));
            }

            return BuildExplicitThumbInfo(layoutProfile, captureSecs);
        }

        private static ThumbInfo BuildExplicitThumbInfo(
            ThumbnailLayoutProfile layoutProfile,
            IReadOnlyList<int> captureSecs
        )
        {
            int thumbCount = Math.Max(1, layoutProfile?.DivCount ?? 1);
            ThumbnailSheetSpec spec = new()
            {
                ThumbWidth = layoutProfile?.Width ?? 160,
                ThumbHeight = layoutProfile?.Height ?? 120,
                ThumbRows = layoutProfile?.Rows ?? 1,
                ThumbColumns = layoutProfile?.Columns ?? 1,
                ThumbCount = thumbCount,
            };

            int fallbackSec = 0;
            if (captureSecs != null && captureSecs.Count > 0)
            {
                fallbackSec = Math.Max(0, captureSecs[captureSecs.Count - 1]);
            }

            for (int i = 0; i < thumbCount; i++)
            {
                int captureSec =
                    captureSecs != null && i < captureSecs.Count
                        ? Math.Max(0, captureSecs[i])
                        : fallbackSec;
                spec.CaptureSeconds.Add(captureSec);
            }
            return ThumbInfo.FromSheetSpec(spec);
        }

        private static int ResolveSafeMaxCaptureSec(double durationSec)
        {
            if (durationSec <= 0 || double.IsNaN(durationSec) || double.IsInfinity(durationSec))
            {
                return 0;
            }

            double safeEnd = Math.Max(0, durationSec - 0.001);
            return Math.Max(0, (int)Math.Floor(safeEnd));
        }

        private static string BuildThumbSecLabel(ThumbInfo thumbInfo)
        {
            return BuildThumbSecCsv(thumbInfo);
        }

        internal static string ResolveSucceededEngineId(
            string fallbackEngineId,
            ThumbnailCreateResult createResult
        )
        {
            string processEngineId = createResult?.ProcessEngineId?.Trim() ?? "";
            return string.IsNullOrWhiteSpace(processEngineId) ? (fallbackEngineId ?? "") : processEngineId;
        }

        private static string ResolveThumbnailOutputPath(QueueObj queueObj, MainDbContext mainDbContext)
        {
            if (queueObj == null || string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return "";
            }

            string hash = queueObj.Hash ?? "";
            if (string.IsNullOrWhiteSpace(hash))
            {
                hash = MovieHashCalculator.GetHashCrc32(queueObj.MovieFullPath);
                queueObj.Hash = hash;
            }

            if (string.IsNullOrWhiteSpace(hash))
            {
                return "";
            }

            return ThumbnailPathResolver.BuildThumbnailPath(
                ResolveOutPath(queueObj.Tabindex, mainDbContext.DbName, mainDbContext.ThumbFolder),
                queueObj.MovieFullPath,
                hash
            );
        }

        private static async Task<(bool ok, string errorMessage)> ExtractSingleFrameJpegWithFfmpegAsync(
            string moviePath,
            double captureSec,
            string outputPath,
            TimeSpan timeout
        )
        {
            if (string.IsNullOrWhiteSpace(moviePath) || !File.Exists(moviePath))
            {
                return (false, "movie file not found");
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = ResolveFfmpegExecutablePath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-an");
            startInfo.ArgumentList.Add("-sn");
            startInfo.ArgumentList.Add("-dn");
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(captureSec.ToString("0.###", CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(moviePath);
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-strict");
            startInfo.ArgumentList.Add("unofficial");
            startInfo.ArgumentList.Add("-pix_fmt");
            startInfo.ArgumentList.Add("yuv420p");
            startInfo.ArgumentList.Add("-q:v");
            startInfo.ArgumentList.Add("5");
            startInfo.ArgumentList.Add(outputPath);

            try
            {
                using CancellationTokenSource timeoutCts = new();
                timeoutCts.CancelAfter(timeout);
                return await RunProcessAsync(startInfo, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (false, $"decimal near-black retry timeout: timeout_sec={timeout.TotalSeconds:0}");
            }
        }

        private static Bitmap BuildRepeatedFrameBitmap(
            Bitmap sourceBitmap,
            ThumbnailLayoutProfile layoutProfile
        )
        {
            int panelWidth = Math.Max(1, layoutProfile?.Width ?? 160);
            int panelHeight = Math.Max(1, layoutProfile?.Height ?? 120);
            int columns = Math.Max(1, layoutProfile?.Columns ?? 1);
            int rows = Math.Max(1, layoutProfile?.Rows ?? 1);
            int totalWidth = panelWidth * columns;
            int totalHeight = panelHeight * rows;

            Bitmap canvas = new(totalWidth, totalHeight);
            using Graphics g = Graphics.FromImage(canvas);
            using Bitmap panelBitmap = BuildSinglePanelBitmap(sourceBitmap, panelWidth, panelHeight);
            g.Clear(Color.Black);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    g.DrawImage(panelBitmap, x * panelWidth, y * panelHeight, panelWidth, panelHeight);
                }
            }

            return canvas;
        }

        private static Bitmap BuildMultiFrameBitmap(
            IReadOnlyList<UltraShortFrameCandidate> selectedCandidates,
            ThumbnailLayoutProfile layoutProfile
        )
        {
            int panelWidth = Math.Max(1, layoutProfile?.Width ?? 160);
            int panelHeight = Math.Max(1, layoutProfile?.Height ?? 120);
            int columns = Math.Max(1, layoutProfile?.Columns ?? 1);
            int rows = Math.Max(1, layoutProfile?.Rows ?? 1);
            int panelCount = Math.Max(1, columns * rows);
            int totalWidth = panelWidth * columns;
            int totalHeight = panelHeight * rows;

            List<UltraShortFrameCandidate> arrangedCandidates = ExpandUltraShortRetryCandidates(
                selectedCandidates,
                panelCount
            );
            Bitmap canvas = new(totalWidth, totalHeight);
            using Graphics g = Graphics.FromImage(canvas);
            g.Clear(Color.Black);
            for (int i = 0; i < arrangedCandidates.Count; i++)
            {
                UltraShortFrameCandidate candidate = arrangedCandidates[i];
                using Bitmap sourceBitmap = new(candidate.ImagePath);
                using Bitmap panelBitmap = BuildSinglePanelBitmap(sourceBitmap, panelWidth, panelHeight);
                int x = (i % columns) * panelWidth;
                int y = (i / columns) * panelHeight;
                g.DrawImage(panelBitmap, x, y, panelWidth, panelHeight);
            }

            return canvas;
        }

        private static Bitmap BuildSinglePanelBitmap(Bitmap sourceBitmap, int panelWidth, int panelHeight)
        {
            Bitmap panelBitmap = new(panelWidth, panelHeight);
            using Graphics g = Graphics.FromImage(panelBitmap);
            g.Clear(Color.Black);

            double scale = Math.Min(
                (double)panelWidth / Math.Max(1, sourceBitmap.Width),
                (double)panelHeight / Math.Max(1, sourceBitmap.Height)
            );
            int drawWidth = Math.Max(1, (int)Math.Round(sourceBitmap.Width * scale));
            int drawHeight = Math.Max(1, (int)Math.Round(sourceBitmap.Height * scale));
            int drawX = Math.Max(0, (panelWidth - drawWidth) / 2);
            int drawY = Math.Max(0, (panelHeight - drawHeight) / 2);
            g.DrawImage(sourceBitmap, drawX, drawY, drawWidth, drawHeight);
            return panelBitmap;
        }

        private static void SaveBitmapWithThumbInfo(Bitmap bitmap, ThumbInfo thumbInfo, string savePath)
        {
            string saveDir = Path.GetDirectoryName(savePath) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            bitmap.Save(savePath, ImageFormat.Jpeg);
            WhiteBrowserThumbInfoSerializer.AppendToJpeg(savePath, thumbInfo?.ToSheetSpec());
        }

        private static async Task<(bool ok, string errorMessage)> RunProcessAsync(
            ProcessStartInfo startInfo,
            CancellationToken cts
        )
        {
            Process process = null;
            try
            {
                process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    return (false, "process start returned false");
                }

                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync(cts).ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);
                _ = await stdoutTask.ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    return (false, $"exit={process.ExitCode}, err={stderr}");
                }

                return (true, "");
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (process != null && !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(2000);
                    }
                }
                catch
                {
                    // timeout時の後始末失敗よりも、救済本体が戻ることを優先する。
                }

                throw;
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            finally
            {
                process?.Dispose();
            }
        }

        private static string ResolveFfmpegExecutablePath()
        {
            string configuredPath =
                Environment.GetEnvironmentVariable("IMM_FFMPEG_EXE_PATH")?.Trim().Trim('"') ?? "";
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                if (File.Exists(configuredPath))
                {
                    return configuredPath;
                }

                if (Directory.Exists(configuredPath))
                {
                    string candidate = Path.Combine(configuredPath, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            string baseDir = AppContext.BaseDirectory;
            string[] bundledCandidates =
            [
                Path.Combine(baseDir, "ffmpeg.exe"),
                Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDir, "tools", "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDir, "runtimes", "win-x64", "native", "ffmpeg.exe"),
                Path.Combine(baseDir, "runtimes", "win-x86", "native", "ffmpeg.exe"),
            ];

            for (int i = 0; i < bundledCandidates.Length; i++)
            {
                if (File.Exists(bundledCandidates[i]))
                {
                    return bundledCandidates[i];
                }
            }

            return "ffmpeg";
        }

        private static string ResolveFfprobeExecutablePath()
        {
            string ffmpegPath = ResolveFfmpegExecutablePath();
            if (!string.IsNullOrWhiteSpace(ffmpegPath))
            {
                try
                {
                    string ffprobeCandidate = Path.Combine(
                        Path.GetDirectoryName(ffmpegPath) ?? "",
                        "ffprobe.exe"
                    );
                    if (File.Exists(ffprobeCandidate))
                    {
                        return ffprobeCandidate;
                    }
                }
                catch
                {
                    // ffprobe 解決失敗時は最後に PATH 解決へ落とす。
                }
            }

            return "ffprobe";
        }

        private static List<UltraShortFrameCandidate> ExpandUltraShortRetryCandidates(
            IReadOnlyList<UltraShortFrameCandidate> selectedCandidates,
            int panelCount
        )
        {
            List<UltraShortFrameCandidate> arranged = [];
            if (selectedCandidates == null || selectedCandidates.Count < 1)
            {
                return arranged;
            }

            UltraShortFrameCandidate bestCandidate = selectedCandidates
                .OrderByDescending(x => x.Score)
                .First();
            for (int i = 0; i < panelCount; i++)
            {
                if (i < selectedCandidates.Count)
                {
                    arranged.Add(selectedCandidates[i]);
                }
                else
                {
                    arranged.Add(bestCandidate);
                }
            }

            return arranged;
        }

        private static IReadOnlyList<int> BuildUltraShortCompositeCaptureSecs(
            IReadOnlyList<UltraShortFrameCandidate> selectedCandidates,
            int panelCount
        )
        {
            List<int> captureSecs = [];
            foreach (
                UltraShortFrameCandidate candidate in ExpandUltraShortRetryCandidates(
                    selectedCandidates,
                    Math.Max(1, panelCount)
                )
            )
            {
                captureSecs.Add(Math.Max(0, (int)Math.Floor(candidate.CaptureSec)));
            }

            return captureSecs;
        }

        internal static IReadOnlyList<double> BuildExperimentalFinalSeekCaptureSeconds(
            double durationSec,
            int sampleCount
        )
        {
            if (
                durationSec <= 0d
                || double.IsNaN(durationSec)
                || double.IsInfinity(durationSec)
                || sampleCount < 1
            )
            {
                return [];
            }

            double safeDurationSec = Math.Max(0.001d, durationSec - 0.001d);
            double intervalSec = safeDurationSec / (sampleCount + 1);
            if (intervalSec <= 0d)
            {
                return [];
            }

            List<double> captureSecs = [];
            for (int i = 1; i <= sampleCount; i++)
            {
                double captureSec = Math.Clamp(intervalSec * i, 0.001d, safeDurationSec);
                captureSecs.Add(
                    Math.Round(captureSec, 3, MidpointRounding.AwayFromZero)
                );
            }

            return captureSecs;
        }

        // seek が重い個体では random seek を連打せず、先頭からの低fps decode で候補だけ拾う。
        private static async Task<(
            bool IsSuccess,
            List<UltraShortFrameCandidate> Candidates,
            string ErrorMessage
        )> ExtractExperimentalFinalSeekCandidatesAsync(
            string moviePath,
            IReadOnlyList<double> captureSecs,
            TimeSpan timeout,
            string outputDirectoryPath
        )
        {
            if (string.IsNullOrWhiteSpace(moviePath) || !File.Exists(moviePath))
            {
                return (false, [], "experimental final seek movie file not found");
            }

            if (captureSecs == null || captureSecs.Count < 1)
            {
                return (false, [], "experimental final seek produced no capture points");
            }

            Directory.CreateDirectory(outputDirectoryPath);
            foreach (string existingPath in Directory.GetFiles(outputDirectoryPath, "*.jpg"))
            {
                TryDeleteFileQuietly(existingPath);
            }

            double intervalSec = Math.Max(0.001d, captureSecs[0]);
            string outputPattern = Path.Combine(outputDirectoryPath, "frame-%03d.jpg");
            ProcessStartInfo startInfo = new()
            {
                FileName = ResolveFfmpegExecutablePath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-an");
            startInfo.ArgumentList.Add("-sn");
            startInfo.ArgumentList.Add("-dn");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(moviePath);
            startInfo.ArgumentList.Add("-vf");
            startInfo.ArgumentList.Add(
                FormattableString.Invariant(
                    $"fps=1/{intervalSec:0.###},scale={ExperimentalFinalSeekScaleWidth}:-1"
                )
            );
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add(captureSecs.Count.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-pix_fmt");
            startInfo.ArgumentList.Add("yuv420p");
            startInfo.ArgumentList.Add("-q:v");
            startInfo.ArgumentList.Add("5");
            startInfo.ArgumentList.Add(outputPattern);

            (bool ok, string errorMessage) processResult;
            try
            {
                using CancellationTokenSource timeoutCts = new();
                timeoutCts.CancelAfter(timeout);
                processResult = await RunProcessAsync(startInfo, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (
                    false,
                    [],
                    $"experimental final seek timeout: timeout_sec={timeout.TotalSeconds:0}"
                );
            }

            if (!processResult.ok)
            {
                return (false, [], processResult.errorMessage);
            }

            string[] imagePaths = Directory
                .GetFiles(outputDirectoryPath, "frame-*.jpg")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (imagePaths.Length < 1)
            {
                return (false, [], "experimental final seek produced no frames");
            }

            List<UltraShortFrameCandidate> candidates = [];
            bool nearBlackOnly = false;
            for (int i = 0; i < imagePaths.Length; i++)
            {
                string imagePath = imagePaths[i];
                if (TryRejectNearBlackOutput(imagePath, out _))
                {
                    nearBlackOnly = true;
                    continue;
                }

                using Bitmap sourceBitmap = new(imagePath);
                double score = CalculateFrameVisualScore(
                    sourceBitmap,
                    out double averageLuma,
                    out double averageSaturation,
                    out double lumaStdDev
                );
                double captureSec = captureSecs[Math.Min(i, captureSecs.Count - 1)];
                candidates.Add(
                    new UltraShortFrameCandidate(
                        imagePath,
                        captureSec,
                        score,
                        averageLuma,
                        averageSaturation,
                        lumaStdDev
                    )
                );
            }

            if (candidates.Count < 1)
            {
                return (
                    false,
                    [],
                    nearBlackOnly
                        ? "experimental final seek produced only near-black frames"
                        : "experimental final seek produced no usable frames"
                );
            }

            return (true, candidates, "");
        }

        private static double? TryProbeDurationSecWithFfprobe(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath) || !File.Exists(moviePath))
            {
                return null;
            }

            Process process = null;
            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = ResolveFfprobeExecutablePath(),
                    Arguments =
                        "-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 "
                        + $"\"{moviePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    return null;
                }

                string stdout = process.StandardOutput.ReadToEnd();
                _ = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // probe 失敗は救済本体を止めない。
                    }

                    return null;
                }

                if (process.ExitCode != 0)
                {
                    return null;
                }

                if (
                    double.TryParse(
                        (stdout ?? "").Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double parsedDurationSec
                    )
                    && parsedDurationSec > 0
                )
                {
                    return parsedDurationSec;
                }

                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                process?.Dispose();
            }
        }

        private static async Task<int> RunIsolatedAttemptChildAsync(
            IsolatedEngineAttemptRequest request
        )
        {
            ThumbnailCreateResult result;

            try
            {
                Environment.SetEnvironmentVariable(ThumbnailEnvConfig.ThumbEngine, request.EngineId);
                IThumbnailCreationService thumbnailCreationService =
                    RescueWorkerThumbnailCreationServiceFactory.Create(request.LogDirectoryPath);
                QueueObj queueObj = new()
                {
                    MovieFullPath = request.MoviePath ?? "",
                    MovieSizeBytes = Math.Max(0, request.MovieSizeBytes),
                    Tabindex = request.TabIndex,
                };
                result = await thumbnailCreationService
                    .CreateThumbAsync(
                        new ThumbnailCreateArgs
                        {
                            QueueObj = queueObj,
                            DbName = request.DbName,
                            ThumbFolder = request.ThumbFolder,
                            IsResizeThumb = false,
                            IsManual = false,
                            SourceMovieFullPathOverride = string.Equals(
                                request.SourceMoviePath,
                                request.MoviePath,
                                StringComparison.OrdinalIgnoreCase
                            )
                                ? null
                                : request.SourceMoviePath,
                            TraceId = request.TraceId,
                            ThumbInfoOverride = BuildThumbInfoFromCsv(
                                request.TabIndex,
                                request.DbName,
                                request.ThumbFolder,
                                request.ThumbSecCsv
                            ),
                        },
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new ThumbnailCreateResult
                {
                    SaveThumbFileName = "",
                    DurationSec = null,
                    IsSuccess = false,
                    ErrorMessage = ex.Message ?? "isolated engine attempt failed",
                };
            }

            IsolatedEngineAttemptResultPayload payload = new()
            {
                SaveThumbFileName = result.SaveThumbFileName ?? "",
                DurationSec = result.DurationSec,
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage ?? "",
            };
            string json = JsonSerializer.Serialize(payload);
            Directory.CreateDirectory(
                Path.GetDirectoryName(request.ResultJsonPath) ?? Path.GetTempPath()
            );
            await File.WriteAllTextAsync(
                    request.ResultJsonPath,
                    json,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                )
                .ConfigureAwait(false);
            return payload.IsSuccess ? 0 : 1;
        }

        internal static bool TryParseIsolatedAttemptArguments(
            string[] args,
            out IsolatedEngineAttemptRequest request
        )
        {
            string engineId = "";
            string moviePath = "";
            string sourceMoviePath = "";
            string dbName = "";
            string thumbFolder = "";
            string thumbSecCsv = "";
            string resultJsonPath = "";
            string logDirectoryPath = "";
            string traceId = "";
            int tabIndex = 0;
            long movieSizeBytes = 0;

            request = default;
            if (!HasArgument(args, AttemptChildModeArg))
            {
                return false;
            }

            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                switch (args[i]?.Trim())
                {
                    case AttemptChildModeArg:
                        break;
                    case "--engine" when i + 1 < args.Length:
                        engineId = args[++i] ?? "";
                        break;
                    case "--movie" when i + 1 < args.Length:
                        moviePath = args[++i] ?? "";
                        break;
                    case "--source-movie" when i + 1 < args.Length:
                        sourceMoviePath = args[++i] ?? "";
                        break;
                    case "--db-name" when i + 1 < args.Length:
                        dbName = args[++i] ?? "";
                        break;
                    case "--thumb-folder" when i + 1 < args.Length:
                        thumbFolder = args[++i] ?? "";
                        break;
                    case "--tab-index" when i + 1 < args.Length:
                        _ = int.TryParse(args[++i], out tabIndex);
                        break;
                    case "--movie-size-bytes" when i + 1 < args.Length:
                        _ = long.TryParse(args[++i], out movieSizeBytes);
                        break;
                    case "--thumb-sec-csv" when i + 1 < args.Length:
                        thumbSecCsv = args[++i] ?? "";
                        break;
                    case "--log-dir" when i + 1 < args.Length:
                        logDirectoryPath = args[++i] ?? "";
                        break;
                    case "--result-json" when i + 1 < args.Length:
                        resultJsonPath = args[++i] ?? "";
                        break;
                    case "--trace-id" when i + 1 < args.Length:
                        traceId = args[++i] ?? "";
                        break;
                }
            }

            if (
                string.IsNullOrWhiteSpace(engineId)
                || string.IsNullOrWhiteSpace(moviePath)
                || string.IsNullOrWhiteSpace(dbName)
                || string.IsNullOrWhiteSpace(thumbFolder)
                || string.IsNullOrWhiteSpace(resultJsonPath)
            )
            {
                return false;
            }

            request = new IsolatedEngineAttemptRequest(
                engineId,
                moviePath,
                string.IsNullOrWhiteSpace(sourceMoviePath) ? moviePath : sourceMoviePath,
                dbName,
                thumbFolder,
                tabIndex,
                Math.Max(0, movieSizeBytes),
                thumbSecCsv,
                resultJsonPath,
                logDirectoryPath,
                traceId
            );
            return true;
        }

        private static IsolatedEngineAttemptResultPayload TryReadIsolatedAttemptResult(
            string resultJsonPath
        )
        {
            try
            {
                if (!File.Exists(resultJsonPath))
                {
                    return null;
                }

                string json = File.ReadAllText(resultJsonPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<IsolatedEngineAttemptResultPayload>(json);
            }
            catch
            {
                return null;
            }
        }

        internal static string BuildIsolatedAttemptFailureMessage(
            int exitCode,
            string stdout,
            string stderr,
            string engineId
        )
        {
            string detail = FirstNonEmptyLine(stderr) ?? FirstNonEmptyLine(stdout) ?? "";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return $"isolated engine attempt failed: engine={engineId}, exit_code={exitCode}, detail={detail}";
            }

            return $"isolated engine attempt failed: engine={engineId}, exit_code={exitCode}";
        }

        private static string FirstNonEmptyLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            using StringReader reader = new(text);
            while (true)
            {
                string line = reader.ReadLine();
                if (line == null)
                {
                    return "";
                }

                string trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    return trimmed;
                }
            }
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // timeout 終了時は best effort で殺せれば十分。
            }
        }

        private static void AppendRescueAttemptRecord(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            string engineId,
            ThumbnailFailureKind failureKind,
            string failureReason,
            long elapsedMs,
            string routeId,
            string symptomClass,
            bool repairApplied,
            string sourceMovieFullPathOverride,
            int attemptNo
        )
        {
            string traceId = TryExtractTraceId(leasedRecord?.ExtraJson);
            DateTime nowUtc = DateTime.UtcNow;
            _ = failureDbService.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MainDbFullPath = leasedRecord.MainDbFullPath,
                    MainDbPathHash = leasedRecord.MainDbPathHash,
                    MoviePath = leasedRecord.MoviePath,
                    MoviePathKey = leasedRecord.MoviePathKey,
                    TabIndex = leasedRecord.TabIndex,
                    Lane = "rescue",
                    AttemptGroupId = leasedRecord.AttemptGroupId,
                    AttemptNo = attemptNo,
                    // 試行ログは open request の本体ではないので、完了済み失敗として固定する。
                    Status = "attempt_failed",
                    LeaseOwner = "",
                    LeaseUntilUtc = "",
                    Engine = engineId,
                    FailureKind = failureKind,
                    FailureReason = failureReason ?? "",
                    ElapsedMs = Math.Max(0, elapsedMs),
                    SourcePath = string.IsNullOrWhiteSpace(sourceMovieFullPathOverride)
                        ? leasedRecord.MoviePath
                        : sourceMovieFullPathOverride,
                    RepairApplied = repairApplied,
                    ResultSignature = "",
                    ExtraJson = BuildAttemptExtraJson(
                        engineId,
                        repairApplied,
                        routeId,
                        symptomClass,
                        traceId
                    ),
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                }
            );
        }

        // p1段階では fixed 順のままでも、親行だけ見れば進行中 phase を追えるようにする。
        private static void UpdateProgressSnapshot(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            string phase,
            string engineId,
            bool repairApplied,
            string detail,
            int attemptNo,
            string routeId,
            string symptomClass,
            string sourceMovieFullPath,
            ThumbnailFailureKind currentFailureKind,
            string currentFailureReason
        )
        {
            string traceId = TryExtractTraceId(leasedRecord?.ExtraJson);
            _ = failureDbService.UpdateProcessingSnapshot(
                leasedRecord.FailureId,
                leaseOwner,
                DateTime.UtcNow,
                BuildProgressExtraJson(
                    phase,
                    engineId,
                    repairApplied,
                    detail,
                    attemptNo,
                    routeId,
                    symptomClass,
                    sourceMovieFullPath,
                    currentFailureKind,
                    currentFailureReason,
                    traceId
                )
            );
        }

        private static void WriteRescueTrace(
            ThumbnailFailureRecord leasedRecord,
            string dbName,
            string thumbFolder,
            string action,
            string result,
            string routeId = "",
            string symptomClass = "",
            string phase = "",
            string engine = "",
            long elapsedMs = -1,
            ThumbnailFailureKind failureKind = ThumbnailFailureKind.None,
            string reason = "",
            string outputPath = ""
        )
        {
            string traceId = TryExtractTraceId(leasedRecord?.ExtraJson);
            ThumbnailRescueTraceLog.Write(
                source: "worker",
                action: action,
                result: result,
                failureId: leasedRecord?.FailureId ?? 0,
                moviePath: leasedRecord?.MoviePath ?? "",
                tabIndex: leasedRecord?.TabIndex ?? -1,
                panelSize: ThumbnailRescueTraceLog.BuildPanelSizeLabel(
                    leasedRecord?.TabIndex ?? -1,
                    dbName ?? "",
                    thumbFolder ?? ""
                ),
                routeId: routeId ?? "",
                symptomClass: symptomClass ?? "",
                phase: phase ?? "",
                engine: engine ?? "",
                elapsedMs: elapsedMs,
                failureKind: failureKind == ThumbnailFailureKind.None ? "" : failureKind.ToString(),
                reason: reason ?? "",
                outputPath: outputPath ?? ""
            );
            ThumbnailMovieTraceLog.Write(
                traceId,
                source: "rescue-worker",
                phase: string.IsNullOrWhiteSpace(phase) ? action : phase,
                moviePath: leasedRecord?.MoviePath ?? "",
                tabIndex: leasedRecord?.TabIndex ?? -1,
                engine: engine ?? "",
                result: result ?? "",
                detail: $"action={action}; reason={reason ?? ""}",
                outputPath: outputPath ?? "",
                routeId: routeId ?? "",
                symptomClass: symptomClass ?? "",
                failureId: leasedRecord?.FailureId ?? 0,
                attemptNo: leasedRecord?.AttemptNo ?? 0
            );
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

        // worker を無限待ちにしないため、重い処理は明示 timeout で包む。
        internal static async Task<T> RunWithTimeoutAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            TimeSpan timeout,
            string timeoutMessage
        )
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            using CancellationTokenSource timeoutCts = new();
            timeoutCts.CancelAfter(timeout);

            try
            {
                return await operation(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"{timeoutMessage}, timeout_sec={timeout.TotalSeconds:0}",
                    ex
                );
            }
        }

        // MainDBへは書き込まず、systemテーブルの読み取りだけを許容する。
        private static MainDbContext ResolveMainDbContext(
            string mainDbFullPath,
            string thumbFolderOverride
        )
        {
            string dbName = Path.GetFileNameWithoutExtension(mainDbFullPath) ?? "";
            string thumbFolder = NormalizeThumbFolderPath(thumbFolderOverride);

            if (!string.IsNullOrWhiteSpace(thumbFolder))
            {
                return new MainDbContext(dbName, thumbFolder);
            }

            using SQLiteConnection connection = CreateReadOnlyMainDbConnection(mainDbFullPath);
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM system WHERE attr = 'thum' LIMIT 1;";
            object value = command.ExecuteScalar();
            thumbFolder = NormalizeThumbFolderPath(Convert.ToString(value) ?? "");

            if (string.IsNullOrWhiteSpace(thumbFolder))
            {
                thumbFolder = NormalizeThumbFolderPath(
                    Path.Combine(AppContext.BaseDirectory, "Thumb", dbName)
                );
            }

            return new MainDbContext(dbName, thumbFolder);
        }

        // rescue worker は mainDB を読むだけなので、読取専用で開いて余計なロックを増やさない。
        private static SQLiteConnection CreateReadOnlyMainDbConnection(string mainDbFullPath)
        {
            SQLiteConnectionStringBuilder builder = new()
            {
                DataSource = mainDbFullPath,
                FailIfMissing = true,
                ReadOnly = true,
            };

            return new SQLiteConnection(builder.ToString());
        }

        // launcher から渡された絶対パスを優先し、相対パスも worker 側で固定化してぶらさない。
        private static string NormalizeThumbFolderPath(string thumbFolder)
        {
            if (string.IsNullOrWhiteSpace(thumbFolder))
            {
                return "";
            }

            string normalized = thumbFolder.Trim();
            if (Path.IsPathRooted(normalized))
            {
                return Path.GetFullPath(normalized);
            }

            return Path.GetFullPath(normalized, AppContext.BaseDirectory);
        }

        private static ThumbnailLayoutProfile ResolveLayoutProfile(int tabIndex)
        {
            // 詳細タブの実行時モードまで含めて、worker 側のレイアウト依存をここへ集約する。
            return ThumbnailLayoutProfileResolver.Resolve(
                tabIndex,
                ThumbnailDetailModeRuntime.ReadRuntimeMode()
            );
        }

        private static string ResolveOutPath(int tabIndex, string dbName, string thumbFolder)
        {
            ThumbnailLayoutProfile layoutProfile = ResolveLayoutProfile(tabIndex);
            string thumbRoot = string.IsNullOrWhiteSpace(thumbFolder)
                ? ThumbRootResolver.GetDefaultThumbRoot(dbName)
                : thumbFolder;
            return layoutProfile.BuildOutPath(thumbRoot);
        }

        private static void DeleteStaleErrorMarker(string thumbFolder, int tabIndex, string moviePath)
        {
            try
            {
                string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                    ResolveOutPath(tabIndex, "", thumbFolder),
                    moviePath
                );
                if (File.Exists(errorMarkerPath))
                {
                    File.Delete(errorMarkerPath);
                }
            }
            catch
            {
                // エラーマーカー掃除に失敗しても救済本体を優先する。
            }
        }

        // 既に正常jpgがある個体は再救済せず、古い pending_rescue を整理する。
        internal static bool TryFindExistingSuccessThumbnailPath(
            string thumbFolder,
            int tabIndex,
            string moviePath,
            out string successThumbnailPath
        )
        {
            successThumbnailPath = "";
            if (string.IsNullOrWhiteSpace(thumbFolder) || string.IsNullOrWhiteSpace(moviePath))
            {
                return false;
            }

            string outPath = ResolveOutPath(tabIndex, "", thumbFolder);
            return ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                outPath,
                moviePath,
                out successThumbnailPath
            );
        }

        // 救済workerは最後の失敗文言から repair 入口を決めるため、語彙漏れはここで吸収する。
        internal static bool ShouldTryIndexRepair(string moviePath, string failureReason)
        {
            if (
                !SupportsRepair(moviePath)
                || string.IsNullOrWhiteSpace(failureReason)
            )
            {
                return false;
            }

            string normalizedReason = failureReason.Trim().ToLowerInvariant();
            for (int i = 0; i < RepairErrorKeywords.Length; i++)
            {
                if (normalizedReason.Contains(RepairErrorKeywords[i]))
                {
                    return true;
                }
            }

            return false;
        }

        // p5 では route ごとに repair gate を分け、fixed/unclassified の取りこぼしを減らす。
        internal static bool ShouldEnterRepairPath(
            string routeId,
            string moviePath,
            ThumbnailFailureKind failureKind,
            string failureReason
        )
        {
            if (!SupportsRepair(moviePath))
            {
                return false;
            }

            if (string.Equals(routeId, CorruptOrPartialRouteId, StringComparison.Ordinal))
            {
                return true;
            }

            string normalizedReason = (failureReason ?? "").Trim().ToLowerInvariant();
            if (string.Equals(routeId, LongNoFramesRouteId, StringComparison.Ordinal))
            {
                return failureKind == ThumbnailFailureKind.TransientDecodeFailure
                    || failureKind == ThumbnailFailureKind.HangSuspected
                    || ContainsAnyKeyword(normalizedReason, LongNoFramesRepairKeywords)
                    || ShouldTryIndexRepair(moviePath, failureReason);
            }

            return ShouldTryIndexRepair(moviePath, failureReason);
        }

        // p3 では失敗種別と軽量情報から route を選び、判定できないものだけ fixed へ逃がす。
        internal static string ClassifyRescueSymptom(
            ThumbnailFailureKind failureKind,
            string failureReason,
            long movieSizeBytes,
            string moviePath
        )
        {
            string normalizedReason = (failureReason ?? "").Trim().ToLowerInvariant();

            if (
                failureKind == ThumbnailFailureKind.IndexCorruption
                || ContainsAnyKeyword(normalizedReason, CorruptionClassificationKeywords)
            )
            {
                return CorruptOrPartialSymptomClass;
            }

            if (ContainsAnyKeyword(normalizedReason, NearBlackClassificationKeywords))
            {
                return NearBlackOrOldFrameSymptomClass;
            }

            bool isNoFramesLike =
                failureKind == ThumbnailFailureKind.TransientDecodeFailure
                || failureKind == ThumbnailFailureKind.HangSuspected
                || ContainsAnyKeyword(normalizedReason, LongNoFramesClassificationKeywords);

            if (
                isNoFramesLike
            )
            {
                if (IsLikelyUltraShort(movieSizeBytes, moviePath))
                {
                    return UltraShortNoFramesSymptomClass;
                }

                if (movieSizeBytes > UltraShortMaxMovieSizeBytes)
                {
                    return LongNoFramesSymptomClass;
                }
            }

            return UnclassifiedSymptomClass;
        }

        // まずコンテナに映像 stream 自体が無いかだけを確定させる。
        private static async Task<VideoIndexProbeResult> TryProbeContainerAsync(
            VideoIndexRepairService repairService,
            ThumbnailFailureRecord leasedRecord,
            MainDbContext mainDbContext,
            string moviePath
        )
        {
            if (repairService == null)
            {
                throw new ArgumentNullException(nameof(repairService));
            }

            TimeSpan timeout = ResolveRepairProbeTimeout();
            Console.WriteLine(
                $"container probe start: failure_id={leasedRecord.FailureId} timeout_sec={timeout.TotalSeconds:0} movie='{moviePath}'"
            );

            try
            {
                VideoIndexProbeResult probeResult = await RunWithTimeoutAsync(
                        cts => repairService.ProbeAsync(moviePath, cts),
                        timeout,
                        $"container probe timeout: failure_id={leasedRecord.FailureId}"
                    )
                    .ConfigureAwait(false);

                string resultLabel = IsDefinitiveNoVideoStreamProbeResult(probeResult)
                    ? "no_video_stream"
                    : string.Equals(
                        probeResult?.DetectionReason,
                        "probe_ok",
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? "video_present"
                        : "indeterminate";
                string reason = BuildContainerProbeReason(probeResult);

                Console.WriteLine(
                    $"container probe end: failure_id={leasedRecord.FailureId} result={resultLabel} reason='{reason}'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "container_probe",
                    result: resultLabel,
                    phase: "container_probe",
                    failureKind: IsDefinitiveNoVideoStreamProbeResult(probeResult)
                        ? ThumbnailFailureKind.NoVideoStream
                        : ThumbnailFailureKind.None,
                    reason: reason
                );
                return probeResult;
            }
            catch (Exception ex)
            {
                string reason = $"probe_exception: {ex.Message}";
                Console.WriteLine(
                    $"container probe failed: failure_id={leasedRecord.FailureId} reason='{reason}'"
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "container_probe",
                    result: "indeterminate",
                    phase: "container_probe",
                    reason: reason
                );
                return new VideoIndexProbeResult
                {
                    MoviePath = moviePath ?? "",
                    DetectionReason = reason,
                    ErrorCode = "probe_exception",
                };
            }
        }

        internal static RescueExecutionPlan BuildRescuePlan(string symptomClass)
        {
            // route ごとに direct 順と repair 有無を固定し、以後の改善点を局所化する。
            return symptomClass switch
            {
                UltraShortNoFramesSymptomClass => new RescueExecutionPlan(
                    UltraShortNoFramesRouteId,
                    UltraShortNoFramesSymptomClass,
                    UltraShortDirectEngineOrder,
                    false,
                    []
                ),
                LongNoFramesSymptomClass => new RescueExecutionPlan(
                    LongNoFramesRouteId,
                    LongNoFramesSymptomClass,
                    LongNoFramesDirectEngineOrder,
                    true,
                    LongNoFramesRepairEngineOrder
                ),
                NearBlackOrOldFrameSymptomClass => new RescueExecutionPlan(
                    NearBlackOrOldFrameRouteId,
                    NearBlackOrOldFrameSymptomClass,
                    NearBlackDirectEngineOrder,
                    false,
                    []
                ),
                CorruptOrPartialSymptomClass => new RescueExecutionPlan(
                    CorruptOrPartialRouteId,
                    CorruptOrPartialSymptomClass,
                    CorruptOrPartialDirectEngineOrder,
                    true,
                    CorruptOrPartialRepairEngineOrder
                ),
                _ => new RescueExecutionPlan(
                    FixedRouteId,
                    UnclassifiedSymptomClass,
                    FixedDirectEngineOrder,
                    true,
                    FixedDirectEngineOrder
                ),
            };
        }

        internal static bool IsDefinitiveNoVideoStreamProbeResult(VideoIndexProbeResult probeResult)
        {
            if (probeResult == null)
            {
                return false;
            }

            if (probeResult.IsIndexCorruptionDetected)
            {
                return false;
            }

            return string.Equals(
                    probeResult.ErrorCode,
                    "video_stream_not_found",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    probeResult.DetectionReason,
                    "video stream not found",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        internal static string BuildNoVideoStreamProbeReason(VideoIndexProbeResult probeResult)
        {
            string format = probeResult?.ContainerFormat ?? "";
            if (string.IsNullOrWhiteSpace(format))
            {
                return "container probe confirmed: no video stream";
            }

            return $"container probe confirmed: no video stream, format={format}";
        }

        internal static string BuildContainerProbeReason(VideoIndexProbeResult probeResult)
        {
            if (probeResult == null)
            {
                return "probe_result_missing";
            }

            string detectionReason = probeResult.DetectionReason ?? "";
            string errorCode = probeResult.ErrorCode ?? "";
            string format = probeResult.ContainerFormat ?? "";
            return $"detection={detectionReason}; code={errorCode}; format={format}";
        }

        // 親行 reason が弱い時は、最初の direct failure を見て fixed から既存 route へ昇格させる。
        internal static RescueExecutionPlan TryPromoteRescuePlan(
            RescueExecutionPlan currentPlan,
            IReadOnlyList<string> attemptedEngines,
            ThumbnailFailureKind failureKind,
            string failureReason,
            long movieSizeBytes,
            string moviePath,
            bool repairApplied
        )
        {
            if (
                repairApplied
                || !string.Equals(currentPlan.RouteId, FixedRouteId, StringComparison.Ordinal)
            )
            {
                return currentPlan;
            }

            string symptomClass = ClassifyRescueSymptom(
                failureKind,
                failureReason,
                movieSizeBytes,
                moviePath
            );
            RescueExecutionPlan candidatePlan = BuildRescuePlan(symptomClass);
            if (
                string.Equals(candidatePlan.RouteId, FixedRouteId, StringComparison.Ordinal)
                || !IsCompatiblePromotion(attemptedEngines, candidatePlan.DirectEngineOrder)
            )
            {
                return currentPlan;
            }

            Console.WriteLine(
                $"rescue plan promoted: from={currentPlan.RouteId} to={candidatePlan.RouteId} symptom={candidatePlan.SymptomClass} attempted={string.Join(">", attemptedEngines)}"
            );
            return candidatePlan;
        }

        // repair probe が negative でも、途中で index 系が見えている個体は残り engine を回す余地がある。
        internal static RescueExecutionPlan TryPromoteAfterRepairProbeNegative(
            RescueExecutionPlan currentPlan,
            ThumbnailFailureKind failureKind,
            string failureReason,
            long movieSizeBytes,
            string moviePath
        )
        {
            if (!string.Equals(currentPlan.RouteId, LongNoFramesRouteId, StringComparison.Ordinal))
            {
                return currentPlan;
            }

            string symptomClass = ClassifyRescueSymptom(
                failureKind,
                failureReason,
                movieSizeBytes,
                moviePath
            );
            RescueExecutionPlan candidatePlan = BuildRescuePlan(symptomClass);
            if (
                string.Equals(candidatePlan.RouteId, CorruptOrPartialRouteId, StringComparison.Ordinal)
            )
            {
                return candidatePlan;
            }

            return currentPlan;
        }

        // ultra-short 系でも全 direct を使い切った結果 index 系が見えたなら、
        // そこで初めて corruption 側へ上げて repair へ入る余地を作る。
        internal static RescueExecutionPlan TryPromoteAfterDirectExhausted(
            RescueExecutionPlan currentPlan,
            ThumbnailFailureKind failureKind,
            string failureReason,
            long movieSizeBytes,
            string moviePath
        )
        {
            if (
                !string.Equals(currentPlan.RouteId, UltraShortNoFramesRouteId, StringComparison.Ordinal)
            )
            {
                return currentPlan;
            }

            string symptomClass = ClassifyRescueSymptom(
                failureKind,
                failureReason,
                movieSizeBytes,
                moviePath
            );
            RescueExecutionPlan candidatePlan = BuildRescuePlan(symptomClass);
            if (
                string.Equals(candidatePlan.RouteId, CorruptOrPartialRouteId, StringComparison.Ordinal)
            )
            {
                return candidatePlan;
            }

            return currentPlan;
        }

        internal static bool ShouldContinueAfterRepairProbeNegative(
            string routeId,
            ThumbnailFailureKind failureKind,
            string failureReason
        )
        {
            string normalizedReason = (failureReason ?? "").Trim().ToLowerInvariant();

            if (string.Equals(routeId, CorruptOrPartialRouteId, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(routeId, LongNoFramesRouteId, StringComparison.Ordinal))
            {
                return failureKind == ThumbnailFailureKind.IndexCorruption
                    || ContainsAnyKeyword(normalizedReason, CorruptionClassificationKeywords);
            }

            return false;
        }

        internal static bool ShouldForceRepairAfterProbeNegative(
            string routeId,
            string moviePath,
            ThumbnailFailureKind directFailureKind,
            string directFailureReason
        )
        {
            if (
                !string.Equals(routeId, CorruptOrPartialRouteId, StringComparison.Ordinal)
                || !SupportsRepair(moviePath)
            )
            {
                return false;
            }

            string extension = Path.GetExtension(moviePath ?? "");
            if (
                !ForcedRepairAfterProbeNegativeExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            string normalizedReason = (directFailureReason ?? "").Trim().ToLowerInvariant();
            return directFailureKind == ThumbnailFailureKind.IndexCorruption
                || ContainsAnyKeyword(normalizedReason, CorruptionClassificationKeywords);
        }

        // WMV/ASF の古い破損系は probe が negative でも remux が効く個体があるため、
        // route が corruption 側まで上がって OpenCV timeout まで見えた時だけ強制 repair を許可する。
        internal static bool ShouldForceRepairAfterProbeNegativeExhausted(
            string routeId,
            string moviePath,
            ThumbnailFailureKind directFailureKind,
            ThumbnailFailureKind finalFailureKind,
            string finalFailureReason
        )
        {
            if (
                !string.Equals(routeId, CorruptOrPartialRouteId, StringComparison.Ordinal)
                || !SupportsRepair(moviePath)
            )
            {
                return false;
            }

            string extension = Path.GetExtension(moviePath ?? "");
            if (
                !ForcedRepairAfterProbeNegativeExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            string normalizedReason = (finalFailureReason ?? "").Trim().ToLowerInvariant();
            return directFailureKind == ThumbnailFailureKind.IndexCorruption
                && (
                    finalFailureKind == ThumbnailFailureKind.HangSuspected
                    || finalFailureKind == ThumbnailFailureKind.TransientDecodeFailure
                    || normalizedReason.Contains("timeout")
                );
        }

        internal static IReadOnlyList<string> BuildRemainingEngineOrder(
            IReadOnlyList<string> fullOrder,
            IReadOnlyList<string> attemptedEngines
        )
        {
            if (fullOrder == null || fullOrder.Count < 1)
            {
                return [];
            }

            HashSet<string> attempted = attemptedEngines == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(attemptedEngines, StringComparer.OrdinalIgnoreCase);
            List<string> remaining = [];
            for (int i = 0; i < fullOrder.Count; i++)
            {
                string engineId = fullOrder[i] ?? "";
                if (string.IsNullOrWhiteSpace(engineId) || attempted.Contains(engineId))
                {
                    continue;
                }

                remaining.Add(engineId);
            }

            return remaining;
        }

        private static bool ContainsAnyKeyword(string normalizedReason, IReadOnlyList<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(normalizedReason))
            {
                return false;
            }

            for (int i = 0; i < keywords.Count; i++)
            {
                if (normalizedReason.Contains(keywords[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCompatiblePromotion(
            IReadOnlyList<string> attemptedEngines,
            IReadOnlyList<string> candidateDirectEngineOrder
        )
        {
            if (attemptedEngines == null || attemptedEngines.Count < 1)
            {
                return false;
            }

            int comparableCount = Math.Min(attemptedEngines.Count, candidateDirectEngineOrder.Count);
            for (int i = 0; i < comparableCount; i++)
            {
                if (
                    !string.Equals(
                        attemptedEngines[i],
                        candidateDirectEngineOrder[i],
                        StringComparison.Ordinal
                    )
                )
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SupportsRepair(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return false;
            }

            string extension = Path.GetExtension(moviePath ?? "");
            return RepairExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsLikelyUltraShort(long movieSizeBytes, string moviePath)
        {
            if (movieSizeBytes <= 0)
            {
                return false;
            }

            string extension = Path.GetExtension(moviePath ?? "");
            if (string.Equals(extension, ".avi", StringComparison.OrdinalIgnoreCase))
            {
                return movieSizeBytes <= UltraShortMaxMovieSizeBytes * 2;
            }

            return movieSizeBytes <= UltraShortMaxMovieSizeBytes;
        }

        private static long TryGetMovieFileLength(string moviePath)
        {
            try
            {
                return new FileInfo(moviePath).Length;
            }
            catch
            {
                return 0;
            }
        }

        private static string BuildRepairOutputPath(string moviePath)
        {
            string repairRoot = Path.Combine(
                Path.GetTempPath(),
                "IndigoMovieManager_fork_workthree",
                "thumbnail-repair"
            );
            Directory.CreateDirectory(repairRoot);

            string extension = Path.GetExtension(moviePath ?? "");
            string normalizedExtension =
                string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase)
                    ? extension
                    : ".mkv";
            return Path.Combine(repairRoot, $"{Guid.NewGuid():N}_repair{normalizedExtension}");
        }

        // 救済worker でも真っ黒jpgは成功扱いにせず、次の勝ち筋へ進める。
        internal static bool TryRejectNearBlackOutput(
            string outputThumbPath,
            out string failureReason
        )
        {
            failureReason = "";
            if (string.IsNullOrWhiteSpace(outputThumbPath) || !File.Exists(outputThumbPath))
            {
                return false;
            }

            if (!IsNearBlackImageFile(outputThumbPath, out double averageLuma))
            {
                return false;
            }

            failureReason = $"near-black thumbnail rejected: avg_luma={averageLuma:0.##}";
            try
            {
                File.Delete(outputThumbPath);
            }
            catch
            {
                // 黒jpgの削除失敗よりも、次のengineへ進めることを優先する。
            }

            return true;
        }

        internal static bool IsNearBlackFailureReason(string failureReason)
        {
            return !string.IsNullOrWhiteSpace(failureReason)
                && failureReason.IndexOf(
                    "near-black thumbnail rejected",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        internal static bool IsNearBlackImageFile(string imagePath, out double averageLuma)
        {
            averageLuma = 0d;
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return false;
            }

            try
            {
                using Bitmap bitmap = new(imagePath);
                return IsNearBlackBitmap(bitmap, out averageLuma);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsNearBlackBitmap(Bitmap source, out double averageLuma)
        {
            averageLuma = 0d;
            if (source == null || source.Width < 1 || source.Height < 1)
            {
                return false;
            }

            double sum = 0d;
            int count = 0;
            for (int y = 0; y < source.Height; y += NearBlackThumbnailSampleStep)
            {
                for (int x = 0; x < source.Width; x += NearBlackThumbnailSampleStep)
                {
                    Color pixel = source.GetPixel(x, y);
                    sum += (0.2126d * pixel.R) + (0.7152d * pixel.G) + (0.0722d * pixel.B);
                    count++;
                }
            }

            if (count < 1)
            {
                return false;
            }

            averageLuma = sum / count;
            return averageLuma <= NearBlackThumbnailLumaThreshold;
        }

        internal static bool IsFailurePlaceholderSuccess(ThumbnailCreateResult result)
        {
            if (result == null || !result.IsSuccess)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(result.ProcessEngineId)
                && result.ProcessEngineId.StartsWith("placeholder-", StringComparison.OrdinalIgnoreCase);
        }

        // attempt_failed の kind が Unknown に寄りすぎると束読みが鈍るため、
        // rescue worker 側でよく出る文言はここで先に failure kind へ寄せる。
        internal static ThumbnailFailureKind ResolveFailureKind(
            Exception ex,
            string moviePath,
            string failureReasonOverride = ""
        )
        {
            return ThumbnailRescueHandoffPolicy.ResolveFailureKind(
                ex,
                moviePath,
                failureReasonOverride
            );
        }

        private static string BuildAttemptExtraJson(
            string engineId,
            bool repairApplied,
            string routeId,
            string symptomClass,
            string traceId = ""
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    WorkerRole = "rescue",
                    RouteId = routeId ?? FixedRouteId,
                    SymptomClass = symptomClass ?? UnclassifiedSymptomClass,
                    EngineForced = engineId ?? "",
                    RepairApplied = repairApplied,
                    TraceId = ThumbnailMovieTraceRuntime.NormalizeTraceId(traceId),
                }
            );
        }

        private static string BuildProgressExtraJson(
            string phase,
            string engineId,
            bool repairApplied,
            string detail,
            int attemptNo,
            string routeId,
            string symptomClass,
            string sourceMovieFullPath,
            ThumbnailFailureKind currentFailureKind,
            string currentFailureReason,
            string traceId = ""
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    WorkerRole = "rescue",
                    RouteId = routeId ?? FixedRouteId,
                    SymptomClass = symptomClass ?? UnclassifiedSymptomClass,
                    CurrentPhase = phase ?? "",
                    CurrentEngine = engineId ?? "",
                    RepairApplied = repairApplied,
                    AttemptNo = attemptNo,
                    SourcePath = sourceMovieFullPath ?? "",
                    CurrentFailureKind = currentFailureKind == ThumbnailFailureKind.None
                        ? ""
                        : currentFailureKind.ToString(),
                    CurrentFailureReason = currentFailureReason ?? "",
                    Detail = detail ?? "",
                    TraceId = ThumbnailMovieTraceRuntime.NormalizeTraceId(traceId),
                }
            );
        }

        private static string BuildTerminalExtraJson(
            string phase,
            string engineId,
            bool repairApplied,
            string detail,
            string traceId = ""
        )
        {
            return BuildTerminalExtraJson(
                phase,
                engineId,
                repairApplied,
                detail,
                FixedRouteId,
                UnclassifiedSymptomClass,
                traceId
            );
        }

        private static string BuildTerminalExtraJson(
            string phase,
            string engineId,
            bool repairApplied,
            string detail,
            string routeId,
            string symptomClass,
            string traceId = ""
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    WorkerRole = "rescue",
                    RouteId = routeId ?? FixedRouteId,
                    SymptomClass = symptomClass ?? UnclassifiedSymptomClass,
                    Phase = phase ?? "",
                    EngineForced = engineId ?? "",
                    RepairApplied = repairApplied,
                    Detail = detail ?? "",
                    TraceId = ThumbnailMovieTraceRuntime.NormalizeTraceId(traceId),
                }
            );
        }

        private static string TryExtractTraceId(string extraJson)
        {
            if (string.IsNullOrWhiteSpace(extraJson))
            {
                return "";
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(extraJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return "";
                }

                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (
                        !string.Equals(property.Name, "trace_id", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(property.Name, "TraceId", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        continue;
                    }

                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return "";
                    }

                    return ThumbnailMovieTraceRuntime.NormalizeTraceId(
                        property.Value.GetString()
                    );
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        internal static bool TryParseArguments(
            string[] args,
            out string mainDbFullPath,
            out string thumbFolderOverride,
            out string logDirectoryPath,
            out string failureDbDirectoryPath
        )
        {
            mainDbFullPath = "";
            thumbFolderOverride = "";
            logDirectoryPath = "";
            failureDbDirectoryPath = "";
            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                if (
                    string.Equals(args[i], "--main-db", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    mainDbFullPath = args[i + 1] ?? "";
                    i++;
                    continue;
                }

                if (
                    string.Equals(args[i], "--thumb-folder", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    thumbFolderOverride = args[i + 1] ?? "";
                    i++;
                    continue;
                }

                if (
                    string.Equals(args[i], "--log-dir", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    logDirectoryPath = args[i + 1] ?? "";
                    i++;
                    continue;
                }

                if (
                    string.Equals(args[i], "--failure-db-dir", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    failureDbDirectoryPath = args[i + 1] ?? "";
                    i++;
                }
            }

            return !string.IsNullOrWhiteSpace(mainDbFullPath);
        }

        private static string ResolveLogDirectoryPathFromArgs(string[] args)
        {
            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                if (
                    string.Equals(args[i], "--log-dir", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    return args[i + 1] ?? "";
                }
            }

            return "";
        }

        private static string ResolveFailureDbDirectoryPathFromArgs(string[] args)
        {
            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                if (
                    string.Equals(args[i], "--failure-db-dir", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    return args[i + 1] ?? "";
                }
            }

            return "";
        }

        private static bool HasArgument(string[] args, string target)
        {
            if (args == null || args.Length < 1 || string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal static TimeSpan ResolveEngineAttemptTimeout()
        {
            return TimeSpan.FromSeconds(
                ResolveTimeoutSeconds(
                    EngineAttemptTimeoutSecEnvName,
                    DefaultEngineAttemptTimeoutSec,
                    minSeconds: 15,
                    maxSeconds: 3600
                )
            );
        }

        // OpenCV は token 非対応で戻りが遅い個体があるため、既定 budget を長めに分けて持つ。
        internal static TimeSpan ResolveEngineAttemptTimeout(string engineId)
        {
            if (string.Equals(engineId, "opencv", StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.FromSeconds(
                    ResolveTimeoutSeconds(
                        OpenCvAttemptTimeoutSecEnvName,
                        DefaultOpenCvAttemptTimeoutSec,
                        minSeconds: 30,
                        maxSeconds: 3600
                    )
                );
            }

            return ResolveEngineAttemptTimeout();
        }

        internal static TimeSpan ResolveRepairProbeTimeout()
        {
            return TimeSpan.FromSeconds(
                ResolveTimeoutSeconds(
                    RepairProbeTimeoutSecEnvName,
                    DefaultRepairProbeTimeoutSec,
                    minSeconds: 15,
                    maxSeconds: 1800
                )
            );
        }

        internal static TimeSpan ResolveRepairTimeout()
        {
            return TimeSpan.FromSeconds(
                ResolveTimeoutSeconds(
                    RepairTimeoutSecEnvName,
                    DefaultRepairTimeoutSec,
                    minSeconds: 30,
                    maxSeconds: 7200
                )
            );
        }

        internal static int ResolveTimeoutSeconds(
            string envName,
            int defaultSeconds,
            int minSeconds,
            int maxSeconds
        )
        {
            string raw = Environment.GetEnvironmentVariable(envName)?.Trim() ?? "";
            if (int.TryParse(raw, out int parsed))
            {
                return Math.Clamp(parsed, minSeconds, maxSeconds);
            }

            return Math.Clamp(defaultSeconds, minSeconds, maxSeconds);
        }

        private static void TryDeleteFileQuietly(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 一時修復ファイルの掃除失敗は観測だけ残して続行する。
            }
        }

        private readonly record struct MainDbContext(string DbName, string ThumbFolder);

        internal readonly record struct RescueExecutionPlan(
            string RouteId,
            string SymptomClass,
            IReadOnlyList<string> DirectEngineOrder,
            bool UseRepairAfterDirect,
            IReadOnlyList<string> RepairEngineOrder
        );

        internal readonly record struct IsolatedEngineAttemptRequest(
            string EngineId,
            string MoviePath,
            string SourceMoviePath,
            string DbName,
            string ThumbFolder,
            int TabIndex,
            long MovieSizeBytes,
            string ThumbSecCsv,
            string ResultJsonPath,
            string LogDirectoryPath,
            string TraceId
        );

        internal readonly record struct UltraShortFrameCandidate(
            string ImagePath,
            double CaptureSec,
            double Score,
            double AverageLuma,
            double AverageSaturation,
            double LumaStdDev
        );

        internal readonly record struct AutogenVirtualDurationRetryPlan(
            double DurationDivisor,
            double VirtualDurationSec,
            ThumbInfo ThumbInfo
        );

        private sealed class IsolatedEngineAttemptResultPayload
        {
            public string SaveThumbFileName { get; set; } = "";
            public double? DurationSec { get; set; }
            public bool IsSuccess { get; set; }
            public string ErrorMessage { get; set; } = "";
        }

        private sealed class RescueAttemptResult
        {
            public bool IsSuccess { get; set; }
            public string EngineId { get; set; } = "";
            public string OutputThumbPath { get; set; } = "";
            public IReadOnlyList<string> AttemptedEngines { get; set; } = [];
            public RescueExecutionPlan EffectiveRescuePlan { get; set; } =
                new(
                    FixedRouteId,
                    UnclassifiedSymptomClass,
                    FixedDirectEngineOrder,
                    true,
                    FixedDirectEngineOrder
                );
            public string LastFailureReason { get; set; } = "";
            public ThumbnailFailureKind LastFailureKind { get; set; } =
                ThumbnailFailureKind.Unknown;
            public int NextAttemptNo { get; set; }
        }
    }
}
