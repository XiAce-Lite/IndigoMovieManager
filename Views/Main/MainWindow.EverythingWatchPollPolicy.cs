using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int EverythingWatchPollCalmStartupGraceMs = 15000;
        private long _everythingWatchPollLoopStartedTick64;

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
                && HasEverythingWatchPollCalmDelayWarmupElapsed()
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

        // DB切替や監視設定変更後は、別スコープの静穏判定を持ち越さない。
        private void ResetEverythingWatchPollAdaptiveDelayState()
        {
            Volatile.Write(ref _lastEverythingPollUpdateCount, 0);
            Volatile.Write(ref _consecutiveCalmEverythingPollCount, 0);
            Volatile.Write(ref _lastEverythingPollDelayMs, EverythingWatchPollIntervalMs);
            Volatile.Write(ref _everythingWatchPollLoopStartedTick64, Environment.TickCount64);
        }

        // poll 起動直後は初期処理とぶつかりやすいため、calm 延長は一定時間後だけ許可する。
        private bool HasEverythingWatchPollCalmDelayWarmupElapsed()
        {
            long startedTick = Volatile.Read(ref _everythingWatchPollLoopStartedTick64);
            if (startedTick <= 0)
            {
                // 初回判定時に開始時刻を掴み、初期処理と競合しやすい時間帯は calm 延長を見送る。
                startedTick = Environment.TickCount64;
                Volatile.Write(ref _everythingWatchPollLoopStartedTick64, startedTick);
                return false;
            }

            long elapsedMs = Environment.TickCount64 - startedTick;
            if (elapsedMs < 0)
            {
                return false;
            }

            return elapsedMs >= EverythingWatchPollCalmStartupGraceMs;
        }
    }
}
