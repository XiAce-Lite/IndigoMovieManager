using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブの一覧更新方式を決める最小ポリシー。
    /// VirtualizingWrapPanel は差分通知で不安定なため、当面は List(DataGrid) だけ Diff/Move を許可する。
    /// </summary>
    public static class UpperTabCollectionUpdatePolicy
    {
        public static FilteredMovieRecsUpdateMode ResolveUpdateMode(
            int? tabIndex,
            bool isSortOnly
        )
        {
            if (tabIndex == 3)
            {
                return isSortOnly
                    ? FilteredMovieRecsUpdateMode.Move
                    : FilteredMovieRecsUpdateMode.Diff;
            }

            return FilteredMovieRecsUpdateMode.Reset;
        }

        // DataGrid の List タブは Diff/Move 通知を素直に扱えるので、
        // ここだけは一覧全体 Refresh を省いて差分反映を主経路にする。
        public static bool ShouldRefreshAfterCollectionApply(
            int? tabIndex,
            FilteredMovieRecsUpdateMode updateMode
        )
        {
            return !(tabIndex == 3 && updateMode != FilteredMovieRecsUpdateMode.Reset);
        }
    }
}
