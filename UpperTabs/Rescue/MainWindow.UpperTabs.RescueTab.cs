using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IndigoMovieManager.Converter;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
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
        private int _upperTabRescueBulkBlackRetryRunning;
        private int _upperTabRescueBlackConfirmRunning;
        private int _upperTabRescueIndexRepairRunning;
        private bool _upperTabRescueTargetSelectionHooked;

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

            if (!_upperTabRescueTargetSelectionHooked)
            {
                UpperTabRescueViewHost.TargetTabComboBoxControl.SelectionChanged +=
                    (_, _) => ResolvePreferredThumbnailTabIndex();
                _upperTabRescueTargetSelectionHooked = true;
            }

            ResolvePreferredThumbnailTabIndex();
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

        // 手動サムネイル系の操作先は、特殊タブを通常サムネイルタブへ正規化して返す。
        private int GetCurrentThumbnailActionTabIndex()
        {
            return ResolveThumbnailActionTabIndex(
                GetCurrentUpperTabFixedIndex(),
                GetSelectedUpperTabRescueTargetOption()?.TabIndex ?? -1
            );
        }

        // 救済タブは選択対象、重複動画タブは Grid へ寄せ、作成結果の保存先とUI反映先を揃える。
        internal static int ResolveThumbnailActionTabIndex(
            int currentTabIndex,
            int rescueTargetTabIndex = -1
        )
        {
            if (currentTabIndex == ThumbnailErrorTabIndex)
            {
                return rescueTargetTabIndex is >= UpperTabSmallFixedIndex and <= UpperTabBig10FixedIndex
                    ? rescueTargetTabIndex
                    : UpperTabGridFixedIndex;
            }

            if (currentTabIndex == DuplicateVideoTabIndex)
            {
                return UpperTabGridFixedIndex;
            }

            return currentTabIndex;
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
                ProgressDetailText = NormalizeUpperTabRescueDetailText(record.ProgressDetailText),
                MoviePath = movie.Movie_Path ?? "",
            };
        }

        // 救済タブの理由列は高密度優先で、path 系の付帯情報を落として要点だけ残す。
        private static string NormalizeUpperTabRescueDetailText(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return "";
            }

            string normalized = detail;
            normalized = Regex.Replace(
                normalized,
                @"(^|,\s*)(movie|path|thumb|thumb_path|output|output_thumb|outpath)='[^']*'",
                "",
                RegexOptions.IgnoreCase
            );
            normalized = Regex.Replace(normalized, @"\s{2,}", " ");
            normalized = Regex.Replace(normalized, @"\s+,", ",");
            normalized = normalized.Trim().Trim(',', ' ');

            // CSV風の詳細は末尾の reason / timeout 系だけ見せて、path などの枝葉は落とす。
            string[] segments = normalized
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                string segment = segments[i];
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                if (segment.Contains("reason=", StringComparison.OrdinalIgnoreCase))
                {
                    return segment;
                }

                if (
                    segment.Contains("timeout_sec", StringComparison.OrdinalIgnoreCase)
                    || segment.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                    || segment.Contains("停止疑い", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return segment;
                }
            }

            if (normalized.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return "timeout";
            }

            return normalized;
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

            // 救済タブの表示行が変わったら、通常再試行用の preferred key も同じ内容へ更新する。
            NotifyUpperTabViewportSourceChanged();
            if (IsUpperTabRescueSelected())
            {
                RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "rescue-items-replaced");
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
                ResolvePreferredThumbnailTabIndex();
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

        // 救済タブの表示行へ、黒背景向けの専用 rescue request を一括投入する。
        private int EnqueueUpperTabRescueItemsToBlackBackgroundRescue(
            IEnumerable<UpperTabRescueListItemViewModel> items,
            bool useLiteMode
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
                QueueObj queueObj = new()
                {
                    MovieId = movie.Movie_Id,
                    MovieFullPath = moviePath,
                    Hash = movie.Hash,
                    Tabindex = item.TabIndex,
                    Priority = ThumbnailQueuePriority.Preferred,
                };

                bool accepted = useLiteMode
                    ? TryEnqueueThumbnailDarkHeavyBackgroundLiteRescueJob(
                        queueObj,
                        requiresIdle: false,
                        reason: "upper-tab-rescue-black-lite",
                        useDedicatedManualWorkerSlot: true
                    )
                    : TryEnqueueThumbnailDarkHeavyBackgroundRescueJob(
                        queueObj,
                        requiresIdle: false,
                        reason: "upper-tab-rescue-black-deep",
                        useDedicatedManualWorkerSlot: true
                    );
                if (accepted)
                {
                    queuedCount++;
                }
            }

            DebugRuntimeLog.Write(
                "upper-tab-rescue",
                $"rescue tab black rescue enqueue end: mode={(useLiteMode ? "lite" : "deep")} visible={visibleCount} queued={queuedCount}"
            );
            return queuedCount;
        }

        private void UpperTabRescuePlayRequested(object sender, MouseButtonEventArgs e)
        {
            PlayMovie_Click(sender, e);
        }

        private async void UpperTabRescueBulkBlackLiteRetryButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            await RunUpperTabRescueBulkBlackRetryAsync(useLiteMode: true);
        }

        private async void UpperTabRescueBulkBlackDeepRetryButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            await RunUpperTabRescueBulkBlackRetryAsync(useLiteMode: false);
        }

        private async void UpperTabRescueSelectedBlackLiteRetryButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            await RunUpperTabRescueSelectedBlackRetryAsync(useLiteMode: true);
        }

        private async void UpperTabRescueSelectedBlackDeepRetryButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            await RunUpperTabRescueSelectedBlackRetryAsync(useLiteMode: false);
        }

        private async void UpperTabRescueSelectedIndexRepairButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            await RunUpperTabRescueSelectedIndexRepairAsync();
        }

        private async void UpperTabRescueSelectedBlackConfirmButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            await RunUpperTabRescueSelectedBlackConfirmAsync();
        }

        private async Task RunUpperTabRescueBulkBlackRetryAsync(bool useLiteMode)
        {
            if (Interlocked.Exchange(ref _upperTabRescueBulkBlackRetryRunning, 1) == 1)
            {
                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"rescue tab bulk black retry skipped: already running mode={(useLiteMode ? "lite" : "deep")}"
                );
                return;
            }

            UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
            UpperTabRescueListItemViewModel[] items = GetDisplayedUpperTabRescueItems();
            if (target == null || items.Length < 1)
            {
                Interlocked.Exchange(ref _upperTabRescueBulkBlackRetryRunning, 0);
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                ResolvePreferredThumbnailTabIndex();
                int queuedCount = await Task.Run(
                    () => EnqueueUpperTabRescueItemsToBlackBackgroundRescue(items, useLiteMode)
                );

                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"rescue tab bulk black retry end: mode={(useLiteMode ? "lite" : "deep")} target_tab={target.TabIndex} visible={items.Length} queued={queuedCount}"
                );
            }
            finally
            {
                Mouse.OverrideCursor = null;
                Interlocked.Exchange(ref _upperTabRescueBulkBlackRetryRunning, 0);
                Refresh();
            }
        }

        // 下段ボタンは、現在の選択行だけを黒背景救済へ流す。
        private async Task RunUpperTabRescueSelectedBlackRetryAsync(bool useLiteMode)
        {
            if (Interlocked.Exchange(ref _upperTabRescueBulkBlackRetryRunning, 1) == 1)
            {
                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"rescue tab selected black retry skipped: already running mode={(useLiteMode ? "lite" : "deep")}"
                );
                return;
            }

            UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
            List<UpperTabRescueListItemViewModel> items = GetSelectedUpperTabRescueItems();
            if (target == null || items.Count < 1)
            {
                Interlocked.Exchange(ref _upperTabRescueBulkBlackRetryRunning, 0);
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                ResolvePreferredThumbnailTabIndex();
                int queuedCount = await Task.Run(
                    () => EnqueueUpperTabRescueItemsToBlackBackgroundRescue(items, useLiteMode)
                );

                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"rescue tab selected black retry end: mode={(useLiteMode ? "lite" : "deep")} target_tab={target.TabIndex} selected={items.Count} queued={queuedCount}"
                );
            }
            finally
            {
                Mouse.OverrideCursor = null;
                Interlocked.Exchange(ref _upperTabRescueBulkBlackRetryRunning, 0);
                Refresh();
            }
        }

        // 手動で黒確定した時は rescue worker を通さず、対象サイズの黒jpgを直接保存する。
        private async Task RunUpperTabRescueSelectedBlackConfirmAsync()
        {
            if (Interlocked.Exchange(ref _upperTabRescueBlackConfirmRunning, 1) == 1)
            {
                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    "rescue tab black confirm skipped: already running"
                );
                return;
            }

            UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
            List<UpperTabRescueListItemViewModel> items = GetSelectedUpperTabRescueItems();
            if (target == null || items.Count < 1)
            {
                Interlocked.Exchange(ref _upperTabRescueBlackConfirmRunning, 0);
                return;
            }

            string dbName = MainVM?.DbInfo?.DBName ?? "";
            string thumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                List<(
                    MovieRecords MovieRecord,
                    string MoviePath,
                    int TabIndex,
                    string OutputThumbPath,
                    int DeletedFailureCount
                )> results = await Task.Run(
                    () => CreateUpperTabRescueBlackConfirmResults(items, target, dbName, thumbFolder)
                );

                foreach (
                    (
                        MovieRecords movieRecord,
                        string moviePath,
                        int tabIndex,
                        string outputThumbPath,
                        int _
                    ) in results
                )
                {
                    TryApplyThumbnailPathToMovieRecord(movieRecord, tabIndex, outputThumbPath);
                    TryReflectRescuedThumbnailIntoUpperTabRescueItems(
                        moviePath,
                        tabIndex,
                        outputThumbPath
                    );
                    TryReflectCreatedThumbnailIntoUpperTabDuplicateItems(
                        moviePath,
                        tabIndex,
                        outputThumbPath
                    );
                }

                RefreshUpperTabRescueHistoryPanel();

                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"rescue tab black confirm end: target_tab={target.TabIndex} selected={items.Count} generated={results.Count} deleted_failure={results.Sum(x => x.DeletedFailureCount)}"
                );
            }
            finally
            {
                Mouse.OverrideCursor = null;
                Interlocked.Exchange(ref _upperTabRescueBlackConfirmRunning, 0);
            }
        }

        // インデックス再構築は別名コピーを伴う重い処理なので、押下時に確認を挟んでから流す。
        private async Task RunUpperTabRescueSelectedIndexRepairAsync()
        {
            if (Interlocked.Exchange(ref _upperTabRescueIndexRepairRunning, 1) == 1)
            {
                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    "rescue tab index repair skipped: already running"
                );
                return;
            }

            try
            {
                if (!ConfirmThumbnailIndexRepair())
                {
                    return;
                }

                UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
                List<UpperTabRescueListItemViewModel> items = GetSelectedUpperTabRescueItems();
                if (target == null || items.Count < 1)
                {
                    return;
                }

                string firstMoviePath = items
                    .Select(item => item?.MovieRecord?.Movie_Path ?? item?.MoviePath ?? "")
                    .FirstOrDefault(moviePath => CanTryThumbnailIndexRepair(moviePath));
                if (!string.IsNullOrWhiteSpace(firstMoviePath))
                {
                    RememberManualThumbnailRescueMoviePath(firstMoviePath);
                    ReportManualThumbnailRescueProgress(
                        BuildManualThumbnailRescueModeProgressMessage("force-index-repair"),
                        true
                    );
                }

                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    ResolvePreferredThumbnailTabIndex();
                    int startedCount = await Task.Run(
                        () => StartUpperTabRescueItemsDirectIndexRepair(items)
                    );

                    DebugRuntimeLog.Write(
                        "upper-tab-rescue",
                        $"rescue tab selected index repair end: target_tab={target.TabIndex} selected={items.Count} started={startedCount}"
                    );
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                    Refresh();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _upperTabRescueIndexRepairRunning, 0);
            }
        }

        // repair 対象拡張子だけ worker へ渡し、manual slot で即時起動を試す。
        private int StartUpperTabRescueItemsDirectIndexRepair(
            IEnumerable<UpperTabRescueListItemViewModel> items
        )
        {
            int startedCount = 0;
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (UpperTabRescueListItemViewModel item in items ?? [])
            {
                MovieRecords movie = item?.MovieRecord;
                string moviePath = movie?.Movie_Path ?? item?.MoviePath ?? "";
                if (
                    movie == null
                    || string.IsNullOrWhiteSpace(moviePath)
                    || !CanTryThumbnailIndexRepair(moviePath)
                )
                {
                    continue;
                }

                string dedupeKey = $"{moviePath}|{item.TabIndex}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                bool started = TryStartThumbnailDirectIndexRepairWorker(moviePath);
                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"rescue tab direct index repair start: movie='{moviePath}' tab={item.TabIndex} started={started}"
                );
                if (started)
                {
                    startedCount++;
                }
            }

            return startedCount;
        }

        // 選択行ごとに保存先jpgを決め、黒サムネ作成と FailureDb 後始末をまとめて行う。
        private List<(
            MovieRecords MovieRecord,
            string MoviePath,
            int TabIndex,
            string OutputThumbPath,
            int DeletedFailureCount
        )> CreateUpperTabRescueBlackConfirmResults(
            IEnumerable<UpperTabRescueListItemViewModel> items,
            UpperTabRescueTargetOption target,
            string dbName,
            string thumbFolder
        )
        {
            List<(
                MovieRecords MovieRecord,
                string MoviePath,
                int TabIndex,
                string OutputThumbPath,
                int DeletedFailureCount
            )> results = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();

            foreach (UpperTabRescueListItemViewModel item in items ?? [])
            {
                MovieRecords movie = item?.MovieRecord;
                string moviePath = movie?.Movie_Path ?? item?.MoviePath ?? "";
                if (movie == null || string.IsNullOrWhiteSpace(moviePath))
                {
                    continue;
                }

                int tabIndex = item.TabIndex;
                string dedupeKey = $"{moviePath}|{tabIndex}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                string thumbOutPath = ResolveThumbnailOutPath(tabIndex, dbName, thumbFolder);
                string outputThumbPath = ThumbnailPathResolver.BuildThumbnailPath(
                    thumbOutPath,
                    moviePath,
                    movie.Hash
                );
                ThumbnailLayoutProfile layoutProfile = ResolveThumbnailLayoutProfile(tabIndex);
                int width = Math.Max(1, layoutProfile.Width * layoutProfile.Columns);
                int height = Math.Max(1, layoutProfile.Height * layoutProfile.Rows);
                ThumbInfo thumbInfo = BuildBlackConfirmThumbInfo(layoutProfile);

                TryDeleteThumbnailErrorMarker(thumbOutPath, moviePath);
                WriteSolidBlackThumbnail(outputThumbPath, width, height, thumbInfo);
                ThumbnailPathResolver.RememberSuccessThumbnailPath(outputThumbPath);

                int deletedFailureCount = 0;
                if (failureDbService != null)
                {
                    string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath);
                    deletedFailureCount = failureDbService.DeleteMainFailureRecords(
                    [
                        (moviePathKey, tabIndex),
                    ]
                    );
                }

                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"black confirm generated: tab={tabIndex} size={width}x{height} movie='{moviePath}' output='{outputThumbPath}' deleted_failure={deletedFailureCount}"
                );
                results.Add((movie, moviePath, tabIndex, outputThumbPath, deletedFailureCount));
            }

            return results;
        }

        // 救済打ち切りを明示したい時の固定黒jpgは、いったんtmpへ保存してから差し替える。
        private static ThumbInfo BuildBlackConfirmThumbInfo(ThumbnailLayoutProfile layoutProfile)
        {
            ThumbnailSheetSpec spec = new()
            {
                ThumbWidth = Math.Max(1, layoutProfile?.Width ?? 1),
                ThumbHeight = Math.Max(1, layoutProfile?.Height ?? 1),
                ThumbColumns = Math.Max(1, layoutProfile?.Columns ?? 1),
                ThumbRows = Math.Max(1, layoutProfile?.Rows ?? 1),
                ThumbCount = Math.Max(1, layoutProfile?.DivCount ?? 1),
            };

            for (int i = 1; i <= spec.ThumbCount; i++)
            {
                spec.CaptureSeconds.Add(i);
            }

            return ThumbInfo.FromSheetSpec(spec);
        }

        private static void WriteSolidBlackThumbnail(
            string outputThumbPath,
            int width,
            int height,
            ThumbInfo thumbInfo
        )
        {
            if (string.IsNullOrWhiteSpace(outputThumbPath))
            {
                return;
            }

            string directoryPath = Path.GetDirectoryName(outputThumbPath) ?? "";
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string tempPath = outputThumbPath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using (Bitmap bitmap = new(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
                if (
                    !ThumbnailJpegMetadataWriter.TrySaveJpegWithThumbInfo(
                        bitmap,
                        tempPath,
                        thumbInfo,
                        out string errorMessage
                    )
                )
                {
                    throw new IOException(errorMessage);
                }
            }

            File.Copy(tempPath, outputThumbPath, overwrite: true);
            File.Delete(tempPath);
        }
    }
}
