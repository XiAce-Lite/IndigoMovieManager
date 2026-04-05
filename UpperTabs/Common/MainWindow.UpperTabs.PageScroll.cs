using System.Diagnostics;
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

            int currentTabIndex = TryGetCurrentUpperTabFixedIndex(out int resolvedTabIndex)
                ? resolvedTabIndex
                : -1;
            Stopwatch stopwatch = Stopwatch.StartNew();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"page scroll begin: tab={currentTabIndex} direction={(scrollForward.Value ? "down" : "up")}"
            );

            ScrollViewer scrollViewer = GetUpperTabViewportScrollViewer(activeItemsControl);
            if (!UpperTabScrollNavigator.TryScrollPage(scrollViewer, scrollForward.Value))
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"page scroll skipped: tab={currentTabIndex} direction={(scrollForward.Value ? "down" : "up")}"
                );
                return false;
            }

            e.Handled = true;
            // 即時 refresh は残しつつ、直後の ScrollChanged だけ抑える。
            SuppressUpperTabFollowupScrollRefreshBriefly();
            // 起動 partial の append は少し寝かせ、ページ送り直後の仕事重なりを避ける。
            SuppressStartupAppendAfterPageScrollBriefly();
            RequestUpperTabVisibleRangeRefresh(
                immediate: true,
                reason: scrollForward.Value ? "page-down" : "page-up"
            );
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"page scroll end: tab={currentTabIndex} direction={(scrollForward.Value ? "down" : "up")} elapsed_ms={stopwatch.ElapsedMilliseconds}"
            );
            return true;
        }
    }
}
