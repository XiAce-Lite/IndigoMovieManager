using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using IndigoMovieManager.ViewModels;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 仮表示は無制限に積まず、最新100件までを保持する。
        private const int PendingMovieUiKeepLimit = 100;

        // watch 本体から UI スレッド境界を隠し、Dispatcher 依存をこの partial に閉じ込める。
        private async Task TryAppendMovieToViewByPathAsync(
            string snapshotDbFullPath,
            string moviePath
        )
        {
            if (
                string.IsNullOrWhiteSpace(snapshotDbFullPath)
                || string.IsNullOrWhiteSpace(moviePath)
            )
            {
                return;
            }

            DataRow targetRow = await Task.Run(() =>
            {
                string escapedMoviePath = moviePath.Replace("'", "''");
                DataTable dt = GetData(
                    snapshotDbFullPath,
                    $"select * from movie where lower(movie_path) = lower('{escapedMoviePath}') order by movie_id desc limit 1"
                );
                return dt?.Rows.Count > 0 ? dt.Rows[0] : null;
            });

            if (targetRow == null)
            {
                return;
            }

            await Dispatcher.InvokeAsync(
                () => DataRowToViewData(targetRow),
                DispatcherPriority.Background
            );
        }

        // 現在の画面ソースに載っている動画パスを、走査側で安全に参照できるようスナップショット化する。
        private async Task<HashSet<string>> BuildCurrentViewMoviePathLookupAsync()
        {
            return await Dispatcher.InvokeAsync(
                () => BuildMoviePathLookup(MainVM?.MovieRecs?.Select(x => x.Movie_Path)),
                DispatcherPriority.Background
            );
        }

        // 現在の一覧表示ソースと検索条件をまとめて取り、再描画が必要かを判定できるようにする。
        private async Task<(HashSet<string> DisplayedMoviePaths, string SearchKeyword)> BuildCurrentDisplayedMovieStateAsync()
        {
            return await Dispatcher.InvokeAsync(
                () =>
                {
                    HashSet<string> displayedMoviePaths = BuildMoviePathLookup(
                        MainVM?.FilteredMovieRecs?.Select(x => x.Movie_Path)
                    );
                    string currentSearchKeyword = MainVM?.DbInfo?.SearchKeyword ?? "";
                    return (displayedMoviePaths, currentSearchKeyword);
                },
                DispatcherPriority.Background
            );
        }

        // viewport で実際に見えている動画だけを取り、watch の高負荷時ガードに使う。
        private async Task<HashSet<string>> BuildCurrentVisibleMoviePathLookupAsync()
        {
            return await Dispatcher.InvokeAsync(
                () =>
                {
                    HashSet<string> visibleMoviePaths = new(StringComparer.OrdinalIgnoreCase);
                    if (
                        !TryGetCurrentUpperTabFixedIndex(out int currentTabIndex)
                        || !IsStandardUpperTabFixedIndex(currentTabIndex)
                        || !_activeUpperTabVisibleRange.HasVisibleItems
                        || MainVM?.FilteredMovieRecs == null
                    )
                    {
                        return visibleMoviePaths;
                    }

                    int totalCount = MainVM.FilteredMovieRecs.Count;
                    int firstVisibleIndex = Math.Max(0, _activeUpperTabVisibleRange.FirstVisibleIndex);
                    int lastVisibleIndex = Math.Min(
                        totalCount - 1,
                        _activeUpperTabVisibleRange.LastVisibleIndex
                    );
                    if (lastVisibleIndex < firstVisibleIndex)
                    {
                        return visibleMoviePaths;
                    }

                    for (int index = firstVisibleIndex; index <= lastVisibleIndex; index++)
                    {
                        string moviePath = MainVM.FilteredMovieRecs[index]?.Movie_Path ?? "";
                        if (!string.IsNullOrWhiteSpace(moviePath))
                        {
                            visibleMoviePaths.Add(moviePath);
                        }
                    }

                    return visibleMoviePaths;
                },
                DispatcherPriority.Background
            );
        }

        // MainDB反映前の動画を「登録待ち」としてUIへ一時表示する。
        private void AddOrUpdatePendingMoviePlaceholder(
            string moviePath,
            string fileBody,
            int tabIndex,
            PendingMoviePlaceholderStatus status,
            string lastError = ""
        )
        {
            if (string.IsNullOrWhiteSpace(moviePath) || MainVM?.PendingMovieRecs == null)
            {
                return;
            }

            string safeFileBody = string.IsNullOrWhiteSpace(fileBody)
                ? (Path.GetFileNameWithoutExtension(moviePath) ?? "")
                : fileBody;

            _ = Dispatcher.InvokeAsync(
                () =>
                {
                    PendingMoviePlaceholder item = MainVM.PendingMovieRecs
                        .FirstOrDefault(x =>
                            string.Equals(x.MoviePath, moviePath, StringComparison.OrdinalIgnoreCase)
                        );

                    if (item == null)
                    {
                        item = new PendingMoviePlaceholder
                        {
                            MoviePath = moviePath,
                            DetectedAtLocal = DateTime.Now,
                        };
                        MainVM.PendingMovieRecs.Add(item);
                    }

                    item.FileBody = safeFileBody;
                    item.TabIndex = tabIndex;
                    item.Status = status;
                    item.LastError = lastError ?? "";
                    item.UpdatedAtLocal = DateTime.Now;

                    while (MainVM.PendingMovieRecs.Count > PendingMovieUiKeepLimit)
                    {
                        MainVM.PendingMovieRecs.RemoveAt(0);
                    }
                },
                DispatcherPriority.Background
            );
        }

        // DB反映が終わった動画は仮表示から取り除く。
        private void RemovePendingMoviePlaceholder(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath) || MainVM?.PendingMovieRecs == null)
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(
                () =>
                {
                    PendingMoviePlaceholder item = MainVM.PendingMovieRecs
                        .FirstOrDefault(x =>
                            string.Equals(x.MoviePath, moviePath, StringComparison.OrdinalIgnoreCase)
                        );
                    if (item != null)
                    {
                        MainVM.PendingMovieRecs.Remove(item);
                    }
                },
                DispatcherPriority.Background
            );
        }

        // 例外で走査が中断したフォルダ分の仮表示をクリアして残留を防ぐ。
        private void ClearPendingMoviePlaceholdersByFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || MainVM?.PendingMovieRecs == null)
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(
                () =>
                {
                    List<PendingMoviePlaceholder> targets = MainVM.PendingMovieRecs
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x.MoviePath)
                            && x.MoviePath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase)
                        )
                        .ToList();
                    foreach (PendingMoviePlaceholder target in targets)
                    {
                        MainVM.PendingMovieRecs.Remove(target);
                    }
                },
                DispatcherPriority.Background
            );
        }
    }
}
