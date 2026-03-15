using IndigoMovieManager.ModelViews;

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
    }
}
