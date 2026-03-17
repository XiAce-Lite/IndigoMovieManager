using System.Windows.Controls;
using System.Windows.Input;
using IndigoMovieManager.UpperTabs.Common;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 上側タブでは PageUp / PageDown をほぼ1画面送りへ寄せて、一覧移動のテンポを上げる。
        private bool TryHandleUpperTabPageScroll(KeyEventArgs e)
        {
            if (e == null)
            {
                return false;
            }

            bool? scrollForward = e.Key switch
            {
                Key.PageDown => true,
                Key.PageUp => false,
                _ => null,
            };
            if (scrollForward == null)
            {
                return false;
            }

            ItemsControl activeItemsControl = GetActiveUpperTabItemsControl();
            if (activeItemsControl == null)
            {
                return false;
            }

            ScrollViewer scrollViewer = UpperTabViewportTracker.FindScrollViewer(activeItemsControl);
            if (!UpperTabScrollNavigator.TryScrollPage(scrollViewer, scrollForward.Value))
            {
                return false;
            }

            e.Handled = true;
            RequestUpperTabVisibleRangeRefresh(
                immediate: true,
                reason: scrollForward.Value ? "page-down" : "page-up"
            );
            return true;
        }
    }
}
