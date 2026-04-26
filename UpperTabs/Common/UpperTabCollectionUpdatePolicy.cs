using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブの一覧更新方式を決める最小ポリシー。
    /// VirtualizingWrapPanel は差分通知で不安定なため、当面は List(DataGrid) だけ Diff/Move を許可する。
    /// </summary>
    public static class UpperTabCollectionUpdatePolicy
    {
        private const int ListTabIndex = 3;
        private const int PlayerTabIndex = 7;

        public static FilteredMovieRecsUpdateMode ResolveUpdateMode(
            int? tabIndex,
            bool isSortOnly
        )
        {
            // 縦並びの List / Player は差分通知でも安定するので、全件作り直しを避ける。
            if (tabIndex == ListTabIndex || tabIndex == PlayerTabIndex)
            {
                return isSortOnly
                    ? FilteredMovieRecsUpdateMode.Move
                    : FilteredMovieRecsUpdateMode.Diff;
            }

            return FilteredMovieRecsUpdateMode.Reset;
        }

        // 縦並びの List / Player は Diff/Move 通知を素直に扱えるので、
        // ここでは一覧全体 Refresh を省いて差分反映を主経路にする。
        public static bool ShouldRefreshAfterCollectionApply(
            int? tabIndex,
            FilteredMovieRecsUpdateMode updateMode
        )
        {
            bool isStableLinearTab =
                tabIndex == ListTabIndex || tabIndex == PlayerTabIndex;
            return !(isStableLinearTab && updateMode != FilteredMovieRecsUpdateMode.Reset);
        }
    }
}
