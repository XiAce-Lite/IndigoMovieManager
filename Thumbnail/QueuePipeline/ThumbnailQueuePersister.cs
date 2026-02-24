using IndigoMovieManager.Thumbnail.QueueDb;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace IndigoMovieManager.Thumbnail.QueuePipeline
{
    // Channelで受けた要求を短周期バッチでQueueDBへ反映する単一ライター。
    public sealed class ThumbnailQueuePersister
    {
        private readonly ChannelReader<QueueRequest> reader;
        private readonly int batchWindowMs;
        private readonly Action<string> log;
        private readonly ConcurrentDictionary<string, QueueDbService> queueDbServices =
            new(StringComparer.OrdinalIgnoreCase);

        public ThumbnailQueuePersister(
            ChannelReader<QueueRequest> reader,
            int batchWindowMs = 150,
            Action<string> log = null)
        {
            this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
            this.batchWindowMs = Math.Clamp(batchWindowMs, 100, 300);
            this.log = log ?? (_ => { });
        }

        // Persister本体。取り込み窓(100-300ms)ごとに複数要求をまとめてUpsertする。
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            List<QueueRequest> batch = [];

            try
            {
                while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    batch.Clear();

                    // まず現時点で溜まっている分を一気に取り出す。
                    DrainAvailableRequests(batch);
                    if (batch.Count < 1) { continue; }

                    // 少し待って、直後に来る要求も同じバッチへ吸収する。
                    await Task.Delay(batchWindowMs, cancellationToken).ConfigureAwait(false);
                    DrainAvailableRequests(batch);

                    PersistBatch(batch);
                }
            }
            catch (OperationCanceledException)
            {
                log("persister canceled.");
            }
            catch (Exception ex)
            {
                log($"persister fault: {ex.Message}");
                throw;
            }
        }

        // Readerから現在取得可能な分だけを取り出す。
        private void DrainAvailableRequests(List<QueueRequest> buffer)
        {
            while (reader.TryRead(out QueueRequest request))
            {
                if (request == null) { continue; }
                if (string.IsNullOrWhiteSpace(request.MainDbFullPath)) { continue; }
                if (string.IsNullOrWhiteSpace(request.MoviePath)) { continue; }
                buffer.Add(request);
            }
        }

        // MainDB単位でQueueDbServiceを分けてUpsertし、保存先の分離を保つ。
        private void PersistBatch(List<QueueRequest> batch)
        {
            if (batch == null || batch.Count < 1) { return; }

            DateTime nowUtc = DateTime.UtcNow;
            int upsertSubmittedCount = 0;
            int dbAffectedCount = 0;
            int dbInsertedCount = 0;
            int dbUpdatedCount = 0;
            int dbSkippedProcessingCount = 0;
            int uniqueCount = 0;

            foreach (IGrouping<string, QueueRequest> group in batch.GroupBy(x => x.MainDbFullPath, StringComparer.OrdinalIgnoreCase))
            {
                QueueDbService queueDbService = queueDbServices.GetOrAdd(group.Key, static path => new QueueDbService(path));
                List<QueueRequest> groupRequests = group.ToList();
                Dictionary<string, QueueRequest> latestByKey = new(StringComparer.OrdinalIgnoreCase);

                // 同一(MainDB + MoviePathKey + TabIndex)は最新要求だけ残し、無駄なUpsertを圧縮する。
                foreach (QueueRequest request in groupRequests)
                {
                    string key = BuildRequestIdentityKey(request);
                    latestByKey[key] = request;
                }
                List<QueueDbUpsertItem> upsertItems = [];

                foreach (QueueRequest request in latestByKey.Values)
                {
                    upsertItems.Add(new QueueDbUpsertItem
                    {
                        MoviePath = request.MoviePath,
                        MoviePathKey = request.MoviePathKey,
                        TabIndex = request.TabIndex,
                        ThumbPanelPos = request.ThumbPanelPos,
                        ThumbTimePos = request.ThumbTimePos
                    });
                }

                uniqueCount += upsertItems.Count;
                QueueDbUpsertResult upsertResult = queueDbService.Upsert(upsertItems, nowUtc);
                upsertSubmittedCount += upsertResult.SubmittedCount;
                dbAffectedCount += upsertResult.AffectedCount;
                dbInsertedCount += upsertResult.InsertedCount;
                dbUpdatedCount += upsertResult.UpdatedCount;
                dbSkippedProcessingCount += upsertResult.SkippedProcessingCount;
            }

            int dedupedCount = batch.Count - uniqueCount;
            long upsertSubmittedTotal = ThumbnailQueueMetrics.RecordUpsertSubmitted(upsertSubmittedCount);
            long dbAffectedTotal = ThumbnailQueueMetrics.RecordDbAffected(dbAffectedCount);
            long dbInsertedTotal = ThumbnailQueueMetrics.RecordDbInserted(dbInsertedCount);
            long dbUpdatedTotal = ThumbnailQueueMetrics.RecordDbUpdated(dbUpdatedCount);
            long dbSkippedProcessingTotal = ThumbnailQueueMetrics.RecordDbSkippedProcessing(dbSkippedProcessingCount);
            log(
                $"persister upsert: batch_count={batch.Count} unique={uniqueCount} deduped={dedupedCount} " +
                $"upsert_submitted={upsertSubmittedCount} upsert_submitted_total={upsertSubmittedTotal} " +
                $"db_affected={dbAffectedCount} db_affected_total={dbAffectedTotal} " +
                $"db_inserted={dbInsertedCount} db_inserted_total={dbInsertedTotal} " +
                $"db_updated={dbUpdatedCount} db_updated_total={dbUpdatedTotal} " +
                $"db_skipped_processing={dbSkippedProcessingCount} " +
                $"db_skipped_processing_total={dbSkippedProcessingTotal}");
        }

        // 同一ジョブ判定キーを組み立てる。MoviePathKey欠落時はMoviePathから再計算する。
        private static string BuildRequestIdentityKey(QueueRequest request)
        {
            string moviePathKey = request.MoviePathKey;
            if (string.IsNullOrWhiteSpace(moviePathKey))
            {
                moviePathKey = QueueDbPathResolver.CreateMoviePathKey(request.MoviePath ?? "");
            }
            return $"{moviePathKey}:{request.TabIndex}";
        }
    }
}
