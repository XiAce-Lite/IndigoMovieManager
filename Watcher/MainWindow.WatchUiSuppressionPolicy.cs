using System;
using System.Collections.Generic;

namespace IndigoMovieManager;

public partial class MainWindow
{
    // UI抑制は watch 経路だけ止め、manual / auto は通して明示操作を優先する。
    internal static bool ShouldSuppressWatchWorkByUi(
        bool isWatchSuppressedByUi,
        bool isWatchMode
    )
    {
        return isWatchSuppressedByUi && isWatchMode;
    }

    // 抑制解除後に保留があれば、catch-up を1回だけ流して追いつく。
    internal static bool ShouldQueueWatchCatchUpAfterUiSuppression(
        bool isStillSuppressed,
        bool hasDeferredWatchWork
    )
    {
        return !isStillSuppressed && hasDeferredWatchWork;
    }

    // manual reload は直前に全域 manual scan を済ませているため、解除直後の catch-up を重ねない。
    internal static bool ShouldSkipWatchCatchUpAfterUiSuppression(string reason)
    {
        return string.Equals(reason, "manual-reload", StringComparison.OrdinalIgnoreCase);
    }

    // Header再読込の遅延scanだけを見分け、救済抑止の適用範囲を最小に保つ。
    internal static bool IsManualReloadDeferredScanTrigger(string trigger)
    {
        return string.Equals(
            trigger,
            "Header.ReloadButton:deferred",
            StringComparison.OrdinalIgnoreCase
        );
    }

    // suppression へ入る直前までに拾った仕事は、catch-up で1回だけ再開できる形へまとめる。
    internal static List<string> MergeWatchDeferredPathsForUiSuppression(
        IReadOnlyList<string> remainingScanPaths,
        IReadOnlyList<string> pendingInsertPaths,
        IReadOnlyList<string> pendingEnqueuePaths
    )
    {
        return MergeWatchDeferredPathsForUiSuppression(
            [],
            remainingScanPaths,
            pendingInsertPaths,
            pendingEnqueuePaths
        );
    }

    // current item は残件より先頭へ戻し、catch-up 1回で拾い直せる順を守る。
    internal static List<string> MergeWatchDeferredPathsForUiSuppression(
        IReadOnlyList<string> currentScanPaths,
        IReadOnlyList<string> remainingScanPaths,
        IReadOnlyList<string> pendingInsertPaths,
        IReadOnlyList<string> pendingEnqueuePaths
    )
    {
        List<string> mergedPaths = [];
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);

        // watch 経路は '\' と '/' が混ざることがあるので、catch-up では同じファイルとしてまとめる。
        static string NormalizeWatchDeferredPathForUiSuppression(string moviePath)
        {
            return moviePath?.Replace('/', '\\') ?? "";
        }

        void AppendPaths(IEnumerable<string> sourcePaths)
        {
            if (sourcePaths == null)
            {
                return;
            }

            foreach (string moviePath in sourcePaths)
            {
                string normalizedPath = NormalizeWatchDeferredPathForUiSuppression(moviePath);
                if (!string.IsNullOrWhiteSpace(normalizedPath) && seenPaths.Add(normalizedPath))
                {
                    mergedPaths.Add(normalizedPath);
                }
            }
        }

        AppendPaths(currentScanPaths);
        AppendPaths(remainingScanPaths);
        AppendPaths(pendingInsertPaths);
        AppendPaths(pendingEnqueuePaths);
        return mergedPaths;
    }
}
