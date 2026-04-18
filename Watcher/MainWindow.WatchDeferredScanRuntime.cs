using System.Collections.Generic;
using System.Linq;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // DB切り替え時は旧watch差分の持ち越しを残さない。
        private void ClearDeferredWatchScanStates()
        {
            InvalidateWatchScanScope("clear-deferred-state");
            lock (_deferredWatchScanSync)
            {
                _deferredWatchScanStateByScope.Clear();
            }
        }

        // deferred state は先読みだけにし、同じ watch 回で新規再収集との再マージへ使う。
        private bool TryPeekDeferredWatchScanState(
            string dbFullPath,
            long requestScopeStamp,
            string watchFolder,
            bool includeSubfolders,
            out DeferredWatchScanStateSnapshot stateSnapshot
        )
        {
            if (!IsCurrentWatchScanScope(dbFullPath, requestScopeStamp))
            {
                stateSnapshot = default;
                return false;
            }

            string scopeKey = BuildDeferredWatchScanScopeKey(
                dbFullPath,
                watchFolder,
                includeSubfolders
            );
            lock (_deferredWatchScanSync)
            {
                if (
                    !_deferredWatchScanStateByScope.TryGetValue(
                        scopeKey,
                        out DeferredWatchScanState state
                    )
                    || state.PendingPaths.Count < 1
                )
                {
                    stateSnapshot = default;
                    return false;
                }

                List<string> pendingPaths = state.PendingPaths
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                if (pendingPaths.Count < 1)
                {
                    stateSnapshot = default;
                    return false;
                }

                stateSnapshot = new DeferredWatchScanStateSnapshot(
                    pendingPaths,
                    state.DeferredCursorUtc
                );
                return true;
            }
        }

        // 今回処理しきれない watch 候補は、次回以降へ回す。
        private void ReplaceDeferredWatchScanBatch(
            string dbFullPath,
            long requestScopeStamp,
            string watchFolder,
            bool includeSubfolders,
            IEnumerable<string> deferredPaths,
            DateTime? deferredCursorUtc
        )
        {
            List<string> sanitizedPaths = deferredPaths?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList() ?? [];
            if (sanitizedPaths.Count < 1)
            {
                return;
            }

            if (!IsCurrentWatchScanScope(dbFullPath, requestScopeStamp))
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"deferred watch batch skipped stale: db='{dbFullPath}' folder='{watchFolder}'"
                );
                return;
            }

            string scopeKey = BuildDeferredWatchScanScopeKey(
                dbFullPath,
                watchFolder,
                includeSubfolders
            );
            lock (_deferredWatchScanSync)
            {
                _deferredWatchScanStateByScope[scopeKey] = new DeferredWatchScanState(
                    sanitizedPaths,
                    deferredCursorUtc
                );
            }
        }

        // manual / auto が同じフォルダを全量走査する時は、watch の持ち越し分を捨てて重複を防ぐ。
        private void RemoveDeferredWatchScanState(
            string dbFullPath,
            long requestScopeStamp,
            string watchFolder,
            bool includeSubfolders
        )
        {
            if (!IsCurrentWatchScanScope(dbFullPath, requestScopeStamp))
            {
                return;
            }

            string scopeKey = BuildDeferredWatchScanScopeKey(
                dbFullPath,
                watchFolder,
                includeSubfolders
            );
            lock (_deferredWatchScanSync)
            {
                _deferredWatchScanStateByScope.Remove(scopeKey);
            }
        }

        // suppression で止めた今回分は、既存deferredの先頭へ積み直して catch-up で先に回収する。
        private void MergeDeferredWatchScanBatch(
            string dbFullPath,
            long requestScopeStamp,
            string watchFolder,
            bool includeSubfolders,
            IEnumerable<string> deferredPaths
        )
        {
            List<string> mergedPaths = deferredPaths?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList() ?? [];
            if (mergedPaths.Count < 1)
            {
                return;
            }

            DeferredWatchScanStateSnapshot existingState = default;
            bool hasExistingState = TryPeekDeferredWatchScanState(
                dbFullPath,
                requestScopeStamp,
                watchFolder,
                includeSubfolders,
                out existingState
            );
            if (hasExistingState && existingState.PendingPaths.Count > 0)
            {
                mergedPaths = MergeWatchDeferredPathsForUiSuppression(
                    mergedPaths,
                    existingState.PendingPaths,
                    []
                );
            }

            ReplaceDeferredWatchScanBatch(
                dbFullPath,
                requestScopeStamp,
                watchFolder,
                includeSubfolders,
                mergedPaths,
                existingState.DeferredCursorUtc
            );
        }
    }
}
