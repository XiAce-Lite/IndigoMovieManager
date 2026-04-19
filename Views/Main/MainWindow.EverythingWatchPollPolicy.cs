using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // Everything poll を走らせるかどうかの pure 判定を、UI本体から切り離してまとめる。
        internal static bool ShouldRunEverythingWatchPollPolicy(
            bool isStartupFeedPartialActive,
            bool isIntegrationConfigured,
            bool canUseAvailability,
            bool keepPollingForFallback,
            string dbPath,
            IEnumerable<string> watchFolders,
            Func<string, bool> pathExists,
            Func<string, bool> isEverythingEligiblePath
        )
        {
            if (isStartupFeedPartialActive)
            {
                return false;
            }

            if (!isIntegrationConfigured)
            {
                return false;
            }

            if (!canUseAvailability && !keepPollingForFallback)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(dbPath) || !Path.Exists(dbPath))
            {
                return false;
            }

            if (watchFolders == null)
            {
                return false;
            }

            Func<string, bool> exists = pathExists ?? Path.Exists;
            foreach (string watchFolder in watchFolders)
            {
                if (!exists(watchFolder))
                {
                    continue;
                }

                if (isEverythingEligiblePath?.Invoke(watchFolder) == true)
                {
                    return true;
                }
            }

            return false;
        }

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
