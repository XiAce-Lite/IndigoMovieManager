using System.Diagnostics;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 1バッチ分の lease 実行と、次バッチ向けの並列調整をまとめて扱う。
    /// </summary>
    internal static class ThumbnailQueueBatchRunner
    {
        private const int SlowLaneThrottleMinParallelism = 3;

        public static async Task<ThumbnailQueueBatchRunResult> RunAsync(
            QueueDbService queueDbService,
            string ownerInstanceId,
            IReadOnlyList<QueueDbLeaseItem> leasedItems,
            int runtimeLeaseBatchSize,
            int safeLeaseMinutes,
            Func<int?> preferredTabIndexResolver,
            Func<IReadOnlyList<string>> preferredMoviePathKeysResolver,
            Func<QueueObj, string> handoffLaneResolver,
            Func<QueueObj, CancellationToken, Task> createThumbAsync,
            ThumbnailQueueProgressPublisher progressPublisher,
            ThumbnailQueueBatchState batchState,
            ThumbnailParallelController parallelController,
            Func<int> resolveLatestConfiguredParallelism,
            Action<string> log,
            CancellationToken cts
        )
        {
            Action<string> safeLog = log ?? (_ => { });
            Stopwatch batchSw = Stopwatch.StartNew();

            // バッチ開始時点の残件を拾い、進捗側が見る総数を先に確定する。
            int activeCountAtBatchStart = queueDbService.GetActiveQueueCount(ownerInstanceId);
            batchState.BeginBatch(activeCountAtBatchStart, leasedItems.Count);

            int configuredParallelism = resolveLatestConfiguredParallelism();
            int currentParallelism = parallelController.EnsureWithinConfigured(
                configuredParallelism
            );
            if (activeCountAtBatchStart >= 2)
            {
                // バックログがある間は 1 並列貼り付きで先頭表示が止まらないようにする。
                currentParallelism = parallelController.EnsureMinimum(configuredParallelism, 2);
            }

            int maxWorkerPoolParallelism = ThumbnailParallelController.Clamp(int.MaxValue);
            DynamicParallelGate parallelGate = new(currentParallelism, maxWorkerPoolParallelism);
            bool enableSlowLaneThrottle = currentParallelism >= SlowLaneThrottleMinParallelism;
            using CancellationTokenSource parallelMonitorCts =
                CancellationTokenSource.CreateLinkedTokenSource(cts);
            Task parallelMonitorTask = ThumbnailParallelLimitMonitor.RunAsync(
                parallelGate,
                resolveLatestConfiguredParallelism,
                parallelController,
                safeLog,
                parallelMonitorCts.Token
            );
            SemaphoreSlim slowLaneSemaphore = enableSlowLaneThrottle
                ? new SemaphoreSlim(1, 1)
                : null;

            int GetLiveParallelism()
            {
                return parallelGate.CurrentLimit;
            }

            async ValueTask ProcessLeasedItemAsync(
                QueueDbLeaseItem leasedItem,
                CancellationToken token
            )
            {
                QueueObj queueObj = new()
                {
                    MovieFullPath = leasedItem.MoviePath,
                    MovieSizeBytes = leasedItem.MovieSizeBytes,
                    Tabindex = leasedItem.TabIndex,
                    ThumbPanelPos = leasedItem.ThumbPanelPos,
                    ThumbTimePos = leasedItem.ThumbTimePos,
                };
                ThumbnailExecutionLane lane = ThumbnailLaneClassifier.ResolveLane(
                    leasedItem.MovieSizeBytes
                );
                // 実行レーン制御は従来どおりサイズ基準を維持し、救済への受け渡し名だけ呼び出し側指定を尊重する。
                string handoffLaneName = ResolveHandoffLaneName(
                    queueObj,
                    lane,
                    handoffLaneResolver
                );
                bool leaseEntered = false;
                bool slowLaneEntered = false;
                bool startedNotified = false;

                try
                {
                    await parallelGate.WaitAsync(token).ConfigureAwait(false);
                    leaseEntered = true;
                    if (lane == ThumbnailExecutionLane.Slow && slowLaneSemaphore != null)
                    {
                        await slowLaneSemaphore.WaitAsync(token).ConfigureAwait(false);
                        slowLaneEntered = true;
                    }

                    progressPublisher.NotifyJobStarted(queueObj);
                    startedNotified = true;

                    try
                    {
                        await ThumbnailLeaseHeartbeatRunner.ExecuteWithLeaseHeartbeatAsync(
                                queueDbService,
                                leasedItem,
                                ownerInstanceId,
                                safeLeaseMinutes,
                                () => createThumbAsync(queueObj, token),
                                safeLog,
                                token
                            )
                            .ConfigureAwait(false);

                        int updated = queueDbService.UpdateStatus(
                            leasedItem.QueueId,
                            ownerInstanceId,
                            ThumbnailQueueStatus.Done,
                            DateTime.UtcNow
                        );
                        if (updated < 1)
                        {
                            safeLog(
                                $"consumer done skipped: queue_id={leasedItem.QueueId} owner={ownerInstanceId}"
                            );
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        if (cts.IsCancellationRequested)
                        {
                            throw;
                        }

                        // 個別ジョブ都合のキャンセルは失敗として閉じ、他ジョブは継続する。
                        ThumbnailFailureRecorder.HandleFailedItem(
                            queueDbService,
                            leasedItem,
                            ownerInstanceId,
                            ex,
                            handoffLaneName,
                            safeLog
                        );
                        batchState.MarkJobFailed();
                    }
                    catch (Exception ex)
                    {
                        ThumbnailFailureRecorder.HandleFailedItem(
                            queueDbService,
                            leasedItem,
                            ownerInstanceId,
                            ex,
                            handoffLaneName,
                            safeLog
                        );
                        batchState.MarkJobFailed();
                    }

                    int doneInSession = batchState.MarkJobCompleted();
                    progressPublisher.ReportJobCompleted(
                        leasedItem.TabIndex,
                        leasedItem.MoviePath,
                        doneInSession,
                        batchState.SessionTotalCount,
                        GetLiveParallelism(),
                        resolveLatestConfiguredParallelism()
                    );
                }
                finally
                {
                    if (startedNotified)
                    {
                        progressPublisher.NotifyJobCompleted(queueObj);
                    }
                    if (slowLaneEntered)
                    {
                        slowLaneSemaphore?.Release();
                    }
                    if (leaseEntered)
                    {
                        parallelGate.Release();
                    }
                }
            }

            try
            {
                await Parallel.ForEachAsync(
                    ThumbnailLeaseCoordinator.EnumerateLeasedItemsAsync(
                        queueDbService,
                        ownerInstanceId,
                        leasedItems,
                        runtimeLeaseBatchSize,
                        safeLeaseMinutes,
                        preferredTabIndexResolver,
                        preferredMoviePathKeysResolver,
                        safeLog,
                        cts
                    ),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = maxWorkerPoolParallelism,
                        CancellationToken = cts,
                    },
                    ProcessLeasedItemAsync
                );
            }
            finally
            {
                parallelMonitorCts.Cancel();
                try
                {
                    await parallelMonitorTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // monitor 停止時のキャンセルは想定内。
                }
                finally
                {
                    slowLaneSemaphore?.Dispose();
                }
            }

            batchSw.Stop();
            int liveParallelism = GetLiveParallelism();
            ThumbnailEngineRuntimeSnapshot engineSnapshot =
                ThumbnailEngineRuntimeStats.ConsumeWindow();
            int activeCountAfterBatch = queueDbService.GetActiveQueueCount(ownerInstanceId);
            int latestConfiguredParallelism = resolveLatestConfiguredParallelism();
            int nextParallelism = parallelController.EvaluateNext(
                latestConfiguredParallelism,
                batchState.BatchCompletedCount,
                batchState.BatchFailedCount,
                activeCountAfterBatch,
                engineSnapshot,
                safeLog,
                queueDemandPeakCount: activeCountAtBatchStart
            );

            ThumbnailQueuePerfLogger.LogBatchSummary(
                batchState,
                batchSw.ElapsedMilliseconds,
                liveParallelism,
                nextParallelism,
                latestConfiguredParallelism,
                activeCountAfterBatch,
                engineSnapshot
            );

            return new ThumbnailQueueBatchRunResult(
                liveParallelism,
                nextParallelism,
                latestConfiguredParallelism
            );
        }

        // handoff 未指定時だけ従来のサイズ分類へ戻し、失敗時の lane 名を安定させる。
        private static string ResolveHandoffLaneName(
            QueueObj queueObj,
            ThumbnailExecutionLane executionLane,
            Func<QueueObj, string> handoffLaneResolver
        )
        {
            if (handoffLaneResolver != null)
            {
                string resolvedLaneName = handoffLaneResolver(queueObj);
                if (!string.IsNullOrWhiteSpace(resolvedLaneName))
                {
                    return resolvedLaneName;
                }
            }

            return executionLane == ThumbnailExecutionLane.Slow ? "slow" : "normal";
        }
    }

    /// <summary>
    /// 1 バッチ実行後に outer loop が引き継ぐ値だけをまとめる。
    /// </summary>
    internal readonly struct ThumbnailQueueBatchRunResult
    {
        public ThumbnailQueueBatchRunResult(
            int liveParallelism,
            int nextParallelism,
            int latestConfiguredParallelism
        )
        {
            LiveParallelism = liveParallelism;
            NextParallelism = nextParallelism;
            LatestConfiguredParallelism = latestConfiguredParallelism;
        }

        public int LiveParallelism { get; }
        public int NextParallelism { get; }
        public int LatestConfiguredParallelism { get; }
    }
}
