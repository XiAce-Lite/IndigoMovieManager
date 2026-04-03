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
    internal sealed partial class RescueWorkerApplication
    {
        private const string AttemptChildModeArg = "--attempt-child";
        private const string DirectIndexRepairModeArg = "--direct-index-repair";
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
        private const long DirectIndexRepairMinOutputBytes = 1L * 1024L * 1024L;
        private const double DirectIndexRepairMinOutputRatio = 0.10d;
        private const int ExperimentalFinalSeekRescueTimeoutSec = 300;
        private const int ExperimentalFinalSeekSampleCount = 12;
        private const int ExperimentalFinalSeekScaleWidth = 320;
        private const double NearBlackThumbnailLumaThreshold = 2d;
        private const int NearBlackThumbnailSampleStep = 4;
        private const long UltraShortMaxMovieSizeBytes = 4L * 1024L * 1024L;
        private const double UltraShortDecimalRetryDurationThresholdSec = 1d;
        private const double UltraShortDecimalRetryFallbackDurationSec = 0.2d;
        private const double LongDurationNearBlackVirtualRetryThresholdSec = 2d * 60d * 60d;
        private const long HighRiskFfMediaToolkitMovieSizeBytes = 20L * 1024L * 1024L * 1024L;
        private const long HighRiskFfMediaToolkitFallbackMovieSizeBytes =
            40L * 1024L * 1024L * 1024L;
        private const double HighRiskFfMediaToolkitAvgBitrateMbps = 80d;
        private const string DecimalNearBlackRetryEngineId = "black-retry-decimal-ffmpeg";
        private const string ExperimentalFinalSeekRescueEngineId = "final-seek-ffmpeg";
        private const string ForceIndexRepairRescueMode = "force-index-repair";
        private const string DarkHeavyBackgroundRescueMode = "dark-heavy-background";
        private const string DarkHeavyBackgroundLiteRescueMode = "dark-heavy-background-lite";
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
        private static readonly string[] HighRiskFfMediaToolkitExtensions = [".mkv", ".webm"];
        private static readonly string[] FfMediaToolkitEbmlFailureKeywords =
        [
            "invalid as first byte of an ebml number",
            "matroska,webm",
            "ebml number",
            "ebml",
        ];
        private static readonly string[] RescuePreflightAutogenSkipKeywords =
        [
            "autogen",
            "near-black",
            "near black",
            "old frame",
            "black frame",
            "dark frame",
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
        private static readonly double[] DarkHeavyBackgroundRetryRatios =
        [
            0.03d,
            0.05d,
            0.08d,
            0.12d,
            0.18d,
            0.25d,
            0.35d,
            0.50d,
            0.65d,
            0.80d,
            0.90d,
        ];
        private static readonly double[] DarkHeavyBackgroundUltraShortRetryRatios =
        [
            0.03d,
            0.05d,
            0.08d,
            0.10d,
            0.12d,
            0.15d,
            0.20d,
            0.25d,
            0.35d,
            0.50d,
            0.70d,
            0.85d,
        ];
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

            if (HasArgument(args, DirectIndexRepairModeArg))
            {
                if (!TryParseDirectIndexRepairArguments(args, out DirectIndexRepairRequest directIndexRepairRequest))
                {
                    Console.Error.WriteLine(
                        "usage: IndigoMovieManager.Thumbnail.RescueWorker --direct-index-repair --movie <path> [--log-dir <path>]"
                    );
                    return 2;
                }

                return await RunDirectIndexRepairAsync(directIndexRepairRequest).ConfigureAwait(false);
            }

            if (
                !TryParseArguments(
                    args,
                    out string mainDbFullPath,
                    out string thumbFolderOverride,
                    out string logDirectoryPath,
                    out string failureDbDirectoryPath,
                    out long requestedFailureId
                )
            )
            {
                Console.Error.WriteLine(
                    "usage: IndigoMovieManager.Thumbnail.RescueWorker --main-db <path> [--thumb-folder <path>] [--log-dir <path>] [--failure-db-dir <path>]"
                );
                return 2;
            }

            return await RunMainRescueAsync(
                    mainDbFullPath,
                    thumbFolderOverride,
                    logDirectoryPath,
                    failureDbDirectoryPath,
                    requestedFailureId
                )
                .ConfigureAwait(false);
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
                if (
                    ShouldReplaceExistingSuccessThumbnailWhenMetadataMissing(
                        leasedRecord.ExtraJson,
                        existingSuccessThumbnailPath
                    )
                )
                {
                    Console.WriteLine(
                        $"rescue existing success metadata missing: failure_id={leasedRecord.FailureId} output='{existingSuccessThumbnailPath}' action='replace'"
                    );
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "terminal",
                        result: "replace_missing_metadata",
                        phase: "existing_success_missing_metadata",
                        outputPath: existingSuccessThumbnailPath,
                        reason: "existing success thumbnail metadata is missing"
                    );
                }
                else
                {
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
            string rescueMode = TryExtractRescueMode(leasedRecord.ExtraJson);
            bool forceIndexRepair = IsForceIndexRepairRescueMode(rescueMode);
            bool keepRepairedMovieFile = forceIndexRepair;
            string symptomClass = ClassifyRescueSymptom(
                leasedRecord.FailureKind,
                leasedRecord.FailureReason,
                queueObj.MovieSizeBytes,
                moviePath
            );
            RescueExecutionPlan rescuePlan = BuildRescuePlan(symptomClass);
            if (forceIndexRepair)
            {
                rescuePlan = BuildRescuePlan(CorruptOrPartialSymptomClass);
            }
            // 超巨大 MKV/WebM は ffmediatoolkit がネイティブ側で固着しやすいため、
            // rescue では順番から外して他エンジンを先に通す。
            RescueExecutionPlan originalRescuePlan = rescuePlan;
            rescuePlan = ApplyFfMediaToolkitAvoidancePolicies(
                rescuePlan,
                moviePath,
                queueObj.MovieSizeBytes,
                TryProbeDurationSecWithFfprobe(moviePath),
                leasedRecord.FailureReason
            );
            Console.WriteLine(
                $"rescue plan selected: failure_id={leasedRecord.FailureId} route={rescuePlan.RouteId} symptom={rescuePlan.SymptomClass} direct={string.Join(">", rescuePlan.DirectEngineOrder)} repair={rescuePlan.UseRepairAfterDirect}"
            );
            if (!originalRescuePlan.Equals(rescuePlan))
            {
                Console.WriteLine(
                    $"rescue plan adjusted: failure_id={leasedRecord.FailureId} policy=avoid-ffmediatoolkit-risk direct={string.Join(">", rescuePlan.DirectEngineOrder)} repair={string.Join(">", rescuePlan.RepairEngineOrder)}"
                );
            }
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
            RescueAttemptResult directResult = new()
            {
                IsSuccess = false,
                EffectiveRescuePlan = rescuePlan,
                LastFailureKind = ThumbnailFailureKind.Unknown,
                LastFailureReason = "",
                NextAttemptNo = nextAttemptNo,
                AttemptedEngines = [],
            };
            if (forceIndexRepair)
            {
                string forcedReason = "manual index repair requested";
                UpdateProgressSnapshot(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    phase: "repair_forced",
                    engineId: "",
                    repairApplied: true,
                    detail: forcedReason,
                    attemptNo: nextAttemptNo,
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    sourceMovieFullPath: moviePath,
                    currentFailureKind: ThumbnailFailureKind.IndexCorruption,
                    currentFailureReason: forcedReason
                );
                WriteRescueTrace(
                    leasedRecord,
                    mainDbContext.DbName,
                    mainDbContext.ThumbFolder,
                    action: "repair_probe",
                    result: "forced",
                    routeId: rescuePlan.RouteId,
                    symptomClass: rescuePlan.SymptomClass,
                    phase: "repair_forced",
                    failureKind: ThumbnailFailureKind.IndexCorruption,
                    reason: forcedReason
                );
                directResult = new RescueAttemptResult
                {
                    IsSuccess = false,
                    EffectiveRescuePlan = rescuePlan,
                    LastFailureKind = ThumbnailFailureKind.IndexCorruption,
                    LastFailureReason = forcedReason,
                    NextAttemptNo = nextAttemptNo,
                    AttemptedEngines = [],
                };
            }
            else
            {
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

                RescueAttemptResult preflightAutogenResult = null;
                if (
                    ShouldRunRescuePreflightAutogen(
                        rescuePlan,
                        forceIndexRepair,
                        leasedRecord.FailureReason
                    )
                )
                {
                    preflightAutogenResult = await TryRunRescuePreflightAutogenAsync(
                            failureDbService,
                            leasedRecord,
                            leaseOwner,
                            queueObj,
                            thumbnailCreationService,
                            mainDbContext,
                            rescuePlan,
                            nextAttemptNo,
                            logDirectoryPath
                        )
                        .ConfigureAwait(false);
                    rescuePlan = preflightAutogenResult.EffectiveRescuePlan;
                    nextAttemptNo = preflightAutogenResult.NextAttemptNo;
                    if (preflightAutogenResult.IsSuccess)
                    {
                        directResult = preflightAutogenResult;
                    }
                    else
                    {
                        UpdateProgressSnapshot(
                            failureDbService,
                            leasedRecord,
                            leaseOwner,
                            phase: "direct_start",
                            engineId: "",
                            repairApplied: false,
                            detail: "after-preflight-autogen",
                            attemptNo: nextAttemptNo,
                            routeId: rescuePlan.RouteId,
                            symptomClass: rescuePlan.SymptomClass,
                            sourceMovieFullPath: moviePath,
                            currentFailureKind: ThumbnailFailureKind.None,
                            currentFailureReason: ""
                        );
                    }
                }

                if (preflightAutogenResult == null || !preflightAutogenResult.IsSuccess)
                {
                    directResult = await RunEngineAttemptsAsync(
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
                            logDirectoryPath: logDirectoryPath,
                            rescueMode: rescueMode
                        )
                        .ConfigureAwait(false);
                }
            }
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
                TimeSpan repairTimeout = ResolveRepairTimeout();
                if (forceIndexRepair)
                {
                    Console.WriteLine(
                        $"repair probe skipped: failure_id={leasedRecord.FailureId} reason='manual index repair requested'"
                    );
                    WriteRescueTrace(
                        leasedRecord,
                        mainDbContext.DbName,
                        mainDbContext.ThumbFolder,
                        action: "repair_probe",
                        result: "skipped_forced",
                        routeId: rescuePlan.RouteId,
                        symptomClass: rescuePlan.SymptomClass,
                        phase: "repair_forced",
                        failureKind: ThumbnailFailureKind.IndexCorruption,
                        reason: "manual index repair requested"
                    );
                }
                else
                {
                    TimeSpan repairProbeTimeout = ResolveRepairProbeTimeout();
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
                                logDirectoryPath: logDirectoryPath,
                                rescueMode: rescueMode
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

                }

                repairedMoviePath = BuildRepairOutputPath(moviePath, keepRepairedMovieFile);
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
                        logDirectoryPath: logDirectoryPath,
                        rescueMode: rescueMode
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
                if (!keepRepairedMovieFile)
                {
                    TryDeleteFileQuietly(repairedMoviePath);
                }
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

            string saveThumbFileName = ResolveThumbnailOutputPath(
                queueObj,
                mainDbContext,
                sourceMovieFullPath
            );
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
                AppIdentityRuntime.ResolveStorageRootName(),
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

        // 通常側の一時詰まりだけで rescue に落ちた個体を、重い route へ入る前に薄く拾い直す。
        private static async Task<RescueAttemptResult> TryRunRescuePreflightAutogenAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            IThumbnailCreationService thumbnailCreationService,
            MainDbContext mainDbContext,
            RescueExecutionPlan rescuePlan,
            int nextAttemptNo,
            string logDirectoryPath
        )
        {
            Console.WriteLine(
                $"rescue preflight autogen start: failure_id={leasedRecord.FailureId} route={rescuePlan.RouteId}"
            );
            WriteRescueTrace(
                leasedRecord,
                mainDbContext.DbName,
                mainDbContext.ThumbFolder,
                action: "preflight_autogen",
                result: "start",
                routeId: rescuePlan.RouteId,
                symptomClass: rescuePlan.SymptomClass,
                phase: "direct_preflight",
                engine: "autogen"
            );

            RescueAttemptResult result = await RunEngineAttemptsAsync(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    queueObj,
                    thumbnailCreationService,
                    mainDbContext,
                    repairApplied: false,
                    rescuePlan: rescuePlan,
                    engineOrder: ["autogen"],
                    sourceMovieFullPathOverride: null,
                    nextAttemptNo: nextAttemptNo,
                    preserveProvidedEngineOrder: true,
                    logDirectoryPath: logDirectoryPath,
                    rescueMode: ""
                )
                .ConfigureAwait(false);

            Console.WriteLine(
                $"rescue preflight autogen end: failure_id={leasedRecord.FailureId} success={result.IsSuccess} next_attempt={result.NextAttemptNo}"
            );
            WriteRescueTrace(
                leasedRecord,
                mainDbContext.DbName,
                mainDbContext.ThumbFolder,
                action: "preflight_autogen",
                result: result.IsSuccess ? "success" : "failed",
                routeId: result.EffectiveRescuePlan.RouteId,
                symptomClass: result.EffectiveRescuePlan.SymptomClass,
                phase: "direct_preflight",
                engine: result.IsSuccess ? result.EngineId : "autogen",
                failureKind: result.LastFailureKind,
                reason: result.IsSuccess ? "" : result.LastFailureReason,
                outputPath: result.IsSuccess ? result.OutputThumbPath : ""
            );
            return result;
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
            string logDirectoryPath = "",
            string rescueMode = ""
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
                    bool forceDarkHeavyBackgroundRetry = false;
                    bool allowNearBlackSuccess = ShouldAllowDarkHeavyBackgroundLiteSuccess(
                        rescueMode,
                        engineId
                    );
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
                    else if (
                        isSuccess
                        && ShouldForceDarkHeavyBackgroundRetry(rescueMode, engineId)
                    )
                    {
                        nearBlackReason = "manual dark-heavy-background retry requested";
                        shouldRunNearBlackRetry = true;
                        forceDarkHeavyBackgroundRetry = true;
                        WriteRescueTrace(
                            leasedRecord,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            action: "black_retry",
                            result: "forced",
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
                                logDirectoryPath,
                                rescueMode,
                                forceDarkHeavyBackgroundRetry,
                                allowNearBlackSuccess
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
                    promotedPlan = ApplyFfMediaToolkitAvoidancePolicies(
                        promotedPlan,
                        queueObj.MovieFullPath,
                        queueObj.MovieSizeBytes,
                        createResult?.DurationSec,
                        failureReason
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
                    promotedPlan = ApplyFfMediaToolkitAvoidancePolicies(
                        promotedPlan,
                        queueObj.MovieFullPath,
                        queueObj.MovieSizeBytes,
                        null,
                        ex.Message
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
            string logDirectoryPath,
            string rescueMode,
            bool forceDarkHeavyBackgroundRetry,
            bool allowNearBlackSuccess
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
                resolvedDurationSec,
                rescueMode
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
                        initialFailureReason,
                        rescueMode,
                        forceDarkHeavyBackgroundRetry,
                        allowNearBlackSuccess
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
                Console.WriteLine(
                    $"black retry start: failure_id={leasedRecord.FailureId} engine={engineId} retry={i + 1}/{retryThumbInfos.Count} secs={thumbSecLabel} repair={repairApplied}"
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
                    reason: $"retry={i + 1}/{retryThumbInfos.Count}; secs={thumbSecLabel}; mode={NormalizeRescueMode(rescueMode)}"
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
                    if (allowNearBlackSuccess)
                    {
                        WriteRescueTrace(
                            leasedRecord,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            action: "black_retry",
                            result: "accepted_dark",
                            routeId: rescuePlan.RouteId,
                            symptomClass: rescuePlan.SymptomClass,
                            phase: phase,
                            engine: engineId,
                            reason: $"{retryNearBlackReason}; secs={thumbSecLabel}; mode={NormalizeRescueMode(rescueMode)}",
                            outputPath: retryResult.SaveThumbFileName
                        );
                        return retryResult;
                    }

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
                        reason: $"{retryNearBlackReason}; secs={thumbSecLabel}; mode={NormalizeRescueMode(rescueMode)}"
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
                        reason: $"secs={thumbSecLabel}; mode={NormalizeRescueMode(rescueMode)}",
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
                    reason: $"{lastFailureReason}; secs={thumbSecLabel}; mode={NormalizeRescueMode(rescueMode)}"
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
            string initialFailureReason,
            string rescueMode,
            bool forceDarkHeavyBackgroundRetry,
            bool allowNearBlackSuccess
        )
        {
            IReadOnlyList<double> retryCaptureSecs = BuildUltraShortNearBlackRetryCaptureSeconds(
                durationSec,
                rescueMode
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
            string saveThumbFileName = ResolveThumbnailOutputPath(
                queueObj,
                mainDbContext,
                sourceMovieFullPath
            );
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
                        detail: $"retry={i + 1}/{retryCaptureSecs.Count}; secs={captureSecLabel}; mode=decimal:{NormalizeRescueMode(rescueMode)}",
                        attemptNo: attemptNo,
                        routeId: rescuePlan.RouteId,
                        symptomClass: rescuePlan.SymptomClass,
                        sourceMovieFullPath: sourceMovieFullPath,
                    currentFailureKind: ThumbnailFailureKind.Unknown,
                    currentFailureReason: lastFailureReason
                );
                    Console.WriteLine(
                        $"black retry start: failure_id={leasedRecord.FailureId} engine={DecimalNearBlackRetryEngineId} retry={i + 1}/{retryCaptureSecs.Count} secs={captureSecLabel} repair={repairApplied}"
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
                        reason: $"retry={i + 1}/{retryCaptureSecs.Count}; secs={captureSecLabel}; mode=decimal:{NormalizeRescueMode(rescueMode)}"
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
                            reason: $"{lastFailureReason}; secs={captureSecLabel}; mode=decimal:{NormalizeRescueMode(rescueMode)}"
                        );
                        continue;
                    }

                    if (
                        !forceDarkHeavyBackgroundRetry
                        && !allowNearBlackSuccess
                        && TryRejectNearBlackOutput(candidate.ImagePath, out string retryNearBlackReason)
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
                            reason: $"{retryNearBlackReason}; secs={captureSecLabel}; mode=decimal:{NormalizeRescueMode(rescueMode)}"
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
                            $"secs={captureSecLabel}; mode=decimal:{NormalizeRescueMode(rescueMode)}; score={candidate.Score:0.##}; luma={candidate.AverageLuma:0.##}; sat={candidate.AverageSaturation:0.##}",
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
                            reason: $"secs={selectedSecs}; mode=decimal-multi:{NormalizeRescueMode(rescueMode)}; candidates={candidates.Count}",
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
                AppIdentityRuntime.ResolveStorageRootName(),
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
                Console.WriteLine(
                    $"black retry start: failure_id={leasedRecord.FailureId} engine=autogen retry={i + 1}/{retryPlans.Count} secs={thumbSecLabel} repair={repairApplied}"
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
                AppIdentityRuntime.ResolveStorageRootName(),
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
                AppIdentityRuntime.ResolveStorageRootName(),
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

        // 明示救済フラグ付きかつWB互換メタ欠落の時だけ、既存jpgありでも再生成へ進める。
        internal static bool ShouldReplaceExistingSuccessThumbnailWhenMetadataMissing(
            string extraJson,
            string existingSuccessThumbnailPath
        )
        {
            return ShouldReplaceThumbnailWhenMetadataMissing(extraJson)
                && !HasWhiteBrowserThumbnailMetadata(existingSuccessThumbnailPath);
        }

        // manual 差し替えやクリック再生が依存するWB互換メタの有無だけを軽く判定する。
        internal static bool HasWhiteBrowserThumbnailMetadata(string thumbnailPath)
        {
            return WhiteBrowserThumbInfoSerializer.TryReadFromJpeg(
                    thumbnailPath,
                    out ThumbnailSheetSpec spec
                )
                && spec?.CaptureSeconds != null
                && spec.CaptureSeconds.Count > 0;
        }

        // 救済workerは最後の失敗文言から repair 入口を決めるため、語彙漏れはここで吸収する。
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

        private static string BuildRepairOutputPath(string moviePath, bool preserveNamedCopy = false)
        {
            string repairRoot = preserveNamedCopy
                ? Path.GetDirectoryName(moviePath ?? "") ?? ""
                : Path.Combine(
                    Path.GetTempPath(),
                    AppIdentityRuntime.ResolveStorageRootName(),
                    "thumbnail-repair"
                );
            if (string.IsNullOrWhiteSpace(repairRoot))
            {
                repairRoot = Path.Combine(
                    Path.GetTempPath(),
                    AppIdentityRuntime.ResolveStorageRootName(),
                    "thumbnail-repair"
                );
            }
            Directory.CreateDirectory(repairRoot);

            string extension = Path.GetExtension(moviePath ?? "");
            string normalizedExtension =
                string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase)
                    ? extension
                    : ".mkv";
            if (!preserveNamedCopy)
            {
                return Path.Combine(repairRoot, $"{Guid.NewGuid():N}_repair{normalizedExtension}");
            }

            string movieBody = Path.GetFileNameWithoutExtension(moviePath ?? "") ?? "repair";
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
            return Path.Combine(repairRoot, $"{movieBody}_repair_{stamp}{normalizedExtension}");
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

        private static string TryExtractRescueMode(string extraJson)
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
                        !string.Equals(property.Name, "rescue_mode", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(property.Name, "RescueMode", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        continue;
                    }

                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return "";
                    }

                    return NormalizeRescueMode(property.Value.GetString());
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        private static bool ShouldReplaceThumbnailWhenMetadataMissing(string extraJson)
        {
            if (string.IsNullOrWhiteSpace(extraJson))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(extraJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (
                        !string.Equals(
                            property.Name,
                            "replace_if_metadata_missing",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && !string.Equals(
                            property.Name,
                            "ReplaceIfMetadataMissing",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        continue;
                    }

                    if (property.Value.ValueKind == JsonValueKind.True)
                    {
                        return true;
                    }

                    if (property.Value.ValueKind == JsonValueKind.False)
                    {
                        return false;
                    }

                    if (
                        property.Value.ValueKind == JsonValueKind.String
                        && bool.TryParse(property.Value.GetString(), out bool parsed)
                    )
                    {
                        return parsed;
                    }

                    return false;
                }

                return false;
            }
            catch
            {
                return false;
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

        internal readonly record struct DirectIndexRepairRequest(
            string MoviePath,
            string LogDirectoryPath
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
