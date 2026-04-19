using System.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 混雑度と直近の静かさを見て、Everything poll の待機間隔を決める。
        private int ResolveEverythingWatchPollDelayFromState(int queueActiveCount)
        {
            int delayMs = EverythingWatchPollIntervalMs;

            if (queueActiveCount >= EverythingWatchPollBusyThreshold)
            {
                delayMs = EverythingWatchPollIntervalBusyMs;
            }
            else if (queueActiveCount >= EverythingWatchPollMediumThreshold)
            {
                delayMs = EverythingWatchPollIntervalMediumMs;
            }

            // 起動直後を抜け、更新が静かな周期が続く時だけ少し疎にする。
            if (
                delayMs == EverythingWatchPollIntervalMs
                && !IsStartupFeedPartialActive
                && Volatile.Read(ref _consecutiveCalmEverythingPollCount)
                    >= EverythingWatchPollCalmCyclesThreshold
            )
            {
                delayMs = EverythingWatchPollIntervalCalmMs;
            }

            return delayMs;
        }

        // watch ポーリング1周の静かさを記録し、次回の待機間隔判断に使う。
        private void RecordEverythingWatchPollResult(int updateCount)
        {
            Volatile.Write(ref _lastEverythingPollUpdateCount, updateCount);

            if (IsStartupFeedPartialActive)
            {
                Volatile.Write(ref _consecutiveCalmEverythingPollCount, 0);
                return;
            }

            if (updateCount <= EverythingWatchPollLowUpdateThreshold)
            {
                Interlocked.Increment(ref _consecutiveCalmEverythingPollCount);
                return;
            }

            Volatile.Write(ref _consecutiveCalmEverythingPollCount, 0);
        }
    }
}
