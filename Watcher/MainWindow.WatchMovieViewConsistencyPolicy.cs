using System;
using System.Collections.Generic;

namespace IndigoMovieManager;

public partial class MainWindow
{
    // 最後に full reload へ戻る周回では、途中の view repair を積んでも無駄になりやすい。
    internal static bool ResolveAllowViewConsistencyRepair(
        bool allowViewConsistencyRepair,
        bool useIncrementalUiMode
    )
    {
        return allowViewConsistencyRepair && useIncrementalUiMode;
    }

    // 画面側の保持パスを大小文字差異なしで参照できるよう、比較用セットへ正規化する。
    internal static HashSet<string> BuildMoviePathLookup(IEnumerable<string> moviePaths)
    {
        HashSet<string> lookup = new(StringComparer.OrdinalIgnoreCase);
        if (moviePaths == null)
        {
            return lookup;
        }

        foreach (string moviePath in moviePaths)
        {
            if (!string.IsNullOrWhiteSpace(moviePath))
            {
                lookup.Add(moviePath);
            }
        }

        return lookup;
    }

    // 実ファイルとDBは一致していても、画面ソースから抜けている既存動画は表示整合の補正対象にする。
    internal static bool ShouldRepairExistingMovieView(
        ISet<string> existingViewMoviePaths,
        string movieFullPath
    )
    {
        if (string.IsNullOrWhiteSpace(movieFullPath))
        {
            return false;
        }

        return existingViewMoviePaths == null || !existingViewMoviePaths.Contains(movieFullPath);
    }

    // 検索未使用時は、表示側の一覧から抜けた既存動画も再描画対象として扱う。
    internal static bool ShouldRefreshDisplayedMovieView(
        string searchKeyword,
        ISet<string> displayedMoviePaths,
        string movieFullPath
    )
    {
        if (!string.IsNullOrWhiteSpace(searchKeyword) || string.IsNullOrWhiteSpace(movieFullPath))
        {
            return false;
        }

        return displayedMoviePaths == null || !displayedMoviePaths.Contains(movieFullPath);
    }

    // 実ファイル・DB・画面ソース・表示一覧のズレを、監視側がどう補正するかを1か所で判定する。
    internal static MovieViewConsistencyDecision EvaluateMovieViewConsistency(
        bool allowViewConsistencyRepair,
        bool existsInDb,
        ISet<string> existingViewMoviePaths,
        string searchKeyword,
        ISet<string> displayedMoviePaths,
        string movieFullPath
    )
    {
        if (
            !allowViewConsistencyRepair
            || !existsInDb
            || string.IsNullOrWhiteSpace(movieFullPath)
        )
        {
            return MovieViewConsistencyDecision.None;
        }

        bool shouldRepairView = ShouldRepairExistingMovieView(
            existingViewMoviePaths,
            movieFullPath
        );
        if (shouldRepairView)
        {
            return new MovieViewConsistencyDecision(
                ShouldRepairView: true,
                ShouldRefreshDisplayedView: false
            );
        }

        bool shouldRefreshDisplayedView = ShouldRefreshDisplayedMovieView(
            searchKeyword,
            displayedMoviePaths,
            movieFullPath
        );
        return new MovieViewConsistencyDecision(
            ShouldRepairView: false,
            ShouldRefreshDisplayedView: shouldRefreshDisplayedView
        );
    }
}
