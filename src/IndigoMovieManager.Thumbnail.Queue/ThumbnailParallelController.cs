using System;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル生成の並列数を、失敗傾向とキュー滞留に応じて段階的に調整する。
    /// </summary>
    public sealed class ThumbnailParallelController
    {
        private const int HardMinParallelism = 2;
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
        private const double DefaultHighLoadWeightError = 0.30d;
        private const double DefaultHighLoadWeightQueuePressure = 0.25d;
        private const double DefaultHighLoadWeightSlowBacklog = 0.20d;
        private const double DefaultHighLoadWeightThroughputPenalty = 0.10d;
        private const double DefaultHighLoadWeightThermalWarning = 0.20d;
        private const double DefaultHighLoadWeightUsnMftBusy = 0.10d;
        private const double DefaultHighLoadRecoveryThreshold = 0.48d;
        private const double DefaultHighLoadMildThreshold = 0.60d;
        private const double DefaultHighLoadThreshold = 0.82d;
        private const double DefaultHighLoadDangerThreshold = 0.95d;
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
        /// 「下限並列に張り付いたまま長時間復帰しない」状態を避けるための安全弁。
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
            Action<string> log,
            int dynamicMinimumParallelism = SoftMinParallelism,
            bool allowScaleUp = true,
            int scaleUpDemandFactor = 2,
            int queueDemandPeakCount = -1,
            ThumbnailHighLoadInput? highLoadInput = null
        )
        {
            int boundedConfigured = Clamp(configuredParallelism);
            int dynamicMin = Clamp(dynamicMinimumParallelism);
            if (dynamicMin > boundedConfigured)
            {
                dynamicMin = boundedConfigured;
            }
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
            bool shouldScaleDownByError =
                batchFailedCount >= DownBatchFailedCountThreshold
                || engineSnapshot.AutogenTransientFailureCount >= DownTransientFailureCountThreshold
                || transientRate >= DownTransientRateThreshold
                || fallbackRate >= DownFallbackRateThreshold;
            ThumbnailHighLoadScoreResult highLoadScore = highLoadInput.HasValue
                ? CalculateHighLoadScore(highLoadInput.Value)
                : default;
            bool shouldScaleDownByHighLoad = highLoadScore.IsMildHighLoad;
            bool isHighLoadRecoveryWindow = !highLoadInput.HasValue || highLoadScore.IsRecoveryWindow;
            ThumbnailThermalSignalLevel thermalState = highLoadInput.HasValue
                ? highLoadInput.Value.ThermalState
                : ThumbnailThermalSignalLevel.Unavailable;
            bool shouldScaleDownByThermalCritical =
                thermalState == ThumbnailThermalSignalLevel.Critical;

            DateTime nowUtc = DateTime.UtcNow;
            if (
                (shouldScaleDownByError || shouldScaleDownByHighLoad || shouldScaleDownByThermalCritical)
                && currentParallelism > dynamicMin
                && (nowUtc - lastScaleDownUtc) >= ScaleDownCooldown
            )
            {
                int next = currentParallelism;
                string scaleDownMode;
                if (thermalState == ThumbnailThermalSignalLevel.Critical)
                {
                    next = dynamicMin;
                    scaleDownMode = "thermal-critical";
                }
                else if (highLoadScore.IsDanger)
                {
                    next = dynamicMin;
                    scaleDownMode = "high-load-danger";
                }
                else if (shouldScaleDownByError || highLoadScore.IsHighLoad)
                {
                    next = Math.Max(dynamicMin, currentParallelism - ScaleDownStep);
                    scaleDownMode = shouldScaleDownByError && shouldScaleDownByHighLoad
                        ? "error+high-load"
                        : shouldScaleDownByError
                            ? "error"
                            : "high-load";
                }
                else
                {
                    next = Math.Max(dynamicMin, currentParallelism - 1);
                    scaleDownMode = "high-load-mild";
                }

                if (next != currentParallelism)
                {
                    string logCategory = scaleDownMode switch
                    {
                        "error" => "error",
                        "error+high-load" => "error+high-load",
                        _ => "high-load",
                    };
                    log?.Invoke(
                        $"parallel scale-down: {currentParallelism} -> {next} "
                            + $"category={logCategory} "
                            + $"mode={scaleDownMode} "
                            + $"reason=transient_fail={engineSnapshot.AutogenTransientFailureCount} "
                            + $"fallback_1pass={engineSnapshot.FallbackToFfmpegOnePassCount} "
                            + $"batch_failed={batchFailedCount} transient_rate={transientRate:0.000} fallback_rate={fallbackRate:0.000} "
                            + $"high_load={highLoadScore.HighLoadScore:0.000} "
                            + $"error_score={highLoadScore.ErrorScore:0.000} queue_score={highLoadScore.QueuePressureScore:0.000} "
                            + $"slow_score={highLoadScore.SlowBacklogScore:0.000} "
                            + $"throughput_score={highLoadScore.ThroughputPenaltyScore:0.000} "
                            + $"thermal_score={highLoadScore.ThermalScore:0.000} thermal_state={thermalState} "
                            + $"usnmft_score={highLoadScore.UsnMftScore:0.000} usnmft_state={highLoadInput?.UsnMftState ?? ThumbnailUsnMftSignalLevel.Unavailable}"
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
                && engineSnapshot.FallbackToFfmpegOnePassCount == 0
                && isHighLoadRecoveryWindow;
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

            int safeScaleUpDemandFactor = scaleUpDemandFactor < 1 ? 1 : scaleUpDemandFactor;
            // バッチ終端で残件が減っていても、開始時に十分な滞留があったなら復帰候補に含める。
            int demandPeakCount = queueDemandPeakCount < 0
                ? queueActiveCount
                : Math.Max(queueActiveCount, queueDemandPeakCount);
            bool hasDemand = demandPeakCount >= currentParallelism * safeScaleUpDemandFactor;
            bool canScaleUp =
                allowScaleUp
                && hasDemand
                && stableWindowCount >= stableRequired
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
                            + "category=high-load "
                            + $"reason=stable_windows={stableWindowCount}/{stableRequired} active_end={queueActiveCount} demand_peak={demandPeakCount} configured={boundedConfigured} "
                            + $"mode={(inFastRecoveryWindow ? "fast-recovery" : "normal")} demand_factor={safeScaleUpDemandFactor} "
                            + $"high_load={highLoadScore.HighLoadScore:0.000} "
                            + $"thermal_score={highLoadScore.ThermalScore:0.000} thermal_state={thermalState} "
                            + $"usnmft_score={highLoadScore.UsnMftScore:0.000} usnmft_state={highLoadInput?.UsnMftState ?? ThumbnailUsnMftSignalLevel.Unavailable}"
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
            int hardMaxParallelism = ThumbnailEnvConfig.GetThumbnailParallelismUpperBound();
            if (parallelism > hardMaxParallelism)
            {
                return hardMaxParallelism;
            }
            return parallelism;
        }

        /// <summary>
        /// 第1段階の内部メトリクスだけで高負荷スコアを組み立てる。
        /// 後段で CPU / 温度 / IPC シグナルを足しても壊れないよう、ここでは責務を局所化する。
        /// </summary>
        public static ThumbnailHighLoadScoreResult CalculateHighLoadScore(
            ThumbnailHighLoadInput input
        )
        {
            double highLoadWeightError = ResolveConfiguredDouble(
                "ThumbnailParallelHighLoadWeightError",
                DefaultHighLoadWeightError,
                0.0d,
                1.0d
            );
            double highLoadWeightQueuePressure = ResolveConfiguredDouble(
                "ThumbnailParallelHighLoadWeightQueuePressure",
                DefaultHighLoadWeightQueuePressure,
                0.0d,
                1.0d
            );
            double highLoadWeightSlowBacklog = ResolveConfiguredDouble(
                "ThumbnailParallelHighLoadWeightSlowBacklog",
                DefaultHighLoadWeightSlowBacklog,
                0.0d,
                1.0d
            );
            double highLoadWeightThroughputPenalty = ResolveConfiguredDouble(
                "ThumbnailParallelHighLoadWeightThroughputPenalty",
                DefaultHighLoadWeightThroughputPenalty,
                0.0d,
                1.0d
            );
            double highLoadWeightThermalWarning = ResolveConfiguredDouble(
                "ThumbnailParallelHighLoadWeightThermalWarning",
                DefaultHighLoadWeightThermalWarning,
                0.0d,
                1.0d
            );
            double highLoadWeightUsnMftBusy = ResolveConfiguredDouble(
                "ThumbnailParallelHighLoadWeightUsnMftBusy",
                DefaultHighLoadWeightUsnMftBusy,
                0.0d,
                1.0d
            );
            double highLoadRecoveryThreshold = ResolveConfiguredDouble(
                "ThumbnailParallelHighLoadRecoveryThreshold",
                DefaultHighLoadRecoveryThreshold,
                0.0d,
                1.0d
            );
            double highLoadMildThreshold = ResolveConfiguredDouble(
                "ThumbnailParallelHighLoadMildThreshold",
                DefaultHighLoadMildThreshold,
                0.0d,
                1.0d
            );
            double highLoadThreshold = ResolveConfiguredDouble(
                "ThumbnailParallelHighLoadThreshold",
                DefaultHighLoadThreshold,
                0.0d,
                1.0d
            );
            double highLoadDangerThreshold = ResolveConfiguredDouble(
                "ThumbnailParallelHighLoadDangerThreshold",
                DefaultHighLoadDangerThreshold,
                0.0d,
                1.0d
            );
            int safeProcessed = Math.Max(1, input.BatchProcessedCount);
            int safeCurrentParallelism = Math.Max(1, input.CurrentParallelism);
            int safeConfiguredParallelism = Math.Max(1, input.ConfiguredParallelism);

            double batchFailureRate = Clamp01((double)input.BatchFailedCount / safeProcessed);
            double transientRate = Clamp01(
                (double)input.EngineSnapshot.AutogenTransientFailureCount / safeProcessed
            );
            double fallbackRate = Clamp01(
                (double)input.EngineSnapshot.FallbackToFfmpegOnePassCount / safeProcessed
            );
            double retrySuccessRate = Clamp01(
                (double)input.EngineSnapshot.AutogenRetrySuccessCount / safeProcessed
            );

            // 失敗系は強め、ただし再試行成功が多い窓は少しだけ減点して過敏さを抑える。
            double errorScore = Clamp01(
                batchFailureRate * 0.45d
                    + transientRate * 0.30d
                    + fallbackRate * 0.25d
                    - retrySuccessRate * 0.10d
            );

            double currentPressure = Clamp01(
                (double)input.QueueActiveCount / Math.Max(1, safeCurrentParallelism * 2)
            );
            double configuredPressure = Clamp01(
                (double)input.QueueActiveCount / Math.Max(1, safeConfiguredParallelism * 2)
            );
            double scaleDownGap = Clamp01(
                (double)Math.Max(0, safeConfiguredParallelism - safeCurrentParallelism)
                    / safeConfiguredParallelism
            );

            // 現在の並列数で捌けていないかを主軸にしつつ、設定値との乖離も補助情報として入れる。
            double queuePressureScore = Clamp01(
                currentPressure * 0.55d
                    + configuredPressure * 0.25d
                    + scaleDownGap * 0.20d
            );

            double slowBacklogScore = input.HasSlowDemand ? 1.0d : 0.0d;
            double throughputPenaltyScore;
            if (input.BatchProcessedCount <= 0)
            {
                throughputPenaltyScore = input.QueueActiveCount > 0 ? 1.0d : 0.0d;
            }
            else
            {
                double msPerItem = (double)Math.Max(0L, input.BatchElapsedMs) / safeProcessed;
                throughputPenaltyScore = Clamp01((msPerItem - 1500d) / 4500d);
            }

            double thermalScore = input.ThermalState switch
            {
                ThumbnailThermalSignalLevel.Warning => 0.75d,
                ThumbnailThermalSignalLevel.Critical => 1.0d,
                _ => 0.0d,
            };
            double usnMftScore = 0.0d;
            if (input.UsnMftState == ThumbnailUsnMftSignalLevel.Busy)
            {
                double backlogScore = Clamp01(
                    (double)Math.Max(0, input.UsnMftJournalBacklogCount)
                        / Math.Max(8, safeConfiguredParallelism * 2)
                );
                double latencyScore = Clamp01(
                    (Math.Max(0L, input.UsnMftLastScanLatencyMs) - 3000d) / 9000d
                );

                // UsnMft は可用性異常ではなく、Busy で backlog / latency が伸びた時だけ I/O圧迫の補助シグナルに使う。
                usnMftScore = Clamp01(0.50d + backlogScore * 0.30d + latencyScore * 0.20d);
            }

            double highLoadScore = Clamp01(
                errorScore * highLoadWeightError
                    + queuePressureScore * highLoadWeightQueuePressure
                    + slowBacklogScore * highLoadWeightSlowBacklog
                    + throughputPenaltyScore * highLoadWeightThroughputPenalty
                    + thermalScore * highLoadWeightThermalWarning
                    + usnMftScore * highLoadWeightUsnMftBusy
            );

            return new ThumbnailHighLoadScoreResult(
                highLoadScore,
                errorScore,
                queuePressureScore,
                slowBacklogScore,
                throughputPenaltyScore,
                thermalScore,
                usnMftScore,
                highLoadScore <= highLoadRecoveryThreshold,
                highLoadScore >= highLoadMildThreshold,
                highLoadScore >= highLoadThreshold
                    || input.ThermalState == ThumbnailThermalSignalLevel.Critical,
                highLoadScore >= highLoadDangerThreshold
                    || input.ThermalState == ThumbnailThermalSignalLevel.Critical
            );
        }

        private static int ResolveFastRecoveryScaleUpStep()
        {
            return ResolveConfiguredInt(
                "ThumbnailParallelFastRecoveryScaleUpStep",
                DefaultScaleUpStepFastAfterDown,
                1,
                ThumbnailEnvConfig.GetThumbnailParallelismUpperBound()
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

        private static double ResolveConfiguredDouble(
            string settingName,
            double defaultValue,
            double minValue,
            double maxValue
        )
        {
            if (!TryReadUserSettingDouble(settingName, out double configuredValue))
            {
                return defaultValue;
            }

            if (
                double.IsNaN(configuredValue)
                || double.IsInfinity(configuredValue)
                || configuredValue < minValue
                || configuredValue > maxValue
            )
            {
                return defaultValue;
            }

            return configuredValue;
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

        private static bool TryReadUserSettingDouble(string settingName, out double value)
        {
            value = 0d;
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
                if (raw is double doubleValue)
                {
                    value = doubleValue;
                    return true;
                }

                if (raw is float floatValue)
                {
                    value = floatValue;
                    return true;
                }

                if (raw != null)
                {
                    string text = raw.ToString();
                    if (
                        double.TryParse(
                            text,
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out double parsedInvariant
                        )
                    )
                    {
                        value = parsedInvariant;
                        return true;
                    }

                    if (
                        double.TryParse(
                            text,
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.CurrentCulture,
                            out double parsedCurrent
                        )
                    )
                    {
                        value = parsedCurrent;
                        return true;
                    }
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
            return AppIdentityRuntime.ResolveSettingsType();
        }

        private static double Clamp01(double value)
        {
            if (value < 0d)
            {
                return 0d;
            }
            if (value > 1d)
            {
                return 1d;
            }
            return value;
        }
    }
}
