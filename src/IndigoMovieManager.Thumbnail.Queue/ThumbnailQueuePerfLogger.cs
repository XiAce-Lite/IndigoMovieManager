using System.Diagnostics;
using System.IO;
using IndigoMovieManager.Thumbnail.QueuePipeline;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// バッチ単位の perf summary と累計計測をまとめて扱う。
    /// </summary>
    internal static class ThumbnailQueuePerfLogger
    {
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string ThumbFileLogEnvName = "IMM_THUMB_FILE_LOG";
        private static readonly object PerfLogLock = new();
        private static long totalProcessedCount;
        private static long totalElapsedMs;

        public static void LogBatchSummary(
            ThumbnailQueueBatchState batchState,
            long batchElapsedMs,
            int liveParallelism,
            int nextParallelism,
            int latestConfiguredParallelism,
            int activeCountAfterBatch,
            ThumbnailEngineRuntimeSnapshot engineSnapshot
        )
        {
            long totalCountAfter = Interlocked.Add(
                ref totalProcessedCount,
                batchState.BatchCompletedCount
            );
            long totalMsAfter = Interlocked.Add(ref totalElapsedMs, batchElapsedMs);
            WritePerfLog(
                CreateBatchSummaryMessage(
                    batchState,
                    batchElapsedMs,
                    liveParallelism,
                    nextParallelism,
                    latestConfiguredParallelism,
                    activeCountAfterBatch,
                    totalCountAfter,
                    totalMsAfter,
                    engineSnapshot
                )
            );
        }

        private static string CreateBatchSummaryMessage(
            ThumbnailQueueBatchState batchState,
            long batchElapsedMs,
            int liveParallelism,
            int nextParallelism,
            int latestConfiguredParallelism,
            int activeCountAfterBatch,
            long totalCountAfter,
            long totalMsAfter,
            ThumbnailEngineRuntimeSnapshot engineSnapshot
        )
        {
            string gpuMode = Environment.GetEnvironmentVariable(GpuDecodeModeEnvName) ?? "off";
            return
                $"thumb queue summary: gpu={gpuMode}, parallel={liveParallelism}, "
                + $"parallel_next={nextParallelism}, parallel_configured={latestConfiguredParallelism}, "
                + $"batch_count={batchState.BatchCompletedCount}, batch_ms={batchElapsedMs}, "
                + $"batch_failed={batchState.BatchFailedCount}, active={activeCountAfterBatch}, "
                + $"autogen_transient_fail={engineSnapshot.AutogenTransientFailureCount}, "
                + $"autogen_retry_success={engineSnapshot.AutogenRetrySuccessCount}, "
                + $"fallback_1pass={engineSnapshot.FallbackToFfmpegOnePassCount}, "
                + $"total_count={totalCountAfter}, total_ms={totalMsAfter}, "
                + $"{ThumbnailQueueMetrics.CreateSummary()}";
        }

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
                string baseDir = ThumbnailQueueHostPathPolicy.ResolveLogDirectoryPath();
                Directory.CreateDirectory(baseDir);
                string logPath = LogFileTimeWindowSeparator.PrepareForWrite(
                    Path.Combine(baseDir, "thumb_decode.log")
                );

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
