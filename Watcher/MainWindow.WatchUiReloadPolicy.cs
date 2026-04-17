using System;
using System.Collections.Generic;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
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
            if (!hasChanges)
            {
                return;
            }

            if (ShouldSuppressWatchWorkByUi(IsWatchSuppressedByUi(), mode == CheckMode.Watch))
            {
                MarkWatchWorkDeferredWhileSuppressed($"final-reload:{mode}");
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"skip final watch ui reload by suppression: mode={mode} db='{snapshotDbFullPath}'"
                );
                return;
            }

            bool useQueryOnlyReload = ShouldUseQueryOnlyWatchUiReload(
                hasChanges,
                mode == CheckMode.Watch,
                canUseQueryOnlyReload
            );

            if (ShouldUseDeferredWatchUiReload(hasChanges, mode == CheckMode.Watch))
            {
                RequestDeferredWatchUiReload(
                    snapshotDbFullPath,
                    $"check-folder:{mode}",
                    useQueryOnlyReload,
                    changedMovies
                );
                return;
            }

            CancelDeferredWatchUiReload($"immediate-reload:{mode}");
            DebugRuntimeLog.Write(
                "watch-check",
                $"final folder check ui reload apply: mode={mode} db='{snapshotDbFullPath}' reload={(useQueryOnlyReload ? "query-only" : "full")} changed_paths={changedMovies?.Count ?? 0}"
            );
            InvokeWatchUiReload(
                currentSort,
                useQueryOnlyReload,
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
    }
}
