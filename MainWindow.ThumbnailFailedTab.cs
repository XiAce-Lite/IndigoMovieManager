using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using IndigoMovieManager.ModelViews;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int ThumbnailErrorTabIndex = 5;
        private static readonly int[] ThumbnailErrorTargetTabIndices = [0, 1, 2, 3, 4, 99];

        // ERROR マーカーの実在を見て、候補一覧を組み直す。
        private void RefreshThumbnailErrorRecords()
        {
            DebugRuntimeLog.Write("thumbnail-rescue", "error tab refresh start");

            var items = MainVM
                .MovieRecs.Select(BuildThumbnailErrorRecord)
                .Where(x => x != null)
                .OrderByDescending(x => x.LastMarkerWriteTime ?? DateTime.MinValue)
                .ThenBy(x => x.MovieName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            MainVM.ReplaceThumbnailErrorRecs(items);

            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"error tab refresh end: count={items.Length}"
            );
        }

        // 1 動画ぶんの ERROR 状態を 1 行へ集約する。
        private ThumbnailErrorRecordViewModel BuildThumbnailErrorRecord(MovieRecords movie)
        {
            if (movie == null || string.IsNullOrWhiteSpace(movie.Movie_Path))
            {
                return null;
            }

            List<int> failedTabs = [];
            DateTime? lastWriteTime = null;

            foreach (int tabIndex in ThumbnailErrorTargetTabIndices)
            {
                if (!TryGetExistingThumbnailErrorMarkerPath(movie, tabIndex, out string markerPath))
                {
                    continue;
                }

                failedTabs.Add(tabIndex);
                DateTime markerWriteTime = File.GetLastWriteTime(markerPath);
                if (!lastWriteTime.HasValue || markerWriteTime > lastWriteTime.Value)
                {
                    lastWriteTime = markerWriteTime;
                }
            }

            if (failedTabs.Count == 0)
            {
                return null;
            }

            return new ThumbnailErrorRecordViewModel
            {
                MovieRecord = movie,
                MovieId = movie.Movie_Id,
                MovieName = movie.Movie_Name ?? "",
                MoviePath = movie.Movie_Path ?? "",
                FailedTabsText = string.Join(
                    ", ",
                    failedTabs.Select(GetThumbnailTabDisplayName)
                ),
                MarkerCount = failedTabs.Count,
                LastMarkerWriteTime = lastWriteTime,
                LastMarkerWriteTimeText = lastWriteTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                FailedTabIndices = failedTabs.ToArray(),
            };
        }

        // 旧命名が残る環境も考慮して path 名と movie 名の両方で marker を探す。
        private bool TryGetExistingThumbnailErrorMarkerPath(
            MovieRecords movie,
            int tabIndex,
            out string markerPath
        )
        {
            markerPath = null;

            if (movie == null)
            {
                return false;
            }

            TabInfo tabInfo = new(tabIndex, MainVM?.DbInfo?.DBName ?? "", MainVM?.DbInfo?.ThumbFolder ?? "");

            string primaryMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                tabInfo.OutPath,
                movie.Movie_Path
            );
            if (Path.Exists(primaryMarkerPath))
            {
                markerPath = primaryMarkerPath;
                return true;
            }

            string fallbackName = movie.Movie_Name ?? movie.Movie_Body ?? "";
            string fallbackMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                tabInfo.OutPath,
                fallbackName
            );
            if (Path.Exists(fallbackMarkerPath))
            {
                markerPath = fallbackMarkerPath;
                return true;
            }

            return false;
        }

        // UI 表示名は今のタブ表記に揃える。
        private static string GetThumbnailTabDisplayName(int tabIndex)
        {
            return tabIndex switch
            {
                0 => "Small",
                1 => "Big",
                2 => "Grid",
                3 => "List",
                4 => "5x2",
                99 => "詳細",
                _ => $"Tab{tabIndex}",
            };
        }

        // ERROR タブで選択中の行を元動画へ戻して扱う。
        private List<ThumbnailErrorRecordViewModel> GetSelectedThumbnailErrorRecords()
        {
            List<ThumbnailErrorRecordViewModel> items = [];
            foreach (var selectedItem in ErrorListDataGrid.SelectedItems)
            {
                if (selectedItem is ThumbnailErrorRecordViewModel record)
                {
                    items.Add(record);
                }
            }

            if (items.Count == 0 && ErrorListDataGrid.SelectedItem is ThumbnailErrorRecordViewModel single)
            {
                items.Add(single);
            }

            return items;
        }

        // 一括でも選択でも、最後は既存 rescue レーンへ流すだけにする。
        private int EnqueueThumbnailErrorRecordsToRescue(
            IEnumerable<ThumbnailErrorRecordViewModel> records,
            string reason
        )
        {
            if (records == null)
            {
                return 0;
            }

            int movieCount = 0;
            int queuedCount = 0;

            foreach (var record in records.Where(x => x != null))
            {
                movieCount++;

                foreach (int tabIndex in record.FailedTabIndices ?? [])
                {
                    QueueObj queueObj = new()
                    {
                        MovieId = record.MovieId,
                        MovieFullPath = record.MoviePath,
                        Hash = record.MovieRecord?.Hash ?? "",
                        Tabindex = tabIndex,
                    };

                    if (
                        TryEnqueueThumbnailDisplayErrorRescueJob(
                            queueObj,
                            reason: $"{reason}:{GetThumbnailTabDisplayName(tabIndex)}"
                        )
                    )
                    {
                        queuedCount++;
                    }
                }
            }

            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"error tab rescue enqueue end: reason={reason} movie_count={movieCount} queued={queuedCount}"
            );

            RefreshThumbnailErrorRecords();
            return queuedCount;
        }

        // ERROR タブの再読込は source 再取得ではなく marker の再走査だけに留める。
        private void ReloadThumbnailErrorListButton_Click(object sender, RoutedEventArgs e)
        {
            DebugRuntimeLog.Write("thumbnail-rescue", "error tab reload clicked");
            RefreshThumbnailErrorRecords();
            SelectFirstItem();
            Refresh();
        }

        private void RescueSelectedThumbnailErrorsButton_Click(object sender, RoutedEventArgs e)
        {
            int selectedCount = GetSelectedThumbnailErrorRecords().Count;
            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"error tab selected rescue clicked: selected={selectedCount}"
            );
            _ = EnqueueThumbnailErrorRecordsToRescue(
                GetSelectedThumbnailErrorRecords(),
                reason: "error-tab-selected"
            );
            SelectFirstItem();
            Refresh();
        }

        private void RescueAllThumbnailErrorsButton_Click(object sender, RoutedEventArgs e)
        {
            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"error tab all rescue clicked: visible={MainVM.ThumbnailErrorRecs.Count}"
            );
            _ = EnqueueThumbnailErrorRecordsToRescue(
                MainVM.ThumbnailErrorRecs.ToArray(),
                reason: "error-tab-all"
            );
            SelectFirstItem();
            Refresh();
        }
    }
}
