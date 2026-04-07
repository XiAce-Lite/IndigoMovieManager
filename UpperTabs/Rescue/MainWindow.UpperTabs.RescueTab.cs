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
using System.Diagnostics;
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
        private readonly Dictionary<int, HashSet<string>> _upperTabRescueManualMoviePathsByTabIndex =
            [];
        private UpperTabRescueTabPresenter _upperTabRescueTabPresenter;
        private int _upperTabRescueRefreshRunning;
        private int _upperTabRescueBulkNormalRetryRunning;
        private int _upperTabRescueBulkBlackRetryRunning;
        private int _upperTabRescueBlackConfirmRunning;
        private int _upperTabRescueIndexRepairRunning;
        private int _upperTabRescueSingleEngineRunning;

        // 救済タブの対象候補と一覧コレクションを UI へ結び、最初の既定値だけ決める。
        private void InitializeUpperTabRescueTab()
        {
            _upperTabRescueTabPresenter ??= new UpperTabRescueTabPresenter(
                UpperTabRescueViewHost,
                _upperTabRescueTargets,
                _upperTabRescueItems,
                BuildUpperTabRescueTargetOptions,
                () => UpperTabGridFixedIndex,
                () => _ = ResolvePreferredThumbnailTabIndex()
            );
            _upperTabRescueTabPresenter.Initialize();
        }

        // 救済タブ切替時の「先頭選択 → 詳細更新 → ログ」をこの dir 側へ寄せる。
        private void HandleUpperTabRescueSelectionChanged(Stopwatch selectionStopwatch, int tabIndex)
        {
            MovieRecords selectedMovie = RefreshUpperTabExtensionDetailFromCurrentSelection(
                selectFirstItem: true
            );
            int rescueCount = GetUpperTabRescueDataGrid()?.Items.Count ?? 0;
            if (selectedMovie == null)
            {
                selectionStopwatch.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change end: tab={tabIndex} selected=none rescue_count={rescueCount} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            selectionStopwatch.Stop();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"tab change end: tab={tabIndex} selected='{selectedMovie.Movie_Name}' rescue_count={rescueCount} total_ms={selectionStopwatch.ElapsedMilliseconds}"
            );
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
            return _upperTabRescueTabPresenter?.GetSelectedTarget();
        }

        // 対象タブを明示指定したい導線用に、既存候補から一致項目を引く。
        private UpperTabRescueTargetOption FindUpperTabRescueTargetOption(int tabIndex)
        {
            return _upperTabRescueTargets.FirstOrDefault(option => option?.TabIndex == tabIndex);
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

        // 救済タブを前面化し、表示中一覧の先頭行だけ既定選択へ寄せる。
        private void SelectUpperTabRescueAsDefaultView()
        {
            SelectUpperTabByFixedIndex(ThumbnailErrorTabIndex);

            DataGrid rescueListDataGrid = GetUpperTabRescueDataGrid();
            if (rescueListDataGrid?.Items.Count > 0)
            {
                rescueListDataGrid.SelectedIndex = 0;
            }
        }

        private UpperTabRescueListItemViewModel[] BuildUpperTabRescueItems(
            IEnumerable<ThumbnailErrorRecordViewModel> records,
            UpperTabRescueTargetOption target,
            IEnumerable<MovieRecords> movies = null
        )
        {
            if (target == null)
            {
                return [];
            }

            List<UpperTabRescueListItemViewModel> items =
            [
                .. records
                    ?.Where(record =>
                        record?.MovieRecord != null
                        && record.FailedTabIndices?.Contains(target.TabIndex) == true
                    )
                    .Select(record => BuildUpperTabRescueItem(record, target))
                    .Where(item => item != null) ?? [],
            ];

            HashSet<string> existingMoviePaths = new(
                items.Select(item => item?.MoviePath ?? "").Where(path => !string.IsNullOrWhiteSpace(path)),
                StringComparer.OrdinalIgnoreCase
            );
            HashSet<string> manualMoviePaths = GetUpperTabRescueManualMoviePaths(target.TabIndex);
            if (manualMoviePaths.Count < 1)
            {
                return [.. items];
            }

            foreach (
                MovieRecords movie in (movies ?? [])
                    .Where(movie =>
                        movie != null
                        && !string.IsNullOrWhiteSpace(movie.Movie_Path)
                        && manualMoviePaths.Contains(movie.Movie_Path)
                    )
                    .OrderBy(movie => movie.Movie_Name ?? "", StringComparer.CurrentCultureIgnoreCase)
            )
            {
                if (!existingMoviePaths.Add(movie.Movie_Path))
                {
                    continue;
                }

                UpperTabRescueListItemViewModel manualItem = BuildUpperTabManualRescueItem(movie, target);
                if (manualItem != null)
                {
                    items.Add(manualItem);
                }
            }

            return [.. items];
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

        // 手動で送った動画は FailureDb 由来でなくても、救済タブ上で対象として見えるようにする。
        private static UpperTabRescueListItemViewModel BuildUpperTabManualRescueItem(
            MovieRecords movie,
            UpperTabRescueTargetOption target
        )
        {
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
                FailedTabsText = GetThumbnailTabDisplayName(target.TabIndex),
                ProgressStatusText = "手動追加",
                ProgressDetailText = "右クリックから救済タブへ追加",
                MoviePath = movie.Movie_Path ?? "",
            };
        }

        // 右クリックで送った動画パスは、対象タブごとに軽い集合で保持して重複追加を避ける。
        private void RegisterUpperTabRescueManualMoviePaths(
            IEnumerable<MovieRecords> records,
            int targetTabIndex
        )
        {
            if (!IsUpperThumbnailTabIndex(targetTabIndex))
            {
                return;
            }

            List<MovieRecords> normalizedRecords = NormalizeThumbnailUserActionMovieRecords(records);
            if (normalizedRecords.Count < 1)
            {
                return;
            }

            if (
                !_upperTabRescueManualMoviePathsByTabIndex.TryGetValue(
                    targetTabIndex,
                    out HashSet<string> moviePaths
                )
                || moviePaths == null
            )
            {
                moviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _upperTabRescueManualMoviePathsByTabIndex[targetTabIndex] = moviePaths;
            }

            foreach (MovieRecords record in normalizedRecords)
            {
                if (string.IsNullOrWhiteSpace(record?.Movie_Path))
                {
                    continue;
                }

                moviePaths.Add(record.Movie_Path);
            }
        }

        private HashSet<string> GetUpperTabRescueManualMoviePaths(int targetTabIndex)
        {
            if (
                !_upperTabRescueManualMoviePathsByTabIndex.TryGetValue(
                    targetTabIndex,
                    out HashSet<string> moviePaths
                )
                || moviePaths == null
            )
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return new HashSet<string>(moviePaths, StringComparer.OrdinalIgnoreCase);
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
            _upperTabRescueTabPresenter?.ReplaceItems(items);

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

                await RefreshUpperTabRescueItemsAsync(target);

                if (IsUpperTabRescueSelected())
                {
                    RefreshUpperTabExtensionDetailFromCurrentSelection(selectFirstItem: true);
                }
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

        // 救済タブの再集計は既存の Error 集計を流用し、一覧差し替えだけをここへ寄せる。
        private async Task RefreshUpperTabRescueItemsAsync(UpperTabRescueTargetOption target)
        {
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
            ReplaceUpperTabRescueItems(BuildUpperTabRescueItems(result.Items, target, context.Movies));

            DebugRuntimeLog.Write(
                "upper-tab-rescue",
                $"rescue tab refresh end: target_tab={target.TabIndex} count={_upperTabRescueItems.Count}"
            );
        }

        // 一覧から送られた動画を救済タブで見失わないよう、対象タブと選択行を揃えて開く。
        private async Task OpenUpperTabRescueForMovieAsync(int targetTabIndex, string moviePath)
        {
            if (!IsUpperThumbnailTabIndex(targetTabIndex))
            {
                return;
            }

            InitializeUpperTabRescueTab();

            UpperTabRescueTargetOption target =
                FindUpperTabRescueTargetOption(targetTabIndex)
                ?? GetSelectedUpperTabRescueTargetOption();
            if (target == null)
            {
                return;
            }

            if (UpperTabRescueViewHost?.TargetTabComboBoxControl != null)
            {
                UpperTabRescueViewHost.TargetTabComboBoxControl.SelectedItem = target;
            }

            await RefreshUpperTabRescueItemsAsync(target);

            SelectUpperTabByFixedIndex(ThumbnailErrorTabIndex);
            SelectUpperTabRescueMovieByPath(moviePath);
            RefreshUpperTabExtensionDetailFromCurrentSelection(selectFirstItem: true);
        }

        // 送った直後に対象行へフォーカスを寄せ、違う動画の履歴を見せないようにする。
        private void SelectUpperTabRescueMovieByPath(string moviePath)
        {
            DataGrid rescueListDataGrid = GetUpperTabRescueDataGrid();
            if (rescueListDataGrid == null)
            {
                return;
            }

            UpperTabRescueListItemViewModel targetItem = _upperTabRescueItems.FirstOrDefault(item =>
                item != null
                && !string.IsNullOrWhiteSpace(item.MoviePath)
                && string.Equals(item.MoviePath, moviePath, StringComparison.OrdinalIgnoreCase)
            );

            rescueListDataGrid.SelectedItems.Clear();

            if (targetItem == null)
            {
                if (rescueListDataGrid.Items.Count > 0)
                {
                    rescueListDataGrid.SelectedIndex = 0;
                }

                return;
            }

            rescueListDataGrid.SelectedItem = targetItem;
            if (rescueListDataGrid.Columns.Count > 0)
            {
                rescueListDataGrid.CurrentCell = new DataGridCellInfo(
                    targetItem,
                    rescueListDataGrid.Columns[0]
                );
            }

            rescueListDataGrid.ScrollIntoView(targetItem);
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
                ShowThumbnailUserActionPopup(
                    "一括通常再試行",
                    "一括通常再試行は既に実行中です。",
                    MessageBoxImage.Warning
                );
                return;
            }

            UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
            UpperTabRescueListItemViewModel[] items = GetDisplayedUpperTabRescueItems();
            if (target == null || items.Length < 1)
            {
                Interlocked.Exchange(ref _upperTabRescueBulkNormalRetryRunning, 0);
                ShowThumbnailUserActionPopup(
                    "一括通常再試行",
                    "対象動画が選択されていません。",
                    MessageBoxImage.Warning
                );
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
                ShowThumbnailUserActionPopup(
                    "一括通常再試行",
                    BuildThumbnailQueueUserActionPopupMessage(
                        "一括通常再試行",
                        items.Length,
                        queuedCount
                    ),
                    queuedCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning
                );
            }
            finally
            {
                Mouse.OverrideCursor = null;
                Interlocked.Exchange(ref _upperTabRescueBulkNormalRetryRunning, 0);
                Refresh();
            }
        }

        // 下段ボタンは、選択行だけを対象タブの通常キューへ単発再投入する。
        private async void UpperTabRescueSelectedNormalRetryButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            if (Interlocked.Exchange(ref _upperTabRescueBulkNormalRetryRunning, 1) == 1)
            {
                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    "rescue tab selected normal retry skipped: already running"
                );
                ShowThumbnailUserActionPopup(
                    "通常サムネ処理",
                    "通常サムネ処理は既に実行中です。",
                    MessageBoxImage.Warning
                );
                return;
            }

            UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
            List<UpperTabRescueListItemViewModel> items = GetSelectedUpperTabRescueItems();
            if (target == null || items.Count < 1)
            {
                Interlocked.Exchange(ref _upperTabRescueBulkNormalRetryRunning, 0);
                ShowThumbnailUserActionPopup(
                    "通常サムネ処理",
                    "対象動画が選択されていません。",
                    MessageBoxImage.Warning
                );
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
                    $"rescue tab selected normal retry end: target_tab={target.TabIndex} selected={items.Count} queued={queuedCount}"
                );
                ShowThumbnailUserActionPopup(
                    "通常サムネ処理",
                    BuildThumbnailQueueUserActionPopupMessage(
                        "通常サムネ処理",
                        items.Count,
                        queuedCount
                    ),
                    queuedCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning
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

        private async void UpperTabRescueSelectedAutogenButton_Click(object sender, RoutedEventArgs e)
        {
            await RunUpperTabRescueSingleEngineExtractAsync("autogen", "autogen");
        }

        private async void UpperTabRescueSelectedFfmpegButton_Click(object sender, RoutedEventArgs e)
        {
            await RunUpperTabRescueSingleEngineExtractAsync("ffmpeg", "ffmpeg1pass");
        }

        private async void UpperTabRescueSelectedFfmediaToolkitButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            await RunUpperTabRescueSingleEngineExtractAsync(
                "ffmediatoolkit",
                "ffmediatoolkit"
            );
        }

        private async void UpperTabRescueSelectedOpenCvButton_Click(object sender, RoutedEventArgs e)
        {
            await RunUpperTabRescueSingleEngineExtractAsync("opencv", "opencv");
        }

        // エンジン名ボタンは、選択行1件へ initialEngineHint を付けて直接サムネ生成する。
        private async Task RunUpperTabRescueSingleEngineExtractAsync(
            string actionLabel,
            string initialEngineHint
        )
        {
            if (Interlocked.Exchange(ref _upperTabRescueSingleEngineRunning, 1) == 1)
            {
                ShowThumbnailUserActionPopup(
                    actionLabel,
                    $"{actionLabel} は既に実行中です。",
                    MessageBoxImage.Warning
                );
                return;
            }

            UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
            MovieRecords selectedMovie = GetSelectedUpperTabRescueMovieRecord();
            if (target == null || selectedMovie == null || string.IsNullOrWhiteSpace(selectedMovie.Movie_Path))
            {
                Interlocked.Exchange(ref _upperTabRescueSingleEngineRunning, 0);
                ShowThumbnailUserActionPopup(
                    actionLabel,
                    "対象動画が選択されていません。",
                    MessageBoxImage.Warning
                );
                return;
            }

            QueueObj queueObj = new()
            {
                MovieId = selectedMovie.Movie_Id,
                MovieFullPath = selectedMovie.Movie_Path,
                Hash = selectedMovie.Hash,
                Tabindex = target.TabIndex,
                Priority = ThumbnailQueuePriority.Preferred,
            };

            string dbName = MainVM?.DbInfo?.DBName ?? "";
            string thumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            string targetThumbOutPath = ResolveThumbnailOutPath(target.TabIndex, dbName, thumbFolder);

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                TryDeleteThumbnailErrorMarker(targetThumbOutPath, queueObj.MovieFullPath);
                await CreateThumbAsync(
                    queueObj,
                    false,
                    default,
                    null,
                    initialEngineHint,
                    disableNormalLaneTimeout: true
                );

                if (target != null)
                {
                    await RefreshUpperTabRescueItemsAsync(target);
                }

                ShowThumbnailUserActionPopup(
                    actionLabel,
                    $"{actionLabel} で1件処理しました。",
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                string message = ResolveUpperTabRescueSingleEngineFailureMessage(ex);
                DebugRuntimeLog.Write(
                    "upper-tab-rescue",
                    $"single engine extract failed: engine={initialEngineHint} movie='{queueObj.MovieFullPath}' reason='{message}'"
                );
                ShowThumbnailUserActionPopup(actionLabel, message, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                Interlocked.Exchange(ref _upperTabRescueSingleEngineRunning, 0);
            }
        }

        private static string ResolveUpperTabRescueSingleEngineFailureMessage(Exception ex)
        {
            string rawReason = ex switch
            {
                ThumbnailCreateFailureException failureEx
                    when !string.IsNullOrWhiteSpace(failureEx.FailureReason) =>
                    failureEx.FailureReason,
                _ => ex?.Message ?? "",
            };

            if (ex is TimeoutException)
            {
                return "処理が時間内に完了しませんでした。";
            }

            return string.IsNullOrWhiteSpace(rawReason)
                ? "サムネイル処理に失敗しました。"
                : rawReason;
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
                ShowThumbnailUserActionPopup(
                    useLiteMode ? "簡易黒背景対策" : "徹底黒背景対策",
                    "同じ黒背景対策が既に実行中です。",
                    MessageBoxImage.Warning
                );
                return;
            }

            UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
            UpperTabRescueListItemViewModel[] items = GetDisplayedUpperTabRescueItems();
            if (target == null || items.Length < 1)
            {
                Interlocked.Exchange(ref _upperTabRescueBulkBlackRetryRunning, 0);
                ShowThumbnailUserActionPopup(
                    useLiteMode ? "簡易黒背景対策" : "徹底黒背景対策",
                    "対象動画が選択されていません。",
                    MessageBoxImage.Warning
                );
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
                ShowThumbnailUserActionPopup(
                    useLiteMode ? "簡易黒背景対策" : "徹底黒背景対策",
                    BuildThumbnailQueueUserActionPopupMessage(
                        useLiteMode ? "簡易黒背景対策" : "徹底黒背景対策",
                        items.Length,
                        queuedCount
                    ),
                    queuedCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning
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
                ShowThumbnailUserActionPopup(
                    useLiteMode ? "簡易黒背景対策" : "徹底黒背景対策",
                    "同じ黒背景対策が既に実行中です。",
                    MessageBoxImage.Warning
                );
                return;
            }

            UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
            List<UpperTabRescueListItemViewModel> items = GetSelectedUpperTabRescueItems();
            if (target == null || items.Count < 1)
            {
                Interlocked.Exchange(ref _upperTabRescueBulkBlackRetryRunning, 0);
                ShowThumbnailUserActionPopup(
                    useLiteMode ? "簡易黒背景対策" : "徹底黒背景対策",
                    "対象動画が選択されていません。",
                    MessageBoxImage.Warning
                );
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
                ShowThumbnailUserActionPopup(
                    useLiteMode ? "簡易黒背景対策" : "徹底黒背景対策",
                    BuildThumbnailQueueUserActionPopupMessage(
                        useLiteMode ? "簡易黒背景対策" : "徹底黒背景対策",
                        items.Count,
                        queuedCount
                    ),
                    queuedCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning
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
                ShowThumbnailUserActionPopup(
                    "黒確定",
                    "黒確定は既に実行中です。",
                    MessageBoxImage.Warning
                );
                return;
            }

            UpperTabRescueTargetOption target = GetSelectedUpperTabRescueTargetOption();
            List<UpperTabRescueListItemViewModel> items = GetSelectedUpperTabRescueItems();
            if (target == null || items.Count < 1)
            {
                Interlocked.Exchange(ref _upperTabRescueBlackConfirmRunning, 0);
                ShowThumbnailUserActionPopup(
                    "黒確定",
                    "対象動画が選択されていません。",
                    MessageBoxImage.Warning
                );
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
                ShowThumbnailUserActionPopup(
                    "黒確定",
                    BuildThumbnailBlackConfirmUserActionPopupMessage(items.Count, results.Count),
                    results.Count > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning
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
                ShowThumbnailUserActionPopup(
                    "インデックス再構築",
                    "インデックス再構築は既に実行中です。",
                    MessageBoxImage.Warning
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
                    ShowThumbnailUserActionPopup(
                        "インデックス再構築",
                        "対象動画が選択されていません。",
                        MessageBoxImage.Warning
                    );
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
                    List<MovieRecords> rescueRecords = NormalizeThumbnailUserActionMovieRecords(
                        items.Select(item => item?.MovieRecord)
                    );
                    ResolvePreferredThumbnailTabIndex();
                    ThumbnailDirectIndexRepairDispatchResult dispatchResult = await Task.Run(
                        () =>
                            DispatchThumbnailDirectIndexRepairUserAction(
                                rescueRecords,
                                target.TabIndex,
                                "upper-tab-selected-index-repair"
                            )
                    );

                    DebugRuntimeLog.Write(
                        "upper-tab-rescue",
                        $"rescue tab selected index repair end: target_tab={target.TabIndex} selected={dispatchResult.SelectedCount} started={dispatchResult.StartedCount} busy={dispatchResult.BusyCount} unsupported={dispatchResult.UnsupportedCount}"
                    );
                    ShowThumbnailUserActionPopup(
                        "インデックス再構築",
                        BuildThumbnailIndexRepairUserActionPopupMessage(
                            dispatchResult.SelectedCount,
                            dispatchResult.StartedCount,
                            dispatchResult.BusyCount,
                            dispatchResult.UnsupportedCount
                        ),
                        dispatchResult.StartedCount > 0
                            ? MessageBoxImage.Information
                            : MessageBoxImage.Warning
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
