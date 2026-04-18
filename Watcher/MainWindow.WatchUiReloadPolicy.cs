using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // watch 起点の全件再読込は即時連打せず、短い遅延で最新1回へ圧縮する。
        private const int WatchDeferredUiReloadDelayMs = 350;
        private readonly object _watchDeferredUiReloadSync = new();
        private CancellationTokenSource _watchDeferredUiReloadCts = new();
        private int _watchDeferredUiReloadRevision;
        private bool _watchDeferredUiReloadPending;
        private bool _watchDeferredUiReloadQueryOnly;
        private List<WatchChangedMovie> _watchDeferredUiReloadChangedMovies = [];

        // watch 常時監視中だけ、終端のフル reload を短時間 debounce して UI テンポを守る。
        internal static bool ShouldUseDeferredWatchUiReload(bool hasChanges, bool isWatchMode)
        {
            return hasChanges && isWatchMode;
        }

        // watch の変更が in-memory に反映済みなら、DB再読込を飛ばして query-only 再計算へ寄せる。
        internal static bool ShouldUseQueryOnlyWatchUiReload(
            bool hasChanges,
            bool isWatchMode,
            bool canUseQueryOnlyReload
        )
        {
            return hasChanges && isWatchMode && canUseQueryOnlyReload;
        }

        // 遅延実行時には、まだ同じDB向けの最新要求かを確認して stale reload を止める。
        internal static bool CanApplyDeferredWatchUiReload(
            string currentDbFullPath,
            string scheduledDbFullPath,
            bool isWatchSuppressedByUi,
            int requestRevision,
            int currentRevision
        )
        {
            if (isWatchSuppressedByUi)
            {
                return false;
            }

            if (requestRevision != currentRevision)
            {
                return false;
            }

            if (
                string.IsNullOrWhiteSpace(currentDbFullPath)
                || string.IsNullOrWhiteSpace(scheduledDbFullPath)
            )
            {
                return false;
            }

            return string.Equals(
                currentDbFullPath,
                scheduledDbFullPath,
                StringComparison.OrdinalIgnoreCase
            );
        }

        // 終端reloadの分岐を純粋値へ落とし、呼び出し側は plan の実行だけに寄せる。
        internal static WatchUiReloadPlan EvaluateWatchUiReloadPlan(
            bool hasChanges,
            bool isWatchMode,
            bool isSuppressed,
            bool canUseQueryOnlyReload
        )
        {
            if (!hasChanges)
            {
                return new WatchUiReloadPlan(WatchUiReloadAction.SkipNoChanges, false);
            }

            if (isSuppressed)
            {
                return new WatchUiReloadPlan(WatchUiReloadAction.DeferBySuppression, false);
            }

            bool useQueryOnlyReload = ShouldUseQueryOnlyWatchUiReload(
                hasChanges,
                isWatchMode,
                canUseQueryOnlyReload
            );
            if (ShouldUseDeferredWatchUiReload(hasChanges, isWatchMode))
            {
                return new WatchUiReloadPlan(WatchUiReloadAction.ScheduleDeferred, useQueryOnlyReload);
            }

            return new WatchUiReloadPlan(WatchUiReloadAction.ApplyImmediate, useQueryOnlyReload);
        }

        // watch 起点の reload 要求を最新1回へ圧縮し、連続通知時の UI 全面再読込を抑える。
        private void RequestDeferredWatchUiReload(
            string snapshotDbFullPath,
            string reason,
            bool useQueryOnlyReload,
            IEnumerable<WatchChangedMovie> changedMovies
        )
        {
            if (string.IsNullOrWhiteSpace(snapshotDbFullPath))
            {
                return;
            }

            CancellationTokenSource nextCts = new();
            CancellationTokenSource previousCts;
            int requestRevision;
            lock (GetWatchDeferredUiReloadSyncRoot())
            {
                previousCts = _watchDeferredUiReloadCts ?? new CancellationTokenSource();
                _watchDeferredUiReloadCts = nextCts;
                requestRevision = Interlocked.Increment(ref _watchDeferredUiReloadRevision);
                _watchDeferredUiReloadPending = true;
                _watchDeferredUiReloadQueryOnly = useQueryOnlyReload;
                _watchDeferredUiReloadChangedMovies = useQueryOnlyReload
                    ? MergeChangedMovies(_watchDeferredUiReloadChangedMovies, changedMovies)
                    : [];
            }

            try
            {
                previousCts.Cancel();
            }
            catch
            {
                // 旧要求の停止失敗でも最新要求は継続させる。
            }
            finally
            {
                previousCts.Dispose();
            }

            DebugRuntimeLog.Write(
                "watch-check",
                $"deferred ui reload scheduled: db='{snapshotDbFullPath}' revision={requestRevision} reason={reason} reload={(useQueryOnlyReload ? "query-only" : "full")} delay_ms={WatchDeferredUiReloadDelayMs}"
            );
            _ = RunDeferredWatchUiReloadAsync(
                snapshotDbFullPath,
                requestRevision,
                reason,
                nextCts.Token
            );
        }

        // DB切替や手動即時更新時は、残っている watch 用遅延 reload を取り消す。
        private bool CancelDeferredWatchUiReload(string reason)
        {
            CancellationTokenSource nextCts = new();
            CancellationTokenSource previousCts;
            int requestRevision;
            bool hadPendingRequest;
            lock (GetWatchDeferredUiReloadSyncRoot())
            {
                previousCts = _watchDeferredUiReloadCts ?? new CancellationTokenSource();
                _watchDeferredUiReloadCts = nextCts;
                requestRevision = Interlocked.Increment(ref _watchDeferredUiReloadRevision);
                hadPendingRequest = _watchDeferredUiReloadPending;
                _watchDeferredUiReloadPending = false;
                _watchDeferredUiReloadQueryOnly = false;
                _watchDeferredUiReloadChangedMovies = [];
            }

            try
            {
                previousCts.Cancel();
            }
            catch
            {
                // 取消失敗でも後続処理を止めない。
            }
            finally
            {
                previousCts.Dispose();
            }

            DebugRuntimeLog.Write(
                "watch-check",
                $"deferred ui reload canceled: revision={requestRevision} reason={reason}"
            );
            return hadPendingRequest;
        }

        // apply直前に現在要求を消費し、旧reloadが新しい要求のpendingを奪わないようにする。
        private bool TryConsumeDeferredWatchUiReload(
            int requestRevision,
            out bool useQueryOnlyReload,
            out List<WatchChangedMovie> changedMovies
        )
        {
            lock (GetWatchDeferredUiReloadSyncRoot())
            {
                if (
                    !_watchDeferredUiReloadPending
                    || requestRevision != _watchDeferredUiReloadRevision
                )
                {
                    useQueryOnlyReload = false;
                    changedMovies = [];
                    return false;
                }

                _watchDeferredUiReloadPending = false;
                useQueryOnlyReload = _watchDeferredUiReloadQueryOnly;
                changedMovies = _watchDeferredUiReloadChangedMovies?.ToList() ?? [];
                _watchDeferredUiReloadChangedMovies = [];
                return true;
            }
        }

        // テストの未初期化インスタンスでもlock先が null にならないように退避する。
        private object GetWatchDeferredUiReloadSyncRoot()
        {
            return _watchDeferredUiReloadSync ?? _watchUiSuppressionSync ?? this;
        }

        // 遅延窓の後で、まだ最新要求なら現在の sort / search 条件で MainDB を引き直す。
        private async Task RunDeferredWatchUiReloadAsync(
            string snapshotDbFullPath,
            int requestRevision,
            string reason,
            CancellationToken cancellationToken
        )
        {
            try
            {
                await Task.Delay(WatchDeferredUiReloadDelayMs, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await Dispatcher.InvokeAsync(
                () => ApplyDeferredWatchUiReloadOnUiThread(
                    snapshotDbFullPath,
                    requestRevision,
                    reason
                ),
                System.Windows.Threading.DispatcherPriority.Background
            );
        }

        // 遅延reloadの本体を分け、UIスレッド実行時もテスト時も同じ分岐を通す。
        private void ApplyDeferredWatchUiReloadOnUiThread(
            string snapshotDbFullPath,
            int requestRevision,
            string reason
        )
        {
            if (
                !TryConsumeDeferredWatchUiReload(
                    requestRevision,
                    out bool useQueryOnlyReload,
                    out List<WatchChangedMovie> changedMovies
                )
            )
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"deferred ui reload skipped stale: db='{snapshotDbFullPath}' revision={requestRevision} reason={reason}"
                );
                return;
            }

            if (
                !CanApplyDeferredWatchUiReload(
                    MainVM?.DbInfo?.DBFullPath ?? "",
                    snapshotDbFullPath,
                    IsWatchSuppressedByUi(),
                    requestRevision,
                    Volatile.Read(ref _watchDeferredUiReloadRevision)
                )
            )
            {
                if (ShouldSuppressWatchWorkByUi(IsWatchSuppressedByUi(), true))
                {
                    MarkWatchWorkDeferredWhileSuppressed($"deferred-ui-reload:{reason}");
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"deferred ui reload suppressed: db='{snapshotDbFullPath}' revision={requestRevision} reason={reason}"
                    );
                    return;
                }

                DebugRuntimeLog.Write(
                    "watch-check",
                    $"deferred ui reload skipped stale: db='{snapshotDbFullPath}' revision={requestRevision} reason={reason}"
                );
                return;
            }

            string currentSort = MainVM?.DbInfo?.Sort ?? "";
            DebugRuntimeLog.Write(
                "watch-check",
                $"deferred ui reload apply: db='{snapshotDbFullPath}' revision={requestRevision} reason={reason} reload={(useQueryOnlyReload ? "query-only" : "full")} sort={currentSort} changed_paths={changedMovies.Count}"
            );
            InvokeWatchUiReload(
                currentSort,
                useQueryOnlyReload,
                $"deferred:{reason}",
                changedMovies
            );
        }

        // watch本流の reload 方針はここだけで決め、scan 本体から分岐密度を追い出す。
        private void HandleFolderCheckUiReloadAfterChanges(
            bool hasChanges,
            CheckMode mode,
            string snapshotDbFullPath,
            bool canUseQueryOnlyReload,
            IReadOnlyList<WatchChangedMovie> changedMovies
        )
        {
            HandleFolderCheckUiReloadAfterChangesWithSort(
                hasChanges,
                mode,
                snapshotDbFullPath,
                canUseQueryOnlyReload,
                changedMovies,
                MainVM?.DbInfo?.Sort ?? ""
            );
        }

        // 走査完了時の UI 再読込は、呼び出し側で確定した sort を使って MainWindow 依存を薄める。
        private void HandleFolderCheckUiReloadAfterChangesWithSort(
            bool hasChanges,
            CheckMode mode,
            string snapshotDbFullPath,
            bool canUseQueryOnlyReload,
            IReadOnlyList<WatchChangedMovie> changedMovies,
            string currentSort
        )
        {
            WatchUiReloadPlan reloadPlan = EvaluateWatchUiReloadPlan(
                hasChanges,
                mode == CheckMode.Watch,
                ShouldSuppressWatchWorkByUi(IsWatchSuppressedByUi(), mode == CheckMode.Watch),
                canUseQueryOnlyReload
            );
            if (reloadPlan.Action == WatchUiReloadAction.SkipNoChanges)
            {
                return;
            }

            if (reloadPlan.Action == WatchUiReloadAction.DeferBySuppression)
            {
                MarkWatchWorkDeferredWhileSuppressed($"final-reload:{mode}");
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"skip final watch ui reload by suppression: mode={mode} db='{snapshotDbFullPath}'"
                );
                return;
            }

            if (reloadPlan.Action == WatchUiReloadAction.ScheduleDeferred)
            {
                RequestDeferredWatchUiReload(
                    snapshotDbFullPath,
                    $"check-folder:{mode}",
                    reloadPlan.UseQueryOnlyReload,
                    changedMovies
                );
                return;
            }

            CancelDeferredWatchUiReload($"immediate-reload:{mode}");
            DebugRuntimeLog.Write(
                "watch-check",
                $"final folder check ui reload apply: mode={mode} db='{snapshotDbFullPath}' reload={(reloadPlan.UseQueryOnlyReload ? "query-only" : "full")} changed_paths={changedMovies?.Count ?? 0}"
            );
            InvokeWatchUiReload(
                currentSort,
                reloadPlan.UseQueryOnlyReload,
                $"final:{mode}",
                changedMovies
            );
        }

        // watch の query-only は、DB再読込へ戻さず in-memory 一覧から再計算する。
        private void InvokeWatchUiReload(
            string sort,
            bool useQueryOnlyReload,
            string reason,
            IReadOnlyList<WatchChangedMovie> changedMovies
        )
        {
            if (!useQueryOnlyReload)
            {
                InvokeFilterAndSortForWatch(sort, true);
                return;
            }

            Action<string, string, IReadOnlyList<WatchChangedMovie>> refreshTestHook =
                RefreshMovieViewFromCurrentSourceForTesting;
            if (refreshTestHook != null)
            {
                refreshTestHook(sort, reason, changedMovies ?? []);
                return;
            }

            _ = RefreshMovieViewFromCurrentSourceAsync(
                sort,
                "watch-query-only",
                UiHangActivityKind.Watch,
                changedMovies
            );
        }

        // テスト時だけ差し替え可能にし、本番では既存の FilterAndSort をそのまま使う。
        private void InvokeFilterAndSortForWatch(string sort, bool isGetNew)
        {
            Action<string, bool> testHook = FilterAndSortForTesting;
            if (testHook != null)
            {
                testHook(sort, isGetNew);
                return;
            }

            FilterAndSort(sort, isGetNew);
        }

        internal enum WatchUiReloadAction
        {
            SkipNoChanges,
            DeferBySuppression,
            ScheduleDeferred,
            ApplyImmediate,
        }

        internal readonly record struct WatchUiReloadPlan(
            WatchUiReloadAction Action,
            bool UseQueryOnlyReload
        );
    }
}
