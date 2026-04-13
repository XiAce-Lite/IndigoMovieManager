using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager.Skin
{
    /// <summary>
    /// skin 状態保存専用の単一ライター。
    /// UI から来た要求を短い窓で束ね、最後の値だけ DB へ反映する。
    /// </summary>
    public sealed class WhiteBrowserSkinStatePersister
    {
        private readonly ChannelReader<WhiteBrowserSkinStatePersistRequest> reader;
        private readonly int batchWindowMs;
        private readonly Action<string> log;

        public WhiteBrowserSkinStatePersister(
            ChannelReader<WhiteBrowserSkinStatePersistRequest> reader,
            int batchWindowMs = 100,
            Action<string> log = null
        )
        {
            this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
            this.batchWindowMs = Math.Clamp(batchWindowMs, 10, 300);
            this.log = log ?? (_ => { });
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            List<WhiteBrowserSkinStatePersistRequest> batch = [];

            try
            {
                while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    batch.Clear();
                    DrainAvailableRequests(batch);
                    if (batch.Count < 1)
                    {
                        continue;
                    }

                    await Task.Delay(batchWindowMs, cancellationToken).ConfigureAwait(false);
                    DrainAvailableRequests(batch);
                    PersistBatch(batch);
                }
            }
            catch (OperationCanceledException)
            {
                log("skin state persister canceled.");
            }
        }

        private void DrainAvailableRequests(List<WhiteBrowserSkinStatePersistRequest> buffer)
        {
            while (reader.TryRead(out WhiteBrowserSkinStatePersistRequest request))
            {
                if (
                    request == null
                    || string.IsNullOrWhiteSpace(request.DbFullPath)
                    || string.IsNullOrWhiteSpace(request.Key)
                )
                {
                    continue;
                }

                buffer.Add(request);
            }
        }

        private void PersistBatch(List<WhiteBrowserSkinStatePersistRequest> batch)
        {
            if (batch == null || batch.Count < 1)
            {
                return;
            }

            Dictionary<string, WhiteBrowserSkinStatePersistRequest> lastByKey =
                new(StringComparer.OrdinalIgnoreCase);
            foreach (WhiteBrowserSkinStatePersistRequest request in batch)
            {
                string dedupeKey = $"{request.DbFullPath}|{request.BuildIdentityKey()}";
                lastByKey[dedupeKey] = request;
            }

            int profileCount = 0;
            int systemCount = 0;
            int failureCount = 0;
            foreach (
                IGrouping<string, WhiteBrowserSkinStatePersistRequest> group in lastByKey.Values.GroupBy(
                    x => x.DbFullPath,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                foreach (
                    WhiteBrowserSkinStatePersistRequest request in group.OrderBy(
                        x => x.TargetKind == WhiteBrowserSkinStatePersistTargetKind.System ? 0 : 1
                    )
                )
                {
                    try
                    {
                        switch (request.TargetKind)
                        {
                            case WhiteBrowserSkinStatePersistTargetKind.System:
                                UpsertSystemTable(request.DbFullPath, request.Key, request.Value);
                                systemCount++;
                                break;

                            case WhiteBrowserSkinStatePersistTargetKind.Profile:
                                UpsertProfileTable(
                                    request.DbFullPath,
                                    request.ProfileName,
                                    request.Key,
                                    request.Value
                                );
                                profileCount++;
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        log(
                            $"skin state persist failed: db='{request.DbFullPath}' target={request.TargetKind} profile='{request.ProfileName}' key='{request.Key}' err='{ex.GetType().Name}: {ex.Message}'"
                        );
                    }
                }
            }

            int uniqueCount = lastByKey.Count;
            int dedupedCount = batch.Count - uniqueCount;
            log(
                $"skin state persist: batch_count={batch.Count} unique={uniqueCount} deduped={dedupedCount} system={systemCount} profile={profileCount} failed={failureCount}"
            );
        }
    }
}
