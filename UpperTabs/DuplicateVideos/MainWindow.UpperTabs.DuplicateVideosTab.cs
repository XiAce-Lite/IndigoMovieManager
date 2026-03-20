using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IndigoMovieManager.Converter;
using IndigoMovieManager.Data;
using IndigoMovieManager.UpperTabs.DuplicateVideos;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int DuplicateVideoTabIndex = 6;
        private static readonly FileSizeConverter DuplicateVideoFileSizeConverter = new();
        private readonly ObservableCollection<UpperTabDuplicateGroupViewModel> _upperTabDuplicateGroups =
            [];
        private readonly ObservableCollection<UpperTabDuplicateItemViewModel> _upperTabDuplicateItems =
            [];
        private readonly ObservableCollection<UpperTabDuplicateGroupSortOption> _upperTabDuplicateGroupSortOptions =
            [];
        private readonly IUpperTabDuplicateVideoReadService _upperTabDuplicateVideoReadService =
            new UpperTabDuplicateVideoReadService();
        private readonly IMainDbMovieMutationFacade _upperTabDuplicateMovieMutationFacade =
            new MainDbMovieMutationFacade();
        private UpperTabDuplicateMovieRecord[] _upperTabDuplicateDetectedRecords = [];
        private UpperTabDuplicateGroupSummary[] _upperTabDuplicateDetectedGroups = [];
        private int _upperTabDuplicateDetectRunning;

        // 重複動画タブのItemsSourceと初期表示を結び、起動直後でも空状態を安定させる。
        private void InitializeUpperTabDuplicateVideosTab()
        {
            if (UpperTabDuplicateVideosViewHost == null)
            {
                return;
            }

            if (_upperTabDuplicateGroupSortOptions.Count == 0)
            {
                _upperTabDuplicateGroupSortOptions.Add(
                    new UpperTabDuplicateGroupSortOption("duplicate-count", "重複数")
                );
                _upperTabDuplicateGroupSortOptions.Add(
                    new UpperTabDuplicateGroupSortOption("max-size", "サイズ(最大)")
                );
            }

            UpperTabDuplicateVideosViewHost.GroupSortComboBoxControl.ItemsSource =
                _upperTabDuplicateGroupSortOptions;
            UpperTabDuplicateVideosViewHost.GroupSortComboBoxControl.DisplayMemberPath = nameof(
                UpperTabDuplicateGroupSortOption.DisplayName
            );
            if (UpperTabDuplicateVideosViewHost.GroupSortComboBoxControl.SelectedItem == null)
            {
                UpperTabDuplicateVideosViewHost.GroupSortComboBoxControl.SelectedItem =
                    _upperTabDuplicateGroupSortOptions[0];
            }
            UpperTabDuplicateVideosViewHost.DuplicateGroupDataGridControl.ItemsSource = _upperTabDuplicateGroups;
            UpperTabDuplicateVideosViewHost.DuplicateDetailDataGridControl.ItemsSource = _upperTabDuplicateItems;
            SetUpperTabDuplicateVideosHeaderSummary(0, 0, "-");
        }

        private DataGrid GetUpperTabDuplicateGroupDataGrid()
        {
            return UpperTabDuplicateVideosViewHost?.DuplicateGroupDataGridControl;
        }

        private DataGrid GetUpperTabDuplicateDetailDataGrid()
        {
            return UpperTabDuplicateVideosViewHost?.DuplicateDetailDataGridControl;
        }

        private UpperTabDuplicateGroupViewModel GetSelectedUpperTabDuplicateGroup()
        {
            return GetUpperTabDuplicateGroupDataGrid()?.SelectedItem
                as UpperTabDuplicateGroupViewModel;
        }

        private MovieRecords GetSelectedUpperTabDuplicateMovieRecord()
        {
            return (GetUpperTabDuplicateDetailDataGrid()?.SelectedItem
                as UpperTabDuplicateItemViewModel)?.MovieRecord;
        }

        private List<MovieRecords> GetSelectedUpperTabDuplicateMovieRecords()
        {
            DataGrid dataGrid = GetUpperTabDuplicateDetailDataGrid();
            if (dataGrid == null)
            {
                return [];
            }

            List<MovieRecords> result = [];
            foreach (object item in dataGrid.SelectedItems)
            {
                if (item is not UpperTabDuplicateItemViewModel vm || vm.MovieRecord == null)
                {
                    continue;
                }

                result.Add(vm.MovieRecord);
            }

            return result;
        }

        // 検出時だけDBを読み、左ペイン一覧を丸ごと作り直す。
        private async void UpperTabDuplicateVideosDetectButton_Click(object sender, RoutedEventArgs e)
        {
            if (Interlocked.Exchange(ref _upperTabDuplicateDetectRunning, 1) == 1)
            {
                return;
            }

            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            string thumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            string imagesDirectoryPath = Path.Combine(AppContext.BaseDirectory, "Images");
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                UpperTabDuplicateMovieRecord[] records = await Task.Run(
                    () => _upperTabDuplicateVideoReadService.ReadDuplicateMovieRecords(dbFullPath)
                );
                _upperTabDuplicateDetectedRecords = records;

                UpperTabDuplicateGroupSummary[] groups = await Task.Run(
                    () => UpperTabDuplicateVideoAnalyzer.BuildGroupSummaries(records)
                );

                ApplyUpperTabDuplicateGroups(groups, dbName, thumbFolder, imagesDirectoryPath);

                DebugRuntimeLog.Write(
                    "upper-tab-duplicate",
                    $"detect end: groups={groups.Length} records={records.Length}"
                );
            }
            finally
            {
                Mouse.OverrideCursor = null;
                Interlocked.Exchange(ref _upperTabDuplicateDetectRunning, 0);
            }
        }

        // 左ペインでhash束を選ぶと、右ペインだけ差し替える。
        private void UpperTabDuplicateVideosGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplySelectedUpperTabDuplicateGroupDetails();
        }

        private void UpperTabDuplicateVideosDetailSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            List_SelectionChanged(sender, e);
        }

        private void UpperTabDuplicateVideosGroupSortSelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            ApplyUpperTabDuplicateGroupSort();
        }

        private void UpperTabDuplicateVideosPlayRequested(object sender, MouseButtonEventArgs e)
        {
            PlayMovie_Click(sender, e);
        }

        // 右ペインの動画名だけは直接編集を許可し、確定時にDBへ反映する。
        private void UpperTabDuplicateVideosDetailCellEditEnding(
            object sender,
            DataGridCellEditEndingEventArgs e
        )
        {
            if (
                e.EditAction != DataGridEditAction.Commit
                || e.Row?.Item is not UpperTabDuplicateItemViewModel item
                || e.EditingElement is not TextBox textBox
            )
            {
                return;
            }

            string newMovieName = (textBox.Text ?? "").Trim();
            string oldMovieName = item.MovieName ?? "";
            if (string.Equals(newMovieName, oldMovieName, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(newMovieName) || item.MovieRecord == null)
            {
                e.Cancel = true;
                return;
            }

            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                e.Cancel = true;
                return;
            }

            _upperTabDuplicateMovieMutationFacade.UpdateMovieName(
                dbFullPath,
                item.MovieRecord.Movie_Id,
                newMovieName
            );

            item.MovieName = newMovieName;
            item.MovieRecord.Movie_Name = newMovieName;
            UpdateUpperTabDuplicateGroupRepresentativeMovieName(item.MovieRecord.Hash, item.MovieRecord.Movie_Id, newMovieName);
            GetUpperTabDuplicateDetailDataGrid()?.Items.Refresh();
            GetUpperTabDuplicateGroupDataGrid()?.Items.Refresh();
        }

        private void ApplyUpperTabDuplicateGroups(
            IEnumerable<UpperTabDuplicateGroupSummary> groups,
            string dbName,
            string thumbFolder,
            string imagesDirectoryPath
        )
        {
            _upperTabDuplicateDetectedGroups = groups?.ToArray() ?? [];
            _upperTabDuplicateItems.Clear();

            string fallbackThumbnailPath = Path.Combine(imagesDirectoryPath, "errorGrid.jpg");
            ApplyUpperTabDuplicateGroupSort(dbName, thumbFolder, fallbackThumbnailPath);

            SetUpperTabDuplicateVideosHeaderSummary(_upperTabDuplicateGroups.Count, 0, "-");

            if (_upperTabDuplicateGroups.Count > 0)
            {
                GetUpperTabDuplicateGroupDataGrid().SelectedIndex = 0;
                ApplySelectedUpperTabDuplicateGroupDetails();
            }
        }

        private void ApplyUpperTabDuplicateGroupSort()
        {
            ApplyUpperTabDuplicateGroupSort(
                MainVM?.DbInfo?.DBName ?? "",
                MainVM?.DbInfo?.ThumbFolder ?? "",
                Path.Combine(AppContext.BaseDirectory, "Images", "errorGrid.jpg")
            );
        }

        private void ApplyUpperTabDuplicateGroupSort(
            string dbName,
            string thumbFolder,
            string fallbackThumbnailPath
        )
        {
            UpperTabDuplicateGroupViewModel currentSelection = GetSelectedUpperTabDuplicateGroup();
            string selectedHash = currentSelection?.Hash ?? "";
            _upperTabDuplicateGroups.Clear();

            IEnumerable<UpperTabDuplicateGroupSummary> sortedGroups =
                SortUpperTabDuplicateGroups(
                    _upperTabDuplicateDetectedGroups,
                    (UpperTabDuplicateVideosViewHost?.GroupSortComboBoxControl.SelectedItem
                        as UpperTabDuplicateGroupSortOption)?.SortKey ?? "duplicate-count"
                );

            foreach (UpperTabDuplicateGroupSummary group in sortedGroups)
            {
                _upperTabDuplicateGroups.Add(
                    new UpperTabDuplicateGroupViewModel
                    {
                        Hash = group.Hash,
                        RepresentativeThumbnailPath = ResolveUpperTabDuplicateThumbnailPath(
                            group.Representative.MoviePath,
                            group.Representative.MovieName,
                            group.Representative.Hash,
                            dbName,
                            thumbFolder,
                            fallbackThumbnailPath
                        ),
                        RepresentativeMovieName = UpperTabDuplicateVideoAnalyzer.BuildDisplayMovieName(
                            group.Representative.MovieName,
                            group.Representative.MoviePath
                        ),
                        DuplicateCount = group.DuplicateCount,
                        MaxMovieSizeText = DuplicateVideoFileSizeConverter.Convert(
                            group.MaxMovieSize,
                            typeof(string),
                            null,
                            System.Globalization.CultureInfo.CurrentCulture
                        )?.ToString() ?? "",
                        MaxMovieSizeValue = group.MaxMovieSize,
                    }
                );
            }

            if (_upperTabDuplicateGroups.Count < 1)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(selectedHash))
            {
                UpperTabDuplicateGroupViewModel reselection = _upperTabDuplicateGroups.FirstOrDefault(
                    x => string.Equals(x.Hash, selectedHash, StringComparison.OrdinalIgnoreCase)
                );
                if (reselection != null)
                {
                    GetUpperTabDuplicateGroupDataGrid().SelectedItem = reselection;
                    return;
                }
            }

            GetUpperTabDuplicateGroupDataGrid().SelectedIndex = 0;
        }

        private void ApplySelectedUpperTabDuplicateGroupDetails()
        {
            UpperTabDuplicateGroupViewModel selectedGroup = GetSelectedUpperTabDuplicateGroup();
            _upperTabDuplicateItems.Clear();

            if (selectedGroup == null)
            {
                SetUpperTabDuplicateVideosHeaderSummary(_upperTabDuplicateGroups.Count, 0, "-");
                HideExtensionDetail();
                return;
            }

            string dbName = MainVM?.DbInfo?.DBName ?? "";
            string thumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            string fallbackThumbnailPath = Path.Combine(AppContext.BaseDirectory, "Images", "errorGrid.jpg");

            UpperTabDuplicateMovieRecord[] items = _upperTabDuplicateDetectedRecords
                .Where(x => string.Equals(x.Hash, selectedGroup.Hash, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.MovieSize)
                .ThenByDescending(x => x.FileDateText, StringComparer.Ordinal)
                .ThenByDescending(x => x.MovieId)
                .ToArray();

            long maxSize = items.Length > 0 ? items.Max(x => x.MovieSize) : 0;
            long minSize = items.Length > 0 ? items.Min(x => x.MovieSize) : 0;
            foreach (UpperTabDuplicateMovieRecord item in items)
            {
                MovieRecords movieRecord = BuildUpperTabDuplicateMovieRecord(
                    item,
                    dbName,
                    thumbFolder,
                    fallbackThumbnailPath
                );
                _upperTabDuplicateItems.Add(
                    new UpperTabDuplicateItemViewModel
                    {
                        MovieRecord = movieRecord,
                        ThumbnailPath = movieRecord.ThumbPathGrid ?? "",
                        MovieName = movieRecord.Movie_Name ?? "",
                        ProbText = UpperTabDuplicateVideoAnalyzer.ExtractProbText(
                            item.MovieName,
                            item.MoviePath
                        ),
                        MovieSizeText = DuplicateVideoFileSizeConverter.Convert(
                            item.MovieSize,
                            typeof(string),
                            null,
                            System.Globalization.CultureInfo.CurrentCulture
                        )?.ToString() ?? "",
                        SizeCompareText = UpperTabDuplicateVideoAnalyzer.BuildSizeCompareText(
                            item.MovieSize,
                            maxSize,
                            minSize
                        ),
                        FileDateText = item.FileDateText,
                        MoviePath = item.MoviePath ?? "",
                    }
                );
            }

            SetUpperTabDuplicateVideosHeaderSummary(
                _upperTabDuplicateGroups.Count,
                _upperTabDuplicateItems.Count,
                selectedGroup.Hash
            );
            if (_upperTabDuplicateItems.Count > 0)
            {
                GetUpperTabDuplicateDetailDataGrid().SelectedIndex = 0;
                ShowExtensionDetail(_upperTabDuplicateItems[0].MovieRecord);
            }
            else
            {
                HideExtensionDetail();
            }
        }

        private void SetUpperTabDuplicateVideosHeaderSummary(int groupCount, int selectedCount, string selectedHash)
        {
            if (UpperTabDuplicateVideosViewHost == null)
            {
                return;
            }

            UpperTabDuplicateVideosViewHost.GroupCountTextBlockControl.Text = groupCount.ToString();
            UpperTabDuplicateVideosViewHost.SelectedCountTextBlockControl.Text = selectedCount.ToString();
            UpperTabDuplicateVideosViewHost.SelectedHashTextBlockControl.Text =
                string.IsNullOrWhiteSpace(selectedHash) ? "-" : selectedHash;
        }

        // 右ペインの操作や詳細表示に使う最小MovieRecordsをその場で組み立てる。
        private MovieRecords BuildUpperTabDuplicateMovieRecord(
            UpperTabDuplicateMovieRecord source,
            string dbName,
            string thumbFolder,
            string fallbackThumbnailPath
        )
        {
            string displayName = UpperTabDuplicateVideoAnalyzer.BuildDisplayMovieName(
                source.MovieName,
                source.MoviePath
            );
            string extension = Path.GetExtension(source.MoviePath ?? "");
            string movieBody = Path.GetFileNameWithoutExtension(source.MoviePath ?? "");
            string gridThumbnailPath = ResolveUpperTabDuplicateThumbnailPath(
                source.MoviePath,
                source.MovieName,
                source.Hash,
                dbName,
                thumbFolder,
                fallbackThumbnailPath
            );

            return new MovieRecords
            {
                Movie_Id = source.MovieId,
                Movie_Name = displayName,
                Movie_Body = movieBody,
                Movie_Path = source.MoviePath ?? "",
                Movie_Length = TimeSpan.FromSeconds(Math.Max(0, source.MovieLengthSeconds)).ToString(
                    @"hh\:mm\:ss"
                ),
                Movie_Size = source.MovieSize,
                File_Date = source.FileDateText ?? "",
                Score = source.Score,
                Hash = source.Hash ?? "",
                ThumbPathGrid = gridThumbnailPath,
                ThumbDetail = gridThumbnailPath,
                Drive = Path.GetPathRoot(source.MoviePath ?? "") ?? "",
                Dir = Path.GetDirectoryName(source.MoviePath ?? "") ?? "",
                IsExists = File.Exists(source.MoviePath ?? ""),
                Ext = extension,
            };
        }

        private string ResolveUpperTabDuplicateThumbnailPath(
            string moviePath,
            string movieName,
            string hash,
            string dbName,
            string thumbFolder,
            string fallbackThumbnailPath
        )
        {
            string outPath = ResolveThumbnailOutPath(UpperTabGridFixedIndex, dbName, thumbFolder);
            string primaryPath = Thumbnail.ThumbnailPathResolver.BuildThumbnailPath(
                outPath,
                moviePath,
                hash
            );
            if (File.Exists(primaryPath))
            {
                return primaryPath;
            }

            if (!string.IsNullOrWhiteSpace(movieName))
            {
                string legacyNamePath = Thumbnail.ThumbnailPathResolver.BuildThumbnailPath(
                    outPath,
                    movieName,
                    hash
                );
                if (File.Exists(legacyNamePath))
                {
                    return legacyNamePath;
                }
            }

            return fallbackThumbnailPath;
        }

        // 重複動画タブの右ペインは独自ViewModelを持つため、通常生成成功時に行画像を差し替える。
        private void TryReflectCreatedThumbnailIntoUpperTabDuplicateItems(
            string moviePath,
            int tabIndex,
            string outputThumbPath
        )
        {
            if (
                tabIndex != UpperTabGridFixedIndex
                || string.IsNullOrWhiteSpace(moviePath)
                || string.IsNullOrWhiteSpace(outputThumbPath)
            )
            {
                return;
            }

            string normalizedMoviePath = moviePath.Trim();
            string normalizedOutputThumbPath = outputThumbPath.Trim();

            for (int i = 0; i < _upperTabDuplicateItems.Count; i++)
            {
                UpperTabDuplicateItemViewModel item = _upperTabDuplicateItems[i];
                if (
                    !string.Equals(
                        item?.MoviePath,
                        normalizedMoviePath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                _upperTabDuplicateItems[i] = new UpperTabDuplicateItemViewModel
                {
                    MovieRecord = item.MovieRecord,
                    ThumbnailPath = normalizedOutputThumbPath,
                    MovieName = item.MovieName,
                    ProbText = item.ProbText,
                    MovieSizeText = item.MovieSizeText,
                    SizeCompareText = item.SizeCompareText,
                    FileDateText = item.FileDateText,
                    MoviePath = item.MoviePath,
                };
            }

            // 左ペイン代表サムネも、いま更新した動画が代表個体なら同じjpgへ差し替える。
            for (int i = 0; i < _upperTabDuplicateGroups.Count; i++)
            {
                UpperTabDuplicateGroupViewModel group = _upperTabDuplicateGroups[i];
                UpperTabDuplicateGroupSummary summary = _upperTabDuplicateDetectedGroups.FirstOrDefault(
                    x => string.Equals(x.Hash, group?.Hash, StringComparison.OrdinalIgnoreCase)
                );
                if (
                    string.IsNullOrWhiteSpace(summary.Hash)
                    || !string.Equals(
                        summary.Representative.MoviePath,
                        normalizedMoviePath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                _upperTabDuplicateGroups[i] = new UpperTabDuplicateGroupViewModel
                {
                    Hash = group.Hash,
                    RepresentativeThumbnailPath = normalizedOutputThumbPath,
                    RepresentativeMovieName = group.RepresentativeMovieName,
                    DuplicateCount = group.DuplicateCount,
                    MaxMovieSizeText = group.MaxMovieSizeText,
                    MaxMovieSizeValue = group.MaxMovieSizeValue,
                };
            }
        }

        private void UpdateUpperTabDuplicateGroupRepresentativeMovieName(
            string hash,
            long movieId,
            string newMovieName
        )
        {
            if (string.IsNullOrWhiteSpace(hash) || movieId <= 0)
            {
                return;
            }

            UpperTabDuplicateGroupSummary? updatedSummary = null;
            for (int i = 0; i < _upperTabDuplicateDetectedGroups.Length; i++)
            {
                if (
                    !string.Equals(
                        _upperTabDuplicateDetectedGroups[i].Hash,
                        hash,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                UpperTabDuplicateMovieRecord representative = _upperTabDuplicateDetectedGroups[i].Representative;
                if (representative.MovieId != movieId)
                {
                    return;
                }

                updatedSummary = _upperTabDuplicateDetectedGroups[i] with
                {
                    Representative = representative with { MovieName = newMovieName }
                };
                _upperTabDuplicateDetectedGroups[i] = updatedSummary.Value;
                break;
            }

            if (updatedSummary == null)
            {
                return;
            }

            UpperTabDuplicateGroupViewModel group = _upperTabDuplicateGroups.FirstOrDefault(
                x => string.Equals(x.Hash, hash, StringComparison.OrdinalIgnoreCase)
            );
            if (group == null)
            {
                return;
            }

            group.RepresentativeMovieName = UpperTabDuplicateVideoAnalyzer.BuildDisplayMovieName(
                newMovieName,
                newMovieName
            );
        }

        private static IEnumerable<UpperTabDuplicateGroupSummary> SortUpperTabDuplicateGroups(
            IEnumerable<UpperTabDuplicateGroupSummary> groups,
            string sortKey
        )
        {
            IEnumerable<UpperTabDuplicateGroupSummary> source = groups ?? [];
            return sortKey switch
            {
                "max-size" => source
                    .OrderByDescending(x => x.MaxMovieSize)
                    .ThenByDescending(x => x.DuplicateCount)
                    .ThenBy(x => x.Hash, StringComparer.OrdinalIgnoreCase),
                _ => source
                    .OrderByDescending(x => x.DuplicateCount)
                    .ThenByDescending(x => x.MaxMovieSize)
                    .ThenBy(x => x.Hash, StringComparer.OrdinalIgnoreCase),
            };
        }
    }

    internal sealed class UpperTabDuplicateGroupSortOption
    {
        public UpperTabDuplicateGroupSortOption(string sortKey, string displayName)
        {
            SortKey = sortKey ?? "";
            DisplayName = displayName ?? "";
        }

        public string SortKey { get; }

        public string DisplayName { get; }
    }
}
