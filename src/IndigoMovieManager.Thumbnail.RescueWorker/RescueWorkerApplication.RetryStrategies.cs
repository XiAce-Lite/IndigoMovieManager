using System.Drawing;
using System.Globalization;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
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
    }
}