using System;
using System.Collections.Generic;

namespace IndigoMovieManager;

public partial class MainWindow
{
    // watch 差分が多すぎる時は、その場で全部処理せず「今回分」と「次回送り」に分ける。
    internal static (List<string> ImmediatePaths, List<string> DeferredPaths) SplitWatchScanMoviePaths(
        IReadOnlyList<string> moviePaths,
        int limit
    )
    {
        return SplitWatchScanMoviePaths(moviePaths, limit, false, null);
    }

    // visible-only 中は表示中動画を先に今回分へ残し、非表示は deferred 側へ退避する。
    internal static (List<string> ImmediatePaths, List<string> DeferredPaths) SplitWatchScanMoviePaths(
        IReadOnlyList<string> moviePaths,
        int limit,
        bool prioritizeVisibleMovies,
        ISet<string> visibleMoviePaths
    )
    {
        List<string> immediatePaths = [];
        List<string> deferredPaths = [];
        if (moviePaths == null || moviePaths.Count < 1)
        {
            return (immediatePaths, deferredPaths);
        }

        int safeLimit = Math.Max(1, limit);
        if (!prioritizeVisibleMovies || visibleMoviePaths == null || visibleMoviePaths.Count < 1)
        {
            for (int i = 0; i < moviePaths.Count; i++)
            {
                string moviePath = moviePaths[i];
                if (string.IsNullOrWhiteSpace(moviePath))
                {
                    continue;
                }

                if (immediatePaths.Count < safeLimit)
                {
                    immediatePaths.Add(moviePath);
                }
                else
                {
                    deferredPaths.Add(moviePath);
                }
            }

            return (immediatePaths, deferredPaths);
        }

        List<string> deferredVisiblePaths = [];
        List<string> deferredHiddenPaths = [];
        for (int i = 0; i < moviePaths.Count; i++)
        {
            string moviePath = moviePaths[i];
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                continue;
            }

            if (!visibleMoviePaths.Contains(moviePath))
            {
                deferredHiddenPaths.Add(moviePath);
                continue;
            }

            if (immediatePaths.Count < safeLimit)
            {
                immediatePaths.Add(moviePath);
            }
            else
            {
                deferredVisiblePaths.Add(moviePath);
            }
        }

        deferredPaths.AddRange(deferredVisiblePaths);
        deferredPaths.AddRange(deferredHiddenPaths);
        return (immediatePaths, deferredPaths);
    }

    // watch またぎでは、新しく見えた候補を古い backlog の後ろへ固定しない順で再マージする。
    internal static (List<string> ImmediatePaths, List<string> DeferredPaths) MergeDeferredAndCollectedWatchScanMoviePaths(
        IReadOnlyList<string> deferredPaths,
        IReadOnlyList<string> collectedPaths,
        int limit,
        bool prioritizeVisibleMovies,
        ISet<string> visibleMoviePaths
    )
    {
        List<string> mergedPaths = [];
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);

        void AppendPaths(IEnumerable<string> sourcePaths)
        {
            if (sourcePaths == null)
            {
                return;
            }

            foreach (string moviePath in sourcePaths)
            {
                if (!string.IsNullOrWhiteSpace(moviePath) && seenPaths.Add(moviePath))
                {
                    mergedPaths.Add(moviePath);
                }
            }
        }

        if (prioritizeVisibleMovies)
        {
            AppendPaths(collectedPaths);
            AppendPaths(deferredPaths);
            return SplitWatchScanMoviePaths(
                mergedPaths,
                limit,
                prioritizeVisibleMovies,
                visibleMoviePaths
            );
        }

        AppendPaths(deferredPaths);
        AppendPaths(collectedPaths);
        return SplitWatchScanMoviePaths(mergedPaths, limit);
    }
}
