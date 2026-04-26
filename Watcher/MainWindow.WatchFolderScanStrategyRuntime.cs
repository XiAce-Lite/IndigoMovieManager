using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // watch 1回で処理する候補数を抑え、結果件数の多い差分でUIが詰まるのを防ぐ。
        private const int WatchScanProcessLimit = 200;

        /// <summary>
        /// Everything連携を優先して走査し、利用不可時は既存のファイルシステム走査へフォールバックする。
        /// Everything優先で候補収集し、利用不可時だけ既存のファイルシステム走査へ戻す。
        /// </summary>
        private FolderScanWithStrategyResult ScanFolderWithStrategyInBackground(
            CheckMode mode,
            string snapshotDbFullPath,
            long requestScopeStamp,
            string checkFolder,
            bool sub,
            string checkExt,
            bool prioritizeVisibleMovies,
            ISet<string> visibleMoviePaths
        )
        {
            DeferredWatchScanStateSnapshot deferredState = default;
            if (mode != CheckMode.Watch)
            {
                RemoveDeferredWatchScanState(
                    snapshotDbFullPath,
                    requestScopeStamp,
                    checkFolder,
                    sub
                );
            }
            else
            {
                TryPeekDeferredWatchScanState(
                    snapshotDbFullPath,
                    requestScopeStamp,
                    checkFolder,
                    sub,
                    out deferredState
                );
            }

            if (!IsEverythingEligiblePath(checkFolder, out string eligibilityReason))
            {
                FolderScanResult notEligibleFallback = ScanFolderInBackground(
                    checkFolder,
                    sub,
                    checkExt
                );
                if (mode == CheckMode.Watch)
                {
                    return FinalizeWatchScanWithDeferredCandidates(
                        snapshotDbFullPath,
                        requestScopeStamp,
                        checkFolder,
                        sub,
                        deferredState,
                        notEligibleFallback.ScannedCount,
                        notEligibleFallback.NewMoviePaths,
                        FileIndexStrategies.Filesystem,
                        $"{EverythingReasonCodes.PathNotEligiblePrefix}{eligibilityReason}",
                        prioritizeVisibleMovies,
                        visibleMoviePaths,
                        changedSinceUtc: null,
                        observedCursorUtc: null
                    );
                }

                return new FolderScanWithStrategyResult(
                    notEligibleFallback,
                    FileIndexStrategies.Filesystem,
                    $"{EverythingReasonCodes.PathNotEligiblePrefix}{eligibilityReason}",
                    hasIncrementalCursor: false
                );
            }

            bool shouldUseIncrementalCursor =
                mode == CheckMode.Watch || mode == CheckMode.Auto;
            DateTime? changedSinceUtc =
                shouldUseIncrementalCursor
                    ? LoadEverythingLastSyncUtc(
                        snapshotDbFullPath,
                        requestScopeStamp,
                        checkFolder,
                        sub
                    )
                    : null;
            if (shouldUseIncrementalCursor && !changedSinceUtc.HasValue)
            {
                string attr = BuildEverythingLastSyncAttr(checkFolder, sub);
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"incremental cursor unavailable: db='{snapshotDbFullPath}' folder='{checkFolder}' mode={mode} sub={sub} attr='{attr}' deferred_cursor='{deferredState.DeferredCursorUtc:O}'"
                );
            }
            FileIndexQueryOptions options = new()
            {
                RootPath = checkFolder,
                IncludeSubdirectories = sub,
                CheckExt = checkExt,
                ChangedSinceUtc = changedSinceUtc,
            };
            IntegrationMode integrationMode = GetEverythingIntegrationMode();
            ScanByProviderResult providerResult = _indexProviderFacade
                .CollectMoviePathsWithFallback(options, integrationMode);
            bool usedEverything = string.Equals(
                providerResult.Strategy,
                FileIndexStrategies.Everything,
                StringComparison.OrdinalIgnoreCase
            );
            List<string> candidatePaths = providerResult.MoviePaths;
            DateTime? maxObservedChangedUtc = providerResult.MaxObservedChangedUtc;
            string reason = providerResult.Reason;

            if (usedEverything)
            {
                List<string> newMoviePaths = [];
                int scannedCount = 0;
                foreach (string fullPath in candidatePaths)
                {
                    // ゴミ箱配下は検出対象から外し、watch本流へ混ぜない。
                    if (WatchPathFilter.ShouldExcludeFromWatchScan(fullPath))
                    {
                        continue;
                    }

                    scannedCount++;

                    // タブ欠損サムネ再生成の回帰を避けるため、事前除外は行わず空文字だけ弾く。
                    string fileBody = Path.GetFileNameWithoutExtension(fullPath);
                    if (string.IsNullOrWhiteSpace(fileBody))
                    {
                        continue;
                    }

                    newMoviePaths.Add(fullPath);
                }

                // 取りこぼしを避けるため、問い合わせ時刻ではなく「観測できた変更時刻の高水位」を保存する。
                DateTime? nextSyncUtc = maxObservedChangedUtc;
                if (mode == CheckMode.Watch)
                {
                    return FinalizeWatchScanWithDeferredCandidates(
                        snapshotDbFullPath,
                        requestScopeStamp,
                        checkFolder,
                        sub,
                        deferredState,
                        scannedCount,
                        newMoviePaths,
                        FileIndexStrategies.Everything,
                        reason,
                        prioritizeVisibleMovies,
                        visibleMoviePaths,
                        changedSinceUtc,
                        nextSyncUtc
                    );
                }

                if (FileIndexIncrementalSyncPolicy.ShouldAdvanceCursor(nextSyncUtc, changedSinceUtc))
                {
                    SaveEverythingLastSyncUtc(
                        snapshotDbFullPath,
                        requestScopeStamp,
                        checkFolder,
                        sub,
                        nextSyncUtc.Value
                    );
                }
                return new FolderScanWithStrategyResult(
                    new FolderScanResult(scannedCount, newMoviePaths),
                    FileIndexStrategies.Everything,
                    reason,
                    hasIncrementalCursor: shouldUseIncrementalCursor && changedSinceUtc.HasValue
                );
            }

            FolderScanResult fallbackResult = ScanFolderInBackground(checkFolder, sub, checkExt);
            if (mode == CheckMode.Watch)
            {
                return FinalizeWatchScanWithDeferredCandidates(
                    snapshotDbFullPath,
                    requestScopeStamp,
                    checkFolder,
                    sub,
                    deferredState,
                    fallbackResult.ScannedCount,
                    fallbackResult.NewMoviePaths,
                    FileIndexStrategies.Filesystem,
                    reason,
                    prioritizeVisibleMovies,
                    visibleMoviePaths,
                    changedSinceUtc,
                    observedCursorUtc: null
                );
            }
            return new FolderScanWithStrategyResult(
                fallbackResult,
                FileIndexStrategies.Filesystem,
                reason,
                hasIncrementalCursor: false
            );
        }

        // deferred backlog と今回収集分を同一回で再マージし、visible-first と cursor 保持を崩さない。
        private FolderScanWithStrategyResult FinalizeWatchScanWithDeferredCandidates(
            string snapshotDbFullPath,
            long requestScopeStamp,
            string checkFolder,
            bool sub,
            DeferredWatchScanStateSnapshot deferredState,
            int scannedCount,
            IReadOnlyList<string> collectedPaths,
            string strategy,
            string reason,
            bool prioritizeVisibleMovies,
            ISet<string> visibleMoviePaths,
            DateTime? changedSinceUtc,
            DateTime? observedCursorUtc
        )
        {
            (
                List<string> immediatePaths,
                List<string> deferredPaths
            ) = MergeDeferredAndCollectedWatchScanMoviePaths(
                deferredState.PendingPaths,
                collectedPaths,
                WatchScanProcessLimit,
                prioritizeVisibleMovies,
                visibleMoviePaths
            );
            DateTime? cursorToPersistUtc = MergeDeferredWatchScanCursorUtc(
                deferredState.DeferredCursorUtc,
                observedCursorUtc
            );
            if (deferredPaths.Count > 0)
            {
                // 次回送りが残る間は cursor だけ state 側へ持たせ、watch またぎでも visible を拾い直す。
                ReplaceDeferredWatchScanBatch(
                    snapshotDbFullPath,
                    requestScopeStamp,
                    checkFolder,
                    sub,
                    deferredPaths,
                    cursorToPersistUtc
                );
                return new FolderScanWithStrategyResult(
                    new FolderScanResult(scannedCount, immediatePaths),
                    strategy,
                    $"{reason} watch_batch_limit={WatchScanProcessLimit} deferred={deferredPaths.Count}",
                    hasIncrementalCursor:
                        string.Equals(
                            strategy,
                            FileIndexStrategies.Everything,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && changedSinceUtc.HasValue
                );
            }

            RemoveDeferredWatchScanState(
                snapshotDbFullPath,
                requestScopeStamp,
                checkFolder,
                sub
            );
            if (
                cursorToPersistUtc.HasValue
                && (
                    string.Equals(
                        strategy,
                        FileIndexStrategies.Everything,
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? FileIndexIncrementalSyncPolicy.ShouldAdvanceCursor(
                            cursorToPersistUtc,
                            changedSinceUtc
                        )
                        : deferredState.DeferredCursorUtc.HasValue
                )
            )
            {
                SaveEverythingLastSyncUtc(
                    snapshotDbFullPath,
                    requestScopeStamp,
                    checkFolder,
                    sub,
                    cursorToPersistUtc.Value
                );
            }

            return new FolderScanWithStrategyResult(
                new FolderScanResult(scannedCount, immediatePaths),
                strategy,
                reason,
                hasIncrementalCursor:
                    string.Equals(
                        strategy,
                        FileIndexStrategies.Everything,
                        StringComparison.OrdinalIgnoreCase
                )
                    && changedSinceUtc.HasValue
            );
        }

        // watch 1フォルダ分の背景走査をひとまとめにし、Watcher 側では結果だけを見る形にする。
        private async Task<(
            FolderScanWithStrategyResult ScanStrategyResult,
            FolderScanResult ScanResult,
            long ElapsedMs
        )> RunWatchFolderBackgroundScanAsync(
            CheckMode mode,
            string snapshotDbFullPath,
            long snapshotWatchScanScopeStamp,
            string checkFolder,
            bool sub,
            string checkExt,
            bool restrictWatchWorkToVisibleMovies,
            ISet<string> visibleMoviePaths
        )
        {
            Stopwatch scanBackgroundStopwatch = Stopwatch.StartNew();
            FolderScanWithStrategyResult scanStrategyResult = await Task.Run(() =>
                ScanFolderWithStrategyInBackground(
                    mode,
                    snapshotDbFullPath,
                    snapshotWatchScanScopeStamp,
                    checkFolder,
                    sub,
                    checkExt,
                    restrictWatchWorkToVisibleMovies,
                    visibleMoviePaths
                )
            );
            scanBackgroundStopwatch.Stop();

            return (
                scanStrategyResult,
                scanStrategyResult.ScanResult,
                scanBackgroundStopwatch.ElapsedMilliseconds
            );
        }
    }
}
