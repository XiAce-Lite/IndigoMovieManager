using System.Windows.Controls;

namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブのページ単位スクロール量をまとめる。
    /// </summary>
    public static class UpperTabScrollNavigator
    {
        // 1回でほぼ1画面送るが、少しだけ重なりを残して現在地を見失いにくくする。
        internal const double DefaultPageScrollRatio = 0.92;

        public static double CalculateTargetOffset(
            double currentOffset,
            double viewportHeight,
            double extentHeight,
            bool scrollForward,
            double pageScrollRatio = DefaultPageScrollRatio
        )
        {
            if (viewportHeight <= 0 || extentHeight <= 0)
            {
                return Math.Max(0, currentOffset);
            }

            double maxOffset = Math.Max(0, extentHeight - viewportHeight);
            if (maxOffset <= 0)
            {
                return 0;
            }

            double safeRatio = pageScrollRatio > 0 ? pageScrollRatio : DefaultPageScrollRatio;
            double step = viewportHeight * safeRatio;
            if (step <= 0)
            {
                return Math.Clamp(currentOffset, 0, maxOffset);
            }

            double nextOffset = scrollForward
                ? currentOffset + step
                : currentOffset - step;
            return Math.Clamp(nextOffset, 0, maxOffset);
        }

        public static bool TryScrollPage(ScrollViewer scrollViewer, bool scrollForward)
        {
            if (scrollViewer == null)
            {
                return false;
            }

            double targetOffset = CalculateTargetOffset(
                scrollViewer.VerticalOffset,
                scrollViewer.ViewportHeight,
                scrollViewer.ExtentHeight,
                scrollForward
            );

            if (Math.Abs(targetOffset - scrollViewer.VerticalOffset) < 0.5)
            {
                return false;
            }

            scrollViewer.ScrollToVerticalOffset(targetOffset);
            return true;
        }
    }
}
