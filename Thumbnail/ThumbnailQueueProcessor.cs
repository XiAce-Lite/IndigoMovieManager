using System.Diagnostics;
using System.IO;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;
using Notification.Wpf;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【キュー処理の心臓部（コンシューマー）】
    /// QueueDB（SQLite）を正として、非同期でサムネイル生成のジョブを処理するクラスです。
    ///
    /// ＜全体の流れ＞
    /// 1. DBから「未処理(Pending)」のジョブを一定件数まとめて取得（リース取得）し、他プロセスから触れないようにロックする。
    /// 2. 取得したジョブを Parallel.ForEachAsync で並列処理（ThumbnailCreationServiceへ委譲）する。
    /// 3. 処理が長時間に及ぶ場合は、定期的に「処理中だよ」とDBを更新（ハートビート）してロック延長する。
    /// 4. 成功したらDBの状態を「Done」に、失敗したら再試行回数を増やして「Pending」または「Failed」へ更新する。
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

        /// <summary>
        /// キューの監視と処理を行うメインループ。
        /// アプリ起動中にバックグラウンドで常に動き続ける。
        /// </summary>
        public async Task RunAsync(
            Func<QueueDbService> queueDbServiceResolver,
            string ownerInstanceId,
            Func<QueueObj, CancellationToken, Task> createThumbAsync,
            int maxParallelism = 4,
            int pollIntervalMs = 3000,
            int leaseMinutes = 5,
            int leaseBatchSize = 8,
            Func<int?> preferredTabIndexResolver = null,
            Action<string> log = null,
            Func<CancellationToken, Task> onQueueDrainedAsync = null,
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
            NotificationManager notificationManager = new();
            int safePollIntervalMs = pollIntervalMs < 100 ? 100 : pollIntervalMs;
            int safeMaxParallelism = maxParallelism < 1 ? 1 : maxParallelism;
            int safeLeaseMinutes = leaseMinutes < 1 ? 1 : leaseMinutes;
            int safeLeaseBatchSize = leaseBatchSize < 1 ? safeMaxParallelism : leaseBatchSize;
            Action<string> safeLog = log ?? (_ => { });

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
                    List<QueueDbLeaseItem> leasedItems = AcquireLeasedItems(
                        queueDbService,
                        ownerInstanceId,
                        safeLeaseBatchSize,
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
                        await Task.Delay(safePollIntervalMs, cts).ConfigureAwait(false);
                        continue;
                    }

                    object progressLock = new();
                    int sessionCompletedCount = 0;
                    int sessionTotalCount = 0;
                    var progress = notificationManager.ShowProgressBar(
                        title,
                        false,
                        true,
                        "ProgressArea",
                        false,
                        2,
                        ""
                    );
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
                                    break;
                                }

                                await Task.Delay(Math.Min(500, safePollIntervalMs), cts)
                                    .ConfigureAwait(false);
                                leasedItems = AcquireLeasedItems(
                                    queueDbService,
                                    ownerInstanceId,
                                    safeLeaseBatchSize,
                                    safeLeaseMinutes,
                                    preferredTabIndexResolver,
                                    safeLog
                                );
                                continue;
                            }

                            Stopwatch batchSw = Stopwatch.StartNew();
                            int completedCount = 0;
                            int totalCount = leasedItems.Count;
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
                                sessionTotalCount = sessionCompletedCount + totalCount;
                            }

                            // 【STEP 2: 並列処理の実行】
                            // 取得したジョブリストを、指定された並列数（maxParallelism）で並列に生成する
                            await Parallel.ForEachAsync(
                                leasedItems,
                                new ParallelOptions
                                {
                                    MaxDegreeOfParallelism = safeMaxParallelism,
                                    CancellationToken = cts,
                                },
                                async (leasedItem, token) =>
                                {
                                    QueueObj queueObj = new()
                                    {
                                        MovieFullPath = leasedItem.MoviePath,
                                        Tabindex = leasedItem.TabIndex,
                                        ThumbPanelPos = leasedItem.ThumbPanelPos,
                                        ThumbTimePos = leasedItem.ThumbTimePos,
                                    };

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
                                            (totalProgress, message, reportTitle, false)
                                        );
                                    }
                                }
                            );

                            batchSw.Stop();
                            long batchMs = batchSw.ElapsedMilliseconds;
                            long totalCountAfter = Interlocked.Add(
                                ref _totalProcessedCount,
                                completedCount
                            );
                            long totalMsAfter = Interlocked.Add(ref _totalElapsedMs, batchMs);
                            string gpuMode =
                                Environment.GetEnvironmentVariable(GpuDecodeModeEnvName) ?? "off";
                            WritePerfLog(
                                $"thumb queue summary: gpu={gpuMode}, parallel={safeMaxParallelism}, "
                                    + $"batch_count={completedCount}, batch_ms={batchMs}, "
                                    + $"total_count={totalCountAfter}, total_ms={totalMsAfter}, "
                                    + $"{ThumbnailQueueMetrics.CreateSummary()}"
                            );

                            leasedItems = AcquireLeasedItems(
                                queueDbService,
                                ownerInstanceId,
                                safeLeaseBatchSize,
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

        // 毎回のリース取得を共通化し、タブ優先解決とログを一箇所で扱う。
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
            long leaseTotal = ThumbnailQueueMetrics.RecordLeaseAcquired(leasedItems.Count);
            if (leasedItems.Count > 0)
            {
                log?.Invoke($"consumer lease: acquired={leasedItems.Count} total={leaseTotal}");
            }
            return leasedItems;
        }

        // 長時間ジョブ中に定期的にリース期限を延長し、他プロセスへの奪取を防ぐ。
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
                Task delayTask = Task.Delay(TimeSpan.FromSeconds(LeaseHeartbeatSeconds));
                Task completed = await Task.WhenAny(processingTask, delayTask)
                    .ConfigureAwait(false);

                if (completed == processingTask)
                {
                    await processingTask.ConfigureAwait(false);
                    return;
                }

                // キャンセル後は延長ループを止め、処理タスクの終了を待つ。
                // cts連動Delayを使うと即時完了が続いてスピンするため、ここで明示判定する。
                if (cts.IsCancellationRequested)
                {
                    await processingTask.ConfigureAwait(false);
                    return;
                }

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

        // 失敗時は再試行可能か判定し、Pending/Failedへ状態遷移させる。
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

        // 速度比較用に、バッチ単位と累計の数値をログへ追記する。
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
                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IndigoMovieManager_fork",
                    "logs"
                );
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

        // 既定はファイルログ停止。必要時のみ環境変数 IMM_THUMB_FILE_LOG=1 で有効化する。
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
    }
}
