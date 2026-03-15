using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 実際に見えている item container から visible 範囲を拾う。
    /// </summary>
    public static class UpperTabViewportTracker
    {
        public static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            return EnumerateVisualChildren<ScrollViewer>(root).FirstOrDefault();
        }

        public static UpperTabVisibleRange GetVisibleRange(
            ItemsControl itemsControl,
            ScrollViewer scrollViewer,
            int overscanItemCount
        )
        {
            if (itemsControl == null || scrollViewer == null || itemsControl.Items.Count < 1)
            {
                return UpperTabVisibleRange.Empty;
            }

            List<int> visibleIndices = [];
            foreach (FrameworkElement container in EnumerateRealizedContainers(itemsControl))
            {
                int index = itemsControl.ItemContainerGenerator.IndexFromContainer(container);
                if (index < 0)
                {
                    continue;
                }

                if (!IntersectsViewport(container, scrollViewer))
                {
                    continue;
                }

                visibleIndices.Add(index);
            }

            if (visibleIndices.Count < 1)
            {
                return UpperTabVisibleRange.Empty;
            }

            visibleIndices.Sort();
            return UpperTabVisibleRange.Create(
                visibleIndices[0],
                visibleIndices[^1],
                itemsControl.Items.Count,
                overscanItemCount
            );
        }

        private static IEnumerable<FrameworkElement> EnumerateRealizedContainers(ItemsControl itemsControl)
        {
            foreach (FrameworkElement element in EnumerateVisualChildren<FrameworkElement>(itemsControl))
            {
                if (itemsControl.ItemContainerGenerator.IndexFromContainer(element) < 0)
                {
                    continue;
                }

                yield return element;
            }
        }

        private static bool IntersectsViewport(FrameworkElement element, ScrollViewer scrollViewer)
        {
            if (element == null || scrollViewer == null || element.RenderSize.IsEmpty)
            {
                return false;
            }

            double viewportWidth = scrollViewer.ViewportWidth > 0
                ? scrollViewer.ViewportWidth
                : scrollViewer.ActualWidth;
            double viewportHeight = scrollViewer.ViewportHeight > 0
                ? scrollViewer.ViewportHeight
                : scrollViewer.ActualHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                return false;
            }

            try
            {
                Rect bounds = element
                    .TransformToAncestor(scrollViewer)
                    .TransformBounds(new Rect(new Point(0, 0), element.RenderSize));

                return bounds.Bottom >= 0
                    && bounds.Top <= viewportHeight
                    && bounds.Right >= 0
                    && bounds.Left <= viewportWidth;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static IEnumerable<T> EnumerateVisualChildren<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null)
            {
                yield break;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                {
                    yield return match;
                }

                foreach (T descendant in EnumerateVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
