using System.Diagnostics;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// エンジン実行ループと retry / skip / reject を service 本体から切り離す。
    /// </summary>
    internal sealed class ThumbnailEngineExecutionCoordinator
    {
        private readonly ThumbnailEngineExecutionPolicy engineExecutionPolicy;

        public ThumbnailEngineExecutionCoordinator(ThumbnailEngineExecutionPolicy engineExecutionPolicy)
        {
            this.engineExecutionPolicy =
                engineExecutionPolicy ?? throw new ArgumentNullException(nameof(engineExecutionPolicy));
        }

        public async Task<ThumbnailEngineExecutionOutcome> ExecuteAsync(
            IThumbnailGenerationEngine selectedEngine,
            IReadOnlyList<IThumbnailGenerationEngine> engineOrder,
            ThumbnailJobContext context,
            string movieFullPath,
            CancellationToken cts = default
        )
        {
            string processEngineId = selectedEngine?.EngineId ?? "unknown";
            List<string> engineErrorMessages = [];
            ThumbnailCreateResult result = null;

            if (selectedEngine == null || context == null)
            {
                result = ThumbnailCreateResultFactory.CreateFailed(
                    context?.SaveThumbFileName ?? "",
                    context?.DurationSec,
                    "thumbnail engine was not resolved"
                );
                return new ThumbnailEngineExecutionOutcome(result, processEngineId, engineErrorMessages);
            }

            IReadOnlyList<IThumbnailGenerationEngine> safeEngineOrder = engineOrder ?? [];
            for (int i = 0; i < safeEngineOrder.Count; i++)
            {
                IThumbnailGenerationEngine candidate = safeEngineOrder[i];
                processEngineId = candidate.EngineId;
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    i == 0
                        ? $"engine selected: id={candidate.EngineId}, panel={context.PanelCount}, size={context.FileSizeBytes}, avg_mbps={context.AverageBitrateMbps:0.###}, emoji={context.HasEmojiPath}, manual={context.IsManual}, initial_hint='{context.InitialEngineHint}'"
                        : $"engine fallback: from={selectedEngine.EngineId}, to={candidate.EngineId}, attempt={i + 1}/{safeEngineOrder.Count}, initial_hint='{context.InitialEngineHint}'"
                );
                if (
                    engineExecutionPolicy.ShouldRecordFallbackToFfmpegOnePass(
                        selectedEngine,
                        candidate,
                        i
                    )
                )
                {
                    ThumbnailEngineRuntimeStats.RecordFallbackToFfmpegOnePass();
                }

                // 先行エンジンで入力破損が確定している場合、重いffmpeg1pass起動を省略する。
                if (
                    !context.IsManual
                    && string.Equals(
                        candidate.EngineId,
                        "ffmpeg1pass",
                        StringComparison.OrdinalIgnoreCase
                    )
                    && engineExecutionPolicy.ShouldSkipFfmpegOnePassByKnownInvalidInput(
                        engineErrorMessages
                    )
                )
                {
                    const string skipReason = "known invalid input signature";
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"engine skipped: id=ffmpeg1pass, elapsed_ms=0, reason='{skipReason}'"
                    );
                    result = ThumbnailCreateResultFactory.CreateFailed(
                        context.SaveThumbFileName,
                        context.DurationSec,
                        $"ffmpeg1pass skipped: {skipReason}"
                    );
                    engineErrorMessages.Add($"[ffmpeg1pass] skipped: {skipReason}");
                    break;
                }

                Stopwatch sw = Stopwatch.StartNew();
                if (!context.IsManual)
                {
                    // 前回失敗で残った placeholder や古いjpgがあると偽成功しやすいため、毎回掃除してから試す。
                    ThumbnailOutputMarkerCoordinator.ResetExistingOutputBeforeAutomaticAttempt(
                        context.SaveThumbFileName
                    );
                }

                result = await ExecuteCandidateWithRetryAsync(candidate, context, cts);
                sw.Stop();
                if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    engineErrorMessages.Add($"[{candidate.EngineId}] {result.ErrorMessage}");
                }

                if (result.IsSuccess && !context.IsManual && Path.Exists(result.SaveThumbFileName))
                {
                    // 見た目だけ成功した黒jpgは、その場で reject して次候補へ流す。
                    if (
                        ThumbnailNearBlackDetector.IsNearBlackImageFile(
                            result.SaveThumbFileName,
                            out double averageLuma
                        )
                    )
                    {
                        string rejectReason =
                            $"near-black thumbnail rejected: avg_luma={averageLuma:0.##}";
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"engine output rejected: id={candidate.EngineId}, movie='{movieFullPath}', path='{result.SaveThumbFileName}', reason='{rejectReason}'"
                        );
                        ThumbnailOutputMarkerCoordinator.DeleteFileQuietly(result.SaveThumbFileName);
                        result = ThumbnailCreateResultFactory.CreateFailed(
                            context.SaveThumbFileName,
                            context.DurationSec,
                            rejectReason
                        );
                    }
                }

                if (result.IsSuccess)
                {
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"engine succeeded: id={candidate.EngineId}, elapsed_ms={sw.ElapsedMilliseconds}, output='{result.SaveThumbFileName}'"
                    );
                    break;
                }

                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"engine failed: id={candidate.EngineId}, elapsed_ms={sw.ElapsedMilliseconds}, reason='{result.ErrorMessage}', try_next={i < safeEngineOrder.Count - 1}"
                );
            }

            if (result == null)
            {
                result = ThumbnailCreateResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    "thumbnail engine was not executed"
                );
            }

            return new ThumbnailEngineExecutionOutcome(result, processEngineId, engineErrorMessages);
        }

        private async Task<ThumbnailCreateResult> ExecuteCandidateWithRetryAsync(
            IThumbnailGenerationEngine candidate,
            ThumbnailJobContext context,
            CancellationToken cts
        )
        {
            bool isAutogenCandidate = string.Equals(
                candidate.EngineId,
                "autogen",
                StringComparison.OrdinalIgnoreCase
            );
            int autogenRetryCount = 0;
            bool transientFailureRecorded = false;

            while (true)
            {
                ThumbnailCreateResult result;
                try
                {
                    result = await candidate.CreateAsync(context, cts);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // エンジン内部例外は失敗結果へ畳んで、既存どおり次候補へ流せる形にする。
                    result = ThumbnailCreateResultFactory.CreateFailed(
                        context.SaveThumbFileName,
                        context.DurationSec,
                        ex.Message
                    );
                }

                if (result == null)
                {
                    result = ThumbnailCreateResultFactory.CreateFailed(
                        context.SaveThumbFileName,
                        context.DurationSec,
                        "thumbnail engine returned null result"
                    );
                }

                ThumbnailAutogenRetryDecision retryDecision =
                    engineExecutionPolicy.EvaluateAutogenRetry(
                        candidate,
                        result,
                        autogenRetryCount
                    );
                if (retryDecision.IsTransientFailure && !transientFailureRecorded)
                {
                    transientFailureRecorded = true;
                    ThumbnailEngineRuntimeStats.RecordAutogenTransientFailure();
                }

                if (retryDecision.CanRetry)
                {
                    autogenRetryCount++;
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"engine retry scheduled: id=autogen, attempt={autogenRetryCount}/{retryDecision.MaxRetryCount}, delay_ms={retryDecision.RetryDelayMs}, reason='{result.ErrorMessage}'"
                    );
                    if (retryDecision.RetryDelayMs > 0)
                    {
                        await Task.Delay(retryDecision.RetryDelayMs, cts).ConfigureAwait(false);
                    }
                    continue;
                }

                if (isAutogenCandidate && autogenRetryCount > 0 && result.IsSuccess)
                {
                    ThumbnailEngineRuntimeStats.RecordAutogenRetrySuccess();
                    ThumbnailRuntimeLog.Write("thumbnail", "engine retry success: id=autogen");
                }

                return result;
            }
        }
    }

    internal sealed class ThumbnailEngineExecutionOutcome
    {
        public ThumbnailEngineExecutionOutcome(
            ThumbnailCreateResult result,
            string processEngineId,
            IReadOnlyList<string> engineErrorMessages
        )
        {
            Result = result;
            ProcessEngineId = processEngineId ?? "";
            EngineErrorMessages = engineErrorMessages ?? [];
        }

        public ThumbnailCreateResult Result { get; }
        public string ProcessEngineId { get; }
        public IReadOnlyList<string> EngineErrorMessages { get; }
    }
}
