using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;
using Notification.Wpf;
using System.Diagnostics;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    // QueueDBを正として、リース取得 -> 生成 -> 状態更新を行うConsumer。
    public sealed class ThumbnailQueueProcessor
    {
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string ThumbFileLogEnvName = "IMM_THUMB_FILE_LOG";
        private static readonly object PerfLogLock = new();
        private static long _totalProcessedCount = 0;
        private static long _totalElapsedMs = 0;

        private const int DefaultMaxAttemptCount = 5;
        private const int LeaseHeartbeatSeconds = 30;

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
            CancellationToken cts = default)
        {
            if (queueDbServiceResolver == null) { throw new ArgumentNullException(nameof(queueDbServiceResolver)); }
            if (string.IsNullOrWhiteSpace(ownerInstanceId)) { throw new ArgumentException("ownerInstanceId is required.", nameof(ownerInstanceId)); }
            if (createThumbAsync == null) { throw new ArgumentNullException(nameof(createThumbAsync)); }

            string title = "サムネイル作成中";
            NotificationManager notificationManager = new();
            int safePollIntervalMs = pollIntervalMs < 100 ? 100 : pollIntervalMs;
            int safeMaxParallelism = maxParallelism < 1 ? 1 : maxParallelism;
            int safeLeaseMinutes = leaseMinutes < 1 ? 1 : leaseMinutes;
            int safeLeaseBatchSize = leaseBatchSize < 1 ? safeMaxParallelism : leaseBatchSize;
            Action<string> safeLog = log ?? (_ => { });

            try
            {
                while (true)
                {
                    cts.ThrowIfCancellationRequested();
                    QueueDbService queueDbService = queueDbServiceResolver();
                    if (queueDbService == null)
                    {
                        await Task.Delay(safePollIntervalMs, cts).ConfigureAwait(false);
                        continue;
                    }

                    int? preferredTabIndex = null;
                    if (preferredTabIndexResolver != null)
                    {
                        try
                        {
                            int? resolved = preferredTabIndexResolver();
                            preferredTabIndex = (resolved.HasValue && resolved.Value >= 0)
                                ? resolved
                                : null;
                        }
                        catch (Exception ex)
                        {
                            safeLog($"preferred tab resolver failed: {ex.Message}");
                        }
                    }

                    List<QueueDbLeaseItem> leasedItems = queueDbService.GetPendingAndLease(
                        ownerInstanceId,
                        safeLeaseBatchSize,
                        TimeSpan.FromMinutes(safeLeaseMinutes),
                        DateTime.UtcNow,
                        preferredTabIndex);
                    long leaseTotal = ThumbnailQueueMetrics.RecordLeaseAcquired(leasedItems.Count);
                    if (leasedItems.Count > 0)
                    {
                        safeLog($"consumer lease: acquired={leasedItems.Count} total={leaseTotal}");
                    }

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

                    Stopwatch batchSw = Stopwatch.StartNew();
                    var progress = notificationManager.ShowProgressBar(title, false, true, "ProgressArea", false, 2, "");
                    object progressLock = new();
                    int completedCount = 0;
                    int totalCount = leasedItems.Count;

                    await Parallel.ForEachAsync(
                        leasedItems,
                        new ParallelOptions { MaxDegreeOfParallelism = safeMaxParallelism, CancellationToken = cts },
                        async (leasedItem, token) =>
                        {
                            QueueObj queueObj = new()
                            {
                                MovieFullPath = leasedItem.MoviePath,
                                Tabindex = leasedItem.TabIndex,
                                ThumbPanelPos = leasedItem.ThumbPanelPos,
                                ThumbTimePos = leasedItem.ThumbTimePos
                            };

                            try
                            {
                                await ExecuteWithLeaseHeartbeatAsync(
                                    queueDbService,
                                    leasedItem,
                                    ownerInstanceId,
                                    safeLeaseMinutes,
                                    () => createThumbAsync(queueObj, token),
                                    safeLog,
                                    token).ConfigureAwait(false);

                                int updated = queueDbService.UpdateStatus(
                                    leasedItem.QueueId,
                                    ownerInstanceId,
                                    ThumbnailQueueStatus.Done,
                                    DateTime.UtcNow);
                                if (updated < 1)
                                {
                                    safeLog($"consumer done skipped: queue_id={leasedItem.QueueId} owner={ownerInstanceId}");
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                HandleFailedItem(queueDbService, leasedItem, ownerInstanceId, ex, safeLog);
                            }

                            int done = Interlocked.Increment(ref completedCount);
                            string reportTitle = $"{GetTabProgressTitle(leasedItem.TabIndex)} ({done}/{totalCount})";
                            string message = leasedItem.MoviePath;
                            double totalProgress = (double)done * 100d / totalCount;
                            if (totalProgress > 100d) { totalProgress = 100d; }

                            lock (progressLock)
                            {
                                progress.Report((totalProgress, message, reportTitle, false));
                            }
                        });

                    lock (progressLock)
                    {
                        progress.Dispose();
                    }

                    batchSw.Stop();
                    long batchMs = batchSw.ElapsedMilliseconds;
                    long totalCountAfter = Interlocked.Add(ref _totalProcessedCount, completedCount);
                    long totalMsAfter = Interlocked.Add(ref _totalElapsedMs, batchMs);
                    string gpuMode = Environment.GetEnvironmentVariable(GpuDecodeModeEnvName) ?? "off";
                    WritePerfLog(
                        $"thumb queue summary: gpu={gpuMode}, parallel={safeMaxParallelism}, " +
                        $"batch_count={completedCount}, batch_ms={batchMs}, " +
                        $"total_count={totalCountAfter}, total_ms={totalMsAfter}, " +
                        $"{ThumbnailQueueMetrics.CreateSummary()}");
                }
            }
            catch (OperationCanceledException)
            {
                string msg = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss} : サムネイルキュー処理をキャンセルしました。";
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

        // 長時間ジョブ中に定期的にリース期限を延長し、他プロセスへの奪取を防ぐ。
        private static async Task ExecuteWithLeaseHeartbeatAsync(
            QueueDbService queueDbService,
            QueueDbLeaseItem leasedItem,
            string ownerInstanceId,
            int leaseMinutes,
            Func<Task> processingAction,
            Action<string> log,
            CancellationToken cts)
        {
            Task processingTask = processingAction();

            while (true)
            {
                Task delayTask = Task.Delay(TimeSpan.FromSeconds(LeaseHeartbeatSeconds));
                Task completed = await Task.WhenAny(
                    processingTask,
                    delayTask
                ).ConfigureAwait(false);

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
                        nowUtc);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"lease extend failed: queue_id={leasedItem.QueueId} message={ex.Message}");
                }
            }
        }

        // 失敗時は再試行可能か判定し、Pending/Failedへ状態遷移させる。
        private static void HandleFailedItem(
            QueueDbService queueDbService,
            QueueDbLeaseItem leasedItem,
            string ownerInstanceId,
            Exception ex,
            Action<string> log)
        {
            bool exceeded = leasedItem.AttemptCount + 1 >= DefaultMaxAttemptCount;
            bool missingFile = string.IsNullOrWhiteSpace(leasedItem.MoviePath) || !Path.Exists(leasedItem.MoviePath);
            bool retryable = !exceeded && !missingFile;
            ThumbnailQueueStatus nextStatus = retryable ? ThumbnailQueueStatus.Pending : ThumbnailQueueStatus.Failed;
            long failedTotal = ThumbnailQueueMetrics.RecordFailed();

            int updated = queueDbService.UpdateStatus(
                leasedItem.QueueId,
                ownerInstanceId,
                nextStatus,
                DateTime.UtcNow,
                ex.Message,
                incrementAttemptCount: retryable);

            if (updated < 1)
            {
                log?.Invoke($"consumer status skipped: queue_id={leasedItem.QueueId} next={nextStatus} message={ex.Message}");
                return;
            }

            if (failedTotal <= 20 || failedTotal % 50 == 0)
            {
                log?.Invoke(
                    $"consumer failed: queue_id={leasedItem.QueueId} next={nextStatus} " +
                    $"retryable={retryable} failed_total={failedTotal}");
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
            if (!IsThumbFileLogEnabled()) { return; }

            try
            {
                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IndigoMovieManager",
                    "logs");
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
            if (string.IsNullOrWhiteSpace(mode)) { return false; }
            string normalized = mode.Trim().ToLowerInvariant();
            return normalized is "1" or "true" or "on" or "yes";
        }
    }
}
