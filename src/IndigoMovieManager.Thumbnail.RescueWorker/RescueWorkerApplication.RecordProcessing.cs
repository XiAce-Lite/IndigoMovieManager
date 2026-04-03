using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
        // 入口直後の終端条件を host 本体から外し、レコード処理の読み筋を単純にする。
        private static bool TryCompleteMissingMovieAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            string moviePath
        )
        {
            if (!string.IsNullOrWhiteSpace(moviePath) && File.Exists(moviePath))
            {
                return false;
            }

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
            return true;
        }

        // 既存成功サムネがあればここで終端し、metadata 欠落再生成だけ本線へ残す。
        private static bool TryCompleteExistingSuccessThumbnailAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            MainDbContext mainDbContext,
            string moviePath
        )
        {
            if (
                !TryFindExistingSuccessThumbnailPath(
                    mainDbContext.ThumbFolder,
                    leasedRecord.TabIndex,
                    moviePath,
                    out string existingSuccessThumbnailPath
                )
            )
            {
                return false;
            }

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
                return false;
            }

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
            return true;
        }

        // probe で動画ストリーム欠落が確定した個体は、ここで terminal へ落とす。
        private static bool TryCompleteDefinitiveNoVideoStreamProbeAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            MainDbContext mainDbContext,
            VideoIndexProbeResult containerProbeResult
        )
        {
            if (!IsDefinitiveNoVideoStreamProbeResult(containerProbeResult))
            {
                return false;
            }

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
            return true;
        }

        // direct phase の前処理と preflight 分岐をまとめ、主経路では結果だけを見る。
        private static async Task<DirectRescuePhaseResult> RunDirectRescuePhaseAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            IThumbnailCreationService thumbnailCreationService,
            MainDbContext mainDbContext,
            RescueExecutionPlan rescuePlan,
            int nextAttemptNo,
            string logDirectoryPath,
            string rescueMode,
            bool forceIndexRepair,
            string moviePath
        )
        {
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
                return new DirectRescuePhaseResult(rescuePlan, directResult, nextAttemptNo);
            }

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

            return new DirectRescuePhaseResult(
                directResult.EffectiveRescuePlan,
                directResult,
                directResult.NextAttemptNo
            );
        }

        // direct phase 実行と after-direct 判定をひとかたまりにし、host では結果だけをつなぐ。
        private static async Task<DirectWorkflowResult> RunDirectWorkflowAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            IThumbnailCreationService thumbnailCreationService,
            MainDbContext mainDbContext,
            RescueExecutionPlan rescuePlan,
            string logDirectoryPath,
            string rescueMode,
            bool forceIndexRepair,
            string moviePath
        )
        {
            int nextAttemptNo = Math.Max(leasedRecord.AttemptNo + 1, 2);
            DirectRescuePhaseResult directPhaseResult = await RunDirectRescuePhaseAsync(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    queueObj,
                    thumbnailCreationService,
                    mainDbContext,
                    rescuePlan,
                    nextAttemptNo,
                    logDirectoryPath,
                    rescueMode,
                    forceIndexRepair,
                    moviePath
                )
                .ConfigureAwait(false);
            RescueAttemptResult directResult = directPhaseResult.DirectResult;
            AfterDirectDecisionResult afterDirectDecision = await TryCompleteAfterDirectPhaseAsync(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    queueObj,
                    mainDbContext,
                    directPhaseResult.RescuePlan,
                    directResult,
                    moviePath
                )
                .ConfigureAwait(false);
            if (afterDirectDecision.IsCompleted)
            {
                return new DirectWorkflowResult(
                    true,
                    afterDirectDecision.RescuePlan,
                    directResult,
                    ThumbnailFailureKind.None,
                    "",
                    afterDirectDecision.NextAttemptNo
                );
            }

            return new DirectWorkflowResult(
                false,
                afterDirectDecision.RescuePlan,
                directResult,
                afterDirectDecision.RepairTriggerFailureKind,
                afterDirectDecision.RepairTriggerFailureReason,
                afterDirectDecision.NextAttemptNo
            );
        }

        // direct phase の終端と repair 非進入の終端をここでまとめ、主経路では結果だけを見る。
        private static async Task<AfterDirectDecisionResult> TryCompleteAfterDirectPhaseAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            MainDbContext mainDbContext,
            RescueExecutionPlan rescuePlan,
            RescueAttemptResult directResult,
            string moviePath
        )
        {
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
                return new AfterDirectDecisionResult(true, rescuePlan, ThumbnailFailureKind.None, "", directResult.NextAttemptNo);
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
                    return new AfterDirectDecisionResult(true, rescuePlan, ThumbnailFailureKind.None, "", directResult.NextAttemptNo);
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
                return new AfterDirectDecisionResult(true, rescuePlan, ThumbnailFailureKind.None, "", directResult.NextAttemptNo);
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
                    return new AfterDirectDecisionResult(true, rescuePlan, ThumbnailFailureKind.None, "", directResult.NextAttemptNo);
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
                return new AfterDirectDecisionResult(true, rescuePlan, ThumbnailFailureKind.None, "", directResult.NextAttemptNo);
            }

            return new AfterDirectDecisionResult(
                false,
                rescuePlan,
                directResult.LastFailureKind,
                directResult.LastFailureReason,
                directResult.NextAttemptNo
            );
        }

        // repair 実行後の終端はここへ寄せ、host 本体には分岐骨格だけを残す。
        private static async Task<string> RunRepairExecuteAndPostRepairRescueAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            IThumbnailCreationService thumbnailCreationService,
            MainDbContext mainDbContext,
            VideoIndexRepairService repairService,
            RescueExecutionPlan rescuePlan,
            int nextAttemptNo,
            string logDirectoryPath,
            string rescueMode,
            string moviePath,
            ThumbnailFailureKind repairTriggerFailureKind,
            string repairTriggerFailureReason,
            bool keepRepairedMovieFile,
            TimeSpan repairTimeout
        )
        {
            string repairedMoviePath = BuildRepairOutputPath(moviePath, keepRepairedMovieFile);
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
                    return repairedMoviePath;
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
                return repairedMoviePath;
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
                return repairedMoviePath;
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
                return repairedMoviePath;
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
            return repairedMoviePath;
        }

        // repair probe negative 時の fallback / give up / force repair をまとめる。
        private static async Task<RepairProbeDecisionResult> TryHandleRepairProbeNegativeAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            IThumbnailCreationService thumbnailCreationService,
            MainDbContext mainDbContext,
            RescueExecutionPlan rescuePlan,
            RescueAttemptResult directResult,
            int nextAttemptNo,
            string logDirectoryPath,
            string rescueMode,
            string moviePath,
            VideoIndexProbeResult probeResult
        )
        {
            ThumbnailFailureKind repairTriggerFailureKind = directResult.LastFailureKind;
            string repairTriggerFailureReason = directResult.LastFailureReason;
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
                    return new RepairProbeDecisionResult(true, rescuePlan, ThumbnailFailureKind.None, "", postProbeResult.NextAttemptNo);
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
                    return new RepairProbeDecisionResult(
                        false,
                        rescuePlan,
                        postProbeResult.LastFailureKind,
                        postProbeResult.LastFailureReason,
                        postProbeResult.NextAttemptNo
                    );
                }

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
                    return new RepairProbeDecisionResult(true, rescuePlan, ThumbnailFailureKind.None, "", postProbeResult.NextAttemptNo);
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
                return new RepairProbeDecisionResult(true, rescuePlan, ThumbnailFailureKind.None, "", postProbeResult.NextAttemptNo);
            }

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
                return new RepairProbeDecisionResult(
                    false,
                    rescuePlan,
                    repairTriggerFailureKind,
                    repairTriggerFailureReason,
                    nextAttemptNo
                );
            }

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
                return new RepairProbeDecisionResult(true, rescuePlan, ThumbnailFailureKind.None, "", nextAttemptNo);
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
            return new RepairProbeDecisionResult(true, rescuePlan, ThumbnailFailureKind.None, "", nextAttemptNo);
        }

        // repair probe の開始・終了と forced skip をここへ寄せる。
        private static async Task<RepairProbePhaseResult> RunRepairProbePhaseAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            IThumbnailCreationService thumbnailCreationService,
            MainDbContext mainDbContext,
            VideoIndexRepairService repairService,
            RescueExecutionPlan rescuePlan,
            RescueAttemptResult directResult,
            int nextAttemptNo,
            string logDirectoryPath,
            string rescueMode,
            string moviePath,
            bool forceIndexRepair
        )
        {
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
                return new RepairProbePhaseResult(
                    false,
                    rescuePlan,
                    ThumbnailFailureKind.IndexCorruption,
                    "manual index repair requested",
                    nextAttemptNo
                );
            }

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
            if (probeResult.IsIndexCorruptionDetected)
            {
                return new RepairProbePhaseResult(
                    false,
                    rescuePlan,
                    directResult.LastFailureKind,
                    directResult.LastFailureReason,
                    nextAttemptNo
                );
            }

            RepairProbeDecisionResult probeDecision = await TryHandleRepairProbeNegativeAsync(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    queueObj,
                    thumbnailCreationService,
                    mainDbContext,
                    rescuePlan,
                    directResult,
                    nextAttemptNo,
                    logDirectoryPath,
                    rescueMode,
                    moviePath,
                    probeResult
                )
                .ConfigureAwait(false);
            return new RepairProbePhaseResult(
                probeDecision.IsCompleted,
                probeDecision.RescuePlan,
                probeDecision.RepairTriggerFailureKind,
                probeDecision.RepairTriggerFailureReason,
                probeDecision.NextAttemptNo
            );
        }

        private readonly record struct DirectRescuePhaseResult(
            RescueExecutionPlan RescuePlan,
            RescueAttemptResult DirectResult,
            int NextAttemptNo
        );

        private readonly record struct DirectWorkflowResult(
            bool IsCompleted,
            RescueExecutionPlan RescuePlan,
            RescueAttemptResult DirectResult,
            ThumbnailFailureKind RepairTriggerFailureKind,
            string RepairTriggerFailureReason,
            int NextAttemptNo
        );

        private readonly record struct AfterDirectDecisionResult(
            bool IsCompleted,
            RescueExecutionPlan RescuePlan,
            ThumbnailFailureKind RepairTriggerFailureKind,
            string RepairTriggerFailureReason,
            int NextAttemptNo
        );

        private readonly record struct RepairProbeDecisionResult(
            bool IsCompleted,
            RescueExecutionPlan RescuePlan,
            ThumbnailFailureKind RepairTriggerFailureKind,
            string RepairTriggerFailureReason,
            int NextAttemptNo
        );

        private readonly record struct RepairProbePhaseResult(
            bool IsCompleted,
            RescueExecutionPlan RescuePlan,
            ThumbnailFailureKind RepairTriggerFailureKind,
            string RepairTriggerFailureReason,
            int NextAttemptNo
        );
    }
}
