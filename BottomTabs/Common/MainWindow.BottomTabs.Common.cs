using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using IndigoMovieManager.ModelViews;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // タブ切替だけで救済要求が雪崩れないよう、自動投入数は小さく抑える。
        private const int ThumbnailAutoRescuePerTabSwitchLimit = 64;

        /// <summary>
        /// 今開いてるタブの先頭アイテムにカーソルを合わせる！これが俺のスマートなエスコートだ！😎
        /// </summary>
        public void SelectFirstItem()
        {
            switch (Tabs.SelectedIndex)
            {
                case 0:
                    TabSmall.IsSelected = true;
                    if (SmallList.Items.Count > 0)
                    {
                        SmallList.SelectedIndex = 0;
                    }
                    break;
                case 1:
                    TabBig.IsSelected = true;
                    if (BigList.Items.Count > 0)
                    {
                        BigList.SelectedIndex = 0;
                    }
                    break;
                case 2:
                    TabGrid.IsSelected = true;
                    if (GridList.Items.Count > 0)
                    {
                        GridList.SelectedIndex = 0;
                    }
                    break;
                case 3:
                    TabList.IsSelected = true;
                    if (ListDataGrid.Items.Count > 0)
                    {
                        ListDataGrid.SelectedIndex = 0;
                    }
                    break;
                case 4:
                    TabBig10.IsSelected = true;
                    if (BigList10.Items.Count > 0)
                    {
                        BigList10.SelectedIndex = 0;
                    }
                    break;
                case ThumbnailErrorTabIndex:
                    TabThumbnailError.IsSelected = true;
                    if (ErrorListDataGrid.Items.Count > 0)
                    {
                        ErrorListDataGrid.SelectedIndex = 0;
                    }
                    break;
                default:
                    TabSmall.IsSelected = true;
                    if (SmallList.Items.Count > 0)
                    {
                        SmallList.SelectedIndex = 0;
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
            ClearThumbnailQueue();

            var tabControl = sender as TabControl;
            int index = tabControl.SelectedIndex;
            if (index == -1)
            {
                return;
            }

            MainVM.DbInfo.CurrentTabIndex = index;

            if (index == ThumbnailErrorTabIndex)
            {
                RefreshThumbnailErrorRecords();
                SelectFirstItem();

                MovieRecords errorMovie = GetSelectedItemByTabIndex();
                if (errorMovie == null)
                {
                    HideExtensionDetail();
                    selectionStopwatch.Stop();
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"tab change end: tab={index} selected=none error_count={MainVM.ThumbnailErrorRecs.Count} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                    );
                    return;
                }

                ShowExtensionDetail(errorMovie);
                selectionStopwatch.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change end: tab={index} selected='{errorMovie.Movie_Name}' error_count={MainVM.ThumbnailErrorRecs.Count} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            if (MainVM.FilteredMovieRecs.Count == 0)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change skip: tab={index} reason=no_filtered_items total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
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
            if (index >= 0 && index < thumbProps.Length)
            {
                var thumbProp = typeof(MovieRecords).GetProperty(thumbProps[index]);
                var query = MainVM.FilteredMovieRecs
                    .Where(x => IsThumbnailErrorPlaceholderPath(thumbProp?.GetValue(x)?.ToString()))
                    .ToArray();

                SelectFirstItem();

                if (query.Length > 0)
                {
                    MovieRecords[] limitedQuery = query
                        .Take(ThumbnailAutoRescuePerTabSwitchLimit)
                        .ToArray();
                    queuedErrorCount = limitedQuery.Length;
                    if (query.Length > limitedQuery.Length)
                    {
                        DebugRuntimeLog.Write(
                            "thumbnail-rescue",
                            $"tab auto rescue capped: tab={index} detected={query.Length} queued={limitedQuery.Length}"
                        );
                    }

                    _ = EnqueueTabThumbnailErrorsToRescueAsync(index, limitedQuery);
                }
            }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change end: tab={index} selected=none queued_error={queuedErrorCount} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            if (IsThumbnailErrorPlaceholderPath(mv.ThumbDetail))
            {
                QueueObj tempObj = new()
                {
                    MovieId = mv.Movie_Id,
                    MovieFullPath = mv.Movie_Path,
                    Hash = mv.Hash,
                    Tabindex = 99,
                };
                _ = TryEnqueueThumbnailDisplayErrorRescueJob(
                    tempObj,
                    reason: "detail-error-placeholder"
                );
            }

            ShowExtensionDetail(mv);
            selectionStopwatch.Stop();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"tab change end: tab={index} selected='{mv.Movie_Name}' queued_error={queuedErrorCount} total_ms={selectionStopwatch.ElapsedMilliseconds}"
            );
        }

        // タブ切替で見つかった error 画像動画は、通常キューへ戻さず救済レーンへ静かに逃がす。
        private async Task EnqueueTabThumbnailErrorsToRescueAsync(int tabIndex, MovieRecords[] query)
        {
            if (query == null || query.Length == 0)
            {
                return;
            }

            await Task.Delay(1000);

            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(() => { });
            }

            if (Tabs.SelectedIndex != tabIndex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab enqueue skip: tab={tabIndex} reason=tab_changed queued_error={query.Length}"
                );
                return;
            }

            int queuedCount = 0;
            foreach (var item in query)
            {
                QueueObj tempObj = new()
                {
                    MovieId = item.Movie_Id,
                    MovieFullPath = item.Movie_Path,
                    Hash = item.Hash,
                    Tabindex = tabIndex,
                };
                if (
                    TryEnqueueThumbnailDisplayErrorRescueJob(
                        tempObj,
                        reason: "tab-error-placeholder"
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

        // 現在タブから選択中の1件を取得する。
        public MovieRecords GetSelectedItemByTabIndex()
        {
            MovieRecords mv = null;
            switch (Tabs.SelectedIndex)
            {
                case 0:
                    mv = SmallList.SelectedItem as MovieRecords;
                    break;
                case 1:
                    mv = BigList.SelectedItem as MovieRecords;
                    break;
                case 2:
                    mv = GridList.SelectedItem as MovieRecords;
                    break;
                case 3:
                    mv = ListDataGrid.SelectedItem as MovieRecords;
                    break;
                case 4:
                    mv = BigList10.SelectedItem as MovieRecords;
                    break;
                case ThumbnailErrorTabIndex:
                    mv = (ErrorListDataGrid.SelectedItem as ThumbnailErrorRecordViewModel)
                        ?.MovieRecord;
                    break;
            }

            return mv;
        }

        // 現在タブから複数選択中のレコード一覧を取得する。
        private List<MovieRecords> GetSelectedItemsByTabIndex()
        {
            List<MovieRecords> mv = [];
            switch (Tabs.SelectedIndex)
            {
                case 0:
                    foreach (MovieRecords item in SmallList.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case 1:
                    foreach (MovieRecords item in BigList.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case 2:
                    foreach (MovieRecords item in GridList.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case 3:
                    foreach (MovieRecords item in ListDataGrid.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case 4:
                    foreach (MovieRecords item in BigList10.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case ThumbnailErrorTabIndex:
                    foreach (var selectedItem in ErrorListDataGrid.SelectedItems)
                    {
                        if (
                            selectedItem is ThumbnailErrorRecordViewModel record
                            && record.MovieRecord != null
                        )
                        {
                            mv.Add(record.MovieRecord);
                        }
                    }
                    break;
                default:
                    return null;
            }

            return mv;
        }
    }
}
