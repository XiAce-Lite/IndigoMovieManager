using System.Diagnostics;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.QueueDb;

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
            Func<IReadOnlyList<string>> preferredMoviePathKeysResolver = null,
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
            ThumbnailQueueProgressPublisher progressPublisher = new(
                progressSnapshot,
                safeProgressPresenter,
                onJobStarted,
                onJobCompleted,
                safeLog
            );
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
                    progressPublisher.ReportSnapshot(
                        0,
                        0,
                        currentParallelism,
                        ResolveLatestConfiguredParallelism()
                    );
                    int runtimeLeaseBatchSize = ResolveLeaseBatchSize(
                        leaseBatchSize,
                        currentParallelism
                    );
                    List<QueueDbLeaseItem> leasedItems = ThumbnailLeaseAcquirer.AcquireLeasedItems(
                        queueDbService,
                        ownerInstanceId,
                        runtimeLeaseBatchSize,
                        safeLeaseMinutes,
                        preferredTabIndexResolver,
                        preferredMoviePathKeysResolver,
                        safeLog
                    );

                    // DB上で処理対象が無い時だけ待機し、後回しジョブ確認を呼ぶ。
                    if (leasedItems.Count < 1)
                    {
                        if (onQueueDrainedAsync != null)
                        {
                            await onQueueDrainedAsync(cts).ConfigureAwait(false);
                        }
                        progressPublisher.ReportSnapshot(
                            0,
                            0,
                            currentParallelism,
                            ResolveLatestConfiguredParallelism()
                        );
                        await Task.Delay(safePollIntervalMs, cts).ConfigureAwait(false);
                        continue;
                    }

                    ThumbnailQueueBatchState batchState = new();
                    progressPublisher.ReportSnapshot(
                        batchState.SessionCompletedCount,
                        batchState.SessionTotalCount,
                        currentParallelism,
                        ResolveLatestConfiguredParallelism()
                    );
                    progressPublisher.Open(title);

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
                                        $"consumer progress close: reason=queue_empty session_done={batchState.SessionCompletedCount}"
                                    );
                                    progressPublisher.ReportSnapshot(
                                        batchState.SessionCompletedCount,
                                        batchState.SessionTotalCount,
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
                                progressPublisher.ReportSnapshot(
                                    batchState.SessionCompletedCount,
                                    batchState.SessionTotalCount,
                                    currentParallelism,
                                    ResolveLatestConfiguredParallelism()
                                );
                                runtimeLeaseBatchSize = ResolveLeaseBatchSize(
                                    leaseBatchSize,
                                    currentParallelism
                                );
                                leasedItems = ThumbnailLeaseAcquirer.AcquireLeasedItems(
                                    queueDbService,
                                    ownerInstanceId,
                                    runtimeLeaseBatchSize,
                                    safeLeaseMinutes,
                                    preferredTabIndexResolver,
                                    preferredMoviePathKeysResolver,
                                    safeLog
                                );
                                continue;
                            }

                            ThumbnailQueueBatchRunResult batchResult =
                                await ThumbnailQueueBatchRunner.RunAsync(
                                        queueDbService,
                                        ownerInstanceId,
                                        leasedItems,
                                        runtimeLeaseBatchSize,
                                        safeLeaseMinutes,
                                        preferredTabIndexResolver,
                                        preferredMoviePathKeysResolver,
                                        createThumbAsync,
                                        progressPublisher,
                                        batchState,
                                        parallelController,
                                        ResolveLatestConfiguredParallelism,
                                        safeLog,
                                        cts
                                    )
                                    .ConfigureAwait(false);

                            currentParallelism = batchResult.NextParallelism;
                            configuredParallelism = batchResult.LatestConfiguredParallelism;
                            progressPublisher.ReportSnapshot(
                                batchState.SessionCompletedCount,
                                batchState.SessionTotalCount,
                                batchResult.LiveParallelism,
                                batchResult.LatestConfiguredParallelism
                            );
                            runtimeLeaseBatchSize = ResolveLeaseBatchSize(
                                leaseBatchSize,
                                currentParallelism
                            );
                            leasedItems = ThumbnailLeaseAcquirer.AcquireLeasedItems(
                                queueDbService,
                                ownerInstanceId,
                                runtimeLeaseBatchSize,
                                safeLeaseMinutes,
                                preferredTabIndexResolver,
                                preferredMoviePathKeysResolver,
                                safeLog
                            );
                        }
                    }
                    finally
                    {
                        progressPublisher.Close();
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

    }
}
