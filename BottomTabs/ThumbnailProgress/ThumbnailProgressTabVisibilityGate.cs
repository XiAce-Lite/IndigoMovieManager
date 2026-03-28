using AvalonDock.Layout;

namespace IndigoMovieManager.BottomTabs.ThumbnailProgress
{
    // AvalonDock の下部タブが「今前面で描いてよい状態か」を判定する最小ヘルパ。
    internal static class ThumbnailProgressTabVisibilityGate
    {
        public static bool IsVisibleOrSelected(LayoutAnchorable tab)
        {
            if (tab == null)
            {
                return false;
            }

            if (tab.IsHidden)
            {
                return false;
            }

            return tab.IsSelected || tab.IsActive;
        }

        public static bool ShouldReactToProperty(string propertyName)
        {
            return propertyName is "IsSelected" or "IsActive" or "IsVisible" or "IsHidden";
        }
    }
}
