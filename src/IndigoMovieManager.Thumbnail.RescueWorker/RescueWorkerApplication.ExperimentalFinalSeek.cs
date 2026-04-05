using System.Diagnostics;
using System.Drawing;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
        // 最後の勝ち筋だけを別 partial へ出し、host 本体から特殊救済の密度を下げる。
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
            return ThumbnailEnvConfig.IsSlowLaneMovie(queueObj?.MovieSizeBytes ?? 0);
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
    }
}
