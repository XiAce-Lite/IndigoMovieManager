using System.Collections.Generic;
using System.Diagnostics;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 選択変更本体へ渡す最小コンテキストだけを束ね、処理の流れを揃える。
        private readonly record struct UpperTabSelectionChangeContext(
            int TabIndex,
            Stopwatch SelectionStopwatch
        );

        // 選択レコードに応じて詳細ペインを更新し、呼び出し側でも結果を使えるように返す。
        private MovieRecords ApplyUpperTabExtensionDetail(MovieRecords selectedMovie)
        {
            if (selectedMovie == null)
            {
                HideExtensionDetail();
                return null;
            }

            ShowExtensionDetail(selectedMovie);
            return selectedMovie;
        }

        // 現在タブの選択状態から詳細ペインを更新し、必要なら先頭選択もここで揃える。
        private MovieRecords RefreshUpperTabExtensionDetailFromCurrentSelection(
            bool selectFirstItem = false
        )
        {
            if (
                !TryResolveUpperTabExtensionDetailSelection(
                    selectFirstItem,
                    out MovieRecords selectedMovie
                )
            )
            {
                return ApplyUpperTabExtensionDetail(null);
            }

            return ApplyUpperTabExtensionDetail(selectedMovie);
        }

        // 詳細ペイン同期に使う現在選択を解決し、必要なら先頭選択もここで揃える。
        private bool TryResolveUpperTabExtensionDetailSelection(
            bool selectFirstItem,
            out MovieRecords selectedMovie
        )
        {
            if (selectFirstItem)
            {
                SelectFirstItem();
            }

            selectedMovie = GetSelectedItemByTabIndex();
            return selectedMovie != null;
        }

        // 現在の上側タブIDから、選択中 1 件のレコード取得先を 1 か所に集約する。
        private MovieRecords ResolveSelectedUpperTabMovieRecord(int tabIndex)
        {
            if (
                TryResolveSelectedUpperTabSpecialMovieRecord(
                    tabIndex,
                    out MovieRecords specialSelectedMovie
                )
            )
            {
                return specialSelectedMovie;
            }

            return ResolveSelectedUpperTabStandardMovieRecord(tabIndex);
        }

        // 特殊タブの単一選択だけを先に解決し、通常タブ側の分岐を薄くする。
        private bool TryResolveSelectedUpperTabSpecialMovieRecord(
            int tabIndex,
            out MovieRecords selectedMovie
        )
        {
            selectedMovie = tabIndex switch
            {
                ThumbnailErrorTabIndex => GetSelectedUpperTabRescueMovieRecord(),
                DuplicateVideoTabIndex => GetSelectedUpperTabDuplicateMovieRecord(),
                _ => null,
            };
            return tabIndex is ThumbnailErrorTabIndex or DuplicateVideoTabIndex;
        }

        // 通常タブの単一選択解決を 1 か所へ寄せ、入口では流れだけ追えるようにする。
        private MovieRecords ResolveSelectedUpperTabStandardMovieRecord(int tabIndex)
        {
            return tabIndex switch
            {
                UpperTabSmallFixedIndex => GetSelectedUpperTabSmallMovieRecord(),
                UpperTabBigFixedIndex => GetSelectedUpperTabBigMovieRecord(),
                UpperTabGridFixedIndex => GetSelectedUpperTabGridMovieRecord(),
                UpperTabListFixedIndex => GetSelectedUpperTabListMovieRecord(),
                UpperTabBig10FixedIndex => GetSelectedUpperTabBig10MovieRecord(),
                _ => null,
            };
        }

        // 現在の上側タブIDから、複数選択のレコード取得先を 1 か所に集約する。
        private List<MovieRecords> ResolveSelectedUpperTabMovieRecords(int tabIndex)
        {
            if (
                TryResolveSelectedUpperTabSpecialMovieRecords(
                    tabIndex,
                    out List<MovieRecords> specialSelectedMovies
                )
            )
            {
                return specialSelectedMovies;
            }

            return ResolveSelectedUpperTabStandardMovieRecords(tabIndex);
        }

        // 特殊タブの複数選択だけを先に解決し、通常タブ側の分岐を薄くする。
        private bool TryResolveSelectedUpperTabSpecialMovieRecords(
            int tabIndex,
            out List<MovieRecords> selectedMovies
        )
        {
            selectedMovies = tabIndex switch
            {
                ThumbnailErrorTabIndex => [.. GetSelectedUpperTabRescueMovieRecords()],
                DuplicateVideoTabIndex => [.. GetSelectedUpperTabDuplicateMovieRecords()],
                _ => null,
            };
            return tabIndex is ThumbnailErrorTabIndex or DuplicateVideoTabIndex;
        }

        // 通常タブの複数選択解決を 1 か所へ寄せ、入口では流れだけ追えるようにする。
        private List<MovieRecords> ResolveSelectedUpperTabStandardMovieRecords(int tabIndex)
        {
            return tabIndex switch
            {
                UpperTabSmallFixedIndex => [.. GetSelectedUpperTabSmallMovieRecords()],
                UpperTabBigFixedIndex => [.. GetSelectedUpperTabBigMovieRecords()],
                UpperTabGridFixedIndex => [.. GetSelectedUpperTabGridMovieRecords()],
                UpperTabListFixedIndex => [.. GetSelectedUpperTabListMovieRecords()],
                UpperTabBig10FixedIndex => [.. GetSelectedUpperTabBig10MovieRecords()],
                _ => null,
            };
        }

        // 現在の上側通常タブへ、指定レコードの選択だけを返す。
        private void SelectUpperTabMovieRecord(int tabIndex, MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            if (TryHandleUpperTabSpecialMovieRecordSelection(tabIndex, record))
            {
                return;
            }

            SelectUpperTabStandardMovieRecord(tabIndex, record);
        }

        // 特殊タブの直接選択反映は現時点では未対応なので、何もしないことをここで明示する。
        private bool TryHandleUpperTabSpecialMovieRecordSelection(int tabIndex, MovieRecords record)
        {
            _ = record;
            switch (tabIndex)
            {
                case ThumbnailErrorTabIndex:
                case DuplicateVideoTabIndex:
                    return true;
                default:
                    return false;
            }
        }

        // 通常タブの選択反映を 1 か所へ寄せ、入口では流れだけ追えるようにする。
        private void SelectUpperTabStandardMovieRecord(int tabIndex, MovieRecords record)
        {
            switch (tabIndex)
            {
                case UpperTabSmallFixedIndex:
                    SelectUpperTabSmallMovieRecord(record);
                    break;
                case UpperTabBigFixedIndex:
                    SelectUpperTabBigMovieRecord(record);
                    break;
                case UpperTabGridFixedIndex:
                    SelectUpperTabGridMovieRecord(record);
                    break;
                case UpperTabListFixedIndex:
                    SelectUpperTabListMovieRecord(record);
                    break;
                case UpperTabBig10FixedIndex:
                    SelectUpperTabBig10MovieRecord(record);
                    break;
            }
        }

        // 現在の上側タブIDに応じて、既定選択の入口だけを 1 か所へ揃える。
        private void SelectUpperTabDefaultView(int tabIndex)
        {
            if (TrySelectUpperTabSpecialDefaultView(tabIndex))
            {
                return;
            }

            SelectUpperTabStandardDefaultView(tabIndex);
        }

        // 特殊タブの既定選択だけを先に処理し、通常タブ側の分岐を薄くする。
        private bool TrySelectUpperTabSpecialDefaultView(int tabIndex)
        {
            switch (tabIndex)
            {
                case ThumbnailErrorTabIndex:
                    SelectUpperTabRescueAsDefaultView();
                    return true;
                case DuplicateVideoTabIndex:
                    SelectUpperTabDuplicateVideosAsDefaultView();
                    return true;
                default:
                    return false;
            }
        }

        // 通常タブの既定選択を 1 か所へ寄せ、入口では流れだけ追えるようにする。
        private void SelectUpperTabStandardDefaultView(int tabIndex)
        {
            switch (tabIndex)
            {
                case UpperTabSmallFixedIndex:
                    SelectUpperTabSmallAsDefaultView();
                    break;
                case UpperTabBigFixedIndex:
                    SelectUpperTabBigAsDefaultView();
                    break;
                case UpperTabGridFixedIndex:
                    SelectUpperTabGridAsDefaultView();
                    break;
                case UpperTabListFixedIndex:
                    SelectUpperTabListAsDefaultView();
                    break;
                case UpperTabBig10FixedIndex:
                    SelectUpperTabBig10View();
                    break;
                default:
                    SelectUpperTabGridAsDefaultView();
                    break;
            }
        }

        // タブ切替時に Queue/visible-range 側へ通知する有効タブIDを 1 か所で決める。
        private int ResolveEffectiveUpperTabQueueTabIndex(int tabIndex)
        {
            return tabIndex == DuplicateVideoTabIndex ? UpperTabGridFixedIndex : tabIndex;
        }

        // タブ切替の前処理を 1 か所へ寄せ、切替本体は分岐だけに集中させる。
        private void PrepareUpperTabSelectionChange(int tabIndex)
        {
            int effectiveQueueTabIndex = ResolveEffectiveUpperTabQueueTabIndex(tabIndex);
            MainVM.DbInfo.CurrentTabIndex = effectiveQueueTabIndex;
            TryDeletePendingUpperTabJobsForUnselectedTabs(effectiveQueueTabIndex);
            RequestUpperTabVisibleRangeRefresh(reason: "tab-changed");
        }

        // タブ切替イベント本体を共通化し、受け口側は UI イベント判定だけに寄せる。
        private void HandleUpperTabSelectionChangedCore()
        {
            Stopwatch selectionStopwatch = Stopwatch.StartNew();

            // 一覧タブ切替では起動後累計や作業パネルを落とさず、再投入デバウンスだけ解く。
            ClearThumbnailQueue(ThumbnailQueueClearScope.DebounceOnly);

            if (
                !TryResolveUpperTabSelectionChangeContext(
                    selectionStopwatch,
                    out UpperTabSelectionChangeContext selectionChangeContext
                )
            )
            {
                return;
            }

            DispatchUpperTabSelectionChanged(selectionChangeContext);
        }

        // タブ切替前半で必要な context 解決と前処理を 1 か所へ寄せる。
        private bool TryResolveUpperTabSelectionChangeContext(
            Stopwatch selectionStopwatch,
            out UpperTabSelectionChangeContext selectionChangeContext
        )
        {
            selectionChangeContext = default;
            if (!TryGetCurrentUpperTabFixedIndex(out int tabIndex))
            {
                return false;
            }

            PrepareUpperTabSelectionChange(tabIndex);
            selectionChangeContext = new UpperTabSelectionChangeContext(
                tabIndex,
                selectionStopwatch
            );
            return true;
        }

        // 特殊タブと通常タブの振り分けを 1 か所へ寄せ、入口では流れだけを追えるようにする。
        private void DispatchUpperTabSelectionChanged(
            UpperTabSelectionChangeContext selectionChangeContext
        )
        {
            if (selectionChangeContext.TabIndex == ThumbnailErrorTabIndex)
            {
                HandleUpperTabRescueSelectionChanged(
                    selectionChangeContext.SelectionStopwatch,
                    selectionChangeContext.TabIndex
                );
                return;
            }

            if (selectionChangeContext.TabIndex == DuplicateVideoTabIndex)
            {
                HandleUpperTabDuplicateVideosSelectionChanged(
                    selectionChangeContext.SelectionStopwatch,
                    selectionChangeContext.TabIndex
                );
                return;
            }

            HandleStandardUpperTabSelectionChanged(selectionChangeContext);
        }

        // 通常タブ切替後の「先頭選択 → 詳細更新 → visible 範囲更新」を 1 か所へ寄せる。
        private void HandleStandardUpperTabSelectionChanged(
            UpperTabSelectionChangeContext selectionChangeContext
        )
        {
            int tabIndex = selectionChangeContext.TabIndex;
            Stopwatch selectionStopwatch = selectionChangeContext.SelectionStopwatch;
            if (MainVM.FilteredMovieRecs.Count == 0)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change skip: tab={tabIndex} reason=no_filtered_items total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
                ClearUpperTabVisibleRange();
                return;
            }

            // 将来の error 再投入復帰に備えて、ログ項目だけは残しておく。
            int queuedErrorCount = 0;
            MovieRecords selectedMovie = RefreshUpperTabExtensionDetailFromCurrentSelection(
                selectFirstItem: true
            );
            FinalizeStandardUpperTabSelectionChanged(
                selectionChangeContext,
                selectedMovie,
                queuedErrorCount
            );
        }

        // 通常タブ切替後半の visible 更新とログ出力を 1 か所へ寄せる。
        private void FinalizeStandardUpperTabSelectionChanged(
            UpperTabSelectionChangeContext selectionChangeContext,
            MovieRecords selectedMovie,
            int queuedErrorCount
        )
        {
            if (selectedMovie == null)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change end: tab={selectionChangeContext.TabIndex} selected=none queued_error={queuedErrorCount} total_ms={selectionChangeContext.SelectionStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            RequestUpperTabVisibleRangeRefresh(reason: "tab-selected");
            selectionChangeContext.SelectionStopwatch.Stop();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"tab change end: tab={selectionChangeContext.TabIndex} selected='{selectedMovie.Movie_Name}' queued_error={queuedErrorCount} total_ms={selectionChangeContext.SelectionStopwatch.ElapsedMilliseconds}"
            );
        }
    }
}
