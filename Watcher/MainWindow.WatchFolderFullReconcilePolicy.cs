using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager;

public partial class MainWindow
{
    // Watch差分0件が続く時でも、低頻度で実フォルダとDBを再突合する。
    private static readonly TimeSpan WatchFolderFullReconcileMinInterval =
        TimeSpan.FromSeconds(60);
    // DB+監視フォルダ単位で、低頻度の全量再突合を直近いつ実行したかを記録する。
    private readonly object _watchFolderFullReconcileSync = new();
    private readonly Dictionary<string, DateTime> _watchFolderFullReconcileLastRunUtcByScope =
        new(StringComparer.OrdinalIgnoreCase);

    // WatchのEverything差分で0件だった時だけ、低頻度の全量再突合を許可する。
    internal static bool ShouldRunWatchFolderFullReconcile(
        bool isWatchMode,
        string strategy,
        int newMovieCount
    )
    {
        return isWatchMode
            && newMovieCount < 1
            && string.Equals(
                strategy,
                FileIndexStrategies.Everything,
                StringComparison.OrdinalIgnoreCase
            );
    }

    // visible-only や user-priority の都合を先に畳み、Watcher 側では次の動きだけを書く。
    internal static (bool ShouldStart, bool ShouldDeferByUserPriority) ResolveWatchFolderFullReconcileEntryPlan(
        bool restrictWatchWorkToVisibleMovies,
        bool isWatchMode,
        string strategy,
        int newMovieCount,
        bool shouldDeferByUserPriority
    )
    {
        bool shouldStart =
            !restrictWatchWorkToVisibleMovies
            && ShouldRunWatchFolderFullReconcile(isWatchMode, strategy, newMovieCount);
        return (shouldStart, shouldStart && shouldDeferByUserPriority);
    }

    // 現在の run 条件から full reconcile の入口 plan を組み立て、Watcher 側の mode 判定直書きを減らす。
    internal static (bool ShouldStart, bool ShouldDeferByUserPriority)
        ResolveWatchFolderFullReconcilePlanForCurrentRun(
            bool restrictWatchWorkToVisibleMovies,
            object mode,
            string strategy,
            int newMovieCount,
            bool shouldDeferByUserPriority
        )
    {
        return ResolveWatchFolderFullReconcileEntryPlan(
            restrictWatchWorkToVisibleMovies,
            Equals(mode, CheckMode.Watch),
            strategy,
            newMovieCount,
            shouldDeferByUserPriority
        );
    }

    // 現在の user-priority 状態まで含めた入口 plan をここで組み立て、Watcher 側の引数を減らす。
    private (bool ShouldStart, bool ShouldDeferByUserPriority)
        ResolveWatchFolderFullReconcilePlanForCurrentRun(
            bool restrictWatchWorkToVisibleMovies,
            CheckMode mode,
            string strategy,
            int newMovieCount
        )
    {
        return ResolveWatchFolderFullReconcilePlanForCurrentRun(
            restrictWatchWorkToVisibleMovies,
            mode,
            strategy,
            newMovieCount,
            ShouldDeferCurrentBackgroundWork(mode)
        );
    }

    // strategy detail 解決から full reconcile 適用までを1入口へ寄せる。
    private async Task<(
        FolderScanWithStrategyResult ScanStrategyResult,
        FolderScanResult ScanResult,
        string StrategyDetailCode,
        string StrategyDetailMessage,
        string StrategyDetailCategory,
        string StrategyDetailAxis
    )> ResolveStrategyDetailAndApplyWatchFolderFullReconcileAsync(
        bool restrictWatchWorkToVisibleMovies,
        CheckMode mode,
        FolderScanWithStrategyResult scanStrategyResult,
        FolderScanResult scanResult,
        string checkFolder,
        string snapshotDbFullPath,
        bool sub,
        long snapshotWatchScanScopeStamp,
        string checkExt
    )
    {
        (
            string strategyDetailCode,
            string strategyDetailMessage,
            string strategyDetailCategory,
            string strategyDetailAxis
        ) = ResolveAndWriteWatchScanStrategyDetail(
            mode,
            scanStrategyResult,
            scanResult,
            checkFolder
        );

        return await ResolveAndApplyWatchFolderFullReconcileAsync(
            restrictWatchWorkToVisibleMovies,
            mode,
            scanStrategyResult,
            scanResult,
            strategyDetailCode,
            strategyDetailMessage,
            strategyDetailCategory,
            strategyDetailAxis,
            checkFolder,
            snapshotDbFullPath,
            sub,
            snapshotWatchScanScopeStamp,
            checkExt
        );
    }

    // 入口条件の判定から必要時の full reconcile 適用までを1入口へ寄せる。
    private async Task<(
        FolderScanWithStrategyResult ScanStrategyResult,
        FolderScanResult ScanResult,
        string StrategyDetailCode,
        string StrategyDetailMessage,
        string StrategyDetailCategory,
        string StrategyDetailAxis
    )> ResolveAndApplyWatchFolderFullReconcileAsync(
        bool restrictWatchWorkToVisibleMovies,
        CheckMode mode,
        FolderScanWithStrategyResult scanStrategyResult,
        FolderScanResult scanResult,
        string strategyDetailCode,
        string strategyDetailMessage,
        string strategyDetailCategory,
        string strategyDetailAxis,
        string checkFolder,
        string snapshotDbFullPath,
        bool sub,
        long snapshotWatchScanScopeStamp,
        string checkExt
    )
    {
        (
            bool shouldStartFullReconcile,
            bool shouldDeferFullReconcileByUserPriority
        ) = ResolveWatchFolderFullReconcilePlanForCurrentRun(
            restrictWatchWorkToVisibleMovies,
            mode,
            scanStrategyResult.Strategy,
            scanResult.NewMoviePaths.Count
        );

        return await ApplyWatchFolderFullReconcileIfNeededAsync(
            shouldStartFullReconcile,
            shouldDeferFullReconcileByUserPriority,
            scanStrategyResult,
            scanResult,
            strategyDetailCode,
            strategyDetailMessage,
            strategyDetailCategory,
            strategyDetailAxis,
            checkFolder,
            snapshotDbFullPath,
            sub,
            snapshotWatchScanScopeStamp,
            checkExt
        );
    }

    // 入口の分岐（開始不可/優先作業defer/間引き）をここで畳み、Watcher 側のネストを減らす。
    private bool TryBeginWatchFolderFullReconcile(
        bool shouldStartFullReconcile,
        bool shouldDeferFullReconcileByUserPriority,
        string checkFolder,
        string snapshotDbFullPath,
        bool sub,
        DateTime nowUtc
    )
    {
        if (!shouldStartFullReconcile)
        {
            return false;
        }

        if (shouldDeferFullReconcileByUserPriority)
        {
            MarkWatchWorkDeferredWhileSuppressed($"watch-zero-diff-reconcile:{checkFolder}");
            DebugRuntimeLog.Write(
                "watch-check",
                $"scan reconcile deferred by user priority: folder='{checkFolder}' reason=search-priority"
            );
            return false;
        }

        string reconcileScopeKey = BuildWatchFolderFullReconcileScopeKey(
            snapshotDbFullPath,
            checkFolder,
            sub
        );
        if (TryReserveWatchFolderFullReconcileWindow(reconcileScopeKey, nowUtc, out TimeSpan nextIn))
        {
            return true;
        }

        DebugRuntimeLog.Write(
            "watch-check",
            $"scan reconcile throttled: folder='{checkFolder}' next_in_sec={Math.Ceiling(nextIn.TotalSeconds)}"
        );
        return false;
    }

    // full reconcile の実行塊（開始ログ→再走査→結果差し替え→終了ログ）をここへ寄せ、
    // Watcher 側は「必要なら適用する」1 呼び出しだけにする。
    private async Task<(
        FolderScanWithStrategyResult ScanStrategyResult,
        FolderScanResult ScanResult,
        string StrategyDetailCode,
        string StrategyDetailMessage,
        string StrategyDetailCategory,
        string StrategyDetailAxis
    )> ApplyWatchFolderFullReconcileIfNeededAsync(
        bool shouldStartFullReconcile,
        bool shouldDeferFullReconcileByUserPriority,
        FolderScanWithStrategyResult scanStrategyResult,
        FolderScanResult scanResult,
        string strategyDetailCode,
        string strategyDetailMessage,
        string strategyDetailCategory,
        string strategyDetailAxis,
        string checkFolder,
        string snapshotDbFullPath,
        bool sub,
        long snapshotWatchScanScopeStamp,
        string checkExt
    )
    {
        if (
            !TryBeginWatchFolderFullReconcile(
                shouldStartFullReconcile,
                shouldDeferFullReconcileByUserPriority,
                checkFolder,
                snapshotDbFullPath,
                sub,
                DateTime.UtcNow
            )
        )
        {
            return (
                scanStrategyResult,
                scanResult,
                strategyDetailCode,
                strategyDetailMessage,
                strategyDetailCategory,
                strategyDetailAxis
            );
        }

        DebugRuntimeLog.Write(
            "watch-check",
            $"scan reconcile start: folder='{checkFolder}' reason=watch_zero_diff"
        );

        Stopwatch reconcileStopwatch = Stopwatch.StartNew();
        FolderScanWithStrategyResult reconcileResult = await Task.Run(() =>
            ScanFolderWithStrategyInBackground(
                CheckMode.Manual,
                snapshotDbFullPath,
                snapshotWatchScanScopeStamp,
                checkFolder,
                sub,
                checkExt,
                false,
                null
            )
        );
        reconcileStopwatch.Stop();

        scanStrategyResult = reconcileResult;
        scanResult = reconcileResult.ScanResult;
        (
            strategyDetailCode,
            strategyDetailMessage,
            strategyDetailCategory,
            strategyDetailAxis
        ) = ResolveWatchScanStrategyDetail(scanStrategyResult.Detail);
        DebugRuntimeLog.Write(
            "watch-check",
            $"scan reconcile end: category={strategyDetailAxis} folder='{checkFolder}' strategy={scanStrategyResult.Strategy} detail_category={strategyDetailCategory} detail_code={strategyDetailCode} detail_message={strategyDetailMessage} scanned={scanResult.ScannedCount} new={scanResult.NewMoviePaths.Count} elapsed_ms={reconcileStopwatch.ElapsedMilliseconds}"
        );

        return (
            scanStrategyResult,
            scanResult,
            strategyDetailCode,
            strategyDetailMessage,
            strategyDetailCategory,
            strategyDetailAxis
        );
    }

    // 差分0件が続いても、同じ監視フォルダへは一定間隔ごとにだけ全量再突合する。
    private bool TryReserveWatchFolderFullReconcileWindow(
        string scopeKey,
        DateTime nowUtc,
        out TimeSpan nextIn
    )
    {
        lock (_watchFolderFullReconcileSync)
        {
            if (
                _watchFolderFullReconcileLastRunUtcByScope.TryGetValue(
                    scopeKey,
                    out DateTime lastRunUtc
                )
            )
            {
                TimeSpan elapsed = nowUtc - lastRunUtc;
                if (elapsed < WatchFolderFullReconcileMinInterval)
                {
                    nextIn = WatchFolderFullReconcileMinInterval - elapsed;
                    return false;
                }
            }

            _watchFolderFullReconcileLastRunUtcByScope[scopeKey] = nowUtc;
            nextIn = TimeSpan.Zero;

            if (_watchFolderFullReconcileLastRunUtcByScope.Count > 128)
            {
                DateTime cutoff = nowUtc - TimeSpan.FromHours(24);
                List<string> staleKeys = _watchFolderFullReconcileLastRunUtcByScope
                    .Where(x => x.Value < cutoff)
                    .Select(x => x.Key)
                    .ToList();
                foreach (string staleKey in staleKeys)
                {
                    _watchFolderFullReconcileLastRunUtcByScope.Remove(staleKey);
                }
            }

            return true;
        }
    }

    // DB切替や監視設定差分で混線しないよう、DB+フォルダ+sub単位で再突合スコープを固定する。
    internal static string BuildWatchFolderFullReconcileScopeKey(
        string dbFullPath,
        string watchFolder,
        bool sub
    )
    {
        string normalizedDb = NormalizeWatchFolderFullReconcileScopePath(dbFullPath);
        string normalizedFolder = NormalizeWatchFolderFullReconcileScopePath(watchFolder);

        return
            $"{normalizedDb.Trim().ToLowerInvariant()}|{normalizedFolder.Trim().ToLowerInvariant()}|sub={(sub ? 1 : 0)}";
    }

    // scope key 用のパス正規化だけを小さく分離し、同型 try/catch を減らす。
    internal static string NormalizeWatchFolderFullReconcileScopePath(string path)
    {
        string normalizedPath = path ?? "";

        try
        {
            if (
                !string.IsNullOrWhiteSpace(normalizedPath)
                && Path.IsPathFullyQualified(normalizedPath)
            )
            {
                normalizedPath = Path.GetFullPath(normalizedPath);
            }
        }
        catch
        {
            // 正規化に失敗しても、元文字列をキーとして扱って処理継続する。
        }

        return normalizedPath;
    }
}
