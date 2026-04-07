using System.Collections.Generic;
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
            SelectUpperTabDefaultView(GetCurrentUpperTabFixedIndex());
        }

        // タブ切替時に不足サムネイルを検出し、必要な再作成キューを積む。
        private async void Tabs_SelectionChangedAsync(object sender, SelectionChangedEventArgs e)
        {
            if (sender as TabControl == null || e.OriginalSource is not TabControl)
            {
                return;
            }

            HandleUpperTabSelectionChangedCore();
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
                !IsStandardUpperTabFixedIndex(tabIndex)
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
            return ResolveSelectedUpperTabMovieRecord(GetCurrentUpperTabFixedIndex());
        }

        // 現在タブから複数選択中のレコード一覧を取得する。
        private List<MovieRecords> GetSelectedItemsByTabIndex()
        {
            return ResolveSelectedUpperTabMovieRecords(GetCurrentUpperTabFixedIndex());
        }

        // ラベルクリック時は、現在前面にいる通常タブへだけ選択を返す。
        private void SelectCurrentUpperTabMovieRecord(MovieRecords record)
        {
            SelectUpperTabMovieRecord(GetCurrentUpperTabFixedIndex(), record);
        }
    }
}
