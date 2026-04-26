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
                hasDeferredWatchWork = _watchWorkDeferredWhileSuppressed;
                if (!isStillActive && hasDeferredWatchWork)
                {
                    _watchWorkDeferredWhileSuppressed = false;
                }
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
