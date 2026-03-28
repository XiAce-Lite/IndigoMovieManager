using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using IndigoMovieManager.ViewModels;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 上側タブで見えている ERROR だけを少数優先へ寄せ、通常運用の過負荷を避ける。
        private const int ThumbnailVisibleErrorAutoRescueLimit = 16;
        private const int ThumbnailVisibleErrorAutoRescueDelayMs = 250;
        private int _thumbnailVisibleErrorRescueRequestVersion;
        private IReadOnlyList<string> _activeUpperTabVisibleErrorMoviePathKeysSnapshot =
            Array.Empty<string>();

        /// <summary>
        /// 今開いてるタブの先頭アイテムにカーソルを合わせる！これが俺のスマートなエスコートだ！😎
        /// </summary>
        public void SelectFirstItem()
        {
            switch (GetCurrentUpperTabFixedIndex())
            {
                case UpperTabSmallFixedIndex:
                    SelectUpperTabByFixedIndex(UpperTabSmallFixedIndex);
                    if (SmallList.Items.Count > 0)
                    {
                        SmallList.SelectedIndex = 0;
                    }
                    break;
                case UpperTabBigFixedIndex:
                    SelectUpperTabByFixedIndex(UpperTabBigFixedIndex);
                    if (BigList.Items.Count > 0)
                    {
                        BigList.SelectedIndex = 0;
                    }
                    break;
                case UpperTabGridFixedIndex:
                    SelectUpperTabByFixedIndex(UpperTabGridFixedIndex);
                    if (GridList.Items.Count > 0)
                    {
                        GridList.SelectedIndex = 0;
                    }
                    break;
                case UpperTabListFixedIndex:
                    SelectUpperTabByFixedIndex(UpperTabListFixedIndex);
                    if (ListDataGrid.Items.Count > 0)
                    {
                        ListDataGrid.SelectedIndex = 0;
                    }
                    break;
                case UpperTabBig10FixedIndex:
                    SelectUpperTabByFixedIndex(UpperTabBig10FixedIndex);
                    if (BigList10.Items.Count > 0)
                    {
                        BigList10.SelectedIndex = 0;
                    }
                    break;
                case ThumbnailErrorTabIndex:
                    SelectUpperTabByFixedIndex(ThumbnailErrorTabIndex);
                    DataGrid rescueListDataGrid = GetUpperTabRescueDataGrid();
                    if (rescueListDataGrid?.Items.Count > 0)
                    {
                        rescueListDataGrid.SelectedIndex = 0;
                    }
                    break;
                case DuplicateVideoTabIndex:
                    SelectUpperTabByFixedIndex(DuplicateVideoTabIndex);
                    Selector duplicateGroupSelector = GetUpperTabDuplicateGroupSelector();
                    if (duplicateGroupSelector?.Items.Count > 0)
                    {
                        duplicateGroupSelector.SelectedIndex = 0;
                    }
                    break;
                default:
                    SelectUpperTabByFixedIndex(UpperTabGridFixedIndex);
                    if (GridList.Items.Count > 0)
                    {
                        GridList.SelectedIndex = 0;
                    }
                    break;
            }
        }

        // タブ切替時に不足サムネイルを検出し、必要な再作成キューを積む。
        private async void Tabs_SelectionChangedAsync(object sender, SelectionChangedEventArgs e)
        {
            if (sender as TabControl == null || e.OriginalSource is not TabControl)
            {
                return;
            }

            Stopwatch selectionStopwatch = Stopwatch.StartNew();
            // 一覧タブ切替では起動後累計や作業パネルを落とさず、再投入デバウンスだけ解く。
            ClearThumbnailQueue(ThumbnailQueueClearScope.DebounceOnly);

            int index = GetCurrentUpperTabFixedIndex();
            if (index == -1)
            {
                return;
            }

            int effectiveQueueTabIndex = index == DuplicateVideoTabIndex
                ? UpperTabGridFixedIndex
                : index;
            MainVM.DbInfo.CurrentTabIndex = effectiveQueueTabIndex;
            TryDeletePendingUpperTabJobsForUnselectedTabs(effectiveQueueTabIndex);
            RequestUpperTabVisibleRangeRefresh(reason: "tab-changed");

            if (index == ThumbnailErrorTabIndex)
            {
                SelectFirstItem();

                MovieRecords errorMovie = GetSelectedItemByTabIndex();
                int rescueCount = GetUpperTabRescueDataGrid()?.Items.Count ?? 0;
                if (errorMovie == null)
                {
                    HideExtensionDetail();
                    selectionStopwatch.Stop();
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"tab change end: tab={index} selected=none rescue_count={rescueCount} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                    );
                    return;
                }

                ShowExtensionDetail(errorMovie);
                selectionStopwatch.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change end: tab={index} selected='{errorMovie.Movie_Name}' rescue_count={rescueCount} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            if (index == DuplicateVideoTabIndex)
            {
                MovieRecords duplicateMovie = GetSelectedItemByTabIndex();
                if (duplicateMovie == null)
                {
                    HideExtensionDetail();
                    selectionStopwatch.Stop();
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"tab change end: tab={index} selected=none duplicate_groups={GetUpperTabDuplicateGroupSelector()?.Items.Count ?? 0} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                    );
                    return;
                }

                ShowExtensionDetail(duplicateMovie);
                selectionStopwatch.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change end: tab={index} selected='{duplicateMovie.Movie_Name}' duplicate_groups={GetUpperTabDuplicateGroupSelector()?.Items.Count ?? 0} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            if (MainVM.FilteredMovieRecs.Count == 0)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change skip: tab={index} reason=no_filtered_items total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
                ClearUpperTabVisibleRange();
                return;
            }

            string[] thumbProps =
            [
                nameof(MovieRecords.ThumbPathSmall),
                nameof(MovieRecords.ThumbPathBig),
                nameof(MovieRecords.ThumbPathGrid),
                nameof(MovieRecords.ThumbPathList),
                nameof(MovieRecords.ThumbPathBig10),
            ];

            int queuedErrorCount = 0;
            SelectFirstItem();

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change end: tab={index} selected=none queued_error={queuedErrorCount} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            ShowExtensionDetail(mv);
            RequestUpperTabVisibleRangeRefresh(reason: "tab-selected");
            selectionStopwatch.Stop();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"tab change end: tab={index} selected='{mv.Movie_Name}' queued_error={queuedErrorCount} total_ms={selectionStopwatch.ElapsedMilliseconds}"
            );
        }

        // タブ切替で見つかった error 画像動画は、通常キューへ戻さず救済レーンへ静かに逃がす。
        private async Task EnqueueVisibleUpperTabThumbnailErrorsToRescueAsync(
            int tabIndex,
            MovieRecords[] query,
            int requestVersion
        )
        {
            if (query == null || query.Length == 0)
            {
                return;
            }

            await Task.Delay(ThumbnailVisibleErrorAutoRescueDelayMs);

            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(() => { });
            }

            if (
                GetCurrentUpperTabFixedIndex() != tabIndex
                || requestVersion != _thumbnailVisibleErrorRescueRequestVersion
            )
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab enqueue skip: tab={tabIndex} reason=tab_changed queued_error={query.Length}"
                );
                return;
            }

            int queuedCount = 0;
            DateTime preferredUntilUtc = DateTime.UtcNow.Add(ThumbnailVisibleErrorPreferredDuration);
            foreach (var item in query)
            {
                QueueObj tempObj = new()
                {
                    MovieId = item.Movie_Id,
                    MovieFullPath = item.Movie_Path,
                    Hash = item.Hash,
                    Tabindex = tabIndex,
                    Priority = ThumbnailQueuePriority.Preferred,
                };
                if (
                    TryEnqueueThumbnailDisplayErrorRescueJob(
                        tempObj,
                        reason: "tab-error-placeholder",
                        requiresIdle: true,
                        priorityUntilUtc: preferredUntilUtc
                    )
                )
                {
                    queuedCount++;
                }
            }

            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"tab error rescue enqueue end: tab={tabIndex} queued_error={queuedCount}"
            );
        }

        // visible range の placeholder だけを差分で拾い、今見えている ERROR へ優先を付ける。
        private void QueueVisibleUpperTabThumbnailErrorsToRescue(
            int tabIndex,
            IndigoMovieManager.UpperTabs.Common.UpperTabVisibleRange visibleRange
        )
        {
            if (
                tabIndex < 0
                || tabIndex > 4
                || !_activeUpperTabVisibleRange.HasVisibleItems
                || MainVM?.FilteredMovieRecs == null
            )
            {
                _activeUpperTabVisibleErrorMoviePathKeysSnapshot = Array.Empty<string>();
                return;
            }

            MovieRecords[] visibleErrorMovies = ResolveVisibleUpperTabErrorMovies(tabIndex, visibleRange);
            List<string> nextKeys = visibleErrorMovies
                .Select(x => QueueDbPathResolver.CreateMoviePathKey(x?.Movie_Path ?? ""))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (
                AreMoviePathKeyListsEqual(
                    _activeUpperTabVisibleErrorMoviePathKeysSnapshot,
                    nextKeys
                )
            )
            {
                return;
            }

            _activeUpperTabVisibleErrorMoviePathKeysSnapshot = nextKeys;
            if (visibleErrorMovies.Length < 1)
            {
                return;
            }

            int requestVersion = ++_thumbnailVisibleErrorRescueRequestVersion;
            _ = EnqueueVisibleUpperTabThumbnailErrorsToRescueAsync(
                tabIndex,
                visibleErrorMovies,
                requestVersion
            );
        }

        private MovieRecords[] ResolveVisibleUpperTabErrorMovies(
            int tabIndex,
            IndigoMovieManager.UpperTabs.Common.UpperTabVisibleRange visibleRange
        )
        {
            if (!visibleRange.HasVisibleItems || MainVM?.FilteredMovieRecs == null)
            {
                return [];
            }

            string[] thumbProps =
            [
                nameof(MovieRecords.ThumbPathSmall),
                nameof(MovieRecords.ThumbPathBig),
                nameof(MovieRecords.ThumbPathGrid),
                nameof(MovieRecords.ThumbPathList),
                nameof(MovieRecords.ThumbPathBig10),
            ];
            if (tabIndex < 0 || tabIndex >= thumbProps.Length)
            {
                return [];
            }

            var thumbProp = typeof(MovieRecords).GetProperty(thumbProps[tabIndex]);
            if (thumbProp == null)
            {
                return [];
            }

            List<MovieRecords> result = [];
            int totalCount = MainVM.FilteredMovieRecs.Count;
            int lastVisibleIndex = Math.Min(visibleRange.LastVisibleIndex, totalCount - 1);
            for (int index = Math.Max(0, visibleRange.FirstVisibleIndex); index <= lastVisibleIndex; index++)
            {
                MovieRecords movie = MainVM.FilteredMovieRecs[index];
                if (!IsThumbnailErrorPlaceholderPath(thumbProp.GetValue(movie)?.ToString()))
                {
                    continue;
                }

                result.Add(movie);
                if (result.Count >= ThumbnailVisibleErrorAutoRescueLimit)
                {
                    break;
                }
            }

            if (result.Count >= ThumbnailVisibleErrorAutoRescueLimit)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"visible error rescue capped: tab={tabIndex} queued={result.Count}"
                );
            }

            return result.ToArray();
        }

        // 現在タブから選択中の1件を取得する。
        public MovieRecords GetSelectedItemByTabIndex()
        {
            MovieRecords mv = null;
            switch (GetCurrentUpperTabFixedIndex())
            {
                case UpperTabSmallFixedIndex:
                    mv = SmallList.SelectedItem as MovieRecords;
                    break;
                case UpperTabBigFixedIndex:
                    mv = BigList.SelectedItem as MovieRecords;
                    break;
                case UpperTabGridFixedIndex:
                    mv = GridList.SelectedItem as MovieRecords;
                    break;
                case UpperTabListFixedIndex:
                    mv = ListDataGrid.SelectedItem as MovieRecords;
                    break;
                case UpperTabBig10FixedIndex:
                    mv = BigList10.SelectedItem as MovieRecords;
                    break;
                case ThumbnailErrorTabIndex:
                    mv = GetSelectedUpperTabRescueMovieRecord();
                    break;
                case DuplicateVideoTabIndex:
                    mv = GetSelectedUpperTabDuplicateMovieRecord();
                    break;
            }

            return mv;
        }

        // 現在タブから複数選択中のレコード一覧を取得する。
        private List<MovieRecords> GetSelectedItemsByTabIndex()
        {
            List<MovieRecords> mv = [];
            switch (GetCurrentUpperTabFixedIndex())
            {
                case UpperTabSmallFixedIndex:
                    foreach (MovieRecords item in SmallList.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case UpperTabBigFixedIndex:
                    foreach (MovieRecords item in BigList.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case UpperTabGridFixedIndex:
                    foreach (MovieRecords item in GridList.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case UpperTabListFixedIndex:
                    foreach (MovieRecords item in ListDataGrid.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case UpperTabBig10FixedIndex:
                    foreach (MovieRecords item in BigList10.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case ThumbnailErrorTabIndex:
                    mv.AddRange(GetSelectedUpperTabRescueMovieRecords());
                    break;
                case DuplicateVideoTabIndex:
                    mv.AddRange(GetSelectedUpperTabDuplicateMovieRecords());
                    break;
                default:
                    return null;
            }

            return mv;
        }
    }
}
