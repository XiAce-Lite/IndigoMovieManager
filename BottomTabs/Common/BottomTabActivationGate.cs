using AvalonDock.Layout;

namespace IndigoMovieManager.BottomTabs.Common
{
    // AvalonDock 下部タブが「今反映してよい状態か」を見る最小ヘルパ。
    internal static class BottomTabActivationGate
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

            return tab.IsSelected || tab.IsActive || tab.IsVisible;
        }

        public static bool ShouldReactToProperty(string propertyName)
        {
            return propertyName is "IsSelected" or "IsActive" or "IsVisible" or "IsHidden";
        }
    }
}
