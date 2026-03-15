using System.Drawing;
using System.Data.SQLite;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
        private const double NearBlackThumbnailLumaThreshold = 2d;
        private const int NearBlackThumbnailSampleStep = 4;
        private const long UltraShortMaxMovieSizeBytes = 4L * 1024L * 1024L;
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

        public async Task<int> RunAsync(string[] args)
        {
            if (HasArgument(args, AttemptChildModeArg))
            {
                if (!TryParseIsolatedAttemptArguments(args, out IsolatedEngineAttemptRequest attemptRequest))
                {
                    Console.Error.WriteLine(
                        "usage: IndigoMovieManager.Thumbnail.RescueWorker --attempt-child --engine <id> --movie <path> --db-name <name> --thumb-folder <path> --tab-index <index> --movie-size-bytes <size> --result-json <path> [--source-movie <path>]"
                    );
                    return 2;
                }

                return await RunIsolatedAttemptChildAsync(attemptRequest).ConfigureAwait(false);
            }

            if (!TryParseArguments(args, out string mainDbFullPath, out string thumbFolderOverride))
            {
                Console.Error.WriteLine(
                    "usage: IndigoMovieManager.Thumbnail.RescueWorker --main-db <path> [--thumb-folder <path>]"
                );
                return 2;
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
                $"rescue leased: failure_id={leasedRecord.FailureId} movie='{leasedRecord.MoviePath}'"
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
                        thumbFolderOverride
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
            string thumbFolderOverride
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

            ThumbnailCreationService thumbnailCreationService = new();
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
                    nextAttemptNo: nextAttemptNo
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
                                preserveProvidedEngineOrder: true
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
                        preserveProvidedEngineOrder: true
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

        // 失敗時も試行をappendし、後から比較できるようにする。
        private static async Task<RescueAttemptResult> RunEngineAttemptsAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            ThumbnailCreationService thumbnailCreationService,
            MainDbContext mainDbContext,
            bool repairApplied,
            RescueExecutionPlan rescuePlan,
            IReadOnlyList<string> engineOrder,
            string sourceMovieFullPathOverride,
            int nextAttemptNo,
            bool preserveProvidedEngineOrder = false
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
                            thumbInfoOverride: null
                        )
                        .ConfigureAwait(false);
                    attemptedEngines.Add(engineId);

                    bool isSuccess =
                        createResult != null
                        && createResult.IsSuccess
                        && !string.IsNullOrWhiteSpace(createResult.SaveThumbFileName)
                        && File.Exists(createResult.SaveThumbFileName);
                    if (
                        isSuccess
                        && TryRejectNearBlackOutput(
                            createResult.SaveThumbFileName,
                            out string nearBlackReason
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
                                createResult.DurationSec,
                                nearBlackReason
                            )
                            .ConfigureAwait(false);
                        isSuccess =
                            createResult != null
                            && createResult.IsSuccess
                            && !string.IsNullOrWhiteSpace(createResult.SaveThumbFileName)
                            && File.Exists(createResult.SaveThumbFileName);
                    }
                    sw.Stop();

                    if (isSuccess)
                    {
                        Console.WriteLine(
                            $"engine attempt success: failure_id={leasedRecord.FailureId} engine={engineId} elapsed_ms={sw.ElapsedMilliseconds} output='{createResult.SaveThumbFileName}'"
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
                            engine: engineId,
                            elapsedMs: sw.ElapsedMilliseconds,
                            outputPath: createResult.SaveThumbFileName
                        );
                        result.IsSuccess = true;
                        result.EngineId = engineId;
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
            ThumbnailCreationService thumbnailCreationService,
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
            IReadOnlyList<ThumbInfo> retryThumbInfos = BuildNearBlackRetryThumbInfos(
                queueObj?.Tabindex ?? 0,
                mainDbContext.DbName,
                mainDbContext.ThumbFolder,
                durationSec
            );
            if (retryThumbInfos.Count < 1)
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
                        retryThumbInfo
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
                DurationSec = durationSec,
                IsSuccess = false,
                ErrorMessage = lastFailureReason,
            };
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
            ThumbnailCreationService thumbnailCreationService,
            QueueObj queueObj,
            MainDbContext mainDbContext,
            string engineId,
            string sourceMovieFullPathOverride,
            TimeSpan timeout,
            string timeoutMessage,
            ThumbInfo thumbInfoOverride
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
                        thumbInfoOverride
                    )
                    .ConfigureAwait(false);
            }

            // エンジン切替はプロセス環境変数を使うため、このworkerは1プロセス1動画前提で動かす。
            Environment.SetEnvironmentVariable(ThumbnailEnvConfig.ThumbEngine, engineId);
            return await RunWithTimeoutAsync(
                    cts =>
                        thumbnailCreationService.CreateThumbAsync(
                            queueObj,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            isResizeThumb: false,
                            isManual: false,
                            cts: cts,
                            sourceMovieFullPathOverride: sourceMovieFullPathOverride,
                            thumbInfoOverride: thumbInfoOverride
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
            ThumbInfo thumbInfoOverride
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
                resultJsonPath
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

            TabInfo tabInfo = new(tabIndex, dbName ?? "", thumbFolder ?? "");
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

                retryThumbInfos.Add(BuildUniformThumbInfo(tabInfo, captureSec));
            }

            return retryThumbInfos;
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

            return BuildExplicitThumbInfo(new TabInfo(tabIndex, dbName ?? "", thumbFolder ?? ""), captureSecs);
        }

        private static ThumbInfo BuildUniformThumbInfo(TabInfo tabInfo, int captureSec)
        {
            int thumbCount = Math.Max(1, tabInfo?.DivCount ?? 1);
            int[] captureSecs = Enumerable.Repeat(Math.Max(0, captureSec), thumbCount).ToArray();
            return BuildExplicitThumbInfo(tabInfo, captureSecs);
        }

        private static ThumbInfo BuildExplicitThumbInfo(TabInfo tabInfo, IReadOnlyList<int> captureSecs)
        {
            int thumbCount = Math.Max(1, tabInfo?.DivCount ?? 1);
            ThumbInfo thumbInfo = new()
            {
                ThumbWidth = tabInfo?.Width ?? 160,
                ThumbHeight = tabInfo?.Height ?? 120,
                ThumbRows = tabInfo?.Rows ?? 1,
                ThumbColumns = tabInfo?.Columns ?? 1,
                ThumbCounts = thumbCount,
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
                thumbInfo.Add(captureSec);
            }
            thumbInfo.NewThumbInfo();
            return thumbInfo;
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

        private static async Task<int> RunIsolatedAttemptChildAsync(
            IsolatedEngineAttemptRequest request
        )
        {
            ThumbnailCreateResult result;

            try
            {
                Environment.SetEnvironmentVariable(ThumbnailEnvConfig.ThumbEngine, request.EngineId);
                ThumbnailCreationService thumbnailCreationService = new();
                QueueObj queueObj = new()
                {
                    MovieFullPath = request.MoviePath ?? "",
                    MovieSizeBytes = Math.Max(0, request.MovieSizeBytes),
                    Tabindex = request.TabIndex,
                };
                result = await thumbnailCreationService
                    .CreateThumbAsync(
                        queueObj,
                        request.DbName,
                        request.ThumbFolder,
                        isResizeThumb: false,
                        isManual: false,
                        cts: CancellationToken.None,
                        sourceMovieFullPathOverride: string.Equals(
                            request.SourceMoviePath,
                            request.MoviePath,
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? null
                            : request.SourceMoviePath,
                        thumbInfoOverride: BuildThumbInfoFromCsv(
                            request.TabIndex,
                            request.DbName,
                            request.ThumbFolder,
                            request.ThumbSecCsv
                        )
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
                    case "--result-json" when i + 1 < args.Length:
                        resultJsonPath = args[++i] ?? "";
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
                resultJsonPath
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
                        symptomClass
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
                    currentFailureReason
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

        private static void DeleteStaleErrorMarker(string thumbFolder, int tabIndex, string moviePath)
        {
            try
            {
                TabInfo tabInfo = new(tabIndex, "", thumbFolder);
                string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                    tabInfo.OutPath,
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

        // attempt_failed の kind が Unknown に寄りすぎると束読みが鈍るため、
        // rescue worker 側でよく出る文言はここで先に failure kind へ寄せる。
        internal static ThumbnailFailureKind ResolveFailureKind(
            Exception ex,
            string moviePath,
            string failureReasonOverride = ""
        )
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
                    // ファイル状態判定失敗時は文言判定へ進む。
                }
            }

            string normalized = string.IsNullOrWhiteSpace(failureReasonOverride)
                ? (ex?.Message ?? "").Trim().ToLowerInvariant()
                : failureReasonOverride.Trim().ToLowerInvariant();
            if (
                normalized.Contains("thumbnail normal lane timeout")
                || normalized.Contains("engine attempt timeout")
            )
            {
                return ThumbnailFailureKind.HangSuspected;
            }
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
                || normalized.Contains("frame decode failed")
                || normalized.Contains("partial file")
                || normalized.Contains("broken index")
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
            if (normalized.Contains("ffmpeg one-pass failed"))
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

        private static string BuildAttemptExtraJson(
            string engineId,
            bool repairApplied,
            string routeId,
            string symptomClass
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
            string currentFailureReason
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
                }
            );
        }

        private static string BuildTerminalExtraJson(
            string phase,
            string engineId,
            bool repairApplied,
            string detail
        )
        {
            return BuildTerminalExtraJson(
                phase,
                engineId,
                repairApplied,
                detail,
                FixedRouteId,
                UnclassifiedSymptomClass
            );
        }

        private static string BuildTerminalExtraJson(
            string phase,
            string engineId,
            bool repairApplied,
            string detail,
            string routeId,
            string symptomClass
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
                }
            );
        }

        private static bool TryParseArguments(
            string[] args,
            out string mainDbFullPath,
            out string thumbFolderOverride
        )
        {
            mainDbFullPath = "";
            thumbFolderOverride = "";
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
                }
            }

            return !string.IsNullOrWhiteSpace(mainDbFullPath);
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
            string ResultJsonPath
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
