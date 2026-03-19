using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IndigoMovieManager.Converter;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.UpperTabs.Rescue;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private static readonly FileSizeConverter UpperTabRescueFileSizeConverter = new();
        private readonly ObservableCollection<UpperTabRescueTargetOption> _upperTabRescueTargets =
            [];
        private readonly ObservableCollection<UpperTabRescueListItemViewModel> _upperTabRescueItems =
            [];
        private int _upperTabRescueRefreshRunning;
        private int _upperTabRescueBulkNormalRetryRunning;

        // 救済タブの対象候補と一覧コレクションを UI へ結び、最初の既定値だけ決める。
        private void InitializeUpperTabRescueTab()
        {
            if (UpperTabRescueViewHost == null)
            {
                return;
            }

            if (_upperTabRescueTargets.Count == 0)
            {
                foreach (UpperTabRescueTargetOption option in BuildUpperTabRescueTargetOptions())
                {
                    _upperTabRescueTargets.Add(option);
                }
            }

            UpperTabRescueViewHost.TargetTabComboBoxControl.ItemsSource = _upperTabRescueTargets;
            UpperTabRescueViewHost.RescueListDataGridControl.ItemsSource = _upperTabRescueItems;

            if (UpperTabRescueViewHost.TargetTabComboBoxControl.SelectedItem == null)
            {
                UpperTabRescueTargetOption defaultTarget = _upperTabRescueTargets.FirstOrDefault(
                    x => x.TabIndex == UpperTabGridFixedIndex
                );
                UpperTabRescueViewHost.TargetTabComboBoxControl.SelectedItem =
                    defaultTarget ?? _upperTabRescueTargets.FirstOrDefault();
            }
        }

        private static UpperTabRescueTargetOption[] BuildUpperTabRescueTargetOptions()
        {
            return
            [
                new UpperTabRescueTargetOption
                {
                    TabIndex = UpperTabGridFixedIndex,
                    DisplayName = GetThumbnailTabDisplayName(UpperTabGridFixedIndex),
                    ThumbnailWidth = 160,
                    ThumbnailHeight = 120,
                },
                new UpperTabRescueTargetOption
                {
                    TabIndex = UpperTabSmallFixedIndex,
                    DisplayName = GetThumbnailTabDisplayName(UpperTabSmallFixedIndex),
                    ThumbnailWidth = 288,
                    ThumbnailHeight = 72,
                },
                new UpperTabRescueTargetOption
                {
                    TabIndex = UpperTabBigFixedIndex,
                    DisplayName = GetThumbnailTabDisplayName(UpperTabBigFixedIndex),
                    ThumbnailWidth = 600,
                    ThumbnailHeight = 150,
                },
                new UpperTabRescueTargetOption
                {
                    TabIndex = UpperTabListFixedIndex,
                    DisplayName = GetThumbnailTabDisplayName(UpperTabListFixedIndex),
                    ThumbnailWidth = 314,
                    ThumbnailHeight = 42,
                },
                new UpperTabRescueTargetOption
                {
                    TabIndex = UpperTabBig10FixedIndex,
                    DisplayName = GetThumbnailTabDisplayName(UpperTabBig10FixedIndex),
                    ThumbnailWidth = 600,
                    ThumbnailHeight = 180,
                },
            ];
        }

        private UpperTabRescueTargetOption GetSelectedUpperTabRescueTargetOption()
        {
            return UpperTabRescueViewHost?.TargetTabComboBoxControl?.SelectedItem
                as UpperTabRescueTargetOption;
        }

        // 救済タブは手動更新前提なので、選択中かどうかの判定だけ薄く分けておく。
        private bool IsUpperTabRescueSelected()
        {
            return TabThumbnailError?.IsSelected == true;
        }

        // 救済タブ上の操作は、現在の上側タブIDではなく「対象タブ」の固定IDへ解決する。
        private int GetCurrentThumbnailActionTabIndex()
        {
            int currentTabIndex = GetCurrentUpperTabFixedIndex();
            if (currentTabIndex != ThumbnailErrorTabIndex)
            {
                return currentTabIndex;
            }

            return GetSelectedUpperTabRescueTargetOption()?.TabIndex ?? UpperTabGridFixedIndex;
        }

        private DataGrid GetUpperTabRescueDataGrid()
        {
            return UpperTabRescueViewHost?.RescueListDataGridControl;
        }

        private UpperTabRescueListItemViewModel[] BuildUpperTabRescueItems(
            IEnumerable<ThumbnailErrorRecordViewModel> records,
            UpperTabRescueTargetOption target
        )
        {
            if (target == null)
            {
                return [];
            }

            return
            [
                .. records
                    ?.Where(record =>
                        record?.MovieRecord != null
                        && record.FailedTabIndices?.Contains(target.TabIndex) == true
                    )
                    .Select(record => BuildUpperTabRescueItem(record, target))
                    .Where(item => item != null) ?? [],
            ];
        }

        // 既存のエラー集計から、対象タブだけの軽い一覧モデルを作る。
        private static UpperTabRescueListItemViewModel BuildUpperTabRescueItem(
            ThumbnailErrorRecordViewModel record,
            UpperTabRescueTargetOption target
        )
        {
            MovieRecords movie = record?.MovieRecord;
            if (movie == null || target == null)
            {
                return null;
            }

            return new UpperTabRescueListItemViewModel
            {
                MovieRecord = movie,
                TabIndex = target.TabIndex,
                ThumbnailPath = ResolveUpperTabRescueThumbnailPath(movie, target.TabIndex),
                ThumbnailWidth = target.ThumbnailWidth,
                ThumbnailHeight = target.ThumbnailHeight,
                MovieName = movie.Movie_Name ?? "",
                MovieSizeText =
                    UpperTabRescueFileSizeConverter.Convert(
                        movie.Movie_Size,
                        typeof(string),
                        null,
                        CultureInfo.CurrentCulture
                    )?.ToString() ?? "",
                MovieLengthText = movie.Movie_Length ?? "",
                ScoreText = movie.Score.ToString(CultureInfo.CurrentCulture),
                FileDateText = movie.File_Date ?? "",
                FailedTabsText = record.FailedTabsText ?? "",
                ProgressStatusText = record.ProgressStatusText ?? "",
                ProgressDetailText = record.ProgressDetailText ?? "",
                MoviePath = movie.Movie_Path ?? "",
            };
        }

        private static string ResolveUpperTabRescueThumbnailPath(MovieRecords movie, int tabIndex)
        {
            if (movie == null)
            {
                return "";
            }

            return tabIndex switch
            {
                UpperTabSmallFixedIndex => movie.ThumbPathSmall ?? "",
                UpperTabBigFixedIndex => movie.ThumbPathBig ?? "",
                UpperTabGridFixedIndex => movie.ThumbPathGrid ?? "",
                UpperTabListFixedIndex => movie.ThumbPathList ?? "",
                UpperTabBig10FixedIndex => movie.ThumbPathBig10 ?? "",
                _ => "",
            };
        }

        // 救済タブ表示中だけ、該当行のサムネパスを最小差分で差し替える。
        private void TryReflectRescuedThumbnailIntoUpperTabRescueItems(
            string moviePath,
            int tabIndex,
            string outputThumbPath
        )
        {
            if (string.IsNullOrWhiteSpace(moviePath) || string.IsNullOrWhiteSpace(outputThumbPath))
            {
                return;
            }

            UpperTabRescueTargetOption selectedTarget = GetSelectedUpperTabRescueTargetOption();
            if (selectedTarget == null || selectedTarget.TabIndex != tabIndex)
            {
                return;
            }

            for (int i = 0; i < _upperTabRescueItems.Count; i++)
            {
                UpperTabRescueListItemViewModel item = _upperTabRescueItems[i];
                if (
                    !string.Equals(
                        item?.MoviePath,
                        moviePath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                _upperTabRescueItems[i] = new UpperTabRescueListItemViewModel
                {
                    MovieRecord = item.MovieRecord,
                    TabIndex = item.TabIndex,
                    ThumbnailPath = outputThumbPath,
                    ThumbnailWidth = item.ThumbnailWidth,
                    ThumbnailHeight = item.ThumbnailHeight,
                    MovieName = item.MovieName,
                    MovieSizeText = item.MovieSizeText,
                    MovieLengthText = item.MovieLengthText,
                    ScoreText = item.ScoreText,
                    FileDateText = item.FileDateText,
                    FailedTabsText = item.FailedTabsText,
                    ProgressStatusText = item.ProgressStatusText,
                    ProgressDetailText = item.ProgressDetailText,
                    MoviePath = item.MoviePath,
                };
            }
        }

        private void ReplaceUpperTabRescueItems(IEnumerable<UpperTabRescueListItemViewModel> items)
        {
            _upperTabRescueItems.Clear();
            foreach (UpperTabRescueListItemViewModel item in items ?? [])
            {
                _upperTabRescueItems.Add(item);
            }
        }

        private UpperTabRescueListItemViewModel[] GetDisplayedUpperTabRescueItems()
        {
            return [.. _upperTabRescueItems.Where(item => item != null)];
        }

        private List<UpperTabRescueListItemViewModel> GetSelectedUpperTabRescueItems()
        {
            List<UpperTabRescueListItemViewModel> items = [];
            DataGrid rescueListDataGrid = GetUpperTabRescueDataGrid();
            if (rescueListDataGrid == null)
            {
                return items;
            }

            foreach (object selectedRow in rescueListDataGrid.SelectedItems)
            {
                if (selectedRow is UpperTabRescueListItemViewModel item)
                {
                    items.Add(item);
                }
            }

            if (items.Count == 0 && rescueListDataGrid.CurrentItem is UpperTabRescueListItemViewModel current)
            {
                items.Add(current);
            }

            if (
                items.Count == 0
                && rescueListDataGrid.CurrentCell.Item is UpperTabRescueListItemViewModel currentCellItem
            )
            {
                items.Add(currentCellItem);
            }

            if (
                items.Count == 0
                && rescueListDataGrid.SelectedItem is UpperTabRescueListItemViewModel selectedListItem
            )
            {
                items.Add(selectedListItem);
            }

            return items;
        }

        private MovieRecords GetSelectedUpperTabRescueMovieRecord()
        {
            return GetSelectedUpperTabRescueItems().Select(x => x.MovieRecord).FirstOrDefault(
                x => x != null
            );
        }

        private List<MovieRecords> GetSelectedUpperTabRescueMovieRecords()
        {
            return
            [
                .. GetSelectedUpperTabRescueItems()
                    .Select(item => item.MovieRecord)
                    .Where(record => record != null),
            ];
        }

        private async void UpperTabRescueRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (Interlocked.Exchange(ref _upperTabRescueRefreshRunning, 1) == 1)
            {
                return;
            }

            try
            {
                UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
                if (target == null)
                {
                    return;
                }

                ThumbnailErrorRefreshContext context = CaptureThumbnailErrorRefreshContext();
                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"rescue tab refresh start: target_tab={target.TabIndex}"
                );

                ThumbnailErrorRefreshResult result = await Task.Run(
                    () => BuildThumbnailErrorRefreshResult(context)
                );

                MainVM?.ReplaceThumbnailErrorRecs(result.Items);
                MainVM?.ThumbnailErrorProgress?.Apply(result.Items);
                ReplaceUpperTabRescueItems(BuildUpperTabRescueItems(result.Items, target));

                if (TabThumbnailError?.IsSelected == true)
                {
                    SelectFirstItem();
                    MovieRecords selectedMovie = GetSelectedItemByTabIndex();
                    if (selectedMovie != null)
                    {
                        ShowExtensionDetail(selectedMovie);
                    }
                    else
                    {
                        HideExtensionDetail();
                    }
                }

                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"rescue tab refresh end: target_tab={target.TabIndex} count={_upperTabRescueItems.Count}"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"rescue tab refresh failed: {ex.GetType().Name}: {ex.Message}"
                );
            }
            finally
            {
                Interlocked.Exchange(ref _upperTabRescueRefreshRunning, 0);
            }
        }

        // 救済タブからの明示再試行だけは、対象タブの通常キューへ直接戻す。
        private async void UpperTabRescueBulkNormalRetryButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            if (Interlocked.Exchange(ref _upperTabRescueBulkNormalRetryRunning, 1) == 1)
            {
                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    "rescue tab bulk normal retry skipped: already running"
                );
                return;
            }

            UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
            UpperTabRescueListItemViewModel[] items = GetDisplayedUpperTabRescueItems();
            if (target == null || items.Length < 1)
            {
                Interlocked.Exchange(ref _upperTabRescueBulkNormalRetryRunning, 0);
                return;
            }

            string dbName = MainVM?.DbInfo?.DBName ?? "";
            string thumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                int queuedCount = await Task.Run(
                    () =>
                        EnqueueUpperTabRescueItemsToNormalQueue(
                            items,
                            dbName,
                            thumbFolder
                        )
                );

                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"rescue tab bulk normal retry end: target_tab={target.TabIndex} visible={items.Length} queued={queuedCount}"
                );
            }
            finally
            {
                Mouse.OverrideCursor = null;
                Interlocked.Exchange(ref _upperTabRescueBulkNormalRetryRunning, 0);
                Refresh();
            }
        }

        // 現在表示中の救済行を、対象タブの通常キューへ優先再投入する。
        private int EnqueueUpperTabRescueItemsToNormalQueue(
            IEnumerable<UpperTabRescueListItemViewModel> items,
            string dbName,
            string thumbFolder
        )
        {
            int visibleCount = 0;
            int queuedCount = 0;
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (UpperTabRescueListItemViewModel item in items ?? [])
            {
                MovieRecords movie = item?.MovieRecord;
                string moviePath = movie?.Movie_Path ?? item?.MoviePath ?? "";
                if (movie == null || string.IsNullOrWhiteSpace(moviePath))
                {
                    continue;
                }

                string dedupeKey = $"{moviePath}|{item.TabIndex}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                visibleCount++;
                string targetThumbOutPath = ResolveThumbnailOutPath(
                    item.TabIndex,
                    dbName,
                    thumbFolder
                );
                TryDeleteThumbnailErrorMarker(targetThumbOutPath, moviePath);

                QueueObj queueObj = new()
                {
                    MovieId = movie.Movie_Id,
                    MovieFullPath = moviePath,
                    Hash = movie.Hash,
                    Tabindex = item.TabIndex,
                    Priority = ThumbnailQueuePriority.Preferred,
                };
                if (
                    TryEnqueueThumbnailJob(
                        queueObj,
                        bypassDebounce: true,
                        bypassTabGate: true
                    )
                )
                {
                    queuedCount++;
                }
            }

            DebugRuntimeLog.Write(
                "upper-tab-rescue",
                $"rescue tab normal retry enqueue end: visible={visibleCount} queued={queuedCount}"
            );
            return queuedCount;
        }

        private void UpperTabRescuePlayRequested(object sender, MouseButtonEventArgs e)
        {
            PlayMovie_Click(sender, e);
        }
    }
}
