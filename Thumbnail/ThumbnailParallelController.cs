using System;
using System.Reflection;
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
        private const int DefaultScaleUpStepFastAfterDown = 2;
        private const int StableWindowRequired = 2;
        private const int DefaultStableWindowRequiredFastAfterDown = 1;
        private const int DownTransientFailureCountThreshold = 2;
        private const int DownBatchFailedCountThreshold = 3;
        // 24並列バッチで単発1件の揺らぎでは下げすぎないよう、8%（概ね2/24件）を閾値にする。
        private const double DownTransientRateThreshold = 0.08d;
        private const double DownFallbackRateThreshold = 0.08d;
        private static readonly TimeSpan ScaleDownCooldown = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ScaleUpCooldown = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan DefaultScaleUpCooldownFastAfterDown = TimeSpan.FromSeconds(
            12
        );
        private static readonly TimeSpan DefaultScaleUpBlockedAfterDown = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan DefaultFastRecoveryWindowAfterDown = TimeSpan.FromMinutes(
            3
        );

        private int currentParallelism;
        private int lastConfiguredParallelism;
        private int stableWindowCount;
        private DateTime lastScaleDownUtc = DateTime.MinValue;
        private DateTime lastScaleUpUtc = DateTime.MinValue;

        public ThumbnailParallelController(int initialParallelism)
        {
            currentParallelism = Clamp(initialParallelism);
            lastConfiguredParallelism = currentParallelism;
        }

        /// <summary>
        /// 設定値変更時の上限追従を行い、次バッチ実行に使う並列数を返す。
        /// </summary>
        public int EnsureWithinConfigured(int configuredParallelism)
        {
            int boundedConfigured = Clamp(configuredParallelism);
            bool configuredIncreased = boundedConfigured > lastConfiguredParallelism;
            lastConfiguredParallelism = boundedConfigured;

            if (currentParallelism > boundedConfigured)
            {
                currentParallelism = boundedConfigured;
                stableWindowCount = 0;
            }
            else if (configuredIncreased && currentParallelism < boundedConfigured)
            {
                // ユーザーが設定値を上げた時は、次バッチを待たず即時に新上限へ追従する。
                currentParallelism = boundedConfigured;
                stableWindowCount = 0;
                lastScaleUpUtc = DateTime.UtcNow;
            }

            if (currentParallelism < HardMinParallelism)
            {
                currentParallelism = HardMinParallelism;
            }

            return currentParallelism;
        }

        /// <summary>
        /// バックログがある時だけ、最低並列数まで即時に戻す。
        /// 「1並列に落ちたまま長時間復帰しない」状態を避けるための安全弁。
        /// </summary>
        public int EnsureMinimum(int configuredParallelism, int minimumParallelism)
        {
            int boundedConfigured = Clamp(configuredParallelism);
            int boundedMinimum = Clamp(minimumParallelism);
            if (boundedMinimum > boundedConfigured)
            {
                boundedMinimum = boundedConfigured;
            }

            if (currentParallelism < boundedMinimum)
            {
                currentParallelism = boundedMinimum;
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

            TimeSpan sinceLastDown = nowUtc - lastScaleDownUtc;
            int stableWindowRequiredFastAfterDown = ResolveFastRecoveryStableWindowRequired();
            int scaleUpStepFastAfterDown = ResolveFastRecoveryScaleUpStep();
            TimeSpan scaleUpCooldownFastAfterDown = ResolveFastRecoveryScaleUpCooldown();
            TimeSpan scaleUpBlockedAfterDown = ResolveScaleUpBlockedAfterDown();
            TimeSpan fastRecoveryWindowAfterDown = ResolveFastRecoveryWindowAfterDown();
            bool inFastRecoveryWindow =
                lastScaleDownUtc != DateTime.MinValue
                && sinceLastDown <= fastRecoveryWindowAfterDown;
            int stableRequired = inFastRecoveryWindow
                ? stableWindowRequiredFastAfterDown
                : StableWindowRequired;
            int scaleUpStep = inFastRecoveryWindow ? scaleUpStepFastAfterDown : ScaleUpStep;
            TimeSpan scaleUpCooldown = inFastRecoveryWindow
                ? scaleUpCooldownFastAfterDown
                : ScaleUpCooldown;

            bool hasDemand = queueActiveCount > currentParallelism * 2;
            bool canScaleUp =
                stableWindowCount >= stableRequired
                && hasDemand
                && currentParallelism < boundedConfigured
                && (nowUtc - lastScaleUpUtc) >= scaleUpCooldown
                && (nowUtc - lastScaleDownUtc) >= scaleUpBlockedAfterDown;
            if (canScaleUp)
            {
                int next = Math.Min(boundedConfigured, currentParallelism + scaleUpStep);
                if (next != currentParallelism)
                {
                    log?.Invoke(
                        $"parallel scale-up: {currentParallelism} -> {next} "
                            + $"reason=stable_windows={stableWindowCount}/{stableRequired} active={queueActiveCount} configured={boundedConfigured} "
                            + $"mode={(inFastRecoveryWindow ? "fast-recovery" : "normal")}"
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

        private static int ResolveFastRecoveryScaleUpStep()
        {
            return ResolveConfiguredInt(
                "ThumbnailParallelFastRecoveryScaleUpStep",
                DefaultScaleUpStepFastAfterDown,
                1,
                HardMaxParallelism
            );
        }

        private static int ResolveFastRecoveryStableWindowRequired()
        {
            return ResolveConfiguredInt(
                "ThumbnailParallelFastRecoveryStableWindows",
                DefaultStableWindowRequiredFastAfterDown,
                1,
                10
            );
        }

        private static TimeSpan ResolveFastRecoveryScaleUpCooldown()
        {
            int seconds = ResolveConfiguredInt(
                "ThumbnailParallelFastRecoveryScaleUpCooldownSec",
                (int)DefaultScaleUpCooldownFastAfterDown.TotalSeconds,
                1,
                300
            );
            return TimeSpan.FromSeconds(seconds);
        }

        private static TimeSpan ResolveScaleUpBlockedAfterDown()
        {
            int seconds = ResolveConfiguredInt(
                "ThumbnailParallelScaleUpBlockedAfterDownSec",
                (int)DefaultScaleUpBlockedAfterDown.TotalSeconds,
                1,
                300
            );
            return TimeSpan.FromSeconds(seconds);
        }

        private static TimeSpan ResolveFastRecoveryWindowAfterDown()
        {
            int seconds = ResolveConfiguredInt(
                "ThumbnailParallelFastRecoveryWindowSec",
                (int)DefaultFastRecoveryWindowAfterDown.TotalSeconds,
                10,
                1800
            );
            return TimeSpan.FromSeconds(seconds);
        }

        private static int ResolveConfiguredInt(
            string settingName,
            int defaultValue,
            int minValue,
            int maxValue
        )
        {
            if (!TryReadUserSettingInt(settingName, out int configuredValue))
            {
                return defaultValue;
            }

            return ResolveIntSetting(configuredValue, defaultValue, minValue, maxValue);
        }

        private static int ResolveIntSetting(
            int configuredValue,
            int defaultValue,
            int minValue,
            int maxValue
        )
        {
            if (configuredValue < minValue || configuredValue > maxValue)
            {
                return defaultValue;
            }

            return configuredValue;
        }

        private static bool TryReadUserSettingInt(string settingName, out int value)
        {
            value = 0;
            object settings = GetSettingsDefaultInstance();
            if (settings == null)
            {
                return false;
            }

            try
            {
                PropertyInfo settingProperty = settings
                    .GetType()
                    .GetProperty(settingName, BindingFlags.Instance | BindingFlags.Public);
                if (settingProperty == null)
                {
                    return false;
                }

                object raw = settingProperty.GetValue(settings);
                if (raw is int intValue)
                {
                    value = intValue;
                    return true;
                }

                if (raw != null && int.TryParse(raw.ToString(), out int parsed))
                {
                    value = parsed;
                    return true;
                }
            }
            catch
            {
                // 設定取得失敗時は既定値フォールバックで継続する。
            }

            return false;
        }

        private static object GetSettingsDefaultInstance()
        {
            try
            {
                Type settingsType = ResolveSettingsType();
                if (settingsType == null)
                {
                    return null;
                }

                PropertyInfo defaultProperty = settingsType.GetProperty(
                    "Default",
                    BindingFlags.Static | BindingFlags.Public
                );
                return defaultProperty?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveSettingsType()
        {
            const string settingsTypeName = "IndigoMovieManager.Properties.Settings";
            Type resolved = Type.GetType($"{settingsTypeName}, IndigoMovieManager_fork", false);
            if (resolved != null)
            {
                return resolved;
            }

            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                Type found = loadedAssemblies[i].GetType(settingsTypeName, false);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
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
