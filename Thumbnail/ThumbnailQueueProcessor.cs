using Notification.Wpf;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace IndigoMovieManager.Thumbnail
{
    // サムネイル作成キューの監視・進捗表示・実行順制御を担当する。
    public sealed class ThumbnailQueueProcessor
    {
        public async Task RunAsync(
            ConcurrentQueue<QueueObj> queueThumb,
            Func<QueueObj, CancellationToken, Task> createThumbAsync,
            int maxParallelism = 4,
            int pollIntervalMs = 3000,
            Action<string> log = null,
            CancellationToken cts = default)
        {
            var title = "サムネイル作成中";
            NotificationManager notificationManager = new();
            int safePollIntervalMs = pollIntervalMs < 100 ? 100 : pollIntervalMs;
            int safeMaxParallelism = maxParallelism < 1 ? 1 : maxParallelism;

            try
            {
                while (true)
                {
                    // 待機間隔は外部から差し替え可能にし、運用中の調整をしやすくする。
                    await Task.Delay(safePollIntervalMs, cts);
                    if (queueThumb.IsEmpty) { continue; }

                    // いま積まれている分を1バッチとして切り出し、進捗分母を安定させる。
                    List<QueueObj> batch = [];
                    while (queueThumb.TryDequeue(out QueueObj queueObj))
                    {
                        if (queueObj == null) { continue; }
                        batch.Add(queueObj);
                    }
                    if (batch.Count < 1) { continue; }

                    var progress = notificationManager.ShowProgressBar(title, false, true, "ProgressArea", false, 2, "");
                    object progressLock = new();
                    int completedCount = 0;
                    int totalCount = batch.Count;

                    await Parallel.ForEachAsync(
                        batch,
                        new ParallelOptions { MaxDegreeOfParallelism = safeMaxParallelism, CancellationToken = cts },
                        async (queueObj, token) =>
                    {
                        await createThumbAsync(queueObj, token).ConfigureAwait(false);

                        int done = Interlocked.Increment(ref completedCount);
                        var reportTitle = $"{GetTabProgressTitle(queueObj.Tabindex)} ({done}/{totalCount})";
                        var message = $"{queueObj.MovieFullPath}";
                        double totalProgress = (double)done * 100d / totalCount;
                        if (totalProgress > 100d) { totalProgress = 100d; }

                        lock (progressLock)
                        {
                            progress.Report((totalProgress, message, reportTitle, false));
                        }
                    });

                    // 1バッチ分を処理し終わったら進捗バーを閉じる。
                    lock (progressLock)
                    {
                        progress.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 再起動・終了時の正常キャンセルは情報ログとして扱う。
                string msg = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss} : サムネイルキュー監視をキャンセルしました。";
                Debug.WriteLine(msg);
                if (log != null) { log(msg); }
            }
            catch (Exception e)
            {
                string s = string.Format($"{DateTime.Now:yyyy/MM/dd HH:mm:ss} :");
                string msg = $"{s} {e.Message}";
                Debug.WriteLine($"{msg} ");
                if (log != null) { log(msg); }
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
    }
}
