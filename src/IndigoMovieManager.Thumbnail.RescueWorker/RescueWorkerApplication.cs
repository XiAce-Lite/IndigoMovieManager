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
            if (IsJobJsonModeCommand(args))
            {
                if (!TryParseJobJsonArguments(args, out string jobJsonPath, out string resultJsonPath))
                {
                    Console.Error.WriteLine(JobJsonModeUsageText);
                    return 2;
                }

                return await RunJobJsonModeAsync(jobJsonPath, resultJsonPath).ConfigureAwait(false);
            }

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
            if (
                TryCompleteMissingMovieAsync(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    moviePath
                )
            )
            {
                return;
            }

            MainDbContext mainDbContext = ResolveMainDbContext(
                leasedRecord.MainDbFullPath,
                thumbFolderOverride
            );
            if (
                TryCompleteExistingSuccessThumbnailAsync(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    mainDbContext,
                    moviePath
                )
            )
            {
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
            if (
                TryCompleteDefinitiveNoVideoStreamProbeAsync(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    mainDbContext,
                    containerProbeResult
                )
            )
            {
                return;
            }

            DeleteStaleErrorMarker(mainDbContext.ThumbFolder, leasedRecord.TabIndex, moviePath);

            InitialRescueExecutionContext initialContext = BuildInitialRescueExecutionContext(
                leasedRecord,
                mainDbContext,
                moviePath
            );
            QueueObj queueObj = initialContext.QueueObj;
            string rescueMode = initialContext.RescueMode;
            bool forceIndexRepair = initialContext.ForceIndexRepair;
            bool keepRepairedMovieFile = initialContext.KeepRepairedMovieFile;
            RescueExecutionPlan rescuePlan = initialContext.RescuePlan;

            IThumbnailCreationService thumbnailCreationService =
                RescueWorkerThumbnailCreationServiceFactory.Create(logDirectoryPath);
            DirectWorkflowResult directWorkflowResult = await RunDirectWorkflowAsync(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    queueObj,
                    thumbnailCreationService,
                    mainDbContext,
                    rescuePlan,
                    logDirectoryPath,
                    rescueMode,
                    forceIndexRepair,
                    moviePath
                )
                .ConfigureAwait(false);
            if (directWorkflowResult.IsCompleted)
            {
                return;
            }

            rescuePlan = directWorkflowResult.RescuePlan;
            int nextAttemptNo = directWorkflowResult.NextAttemptNo;
            RescueAttemptResult directResult = directWorkflowResult.DirectResult;
            ThumbnailFailureKind repairTriggerFailureKind = directWorkflowResult.RepairTriggerFailureKind;
            string repairTriggerFailureReason = directWorkflowResult.RepairTriggerFailureReason;

            string repairedMoviePath = "";
            try
            {
                TimeSpan repairTimeout = ResolveRepairTimeout();
                RepairProbePhaseResult repairProbePhaseResult = await RunRepairProbePhaseAsync(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        queueObj,
                        thumbnailCreationService,
                        mainDbContext,
                        repairService,
                        rescuePlan,
                        directResult,
                        nextAttemptNo,
                        logDirectoryPath,
                        rescueMode,
                        moviePath,
                        forceIndexRepair
                    )
                    .ConfigureAwait(false);
                if (repairProbePhaseResult.IsCompleted)
                {
                    return;
                }

                rescuePlan = repairProbePhaseResult.RescuePlan;
                nextAttemptNo = repairProbePhaseResult.NextAttemptNo;
                repairTriggerFailureKind = repairProbePhaseResult.RepairTriggerFailureKind;
                repairTriggerFailureReason = repairProbePhaseResult.RepairTriggerFailureReason;

                repairedMoviePath = await RunRepairExecuteAndPostRepairRescueAsync(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        queueObj,
                        thumbnailCreationService,
                        mainDbContext,
                        repairService,
                        rescuePlan,
                        nextAttemptNo,
                        logDirectoryPath,
                        rescueMode,
                        moviePath,
                        repairTriggerFailureKind,
                        repairTriggerFailureReason,
                        keepRepairedMovieFile,
                        repairTimeout
                    )
                    .ConfigureAwait(false);
                return;
            }
            finally
            {
                if (!keepRepairedMovieFile)
                {
                    TryDeleteFileQuietly(repairedMoviePath);
                }
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

    }
}
