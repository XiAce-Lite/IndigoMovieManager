using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Threading;
using IndigoMovieManager.UpperTabs.Common;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int UpperTabViewportOverscanItemCount = 24;
        private const int UpperTabViewportThrottleMs = 33;

        private readonly HashSet<ScrollViewer> _upperTabViewportAttachedScrollViewers = [];
        private DispatcherTimer _upperTabViewportRefreshTimer;
        private UpperTabVisibleRange _activeUpperTabVisibleRange = UpperTabVisibleRange.Empty;
        private IReadOnlyList<string> _preferredVisibleMoviePathKeysSnapshot = Array.Empty<string>();

        // 上側タブの visible 範囲追跡を初期化する。
        private void InitializeUpperTabViewportSupport()
        {
            if (_upperTabViewportRefreshTimer != null)
            {
                return;
            }

            _upperTabViewportRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(UpperTabViewportThrottleMs),
            };
            _upperTabViewportRefreshTimer.Tick += UpperTabViewportRefreshTimer_Tick;

            Loaded += (_, _) =>
            {
                EnsureUpperTabViewportHandlersAttached();
                RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "loaded");
            };
        }

        // スクロールやタブ切替の後で、active tab の visible 範囲を取り直す。
        private void RequestUpperTabVisibleRangeRefresh(bool immediate = false, string reason = "")
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(() => RequestUpperTabVisibleRangeRefresh(immediate, reason));
                return;
            }

            EnsureUpperTabViewportHandlersAttached();
            if (immediate)
            {
                _upperTabViewportRefreshTimer.Stop();
                ApplyUpperTabVisibleRangeRefresh(reason);
                return;
            }

            _upperTabViewportRefreshTimer.Stop();
            _upperTabViewportRefreshTimer.Start();
        }

        private void ClearUpperTabVisibleRange()
        {
            _activeUpperTabVisibleRange = UpperTabVisibleRange.Empty;
            _preferredVisibleMoviePathKeysSnapshot = Array.Empty<string>();
        }

        // 背景スレッドからは UI スレッドで作った snapshot を返し、クロススレッド参照を避ける。
        private IReadOnlyList<string> ResolvePreferredVisibleMoviePathKeys()
        {
            if (!Dispatcher.CheckAccess())
            {
                return _preferredVisibleMoviePathKeysSnapshot;
            }

            return BuildPreferredVisibleMoviePathKeysSnapshot(_activeUpperTabVisibleRange);
        }

        // active tab の visible -> near-visible 順で、優先リース用の MoviePathKey snapshot を組む。
        private IReadOnlyList<string> BuildPreferredVisibleMoviePathKeysSnapshot(
            UpperTabVisibleRange visibleRange
        )
        {
            if (
                Tabs?.SelectedIndex is < 0 or > 4
                || !visibleRange.HasVisibleItems
                || MainVM?.FilteredMovieRecs == null
            )
            {
                return Array.Empty<string>();
            }

            List<string> result = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            int totalCount = MainVM.FilteredMovieRecs.Count;
            if (totalCount < 1)
            {
                return result;
            }

            AppendMoviePathKeysInRange(
                result,
                seen,
                visibleRange.FirstVisibleIndex,
                visibleRange.LastVisibleIndex,
                totalCount
            );
            AppendMoviePathKeysInRange(
                result,
                seen,
                visibleRange.FirstNearVisibleIndex,
                visibleRange.FirstVisibleIndex - 1,
                totalCount
            );
            AppendMoviePathKeysInRange(
                result,
                seen,
                visibleRange.LastVisibleIndex + 1,
                visibleRange.LastNearVisibleIndex,
                totalCount
            );
            return result;
        }

        private void UpperTabViewportRefreshTimer_Tick(object sender, EventArgs e)
        {
            _upperTabViewportRefreshTimer.Stop();
            ApplyUpperTabVisibleRangeRefresh("throttled");
        }

        private void EnsureUpperTabViewportHandlersAttached()
        {
            AttachUpperTabScrollViewer(SmallList);
            AttachUpperTabScrollViewer(BigList);
            AttachUpperTabScrollViewer(GridList);
            AttachUpperTabScrollViewer(ListDataGrid);
            AttachUpperTabScrollViewer(BigList10);
        }

        private void AttachUpperTabScrollViewer(ItemsControl itemsControl)
        {
            if (itemsControl == null)
            {
                return;
            }

            ScrollViewer scrollViewer = UpperTabViewportTracker.FindScrollViewer(itemsControl);
            if (scrollViewer == null || !_upperTabViewportAttachedScrollViewers.Add(scrollViewer))
            {
                return;
            }

            scrollViewer.ScrollChanged += UpperTabScrollViewer_ScrollChanged;
        }

        private void UpperTabScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Tabs?.SelectedIndex is < 0 or > 4)
            {
                return;
            }

            RequestUpperTabVisibleRangeRefresh(reason: "scroll");
        }

        private void ApplyUpperTabVisibleRangeRefresh(string reason)
        {
            ItemsControl activeItemsControl = GetActiveUpperTabItemsControl();
            if (activeItemsControl == null)
            {
                ClearUpperTabVisibleRange();
                return;
            }

            ScrollViewer scrollViewer = UpperTabViewportTracker.FindScrollViewer(activeItemsControl);
            if (scrollViewer == null)
            {
                ClearUpperTabVisibleRange();
                return;
            }

            UpperTabVisibleRange nextRange = UpperTabViewportTracker.GetVisibleRange(
                activeItemsControl,
                scrollViewer,
                UpperTabViewportOverscanItemCount
            );
            IReadOnlyList<string> nextPreferredMoviePathKeys = BuildPreferredVisibleMoviePathKeysSnapshot(
                nextRange
            );
            bool rangeChanged = !nextRange.Equals(_activeUpperTabVisibleRange);
            bool preferredMoviePathKeysChanged = !AreMoviePathKeyListsEqual(
                _preferredVisibleMoviePathKeysSnapshot,
                nextPreferredMoviePathKeys
            );
            _activeUpperTabVisibleRange = nextRange;
            _preferredVisibleMoviePathKeysSnapshot = nextPreferredMoviePathKeys;

            if (!rangeChanged && !preferredMoviePathKeysChanged)
            {
                return;
            }

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"upper tab viewport: tab={Tabs.SelectedIndex} reason={reason} visible={nextRange.FirstVisibleIndex}-{nextRange.LastVisibleIndex} near={nextRange.FirstNearVisibleIndex}-{nextRange.LastNearVisibleIndex}"
            );
        }

        private ItemsControl GetActiveUpperTabItemsControl()
        {
            return Tabs?.SelectedIndex switch
            {
                0 => SmallList,
                1 => BigList,
                2 => GridList,
                3 => ListDataGrid,
                4 => BigList10,
                _ => null,
            };
        }

        private void AppendMoviePathKeysInRange(
            List<string> result,
            HashSet<string> seen,
            int startIndex,
            int endIndex,
            int totalCount
        )
        {
            if (result == null || seen == null || totalCount < 1)
            {
                return;
            }

            int safeStartIndex = Math.Max(0, startIndex);
            int safeEndIndex = Math.Min(totalCount - 1, endIndex);
            if (safeEndIndex < safeStartIndex)
            {
                return;
            }

            for (int index = safeStartIndex; index <= safeEndIndex; index++)
            {
                MovieRecords movie = MainVM.FilteredMovieRecs[index];
                string moviePathKey = QueueDbPathResolver.CreateMoviePathKey(movie?.Movie_Path ?? "");
                if (string.IsNullOrWhiteSpace(moviePathKey) || !seen.Add(moviePathKey))
                {
                    continue;
                }

                result.Add(moviePathKey);
            }
        }

        private static bool AreMoviePathKeyListsEqual(
            IReadOnlyList<string> left,
            IReadOnlyList<string> right
        )
        {
            int leftCount = left?.Count ?? 0;
            int rightCount = right?.Count ?? 0;
            if (leftCount != rightCount)
            {
                return false;
            }

            for (int index = 0; index < leftCount; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
