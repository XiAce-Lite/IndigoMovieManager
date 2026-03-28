using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
        private const int UpperTabFollowupScrollRefreshSuppressMs = 120;
        private const int UpperTabStartupAppendSuppressAfterPageScrollMs = 200;

        private readonly HashSet<ScrollViewer> _upperTabViewportAttachedScrollViewers = [];
        private readonly Dictionary<ItemsControl, Panel> _upperTabViewportItemsHostPanels = [];
        private readonly Dictionary<ItemsControl, ScrollViewer> _upperTabViewportScrollViewers = [];
        private DispatcherTimer _upperTabStartupAppendRetryTimer;
        private DispatcherTimer _upperTabViewportRefreshTimer;
        private UpperTabVisibleRange _activeUpperTabVisibleRange = UpperTabVisibleRange.Empty;
        private IReadOnlyList<string> _preferredVisibleMoviePathKeysSnapshot = Array.Empty<string>();
        private int _upperTabViewportSourceRevision;
        private int _preferredVisibleMoviePathKeysSourceRevision;
        private long _upperTabFollowupScrollRefreshSuppressUntilUtcTicks;
        private long _upperTabStartupAppendSuppressUntilUtcTicks;

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
            _upperTabStartupAppendRetryTimer = new DispatcherTimer();
            _upperTabStartupAppendRetryTimer.Tick += UpperTabStartupAppendRetryTimer_Tick;

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
                StopDispatcherTimerSafely(
                    _upperTabViewportRefreshTimer,
                    nameof(_upperTabViewportRefreshTimer)
                );
                ApplyUpperTabVisibleRangeRefresh(reason);
                return;
            }

            StopDispatcherTimerSafely(
                _upperTabViewportRefreshTimer,
                nameof(_upperTabViewportRefreshTimer)
            );
            TryStartDispatcherTimer(
                _upperTabViewportRefreshTimer,
                nameof(_upperTabViewportRefreshTimer)
            );
        }

        // PageUp / PageDown 直後の ScrollChanged は同じ refresh を積みやすいので少しだけ無視する。
        private void SuppressUpperTabFollowupScrollRefreshBriefly()
        {
            _upperTabFollowupScrollRefreshSuppressUntilUtcTicks =
                DateTime.UtcNow.AddMilliseconds(UpperTabFollowupScrollRefreshSuppressMs).Ticks;
        }

        // ページ送り直後だけ startup append を寝かせ、スクロール直後の重い仕事を後ろへ逃がす。
        private void SuppressStartupAppendAfterPageScrollBriefly()
        {
            _upperTabStartupAppendSuppressUntilUtcTicks =
                DateTime.UtcNow.AddMilliseconds(UpperTabStartupAppendSuppressAfterPageScrollMs).Ticks;
        }

        internal static bool ShouldSuppressUpperTabFollowupScrollRefresh(
            long nowUtcTicks,
            long suppressUntilUtcTicks
        )
        {
            return suppressUntilUtcTicks > 0 && nowUtcTicks <= suppressUntilUtcTicks;
        }

        internal static bool TryGetStartupAppendRetryDelayMs(
            long nowUtcTicks,
            long suppressUntilUtcTicks,
            out int retryDelayMs
        )
        {
            if (suppressUntilUtcTicks <= 0 || nowUtcTicks > suppressUntilUtcTicks)
            {
                retryDelayMs = 0;
                return false;
            }

            long remainingTicks = suppressUntilUtcTicks - nowUtcTicks;
            retryDelayMs = Math.Max(
                1,
                (int)Math.Ceiling(TimeSpan.FromTicks(remainingTicks).TotalMilliseconds)
            );
            return true;
        }

        private void ClearUpperTabVisibleRange()
        {
            _activeUpperTabVisibleRange = UpperTabVisibleRange.Empty;
            _preferredVisibleMoviePathKeysSnapshot = Array.Empty<string>();
            _preferredVisibleMoviePathKeysSourceRevision = _upperTabViewportSourceRevision;
            _activeUpperTabVisibleErrorMoviePathKeysSnapshot = Array.Empty<string>();
            _thumbnailVisibleErrorRescueRequestVersion++;
        }

        // 一覧ソース更新時だけ revision を進め、range 不変時の snapshot 再構築を避ける。
        private void NotifyUpperTabViewportSourceChanged()
        {
            Interlocked.Increment(ref _upperTabViewportSourceRevision);
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
            int currentTabIndex = GetCurrentUpperTabFixedIndex();
            if (currentTabIndex == ThumbnailErrorTabIndex)
            {
                return BuildPreferredRescueMoviePathKeysSnapshot(
                    GetDisplayedUpperTabRescueItems().Select(item => item?.MoviePath ?? "")
                );
            }

            if (
                currentTabIndex is < 0 or > 4
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

        // 救済タブでは表示中の行をそのまま優先キーへ落とし、通常再試行を先頭へ寄せる。
        internal static IReadOnlyList<string> BuildPreferredRescueMoviePathKeysSnapshot(
            IEnumerable<string> moviePaths
        )
        {
            if (moviePaths == null)
            {
                return Array.Empty<string>();
            }

            List<string> result = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string moviePath in moviePaths)
            {
                if (string.IsNullOrWhiteSpace(moviePath))
                {
                    continue;
                }

                string moviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath);
                if (string.IsNullOrWhiteSpace(moviePathKey) || !seen.Add(moviePathKey))
                {
                    continue;
                }

                result.Add(moviePathKey);
            }

            return result;
        }

        private void UpperTabViewportRefreshTimer_Tick(object sender, EventArgs e)
        {
            StopDispatcherTimerSafely(
                _upperTabViewportRefreshTimer,
                nameof(_upperTabViewportRefreshTimer)
            );
            ApplyUpperTabVisibleRangeRefresh("throttled");
        }

        private void UpperTabStartupAppendRetryTimer_Tick(object sender, EventArgs e)
        {
            StopDispatcherTimerSafely(
                _upperTabStartupAppendRetryTimer,
                nameof(_upperTabStartupAppendRetryTimer)
            );
            DebugRuntimeLog.Write("ui-tempo", "startup append retry fired");
            RequestUpperTabVisibleRangeRefresh(reason: "startup-append-retry");
        }

        private void ScheduleStartupAppendRetry(int retryDelayMs)
        {
            if (_upperTabStartupAppendRetryTimer == null)
            {
                return;
            }

            StopDispatcherTimerSafely(
                _upperTabStartupAppendRetryTimer,
                nameof(_upperTabStartupAppendRetryTimer)
            );
            _upperTabStartupAppendRetryTimer.Interval = TimeSpan.FromMilliseconds(
                Math.Max(1, retryDelayMs)
            );
            TryStartDispatcherTimer(
                _upperTabStartupAppendRetryTimer,
                nameof(_upperTabStartupAppendRetryTimer)
            );
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

            ScrollViewer scrollViewer = GetUpperTabViewportScrollViewer(itemsControl);
            if (scrollViewer == null || !_upperTabViewportAttachedScrollViewers.Add(scrollViewer))
            {
                return;
            }

            scrollViewer.ScrollChanged += UpperTabScrollViewer_ScrollChanged;
        }

        private ScrollViewer GetUpperTabViewportScrollViewer(ItemsControl itemsControl)
        {
            if (itemsControl == null)
            {
                return null;
            }

            if (_upperTabViewportScrollViewers.TryGetValue(itemsControl, out ScrollViewer cached))
            {
                return cached;
            }

            ScrollViewer resolved = UpperTabViewportTracker.FindScrollViewer(itemsControl);
            if (resolved != null)
            {
                _upperTabViewportScrollViewers[itemsControl] = resolved;
            }

            return resolved;
        }

        private Panel GetUpperTabItemsHostPanel(ItemsControl itemsControl)
        {
            if (itemsControl == null)
            {
                return null;
            }

            if (_upperTabViewportItemsHostPanels.TryGetValue(itemsControl, out Panel cached))
            {
                return cached;
            }

            Panel resolved = UpperTabViewportTracker.FindItemsHostPanel(itemsControl);
            if (resolved != null)
            {
                _upperTabViewportItemsHostPanels[itemsControl] = resolved;
            }

            return resolved;
        }

        private void UpperTabScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (GetCurrentUpperTabFixedIndex() is < 0 or > 4)
            {
                return;
            }

            if (
                ShouldSuppressUpperTabFollowupScrollRefresh(
                    DateTime.UtcNow.Ticks,
                    _upperTabFollowupScrollRefreshSuppressUntilUtcTicks
                )
            )
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"upper tab scroll follow-up suppressed: tab={GetCurrentUpperTabFixedIndex()}"
                );
                return;
            }

            RequestUpperTabVisibleRangeRefresh(reason: "scroll");
        }

        private void ApplyUpperTabVisibleRangeRefresh(string reason)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int currentTabIndex = GetCurrentUpperTabFixedIndex();
            ItemsControl activeItemsControl = GetActiveUpperTabItemsControl();
            if (activeItemsControl == null)
            {
                ClearUpperTabVisibleRange();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"upper tab viewport: tab={currentTabIndex} reason={reason} visible=empty elapsed_ms={stopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            ScrollViewer scrollViewer = GetUpperTabViewportScrollViewer(activeItemsControl);
            if (scrollViewer == null)
            {
                ClearUpperTabVisibleRange();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"upper tab viewport: tab={currentTabIndex} reason={reason} scrollviewer=missing elapsed_ms={stopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            Panel itemsHostPanel = GetUpperTabItemsHostPanel(activeItemsControl);
            UpperTabVisibleRange nextRange = UpperTabViewportTracker.GetVisibleRange(
                activeItemsControl,
                scrollViewer,
                itemsHostPanel,
                UpperTabViewportOverscanItemCount
            );
            bool rangeChanged = !nextRange.Equals(_activeUpperTabVisibleRange);
            bool sourceChanged =
                _upperTabViewportSourceRevision != _preferredVisibleMoviePathKeysSourceRevision;
            IReadOnlyList<string> nextPreferredMoviePathKeys = _preferredVisibleMoviePathKeysSnapshot;
            bool preferredMoviePathKeysChanged = false;
            if (rangeChanged || sourceChanged)
            {
                nextPreferredMoviePathKeys = BuildPreferredVisibleMoviePathKeysSnapshot(nextRange);
                preferredMoviePathKeysChanged = !AreMoviePathKeyListsEqual(
                    _preferredVisibleMoviePathKeysSnapshot,
                    nextPreferredMoviePathKeys
                );
            }

            _activeUpperTabVisibleRange = nextRange;
            _preferredVisibleMoviePathKeysSnapshot = nextPreferredMoviePathKeys;
            _preferredVisibleMoviePathKeysSourceRevision = _upperTabViewportSourceRevision;
            TryScheduleStartupAppendForCurrentViewport($"viewport:{reason}");
            DebugRuntimeLog.Write(
                "ui-tempo",
                nextRange.HasVisibleItems
                    ? $"upper tab viewport: tab={currentTabIndex} reason={reason} visible={nextRange.FirstVisibleIndex}-{nextRange.LastVisibleIndex} near={nextRange.FirstNearVisibleIndex}-{nextRange.LastNearVisibleIndex} preferred={nextPreferredMoviePathKeys.Count} elapsed_ms={stopwatch.ElapsedMilliseconds}"
                    : $"upper tab viewport: tab={currentTabIndex} reason={reason} visible=empty preferred={nextPreferredMoviePathKeys.Count} elapsed_ms={stopwatch.ElapsedMilliseconds}"
            );

            if (!rangeChanged && !preferredMoviePathKeysChanged)
            {
                return;
            }

            if (currentTabIndex is >= 0 and <= 4)
            {
                QueueVisibleUpperTabThumbnailErrorsToRescue(currentTabIndex, nextRange);
            }
        }

        private ItemsControl GetActiveUpperTabItemsControl()
        {
            return GetItemsControlByUpperTabFixedIndex(GetCurrentUpperTabFixedIndex());
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
