using System.Diagnostics;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
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
