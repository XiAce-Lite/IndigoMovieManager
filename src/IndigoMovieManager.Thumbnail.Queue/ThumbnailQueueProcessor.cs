using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【キュー処理の心臓部（コンシューマー）】🔥
    /// QueueDB（SQLite）を絶対の掟とし、裏でひたすらサムネ生成ジョブをさばき続ける最強の戦士だ！
    ///
    /// ＜アツい全体の流れ＞
    /// 1. DBから「未処理(Pending)」のジョブをごっそり取得し、誰にも触らせないようガッチリロック！🔒
    /// 2. 取ってきたジョブを Parallel.ForEachAsync で並列にブン回す（ThumbnailCreationServiceにバトンタッチ）！🏃‍♂️💨
    /// 3. 長丁場になりそうなら、定期的に「まだまだ処理中だぜ！」とDBに叫んで（ハートビート）ロックを延長！💓
    /// 4. 成功すれば「Done」の勲章を、失敗時は再試行回数を盛って「Pending」か「Failed」に叩き込む！💥
    /// </summary>
    public sealed class ThumbnailQueueProcessor
    {
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string ThumbFileLogEnvName = "IMM_THUMB_FILE_LOG";
        private static readonly object PerfLogLock = new();
        private static long _totalProcessedCount = 0;
        private static long _totalElapsedMs = 0;

        private const int DefaultMaxAttemptCount = 5;
        private const int LeaseHeartbeatSeconds = 30;
        private const int SlowLaneThrottleMinParallelism = 3;

        /// <summary>
        /// 全闘争の幕開け！キューの監視と処理を絶え間なく回し続けるメインループだ！
        /// アプリが息をしている限り、バックグラウンドで果てしなく働き続ける不眠不休のエンジン！⚙️
        /// </summary>
        public async Task RunAsync(
            Func<QueueDbService> queueDbServiceResolver,
            string ownerInstanceId,
            Func<QueueObj, CancellationToken, Task> createThumbAsync,
            int maxParallelism = 8,
            Func<int> maxParallelismResolver = null,
            int pollIntervalMs = 3000,
            int leaseMinutes = 5,
            int leaseBatchSize = 8,
            Func<int?> preferredTabIndexResolver = null,
            Action<string> log = null,
            Func<CancellationToken, Task> onQueueDrainedAsync = null,
            Action<int, int, int, int> progressSnapshot = null,
            Action<QueueObj> onJobStarted = null,
            Action<QueueObj> onJobCompleted = null,
            IThumbnailQueueProgressPresenter progressPresenter = null,
            CancellationToken cts = default
        )
        {
            if (queueDbServiceResolver == null)
            {
                throw new ArgumentNullException(nameof(queueDbServiceResolver));
            }
            if (string.IsNullOrWhiteSpace(ownerInstanceId))
            {
                throw new ArgumentException(
                    "ownerInstanceId is required.",
                    nameof(ownerInstanceId)
                );
            }
            if (createThumbAsync == null)
            {
                throw new ArgumentNullException(nameof(createThumbAsync));
            }

            string title = "サムネイル作成中";
            int safePollIntervalMs = pollIntervalMs < 100 ? 100 : pollIntervalMs;
            int safeLeaseMinutes = leaseMinutes < 1 ? 1 : leaseMinutes;
            Action<string> safeLog = log ?? (_ => { });
            IThumbnailQueueProgressPresenter safeProgressPresenter =
                progressPresenter ?? NoOpThumbnailQueueProgressPresenter.Instance;
            int initialConfiguredParallelism = ResolveConfiguredParallelism(
                maxParallelism,
                maxParallelismResolver
            );
            ThumbnailParallelController parallelController = new(initialConfiguredParallelism);
            int ResolveLatestConfiguredParallelism()
            {
                return ResolveConfiguredParallelism(maxParallelism, maxParallelismResolver);
            }

            try
            {
                // アプリ終了要求（cts）が来るまで無限ループで監視を続ける
                while (true)
                {
                    cts.ThrowIfCancellationRequested();
                    QueueDbService queueDbService = queueDbServiceResolver();
                    if (queueDbService == null)
                    {
                        await Task.Delay(safePollIntervalMs, cts).ConfigureAwait(false);
                        continue;
                    }

                    // 【STEP 1: 処理対象の取得（リース）】
                    // DBから未処理のジョブを取得し、「自分が処理する」という印をつける
                    int configuredParallelism = ResolveConfiguredParallelism(
                        maxParallelism,
                        maxParallelismResolver
                    );
                    int currentParallelism = parallelController.EnsureWithinConfigured(
                        configuredParallelism
                    );
                    ReportProgressSnapshot(
                        progressSnapshot,
                        0,
                        0,
                        currentParallelism,
                        ResolveLatestConfiguredParallelism()
                    );
                    int runtimeLeaseBatchSize = ResolveLeaseBatchSize(
                        leaseBatchSize,
                        currentParallelism
                    );
                    List<QueueDbLeaseItem> leasedItems = AcquireLeasedItems(
                        queueDbService,
                        ownerInstanceId,
                        runtimeLeaseBatchSize,
                        safeLeaseMinutes,
                        preferredTabIndexResolver,
                        safeLog
                    );

                    // DB上で処理対象が無い時だけ待機し、後回しジョブ確認を呼ぶ。
                    if (leasedItems.Count < 1)
                    {
                        if (onQueueDrainedAsync != null)
                        {
                            await onQueueDrainedAsync(cts).ConfigureAwait(false);
                        }
                        ReportProgressSnapshot(
                            progressSnapshot,
                            0,
                            0,
                            currentParallelism,
                            ResolveLatestConfiguredParallelism()
                        );
                        await Task.Delay(safePollIntervalMs, cts).ConfigureAwait(false);
                        continue;
                    }

                    object progressLock = new();
                    int sessionCompletedCount = 0;
                    int sessionTotalCount = 0;
                    ReportProgressSnapshot(
                        progressSnapshot,
                        sessionCompletedCount,
                        sessionTotalCount,
                        currentParallelism,
                        ResolveLatestConfiguredParallelism()
                    );
                    IThumbnailQueueProgressHandle progress = NoOpThumbnailQueueProgressHandle.Instance;
                    try
                    {
                        // 表示層の失敗でキュー処理本体を止めない。
                        progress =
                            safeProgressPresenter.Show(title)
                            ?? NoOpThumbnailQueueProgressHandle.Instance;
                    }
                    catch (Exception ex)
                    {
                        safeLog($"consumer progress open failed: {ex.Message}");
                    }
                    safeLog("consumer progress opened.");

                    try
                    {
                        while (true)
                        {
                            if (leasedItems.Count < 1)
                            {
                                if (onQueueDrainedAsync != null)
                                {
                                    await onQueueDrainedAsync(cts).ConfigureAwait(false);
                                }

                                int activeCount = queueDbService.GetActiveQueueCount(
                                    ownerInstanceId
                                );
                                if (activeCount < 1)
                                {
                                    safeLog(
                                        $"consumer progress close: reason=queue_empty session_done={sessionCompletedCount}"
                                    );
                                    ReportProgressSnapshot(
                                        progressSnapshot,
                                        sessionCompletedCount,
                                        sessionTotalCount,
                                        currentParallelism,
                                        ResolveLatestConfiguredParallelism()
                                    );
                                    break;
                                }

                                await Task.Delay(Math.Min(500, safePollIntervalMs), cts)
                                    .ConfigureAwait(false);
                                configuredParallelism = ResolveConfiguredParallelism(
                                    maxParallelism,
                                    maxParallelismResolver
                                );
                                currentParallelism = parallelController.EnsureWithinConfigured(
                                    configuredParallelism
                                );
                                ReportProgressSnapshot(
                                    progressSnapshot,
                                    sessionCompletedCount,
                                    sessionTotalCount,
                                    currentParallelism,
                                    ResolveLatestConfiguredParallelism()
                                );
                                runtimeLeaseBatchSize = ResolveLeaseBatchSize(
                                    leaseBatchSize,
                                    currentParallelism
                                );
                                leasedItems = AcquireLeasedItems(
                                    queueDbService,
                                    ownerInstanceId,
                                    runtimeLeaseBatchSize,
                                    safeLeaseMinutes,
                                    preferredTabIndexResolver,
                                    safeLog
                                );
                                continue;
                            }

                            Stopwatch batchSw = Stopwatch.StartNew();
                            int completedCount = 0;
                            int failedCount = 0;
                            // バッチ開始時点のアクティブ件数から、セッション総数(完了+残件)を更新する。
                            int activeCountAtBatchStart = queueDbService.GetActiveQueueCount(
                                ownerInstanceId
                            );
                            int estimatedTotal = sessionCompletedCount + activeCountAtBatchStart;
                            if (estimatedTotal > sessionTotalCount)
                            {
                                sessionTotalCount = estimatedTotal;
                            }
                            if (sessionTotalCount < 1)
                            {
                                sessionTotalCount = sessionCompletedCount + leasedItems.Count;
                            }

                            // 【STEP 2: 並列処理の実行】
                            // 取得したジョブリストを、指定された並列数（maxParallelism）で並列に生成する
                            configuredParallelism = ResolveConfiguredParallelism(
                                maxParallelism,
                                maxParallelismResolver
                            );
                            currentParallelism = parallelController.EnsureWithinConfigured(
                                configuredParallelism
                            );
                            if (activeCountAtBatchStart >= 2)
                            {
                                // 巨大ファイル混在時の1並列貼り付きを避けるため、
                                // バックログがある間は最低2並列を即時に確保する。
                                currentParallelism = parallelController.EnsureMinimum(
                                    configuredParallelism,
                                    2
                                );
                            }

                            int maxWorkerPoolParallelism = ThumbnailParallelController.Clamp(
                                int.MaxValue
                            );
                            DynamicParallelGate parallelGate = new(
                                currentParallelism,
                                maxWorkerPoolParallelism
                            );
                            bool enableSlowLaneThrottle =
                                currentParallelism >= SlowLaneThrottleMinParallelism;
                            using CancellationTokenSource parallelMonitorCts =
                                CancellationTokenSource.CreateLinkedTokenSource(cts);
                            Task parallelMonitorTask = RunParallelLimitMonitorAsync(
                                parallelGate,
                                ResolveLatestConfiguredParallelism,
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

                            try
                            {
                                await Parallel.ForEachAsync(
                                    EnumerateLeasedItemsAsync(
                                        queueDbService,
                                        ownerInstanceId,
                                        leasedItems,
                                        runtimeLeaseBatchSize,
                                        safeLeaseMinutes,
                                        preferredTabIndexResolver,
                                        safeLog,
                                        cts
                                    ),
                                    new ParallelOptions
                                    {
                                        MaxDegreeOfParallelism = maxWorkerPoolParallelism,
                                        CancellationToken = cts,
                                    },
                                    async (leasedItem, token) =>
                                    {
                                        QueueObj queueObj = new()
                                        {
                                            MovieFullPath = leasedItem.MoviePath,
                                            MovieSizeBytes = leasedItem.MovieSizeBytes,
                                            Tabindex = leasedItem.TabIndex,
                                            ThumbPanelPos = leasedItem.ThumbPanelPos,
                                            ThumbTimePos = leasedItem.ThumbTimePos,
                                        };
                                        ThumbnailExecutionLane lane =
                                            ThumbnailLaneClassifier.ResolveLane(
                                                leasedItem.MovieSizeBytes
                                            );
                                        bool leaseEntered = false;
                                        bool slowLaneEntered = false;
                                        bool startedNotified = false;

                                        try
                                        {
                                            await parallelGate.WaitAsync(token).ConfigureAwait(false);
                                            leaseEntered = true;
                                            if (
                                                lane == ThumbnailExecutionLane.Slow
                                                && slowLaneSemaphore != null
                                            )
                                            {
                                                await slowLaneSemaphore.WaitAsync(token)
                                                    .ConfigureAwait(false);
                                                slowLaneEntered = true;
                                            }
                                            NotifyJobCallback(onJobStarted, queueObj);
                                            startedNotified = true;

                                            try
                                            {
                                                // 【STEP 3: サムネイル生成処理本体の呼び出し】
                                                // 処理中に他のプロセスが「止まった」と勘違いしないよう、ハートビート（ロック期限延長）しながら生成実行
                                                await ExecuteWithLeaseHeartbeatAsync(
                                                        queueDbService,
                                                        leasedItem,
                                                        ownerInstanceId,
                                                        safeLeaseMinutes,
                                                        () => createThumbAsync(queueObj, token),
                                                        safeLog,
                                                        token
                                                    )
                                                    .ConfigureAwait(false);

                                                // 【STEP 4: 成功時の状態更新】
                                                // 生成が終わったら、DBのステータスを Done（完了）に更新する
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
                                                // アプリ終了要求時のみ外側へ伝播し、並列ループ全体を終了する。
                                                if (cts.IsCancellationRequested)
                                                {
                                                    throw;
                                                }

                                                // 個別ジョブ都合のキャンセルは失敗として記録し、他ジョブは継続する。
                                                HandleFailedItem(
                                                    queueDbService,
                                                    leasedItem,
                                                    ownerInstanceId,
                                                    ex,
                                                    safeLog
                                                );
                                                _ = Interlocked.Increment(ref failedCount);
                                            }
                                            catch (Exception ex)
                                            {
                                                // 【STEP 5: 失敗時のハンドリング】
                                                // エラーになった場合、再試行回数を増やして Pending に戻すか、上限を超えていれば Failed にする
                                                HandleFailedItem(
                                                    queueDbService,
                                                    leasedItem,
                                                    ownerInstanceId,
                                                    ex,
                                                    safeLog
                                                );
                                                _ = Interlocked.Increment(ref failedCount);
                                            }

                                            _ = Interlocked.Increment(ref completedCount);
                                            int doneInSession = Interlocked.Increment(
                                                ref sessionCompletedCount
                                            );
                                            string reportTitle =
                                                $"{GetTabProgressTitle(leasedItem.TabIndex)} ({doneInSession}/{sessionTotalCount})";
                                            string message = leasedItem.MoviePath;
                                            int safeSessionTotalCount =
                                                sessionTotalCount < 1 ? 1 : sessionTotalCount;
                                            double totalProgress =
                                                (double)doneInSession * 100d / safeSessionTotalCount;
                                            if (totalProgress > 100d)
                                            {
                                                totalProgress = 100d;
                                            }

                                            lock (progressLock)
                                            {
                                                progress.Report(
                                                    totalProgress,
                                                    message,
                                                    reportTitle,
                                                    false
                                                );
                                            }

                                            ReportProgressSnapshot(
                                                progressSnapshot,
                                                doneInSession,
                                                sessionTotalCount,
                                                GetLiveParallelism(),
                                                ResolveLatestConfiguredParallelism()
                                            );
                                        }
                                        finally
                                        {
                                            if (startedNotified)
                                            {
                                                NotifyJobCallback(onJobCompleted, queueObj);
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
                                    // monitor停止時のキャンセルは想定内。
                                }
                                finally
                                {
                                    slowLaneSemaphore?.Dispose();
                                }
                            }

                            batchSw.Stop();
                            long batchMs = batchSw.ElapsedMilliseconds;
                            long totalCountAfter = Interlocked.Add(
                                ref _totalProcessedCount,
                                completedCount
                            );
                            long totalMsAfter = Interlocked.Add(ref _totalElapsedMs, batchMs);
                            ThumbnailEngineRuntimeSnapshot engineSnapshot =
                                ThumbnailEngineRuntimeStats.ConsumeWindow();
                            int activeCountAfterBatch = queueDbService.GetActiveQueueCount(
                                ownerInstanceId
                            );
                            int latestConfiguredParallelism = ResolveLatestConfiguredParallelism();
                            int nextParallelism = parallelController.EvaluateNext(
                                latestConfiguredParallelism,
                                completedCount,
                                failedCount,
                                activeCountAfterBatch,
                                engineSnapshot,
                                safeLog
                            );
                            string gpuMode =
                                Environment.GetEnvironmentVariable(GpuDecodeModeEnvName) ?? "off";
                            WritePerfLog(
                                $"thumb queue summary: gpu={gpuMode}, parallel={GetLiveParallelism()}, "
                                    + $"parallel_next={nextParallelism}, parallel_configured={latestConfiguredParallelism}, "
                                    + $"batch_count={completedCount}, batch_ms={batchMs}, "
                                    + $"batch_failed={failedCount}, active={activeCountAfterBatch}, "
                                    + $"autogen_transient_fail={engineSnapshot.AutogenTransientFailureCount}, "
                                    + $"autogen_retry_success={engineSnapshot.AutogenRetrySuccessCount}, "
                                    + $"fallback_1pass={engineSnapshot.FallbackToFfmpegOnePassCount}, "
                                    + $"total_count={totalCountAfter}, total_ms={totalMsAfter}, "
                                    + $"{ThumbnailQueueMetrics.CreateSummary()}"
                            );

                            currentParallelism = nextParallelism;
                            configuredParallelism = latestConfiguredParallelism;
                            ReportProgressSnapshot(
                                progressSnapshot,
                                sessionCompletedCount,
                                sessionTotalCount,
                                GetLiveParallelism(),
                                ResolveLatestConfiguredParallelism()
                            );
                            runtimeLeaseBatchSize = ResolveLeaseBatchSize(
                                leaseBatchSize,
                                currentParallelism
                            );
                            leasedItems = AcquireLeasedItems(
                                queueDbService,
                                ownerInstanceId,
                                runtimeLeaseBatchSize,
                                safeLeaseMinutes,
                                preferredTabIndexResolver,
                                safeLog
                            );
                        }
                    }
                    finally
                    {
                        lock (progressLock)
                        {
                            progress.Dispose();
                        }
                        safeLog("consumer progress closed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                string msg =
                    $"{DateTime.Now:yyyy/MM/dd HH:mm:ss} : サムネイルキュー処理をキャンセルしました。";
                Debug.WriteLine(msg);
                safeLog(msg);
                throw;
            }
            catch (Exception e)
            {
                string msg = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss} : {e.Message}";
                Debug.WriteLine(msg);
                safeLog(msg);
                throw;
            }
        }

        /// <summary>
        /// リース取得の共通窓口だ！タブ優先度の解決やログ出力をここで一手に引き受け、コードをスッキリ保つぜ！🧹
        /// </summary>
        private static List<QueueDbLeaseItem> AcquireLeasedItems(
            QueueDbService queueDbService,
            string ownerInstanceId,
            int leaseBatchSize,
            int leaseMinutes,
            Func<int?> preferredTabIndexResolver,
            Action<string> log
        )
        {
            int? preferredTabIndex = null;
            if (preferredTabIndexResolver != null)
            {
                try
                {
                    int? resolved = preferredTabIndexResolver();
                    preferredTabIndex =
                        (resolved.HasValue && resolved.Value >= 0) ? resolved : null;
                }
                catch (Exception ex)
                {
                    log?.Invoke($"preferred tab resolver failed: {ex.Message}");
                }
            }

            List<QueueDbLeaseItem> leasedItems = queueDbService.GetPendingAndLease(
                ownerInstanceId,
                leaseBatchSize,
                TimeSpan.FromMinutes(leaseMinutes),
                DateTime.UtcNow,
                preferredTabIndex
            );
            SortLeasedItemsByLane(leasedItems);
            long leaseTotal = ThumbnailQueueMetrics.RecordLeaseAcquired(leasedItems.Count);
            if (leasedItems.Count > 0)
            {
                log?.Invoke($"consumer lease: acquired={leasedItems.Count} total={leaseTotal}");
            }
            return leasedItems;
        }

        // バッチ先頭へ通常動画を寄せ、巨大動画を後段へ回す。
        // これにより巨大動画の貼り付きで通常キュー全体が鈍るのを避ける。
        private static void SortLeasedItemsByLane(List<QueueDbLeaseItem> leasedItems)
        {
            if (leasedItems == null || leasedItems.Count < 2)
            {
                return;
            }

            leasedItems.Sort((left, right) =>
            {
                ThumbnailExecutionLane leftLane = ThumbnailLaneClassifier.ResolveLane(
                    left?.MovieSizeBytes ?? 0
                );
                ThumbnailExecutionLane rightLane = ThumbnailLaneClassifier.ResolveLane(
                    right?.MovieSizeBytes ?? 0
                );
                int rankDiff = ThumbnailLaneClassifier.ResolveRank(leftLane)
                    - ThumbnailLaneClassifier.ResolveRank(rightLane);
                if (rankDiff != 0)
                {
                    return rankDiff;
                }

                long leftSize = Math.Max(0, left?.MovieSizeBytes ?? 0);
                long rightSize = Math.Max(0, right?.MovieSizeBytes ?? 0);
                return leftSize.CompareTo(rightSize);
            });
        }

        /// <summary>
        /// 長時間ジョブの命綱！定期的にリース期限を延長し、他のプロセスにジョブを横取りされないように死守するぜ！🛡️
        /// </summary>
        private static async Task ExecuteWithLeaseHeartbeatAsync(
            QueueDbService queueDbService,
            QueueDbLeaseItem leasedItem,
            string ownerInstanceId,
            int leaseMinutes,
            Func<Task> processingAction,
            Action<string> log,
            CancellationToken cts
        )
        {
            Task processingTask = processingAction();

            while (true)
            {
                // キャンセル時は即座にループを抜け、終了処理を優先する。
                Task delayTask = Task.Delay(TimeSpan.FromSeconds(LeaseHeartbeatSeconds), cts);
                Task completed = await Task.WhenAny(processingTask, delayTask)
                    .ConfigureAwait(false);

                if (completed == processingTask)
                {
                    await processingTask.ConfigureAwait(false);
                    return;
                }

                // 終了要求時はジョブ完了待ちせず、外側へキャンセルを伝播させる。
                cts.ThrowIfCancellationRequested();

                DateTime nowUtc = DateTime.UtcNow;
                try
                {
                    queueDbService.ExtendLease(
                        leasedItem.QueueId,
                        ownerInstanceId,
                        nowUtc.AddMinutes(leaseMinutes),
                        nowUtc
                    );
                }
                catch (Exception ex)
                {
                    log?.Invoke(
                        $"lease extend failed: queue_id={leasedItem.QueueId} message={ex.Message}"
                    );
                }
            }
        }

        /// <summary>
        /// 失敗時の駆け込み寺！まだ再試行の余地があるか判定し、Pendingに戻すかFailedの烙印を押す運命の分かれ道だ！⚖️
        /// </summary>
        private static void HandleFailedItem(
            QueueDbService queueDbService,
            QueueDbLeaseItem leasedItem,
            string ownerInstanceId,
            Exception ex,
            Action<string> log
        )
        {
            bool exceeded = leasedItem.AttemptCount + 1 >= DefaultMaxAttemptCount;
            bool missingFile =
                string.IsNullOrWhiteSpace(leasedItem.MoviePath)
                || !Path.Exists(leasedItem.MoviePath);
            bool retryable = !exceeded && !missingFile;
            ThumbnailQueueStatus nextStatus = retryable
                ? ThumbnailQueueStatus.Pending
                : ThumbnailQueueStatus.Failed;
            long failedTotal = ThumbnailQueueMetrics.RecordFailed();

            int updated = queueDbService.UpdateStatus(
                leasedItem.QueueId,
                ownerInstanceId,
                nextStatus,
                DateTime.UtcNow,
                ex.Message,
                incrementAttemptCount: retryable
            );

            if (updated < 1)
            {
                log?.Invoke(
                    $"consumer status skipped: queue_id={leasedItem.QueueId} next={nextStatus} message={ex.Message}"
                );
                return;
            }

            if (failedTotal <= 20 || failedTotal % 50 == 0)
            {
                log?.Invoke(
                    $"consumer failed: queue_id={leasedItem.QueueId} next={nextStatus} "
                        + $"retryable={retryable} failed_total={failedTotal}"
                );
            }
        }

        private static void ReportProgressSnapshot(
            Action<int, int, int, int> progressSnapshot,
            int completedCount,
            int totalCount,
            int currentParallelism,
            int configuredParallelism
        )
        {
            if (progressSnapshot == null)
            {
                return;
            }

            try
            {
                progressSnapshot(
                    Math.Max(0, completedCount),
                    Math.Max(0, totalCount),
                    Math.Max(0, currentParallelism),
                    Math.Max(0, configuredParallelism)
                );
            }
            catch
            {
                // 進捗通知失敗はキュー処理本体を止めない。
            }
        }

        private static void NotifyJobCallback(Action<QueueObj> callback, QueueObj queueObj)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(queueObj);
            }
            catch
            {
                // UI通知失敗でジョブ処理を止めない。
            }
        }

        private static string GetTabProgressTitle(int tabIndex)
        {
            return tabIndex switch
            {
                0 => "サムネイル作成中(Small)",
                1 => "サムネイル作成中(Big)",
                2 => "サムネイル作成中(Grid)",
                3 => "サムネイル作成中(List)",
                4 => "サムネイル作成中(Big10)",
                _ => "サムネイル作成中",
            };
        }

        /// <summary>
        /// 処理速度の計測用！バッチ単位や累計のイケてる数値をログにバシッと刻み込むぜ！📊
        /// </summary>
        private static void WritePerfLog(string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
            Debug.WriteLine(line);
            if (!IsThumbFileLogEnabled())
            {
                return;
            }

            try
            {
                string baseDir = AppLocalDataPaths.LogsPath;
                Directory.CreateDirectory(baseDir);
                string logPath = Path.Combine(baseDir, "thumb_decode.log");

                lock (PerfLogLock)
                {
                    File.AppendAllText(logPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // ログ書き込み失敗時も処理継続を優先する。
            }
        }

        /// <summary>
        /// 隠しコマンド発動判定！環境変数「IMM_THUMB_FILE_LOG」があればファイルログを有効化する、普段は静かな眠れる獅子だ🤫
        /// </summary>
        private static bool IsThumbFileLogEnabled()
        {
            string mode = Environment.GetEnvironmentVariable(ThumbFileLogEnvName);
            if (string.IsNullOrWhiteSpace(mode))
            {
                return false;
            }
            string normalized = mode.Trim().ToLowerInvariant();
            return normalized is "1" or "true" or "on" or "yes";
        }

        // 設定値と実行中設定変更を吸収して、今回バッチの上限並列を決める。
        private static int ResolveConfiguredParallelism(
            int defaultParallelism,
            Func<int> maxParallelismResolver
        )
        {
            int resolved = defaultParallelism;
            if (maxParallelismResolver != null)
            {
                try
                {
                    resolved = maxParallelismResolver();
                }
                catch
                {
                    resolved = defaultParallelism;
                }
            }

            return ThumbnailParallelController.Clamp(resolved);
        }

        // リース取得件数は、指定があればその値、未指定なら現在並列に合わせる。
        private static int ResolveLeaseBatchSize(int configuredLeaseBatchSize, int currentParallelism)
        {
            if (configuredLeaseBatchSize > 0)
            {
                return configuredLeaseBatchSize;
            }

            return Math.Max(1, currentParallelism);
        }

        // 処理中にバッファが尽きても、その場で次のリースを取りに行けるようにする。
        // これで巨大ファイル1件が残っている間も、空いたワーカーへ次ジョブを継続投入できる。
        private static async IAsyncEnumerable<QueueDbLeaseItem> EnumerateLeasedItemsAsync(
            QueueDbService queueDbService,
            string ownerInstanceId,
            IReadOnlyList<QueueDbLeaseItem> initialItems,
            int leaseBatchSize,
            int leaseMinutes,
            Func<int?> preferredTabIndexResolver,
            Action<string> log,
            [EnumeratorCancellation] CancellationToken cts
        )
        {
            Queue<QueueDbLeaseItem> buffer = new();
            if (initialItems != null)
            {
                for (int i = 0; i < initialItems.Count; i++)
                {
                    buffer.Enqueue(initialItems[i]);
                }
            }

            while (!cts.IsCancellationRequested)
            {
                if (buffer.Count < 1)
                {
                    List<QueueDbLeaseItem> nextItems = AcquireLeasedItems(
                        queueDbService,
                        ownerInstanceId,
                        leaseBatchSize,
                        leaseMinutes,
                        preferredTabIndexResolver,
                        log
                    );
                    if (nextItems.Count > 0)
                    {
                        for (int i = 0; i < nextItems.Count; i++)
                        {
                            buffer.Enqueue(nextItems[i]);
                        }
                    }
                    else
                    {
                        int activeCount = queueDbService.GetActiveQueueCount(ownerInstanceId);
                        if (activeCount < 1)
                        {
                            yield break;
                        }

                        // 実行中ジョブが残っている間は短い間隔で再取得を試みる。
                        await Task.Delay(250, cts).ConfigureAwait(false);
                        continue;
                    }
                }

                if (buffer.Count < 1)
                {
                    continue;
                }

                yield return buffer.Dequeue();
            }
        }

        // 実行中バッチでも設定値変更へ追従できるよう、並列上限ゲートを周期更新する。
        private static async Task RunParallelLimitMonitorAsync(
            DynamicParallelGate parallelGate,
            Func<int> resolveConfiguredParallelism,
            ThumbnailParallelController parallelController,
            Action<string> log,
            CancellationToken cts
        )
        {
            if (parallelGate == null || resolveConfiguredParallelism == null || parallelController == null)
            {
                return;
            }

            int lastApplied = parallelGate.CurrentLimit;
            while (!cts.IsCancellationRequested)
            {
                int configured = resolveConfiguredParallelism();
                int next = parallelController.EnsureWithinConfigured(configured);
                parallelGate.SetLimit(next);
                int applied = parallelGate.CurrentLimit;
                if (applied != lastApplied)
                {
                    log?.Invoke(
                        $"parallel apply: {lastApplied} -> {applied} configured={configured}"
                    );
                    lastApplied = applied;
                }

                await Task.Delay(200, cts).ConfigureAwait(false);
            }
        }

        // 並列数の上限を動的に調整する軽量ゲート。
        // ForEach自体は最大プール(24)で回し、実行許可数だけここで制御する。
        private sealed class DynamicParallelGate
        {
            private readonly object syncRoot = new();
            private readonly SemaphoreSlim semaphore;
            private readonly int maxLimit;
            private int targetLimit;
            private int pendingReduction;

            public DynamicParallelGate(int initialLimit, int maxLimit)
            {
                this.maxLimit = maxLimit < 1 ? 1 : maxLimit;
                int clampedInitial = initialLimit;
                if (clampedInitial < 1)
                {
                    clampedInitial = 1;
                }
                if (clampedInitial > this.maxLimit)
                {
                    clampedInitial = this.maxLimit;
                }

                targetLimit = clampedInitial;
                semaphore = new SemaphoreSlim(clampedInitial, this.maxLimit);
            }

            public int CurrentLimit
            {
                get
                {
                    lock (syncRoot)
                    {
                        return targetLimit;
                    }
                }
            }

            public async Task WaitAsync(CancellationToken cts)
            {
                await semaphore.WaitAsync(cts).ConfigureAwait(false);
            }

            public void Release()
            {
                lock (syncRoot)
                {
                    if (pendingReduction > 0)
                    {
                        pendingReduction--;
                        return;
                    }
                }

                semaphore.Release();
            }

            public void SetLimit(int requestedLimit)
            {
                int clamped = requestedLimit;
                if (clamped < 1)
                {
                    clamped = 1;
                }
                if (clamped > maxLimit)
                {
                    clamped = maxLimit;
                }

                lock (syncRoot)
                {
                    if (clamped == targetLimit)
                    {
                        return;
                    }

                    if (clamped > targetLimit)
                    {
                        int deltaUp = clamped - targetLimit;
                        targetLimit = clamped;

                        int consumePending = Math.Min(deltaUp, pendingReduction);
                        pendingReduction -= consumePending;
                        int releaseCount = deltaUp - consumePending;
                        if (releaseCount > 0)
                        {
                            semaphore.Release(releaseCount);
                        }
                        return;
                    }

                    int deltaDown = targetLimit - clamped;
                    targetLimit = clamped;
                    pendingReduction += deltaDown;
                    while (pendingReduction > 0 && semaphore.Wait(0))
                    {
                        pendingReduction--;
                    }
                }
            }
        }
    }
}
