using System;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 検索のような明示的ユーザー要求が走っている間は、背後処理を後ろへ逃がして完了を優先する。
        private readonly object _userPriorityWorkSync = new();
        private int _userPriorityWorkCount;

        private void BeginUserPriorityWork(string reason)
        {
            bool activated = false;
            if (_userPriorityWorkSync == null)
            {
                return;
            }

            lock (_userPriorityWorkSync)
            {
                _userPriorityWorkCount++;
                activated = _userPriorityWorkCount == 1;
            }

            if (activated)
            {
                DebugRuntimeLog.Write("ui-priority", $"user priority begin: reason={reason}");
            }
        }

        private void EndUserPriorityWork(string reason)
        {
            bool wasActive;
            bool isStillActive;
            bool hasDeferredWatchWork;
            if (_userPriorityWorkSync == null)
            {
                return;
            }

            lock (_userPriorityWorkSync)
            {
                wasActive = _userPriorityWorkCount > 0;
                if (_userPriorityWorkCount > 0)
                {
                    _userPriorityWorkCount--;
                }

                isStillActive = _userPriorityWorkCount > 0;
                hasDeferredWatchWork = !isStillActive && ConsumeWatchWorkDeferredForUserPriorityCatchUp();
            }

            if (!wasActive)
            {
                return;
            }

            if (!isStillActive)
            {
                DebugRuntimeLog.Write("ui-priority", $"user priority end: reason={reason}");
            }

            if (ShouldQueueBackgroundCatchUpAfterUserPriority(isStillActive, hasDeferredWatchWork))
            {
                DebugRuntimeLog.Write(
                    "ui-priority",
                    $"user priority catch-up queued: reason={reason}"
                );
                _ = QueueCheckFolderAsync(CheckMode.Watch, $"user-priority-resume:{reason}");
            }
        }

        private bool IsUserPriorityWorkActive()
        {
            if (_userPriorityWorkSync == null)
            {
                return false;
            }

            lock (_userPriorityWorkSync)
            {
                return _userPriorityWorkCount > 0;
            }
        }

        // user-priority の解除と defer 記録を同じロック順に寄せ、解除境界の catch-up 取りこぼしを防ぐ。
        private bool TryMarkWatchWorkDeferredForUserPriorityCatchUp(string trigger)
        {
            if (_userPriorityWorkSync == null || _watchUiSuppressionSync == null)
            {
                return false;
            }

            bool shouldLog = false;
            lock (_userPriorityWorkSync)
            {
                if (_userPriorityWorkCount <= 0)
                {
                    return false;
                }

                lock (_watchUiSuppressionSync)
                {
                    shouldLog = !_watchWorkDeferredWhileSuppressed;
                    _watchWorkDeferredWhileSuppressed = true;
                }
            }

            if (shouldLog)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"watch work deferred for background catch-up: trigger={trigger}"
                );
            }

            return true;
        }

        private bool ConsumeWatchWorkDeferredForUserPriorityCatchUp()
        {
            if (_watchUiSuppressionSync == null)
            {
                return false;
            }

            lock (_watchUiSuppressionSync)
            {
                bool hasDeferredWatchWork = _watchWorkDeferredWhileSuppressed;
                if (hasDeferredWatchWork)
                {
                    _watchWorkDeferredWhileSuppressed = false;
                }

                return hasDeferredWatchWork;
            }
        }

        // 現在の mode で背後処理を後ろへ逃がすべきかを、runtime 状態込みでまとめる。
        private bool ShouldDeferCurrentBackgroundWork(CheckMode mode)
        {
            return ShouldDeferBackgroundWorkForUserPriority(
                IsUserPriorityWorkActive(),
                mode == CheckMode.Manual
            );
        }
    }
}
