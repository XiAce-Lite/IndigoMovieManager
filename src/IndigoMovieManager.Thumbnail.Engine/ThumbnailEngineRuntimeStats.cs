using System.Threading;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// エンジン実行中の一時失敗やフォールバック件数を、並列制御と観測の共通入力として集約する。
    /// Queue 側へ寄せる並列制御本体とは分け、Engine 側の生成処理からも参照できるようにする。
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

    public readonly struct ThumbnailHighLoadInput
    {
        public ThumbnailHighLoadInput(
            int batchProcessedCount,
            int batchFailedCount,
            long batchElapsedMs,
            int queueActiveCount,
            int currentParallelism,
            int configuredParallelism,
            bool hasSlowDemand,
            ThumbnailEngineRuntimeSnapshot engineSnapshot,
            ThumbnailThermalSignalLevel thermalState = ThumbnailThermalSignalLevel.Unavailable,
            ThumbnailUsnMftSignalLevel usnMftState = ThumbnailUsnMftSignalLevel.Unavailable,
            long usnMftLastScanLatencyMs = 0,
            int usnMftJournalBacklogCount = 0
        )
        {
            BatchProcessedCount = batchProcessedCount;
            BatchFailedCount = batchFailedCount;
            BatchElapsedMs = batchElapsedMs;
            QueueActiveCount = queueActiveCount;
            CurrentParallelism = currentParallelism;
            ConfiguredParallelism = configuredParallelism;
            HasSlowDemand = hasSlowDemand;
            EngineSnapshot = engineSnapshot;
            ThermalState = thermalState;
            UsnMftState = usnMftState;
            UsnMftLastScanLatencyMs = usnMftLastScanLatencyMs;
            UsnMftJournalBacklogCount = usnMftJournalBacklogCount;
        }

        public int BatchProcessedCount { get; }
        public int BatchFailedCount { get; }
        public long BatchElapsedMs { get; }
        public int QueueActiveCount { get; }
        public int CurrentParallelism { get; }
        public int ConfiguredParallelism { get; }
        public bool HasSlowDemand { get; }
        public ThumbnailEngineRuntimeSnapshot EngineSnapshot { get; }
        public ThumbnailThermalSignalLevel ThermalState { get; }
        public ThumbnailUsnMftSignalLevel UsnMftState { get; }
        public long UsnMftLastScanLatencyMs { get; }
        public int UsnMftJournalBacklogCount { get; }
    }

    public enum ThumbnailThermalSignalLevel
    {
        Normal = 0,
        Warning = 1,
        Critical = 2,
        Unavailable = 3,
    }

    public enum ThumbnailUsnMftSignalLevel
    {
        Ready = 0,
        Busy = 1,
        Unavailable = 2,
        AccessDenied = 3,
    }

    public readonly struct ThumbnailHighLoadScoreResult
    {
        public ThumbnailHighLoadScoreResult(
            double highLoadScore,
            double errorScore,
            double queuePressureScore,
            double slowBacklogScore,
            double throughputPenaltyScore,
            double thermalScore,
            double usnMftScore,
            bool isRecoveryWindow,
            bool isMildHighLoad,
            bool isHighLoad,
            bool isDanger
        )
        {
            HighLoadScore = highLoadScore;
            ErrorScore = errorScore;
            QueuePressureScore = queuePressureScore;
            SlowBacklogScore = slowBacklogScore;
            ThroughputPenaltyScore = throughputPenaltyScore;
            ThermalScore = thermalScore;
            UsnMftScore = usnMftScore;
            IsRecoveryWindow = isRecoveryWindow;
            IsMildHighLoad = isMildHighLoad;
            IsHighLoad = isHighLoad;
            IsDanger = isDanger;
        }

        public double HighLoadScore { get; }
        public double ErrorScore { get; }
        public double QueuePressureScore { get; }
        public double SlowBacklogScore { get; }
        public double ThroughputPenaltyScore { get; }
        public double ThermalScore { get; }
        public double UsnMftScore { get; }
        public bool IsRecoveryWindow { get; }
        public bool IsMildHighLoad { get; }
        public bool IsHighLoad { get; }
        public bool IsDanger { get; }
    }
}
