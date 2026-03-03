using System;
using System.Threading;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル生成の並列数を、失敗傾向とキュー滞留に応じて段階的に調整する。
    /// </summary>
    public sealed class ThumbnailParallelController
    {
        private const int HardMinParallelism = 1;
        private const int HardMaxParallelism = 24;
        private const int SoftMinParallelism = 4;
        private const int ScaleDownStep = 2;
        private const int ScaleUpStep = 1;
        private const int StableWindowRequired = 2;
        private const int DownTransientFailureCountThreshold = 2;
        private const int DownBatchFailedCountThreshold = 3;
        // 24並列バッチで単発1件の揺らぎでは下げすぎないよう、8%（概ね2/24件）を閾値にする。
        private const double DownTransientRateThreshold = 0.08d;
        private const double DownFallbackRateThreshold = 0.08d;
        private static readonly TimeSpan ScaleDownCooldown = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ScaleUpCooldown = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ScaleUpBlockedAfterDown = TimeSpan.FromSeconds(90);

        private int currentParallelism;
        private int stableWindowCount;
        private DateTime lastScaleDownUtc = DateTime.MinValue;
        private DateTime lastScaleUpUtc = DateTime.MinValue;

        public ThumbnailParallelController(int initialParallelism)
        {
            currentParallelism = Clamp(initialParallelism);
        }

        /// <summary>
        /// 設定値変更時の上限追従を行い、次バッチ実行に使う並列数を返す。
        /// </summary>
        public int EnsureWithinConfigured(int configuredParallelism)
        {
            int boundedConfigured = Clamp(configuredParallelism);
            if (currentParallelism > boundedConfigured)
            {
                currentParallelism = boundedConfigured;
                stableWindowCount = 0;
            }

            if (currentParallelism < HardMinParallelism)
            {
                currentParallelism = HardMinParallelism;
            }

            return currentParallelism;
        }

        /// <summary>
        /// 直近バッチの結果を見て、次バッチで使う並列数を決める。
        /// </summary>
        public int EvaluateNext(
            int configuredParallelism,
            int batchProcessedCount,
            int batchFailedCount,
            int queueActiveCount,
            ThumbnailEngineRuntimeSnapshot engineSnapshot,
            Action<string> log
        )
        {
            int boundedConfigured = Clamp(configuredParallelism);
            int dynamicMin = Math.Min(SoftMinParallelism, boundedConfigured);
            if (dynamicMin < HardMinParallelism)
            {
                dynamicMin = HardMinParallelism;
            }

            if (currentParallelism > boundedConfigured)
            {
                currentParallelism = boundedConfigured;
            }

            int safeProcessed = Math.Max(1, batchProcessedCount);
            double transientRate = (double)engineSnapshot.AutogenTransientFailureCount / safeProcessed;
            double fallbackRate = (double)engineSnapshot.FallbackToFfmpegOnePassCount / safeProcessed;
            bool shouldScaleDown =
                batchFailedCount >= DownBatchFailedCountThreshold
                || engineSnapshot.AutogenTransientFailureCount >= DownTransientFailureCountThreshold
                || transientRate >= DownTransientRateThreshold
                || fallbackRate >= DownFallbackRateThreshold;

            DateTime nowUtc = DateTime.UtcNow;
            if (
                shouldScaleDown
                && currentParallelism > dynamicMin
                && (nowUtc - lastScaleDownUtc) >= ScaleDownCooldown
            )
            {
                int next = Math.Max(dynamicMin, currentParallelism - ScaleDownStep);
                if (next != currentParallelism)
                {
                    log?.Invoke(
                        $"parallel scale-down: {currentParallelism} -> {next} "
                            + $"reason=transient_fail={engineSnapshot.AutogenTransientFailureCount} "
                            + $"fallback_1pass={engineSnapshot.FallbackToFfmpegOnePassCount} "
                            + $"batch_failed={batchFailedCount} transient_rate={transientRate:0.000} fallback_rate={fallbackRate:0.000}"
                    );
                    currentParallelism = next;
                    stableWindowCount = 0;
                    lastScaleDownUtc = nowUtc;
                }

                return currentParallelism;
            }

            bool isStableWindow =
                batchFailedCount == 0
                && engineSnapshot.AutogenTransientFailureCount == 0
                && engineSnapshot.FallbackToFfmpegOnePassCount == 0;
            if (isStableWindow)
            {
                stableWindowCount++;
            }
            else
            {
                stableWindowCount = 0;
            }

            bool hasDemand = queueActiveCount > currentParallelism * 2;
            bool canScaleUp =
                stableWindowCount >= StableWindowRequired
                && hasDemand
                && currentParallelism < boundedConfigured
                && (nowUtc - lastScaleUpUtc) >= ScaleUpCooldown
                && (nowUtc - lastScaleDownUtc) >= ScaleUpBlockedAfterDown;
            if (canScaleUp)
            {
                int next = Math.Min(boundedConfigured, currentParallelism + ScaleUpStep);
                if (next != currentParallelism)
                {
                    log?.Invoke(
                        $"parallel scale-up: {currentParallelism} -> {next} "
                            + $"reason=stable_windows={stableWindowCount} active={queueActiveCount} configured={boundedConfigured}"
                    );
                    currentParallelism = next;
                    stableWindowCount = 0;
                    lastScaleUpUtc = nowUtc;
                }
            }

            return currentParallelism;
        }

        public static int Clamp(int parallelism)
        {
            if (parallelism < HardMinParallelism)
            {
                return HardMinParallelism;
            }
            if (parallelism > HardMaxParallelism)
            {
                return HardMaxParallelism;
            }
            return parallelism;
        }
    }

    /// <summary>
    /// エンジン実行中の一時失敗やフォールバック件数を、並列制御の入力として集約する。
    /// </summary>
    public static class ThumbnailEngineRuntimeStats
    {
        private static long autogenTransientFailureCountWindow;
        private static long autogenRetrySuccessCountWindow;
        private static long fallbackToFfmpegOnePassCountWindow;

        public static void RecordAutogenTransientFailure()
        {
            _ = Interlocked.Increment(ref autogenTransientFailureCountWindow);
        }

        public static void RecordAutogenRetrySuccess()
        {
            _ = Interlocked.Increment(ref autogenRetrySuccessCountWindow);
        }

        public static void RecordFallbackToFfmpegOnePass()
        {
            _ = Interlocked.Increment(ref fallbackToFfmpegOnePassCountWindow);
        }

        public static ThumbnailEngineRuntimeSnapshot ConsumeWindow()
        {
            return new ThumbnailEngineRuntimeSnapshot(
                Interlocked.Exchange(ref autogenTransientFailureCountWindow, 0),
                Interlocked.Exchange(ref autogenRetrySuccessCountWindow, 0),
                Interlocked.Exchange(ref fallbackToFfmpegOnePassCountWindow, 0)
            );
        }
    }

    public readonly struct ThumbnailEngineRuntimeSnapshot
    {
        public ThumbnailEngineRuntimeSnapshot(
            long autogenTransientFailureCount,
            long autogenRetrySuccessCount,
            long fallbackToFfmpegOnePassCount
        )
        {
            AutogenTransientFailureCount = autogenTransientFailureCount;
            AutogenRetrySuccessCount = autogenRetrySuccessCount;
            FallbackToFfmpegOnePassCount = fallbackToFfmpegOnePassCount;
        }

        public long AutogenTransientFailureCount { get; }
        public long AutogenRetrySuccessCount { get; }
        public long FallbackToFfmpegOnePassCount { get; }
    }
}
