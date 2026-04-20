using System.Collections.Generic;
using System.Linq;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 左ドロワー表示中は、watch 起点の新規仕事だけ抑えて操作テンポを守る。
        private readonly object _watchUiSuppressionSync = new();
        private int _watchUiSuppressionCount;
        private bool _watchWorkDeferredWhileSuppressed;

        // watch 開始直後の抑止判定を 1 か所へまとめ、Watcher 側の入口分岐を薄くする。
        private bool TryDeferWatchStart(CheckMode mode)
        {
            if (ShouldSuppressWatchWorkByUi(IsWatchSuppressedByUi(), mode == CheckMode.Watch))
            {
                MarkWatchWorkDeferredWhileSuppressed($"check-start:{mode}");
                return true;
            }

            if (
                ShouldDeferBackgroundWorkForUserPriority(
                    IsUserPriorityWorkActive(),
                    mode == CheckMode.Manual
                )
            )
            {
                MarkWatchWorkDeferredWhileSuppressed($"check-start-user-priority:{mode}");
                return true;
            }

            return false;
        }

        // 現在の mode で watch 仕事を抑止すべきかを 1 か所へまとめる。
        private bool ShouldSuppressCurrentWatchWork(CheckMode mode)
        {
            return ShouldSuppressWatchWorkByUi(IsWatchSuppressedByUi(), mode == CheckMode.Watch);
        }

        private void MergeWatchFolderDeferredWorkByUiSuppression(
            string snapshotDbFullPath,
            long requestScopeStamp,
            string checkFolder,
            bool includeSubfolders,
            IEnumerable<string> currentDeferredPaths,
            IEnumerable<string> remainingScanPaths,
            List<PendingMovieRegistration> pendingNewMovies,
            List<QueueObj> pendingQueueItems
        )
        {
            if (!IsCurrentWatchScanScope(snapshotDbFullPath, requestScopeStamp))
            {
                return;
            }

            List<string> deferredPaths = MergeWatchDeferredPathsForUiSuppression(
                currentDeferredPaths?.ToList() ?? [],
                remainingScanPaths?.ToList() ?? [],
                pendingNewMovies?.Select(x => x.MovieFullPath).ToList() ?? [],
                pendingQueueItems?.Select(x => x.MovieFullPath).ToList() ?? []
            );
            if (deferredPaths.Count > 0)
            {
                MergeDeferredWatchScanBatch(
                    snapshotDbFullPath,
                    requestScopeStamp,
                    checkFolder,
                    includeSubfolders,
                    deferredPaths
                );
            }
        }

        // 左ドロワー開中かどうかを、watch バックグラウンド側からも安全に見られるようにする。
        private bool IsWatchSuppressedByUi()
        {
            lock (_watchUiSuppressionSync)
            {
                return _watchUiSuppressionCount > 0;
            }
        }

        // 左ドロワーを開いた間は、新規の watch 仕事を入口で抑える。
        private void BeginWatchUiSuppression(string reason)
        {
            bool activated = false;
            bool hadPendingDeferredUiReload = false;
            lock (_watchUiSuppressionSync)
            {
                _watchUiSuppressionCount++;
                activated = _watchUiSuppressionCount == 1;
            }

            if (activated)
            {
                hadPendingDeferredUiReload = CancelDeferredWatchUiReload(
                    $"suppression-begin:{reason}"
                );
                if (hadPendingDeferredUiReload)
                {
                    // 旧reloadを潰しただけで終わらせず、解除後のcatch-upへ必ず戻す。
                    MarkWatchWorkDeferredWhileSuppressed($"deferred-ui-reload:{reason}");
                }

                DebugRuntimeLog.Write("watch-check", $"watch ui suppression begin: reason={reason}");
            }
        }

        // DB切替時は旧DB向けの保留だけ捨て、抑制状態そのものはUI実態へ合わせて維持する。
        private void ClearDeferredWatchWorkByUiSuppression()
        {
            lock (_watchUiSuppressionSync)
            {
                _watchWorkDeferredWhileSuppressed = false;
            }
        }

        // 左ドロワーを閉じたら、保留がある時だけ watch を1回再開させる。
        private void EndWatchUiSuppression(string reason)
        {
            bool wasSuppressed;
            bool isStillSuppressed;
            bool hasDeferredWatchWork;
            bool shouldSkipCatchUp;
            lock (_watchUiSuppressionSync)
            {
                wasSuppressed = _watchUiSuppressionCount > 0;
                if (_watchUiSuppressionCount > 0)
                {
                    _watchUiSuppressionCount--;
                }

                isStillSuppressed = _watchUiSuppressionCount > 0;
                hasDeferredWatchWork = _watchWorkDeferredWhileSuppressed;
                shouldSkipCatchUp = ShouldSkipWatchCatchUpAfterUiSuppression(reason);
                if (
                    wasSuppressed
                    && ShouldQueueWatchCatchUpAfterUiSuppression(
                        isStillSuppressed,
                        hasDeferredWatchWork
                    )
                )
                {
                    _watchWorkDeferredWhileSuppressed = false;
                }
            }

            if (!wasSuppressed)
            {
                return;
            }

            if (!isStillSuppressed)
            {
                DebugRuntimeLog.Write("watch-check", $"watch ui suppression end: reason={reason}");
            }

            if (
                ShouldQueueWatchCatchUpAfterUiSuppression(isStillSuppressed, hasDeferredWatchWork)
                && shouldSkipCatchUp
            )
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"watch ui suppression catch-up skipped: reason={reason}"
                );
                return;
            }

            if (
                ShouldQueueWatchCatchUpAfterUiSuppression(isStillSuppressed, hasDeferredWatchWork)
            )
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"watch ui suppression catch-up queued: reason={reason}"
                );
                _ = QueueCheckFolderAsync(CheckMode.Watch, $"ui-resume:{reason}");
            }
        }

        // 抑制中に入ってきた watch 仕事は、理由だけ記録して解除後の1回へ集約する。
        private void MarkWatchWorkDeferredWhileSuppressed(string trigger)
        {
            bool shouldLog = false;
            lock (_watchUiSuppressionSync)
            {
                if (_watchUiSuppressionCount < 1)
                {
                    return;
                }

                shouldLog = !_watchWorkDeferredWhileSuppressed;
                _watchWorkDeferredWhileSuppressed = true;
            }

            if (shouldLog)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"watch work deferred by ui suppression: trigger={trigger}"
                );
            }
        }

        private bool TryDeferWatchFolderWorkByUiSuppression(
            bool isWatchMode,
            string snapshotDbFullPath,
            long requestScopeStamp,
            string checkFolder,
            bool includeSubfolders,
            IEnumerable<string> currentDeferredPaths,
            IEnumerable<string> remainingScanPaths,
            List<PendingMovieRegistration> pendingNewMovies,
            List<QueueObj> pendingQueueItems,
            string trigger
        )
        {
            if (!ShouldSuppressWatchWorkByUi(IsWatchSuppressedByUi(), isWatchMode))
            {
                return false;
            }

            MarkWatchWorkDeferredWhileSuppressed(trigger);
            MergeWatchFolderDeferredWorkByUiSuppression(
                snapshotDbFullPath,
                requestScopeStamp,
                checkFolder,
                includeSubfolders,
                currentDeferredPaths,
                remainingScanPaths,
                pendingNewMovies,
                pendingQueueItems
            );
            return true;
        }
    }
}
