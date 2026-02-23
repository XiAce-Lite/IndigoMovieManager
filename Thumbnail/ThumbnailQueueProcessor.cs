using Notification.Wpf;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    // サムネイル作成キューの処理・進捗表示・計測ログ出力をまとめる。
    public sealed class ThumbnailQueueProcessor
    {
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string ThumbFileLogEnvName = "IMM_THUMB_FILE_LOG";
        private static readonly object PerfLogLock = new();
        private static long _totalProcessedCount = 0;
        private static long _totalElapsedMs = 0;

        public async Task RunAsync(
            ConcurrentQueue<QueueObj> queueThumb,
            Func<QueueObj, CancellationToken, Task> createThumbAsync,
            int maxParallelism = 4,
            int pollIntervalMs = 3000,
            Action<string> log = null,
            CancellationToken cts = default)
        {
            string title = "サムネイル作成中";
            NotificationManager notificationManager = new();
            int safePollIntervalMs = pollIntervalMs < 100 ? 100 : pollIntervalMs;
            int safeMaxParallelism = maxParallelism < 1 ? 1 : maxParallelism;

            try
            {
                while (true)
                {
                    await Task.Delay(safePollIntervalMs, cts);
                    if (queueThumb.IsEmpty) { continue; }

                    // いま溜まっているキューを1バッチとして取り出す。
                    List<QueueObj> batch = [];
                    while (queueThumb.TryDequeue(out QueueObj queueObj))
                    {
                        if (queueObj == null) { continue; }
                        batch.Add(queueObj);
                    }
                    if (batch.Count < 1) { continue; }

                    // バッチ単位で処理時間を計測し、GPUあり/なし比較用の集計に使う。
                    Stopwatch batchSw = Stopwatch.StartNew();

                    var progress = notificationManager.ShowProgressBar(title, false, true, "ProgressArea", false, 2, "");
                    object progressLock = new();
                    int completedCount = 0;
                    int totalCount = batch.Count;

                    await Parallel.ForEachAsync(
                        batch,
                        new ParallelOptions { MaxDegreeOfParallelism = safeMaxParallelism, CancellationToken = cts },
                        async (item, token) =>
                        {
                            await createThumbAsync(item, token).ConfigureAwait(false);

                            int done = Interlocked.Increment(ref completedCount);
                            string reportTitle = $"{GetTabProgressTitle(item.Tabindex)} ({done}/{totalCount})";
                            string message = item.MovieFullPath;
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

                    // バッチ結果と累計を同じログへ出して、GPU有無の比較をしやすくする。
                    batchSw.Stop();
                    long batchMs = batchSw.ElapsedMilliseconds;
                    long totalCountAfter = Interlocked.Add(ref _totalProcessedCount, completedCount);
                    long totalMsAfter = Interlocked.Add(ref _totalElapsedMs, batchMs);
                    string gpuMode = Environment.GetEnvironmentVariable(GpuDecodeModeEnvName) ?? "off";
                    WritePerfLog(
                        $"thumb queue summary: gpu={gpuMode}, parallel={safeMaxParallelism}, " +
                        $"batch_count={completedCount}, batch_ms={batchMs}, " +
                        $"total_count={totalCountAfter}, total_ms={totalMsAfter}");
                }
            }
            catch (OperationCanceledException)
            {
                string msg = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss} : サムネイルキュー処理をキャンセルしました。";
                Debug.WriteLine(msg);
                if (log != null) { log(msg); }
            }
            catch (Exception e)
            {
                string s = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss} :";
                string msg = $"{s} {e.Message}";
                Debug.WriteLine(msg);
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
