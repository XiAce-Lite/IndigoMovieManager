using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
        // route 判定と repair 昇格ルールはまとめて置き、host 側から読み解きやすくする。
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

        internal static bool ShouldRunRescuePreflightAutogen(
            RescueExecutionPlan rescuePlan,
            bool forceIndexRepair,
            string failureReason
        )
        {
            if (forceIndexRepair)
            {
                return false;
            }

            if (
                string.Equals(
                    rescuePlan.RouteId,
                    CorruptOrPartialRouteId,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }

            return !ShouldSkipRescuePreflightAutogenByFailureReason(failureReason);
        }

        internal static bool ShouldSkipRescuePreflightAutogenByFailureReason(string failureReason)
        {
            string normalizedReason = (failureReason ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedReason))
            {
                return false;
            }

            return ContainsAnyKeyword(normalizedReason, RescuePreflightAutogenSkipKeywords);
        }

        internal static RescueExecutionPlan ApplyFfMediaToolkitAvoidancePolicies(
            RescueExecutionPlan rescuePlan,
            string moviePath,
            long movieSizeBytes,
            double? durationSec,
            string failureReason
        )
        {
            if (
                !IsHighRiskFfMediaToolkitMovie(moviePath, movieSizeBytes, durationSec)
                && !ShouldAvoidFfMediaToolkitByFailureReason(failureReason)
            )
            {
                return rescuePlan;
            }

            return new RescueExecutionPlan(
                rescuePlan.RouteId,
                rescuePlan.SymptomClass,
                RemoveEngine(rescuePlan.DirectEngineOrder, "ffmediatoolkit"),
                rescuePlan.UseRepairAfterDirect,
                RemoveEngine(rescuePlan.RepairEngineOrder, "ffmediatoolkit")
            );
        }

        internal static bool IsHighRiskFfMediaToolkitMovie(
            string moviePath,
            long movieSizeBytes,
            double? durationSec
        )
        {
            if (
                movieSizeBytes < HighRiskFfMediaToolkitMovieSizeBytes
                || !HasKnownExtension(moviePath, HighRiskFfMediaToolkitExtensions)
            )
            {
                return false;
            }

            if (durationSec.HasValue && durationSec.Value > 0d)
            {
                double avgBitrateMbps = (movieSizeBytes * 8d) / (durationSec.Value * 1_000_000d);
                return avgBitrateMbps >= HighRiskFfMediaToolkitAvgBitrateMbps;
            }

            return movieSizeBytes >= HighRiskFfMediaToolkitFallbackMovieSizeBytes;
        }

        internal static bool ShouldAvoidFfMediaToolkitByFailureReason(string failureReason)
        {
            string normalizedReason = (failureReason ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedReason))
            {
                return false;
            }

            return ContainsAnyKeyword(normalizedReason, FfMediaToolkitEbmlFailureKeywords);
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

        private static string[] RemoveEngine(IReadOnlyList<string> engineOrder, string engineId)
        {
            if (engineOrder == null || engineOrder.Count < 1)
            {
                return [];
            }

            return engineOrder
                .Where(x => !string.Equals(x, engineId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static bool HasKnownExtension(string moviePath, IReadOnlyList<string> extensions)
        {
            if (string.IsNullOrWhiteSpace(moviePath) || extensions == null || extensions.Count < 1)
            {
                return false;
            }

            string extension = Path.GetExtension(moviePath) ?? "";
            return extensions.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase));
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

    }
}
