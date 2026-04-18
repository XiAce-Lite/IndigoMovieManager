using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        string normalizedDb = dbFullPath ?? "";
        string normalizedFolder = watchFolder ?? "";

        try
        {
            if (!string.IsNullOrWhiteSpace(normalizedDb) && Path.IsPathFullyQualified(normalizedDb))
            {
                normalizedDb = Path.GetFullPath(normalizedDb);
            }
        }
        catch
        {
            // 正規化に失敗しても、元文字列をキーとして扱って処理継続する。
        }

        try
        {
            if (
                !string.IsNullOrWhiteSpace(normalizedFolder)
                && Path.IsPathFullyQualified(normalizedFolder)
            )
            {
                normalizedFolder = Path.GetFullPath(normalizedFolder);
            }
        }
        catch
        {
            // 正規化に失敗しても、元文字列をキーとして扱って処理継続する。
        }

        return
            $"{normalizedDb.Trim().ToLowerInvariant()}|{normalizedFolder.Trim().ToLowerInvariant()}|sub={(sub ? 1 : 0)}";
    }
}
