using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using IndigoMovieManager.DB;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.ViewModels
{
    public enum FilteredMovieRecsUpdateMode
    {
        Reset = 0,
        Diff = 1,
        Move = 2,
    }

    public readonly record struct FilteredMovieRecsUpdateResult(
        bool HasChanges,
        int RetainedPrefixCount,
        int RetainedSuffixCount,
        int RemovedCount,
        int InsertedCount,
        int MovedCount
    );

    /// <summary>
    /// メイン画面(MainWindow)のUIとガッツリ連携する、縁の下の力持ちViewModel！💪
    /// DBデータの保持から、TreeViewメニューの構築、一覧画面の爆速検索・ソートロジックまで、裏方の全責任を背負い込む最高にタフなクラスだ！✨
    /// </summary>
    public class MainWindowViewModel
    {
        // アプリ全体の設定情報（DBパスやスキンなど）を持つプロパティ
        public DBInfo DbInfo { get; set; }

        // 画面左側のTreeViewに表示する各項目のルートコレクション群
        public ObservableCollection<TreeSource> RecentTreeRoot { get; set; }
        public ObservableCollection<TreeSource> ConfigTreeRoot { get; set; }
        public ObservableCollection<TreeSource> ToolTreeRoot { get; set; }

        // メインの一覧画面に表示する動画レコードの管理用コレクション
        public ResettableObservableCollection<MovieRecords> MovieRecs { get; set; }

        // 検索や絞り込みをかけた後の、実際に画面へ表示するコレクション
        public ResettableObservableCollection<MovieRecords> FilteredMovieRecs { get; set; }

        // ERROR マーカー付き動画の専用一覧。
        public ObservableCollection<ThumbnailErrorRecordViewModel> ThumbnailErrorRecs { get; set; }

        // サムネ失敗タブ上部の進行状況サマリ。
        public ThumbnailErrorProgressViewState ThumbnailErrorProgress { get; }

        // MainDB書き込み前の仮表示（登録待ち）を保持するコレクション。
        public ObservableCollection<PendingMoviePlaceholder> PendingMovieRecs { get; set; }

        // 下部タブ「サムネイル進捗」の表示状態。
        public ThumbnailProgressViewState ThumbnailProgress { get; }

        // ブックマーク一覧、履歴一覧用のコレクション
        public ObservableCollection<MovieRecords> BookmarkRecs { get; set; }
        public ObservableCollection<History> HistoryRecs { get; set; }

        // 画面上部のソートドロップダウンに表示する選択肢のリスト
        public ObservableCollection<SortItem> SortLists { get; set; }

        /// <summary>
        /// 立ち上げの儀！空っぽの器（コレクション）たちを用意し、魅惑のメニューツリーやソート項目を一気に組み上げるぜ！🛠️
        /// </summary>
        public MainWindowViewModel()
        {
            DbInfo = new DBInfo();
            RecentTreeRoot = [];

            // 設定メニューのツリー構造を定義
            ConfigTreeRoot =
            [
                new TreeSource
                {
                    Text = "設定",
                    IconKind = MaterialDesignThemes.Wpf.PackIconKind.SettingsApplications,
                    IsExpanded = false,
                    Children =
                    [
                        new TreeSource
                        {
                            Text = "共通設定",
                            IconKind = MaterialDesignThemes.Wpf.PackIconKind.Settings,
                        },
                        new TreeSource
                        {
                            Text = "個別設定",
                            IconKind = MaterialDesignThemes.Wpf.PackIconKind.Cogs,
                        },
                    ],
                },
            ];

            // ツールメニューのツリー構造を定義
            ToolTreeRoot =
            [
                new TreeSource
                {
                    Text = "ツール",
                    IconKind = MaterialDesignThemes.Wpf.PackIconKind.Toolbox,
                    IsExpanded = false,
                    Children =
                    [
                        new TreeSource
                        {
                            Text = "監視フォルダ編集",
                            IconKind = MaterialDesignThemes.Wpf.PackIconKind.Binoculars,
                        },
                        new TreeSource
                        {
                            Text = "監視フォルダ更新チェック",
                            IconKind = MaterialDesignThemes.Wpf.PackIconKind.Reload,
                        },
                        new TreeSource
                        {
                            Text = "全ファイルサムネイル再作成",
                            IconKind = MaterialDesignThemes.Wpf.PackIconKind.Image,
                        },
                    ],
                },
            ];

            MovieRecs = [];
            FilteredMovieRecs = [];
            ThumbnailErrorRecs = [];
            ThumbnailErrorProgress = new ThumbnailErrorProgressViewState();
            PendingMovieRecs = [];
            ThumbnailProgress = new ThumbnailProgressViewState();
            BookmarkRecs = [];
            HistoryRecs = [];

            // UIスレッド外の無法地帯（別タスク）からコレクションをいじっても落ちないように、神の盾（ロック）を展開するぜ！🛡️
            BindingOperations.EnableCollectionSynchronization(MovieRecs, new object());
            BindingOperations.EnableCollectionSynchronization(FilteredMovieRecs, new object());
            BindingOperations.EnableCollectionSynchronization(ThumbnailErrorRecs, new object());
            BindingOperations.EnableCollectionSynchronization(PendingMovieRecs, new object());

            // ユーザーが選択可能なソート順の定義一覧
            SortLists =
            [
                new SortItem("0", "アクセス(新しい順)"),
                new SortItem("1", "アクセス(古い順)"),
                new SortItem("2", "ファイル(新しい順)"),
                new SortItem("3", "ファイル(古い順)"),
                //new SortItem("4", "スター数(多い順)"),    //tag内のスターを数えるのがかったるいので実装しない
                //new SortItem("5", "スター数(少ない順)"),  //tag内のスターを数えるのがかったるいので実装しない
                new SortItem("6", "スコア(高い順)"),
                new SortItem("7", "スコア(低い順)"),
                new SortItem("8", "再生数(多い順)"),
                new SortItem("9", "再生数(少ない順)"),
                new SortItem("10", "名前かな(昇順)"),
                new SortItem("11", "名前かな(降順)"),
                new SortItem("12", "ファイル名(昇順)"),
                new SortItem("13", "ファイル名(降順)"),
                new SortItem("14", "ファイルパス(昇順)"),
                new SortItem("15", "ファイルパス(降順)"),
                new SortItem("16", "サイズ(大きい順)"),
                new SortItem("17", "サイズ(小さい順)"),
                new SortItem("18", "登録(新しい順)"),
                new SortItem("19", "登録(古い順)"),
                new SortItem("20", "再生時間(長い順)"),
                new SortItem("21", "再生時間(短い順)"),
                new SortItem("22", "コメント1(昇順)"),
                new SortItem("23", "コメント1(降順)"),
                new SortItem("24", "コメント2(昇順)"),
                new SortItem("25", "コメント2(降順)"),
                new SortItem("26", "コメント3(昇順)"),
                new SortItem("27", "コメント3(降順)"),
                new SortItem("28", "エラー(多い順)"),
                //new SortList("28", "ランダム")            //ランダムソートもかったるいので実装しない。要るか？
            ];
        }

        /// <summary>
        /// 検索結果で表示用コレクションの中身を丸ごと総入れ替えする荒業！🧹
        /// XAML側のバインディング（FilteredMovieRecs）を一切壊さず、中身だけを最新にすり替えるスマートなヘルパーだぜ！✨
        /// </summary>
        public FilteredMovieRecsUpdateResult ReplaceFilteredMovieRecs(
            IEnumerable<MovieRecords> source,
            FilteredMovieRecsUpdateMode updateMode = FilteredMovieRecsUpdateMode.Diff
        )
        {
            List<MovieRecords> nextItems = source?.Where(movie => movie != null).ToList() ?? [];
            int currentCount = FilteredMovieRecs.Count;
            int nextCount = nextItems.Count;

            if (IsSameSequence(nextItems))
            {
                return new FilteredMovieRecsUpdateResult(
                    HasChanges: false,
                    RetainedPrefixCount: nextCount,
                    RetainedSuffixCount: 0,
                    RemovedCount: 0,
                    InsertedCount: 0,
                    MovedCount: 0
                );
            }

            if (updateMode == FilteredMovieRecsUpdateMode.Reset)
            {
                return ResetFilteredMovieRecs(nextItems);
            }

            int retainedPrefixCount = 0;
            while (
                retainedPrefixCount < currentCount
                && retainedPrefixCount < nextCount
                && ReferenceEquals(
                    FilteredMovieRecs[retainedPrefixCount],
                    nextItems[retainedPrefixCount]
                )
            )
            {
                retainedPrefixCount++;
            }

            int retainedSuffixCount = 0;
            while (
                retainedSuffixCount < currentCount - retainedPrefixCount
                && retainedSuffixCount < nextCount - retainedPrefixCount
                && ReferenceEquals(
                    FilteredMovieRecs[currentCount - 1 - retainedSuffixCount],
                    nextItems[nextCount - 1 - retainedSuffixCount]
                )
            )
            {
                retainedSuffixCount++;
            }

            if (
                updateMode == FilteredMovieRecsUpdateMode.Move
                && TryReorderFilteredMovieRecsWithMove(nextItems, out int movedCount)
            )
            {
                return new FilteredMovieRecsUpdateResult(
                    HasChanges: movedCount > 0,
                    RetainedPrefixCount: retainedPrefixCount,
                    RetainedSuffixCount: retainedSuffixCount,
                    RemovedCount: 0,
                    InsertedCount: 0,
                    MovedCount: movedCount
                );
            }

            int removeStartIndex = retainedPrefixCount;
            int removedCount = currentCount - retainedPrefixCount - retainedSuffixCount;
            for (int index = 0; index < removedCount; index++)
            {
                FilteredMovieRecs.RemoveAt(removeStartIndex);
            }

            int insertedCount = nextCount - retainedPrefixCount - retainedSuffixCount;
            for (int index = 0; index < insertedCount; index++)
            {
                FilteredMovieRecs.Insert(
                    removeStartIndex + index,
                    nextItems[removeStartIndex + index]
                );
            }

            return new FilteredMovieRecsUpdateResult(
                HasChanges: removedCount > 0 || insertedCount > 0,
                RetainedPrefixCount: retainedPrefixCount,
                RetainedSuffixCount: retainedSuffixCount,
                RemovedCount: removedCount,
                InsertedCount: insertedCount,
                MovedCount: 0
            );
        }

        /// <summary>
        /// 元データ一覧をまとめて差し替え、起動時の全件通知地獄を避ける。
        /// </summary>
        public void ReplaceMovieRecs(IEnumerable<MovieRecords> source)
        {
            MovieRecs.ReplaceAll(source?.Where(movie => movie != null) ?? []);
        }

        private bool IsSameSequence(IReadOnlyList<MovieRecords> nextItems)
        {
            if (nextItems == null || FilteredMovieRecs.Count != nextItems.Count)
            {
                return false;
            }

            for (int index = 0; index < nextItems.Count; index++)
            {
                if (!ReferenceEquals(FilteredMovieRecs[index], nextItems[index]))
                {
                    return false;
                }
            }

            return true;
        }

        // VirtualizingWrapPanel で崩れないよう、全件入れ直しへ戻す安全経路。
        private FilteredMovieRecsUpdateResult ResetFilteredMovieRecs(
            IReadOnlyList<MovieRecords> nextItems
        )
        {
            int removedCount = FilteredMovieRecs.Count;
            int insertedCount = nextItems?.Count ?? 0;
            FilteredMovieRecs.ReplaceAll(nextItems);

            return new FilteredMovieRecsUpdateResult(
                HasChanges: removedCount > 0 || insertedCount > 0,
                RetainedPrefixCount: 0,
                RetainedSuffixCount: 0,
                RemovedCount: removedCount,
                InsertedCount: insertedCount,
                MovedCount: 0
            );
        }

        // sort-only で要素集合が同じ時は、remove/insert ではなく Move だけで並び替える。
        private bool TryReorderFilteredMovieRecsWithMove(
            IReadOnlyList<MovieRecords> nextItems,
            out int movedCount
        )
        {
            movedCount = 0;
            int count = FilteredMovieRecs.Count;
            if (count != nextItems.Count)
            {
                return false;
            }

            Dictionary<MovieRecords, int> indexByItem = new(MovieRecordReferenceComparer.Instance);
            for (int index = 0; index < count; index++)
            {
                MovieRecords currentItem = FilteredMovieRecs[index];
                if (currentItem == null || !indexByItem.TryAdd(currentItem, index))
                {
                    return false;
                }
            }

            for (int targetIndex = 0; targetIndex < count; targetIndex++)
            {
                MovieRecords nextItem = nextItems[targetIndex];
                if (nextItem == null || !indexByItem.ContainsKey(nextItem))
                {
                    return false;
                }
            }

            for (int targetIndex = 0; targetIndex < count; targetIndex++)
            {
                MovieRecords nextItem = nextItems[targetIndex];
                int currentIndex = indexByItem[nextItem];
                if (currentIndex == targetIndex)
                {
                    continue;
                }

                FilteredMovieRecs.Move(currentIndex, targetIndex);
                movedCount++;

                int rangeStart = Math.Min(targetIndex, currentIndex);
                int rangeEnd = Math.Max(targetIndex, currentIndex);
                for (int index = rangeStart; index <= rangeEnd; index++)
                {
                    indexByItem[FilteredMovieRecs[index]] = index;
                }
            }

            return true;
        }

        private sealed class MovieRecordReferenceComparer : IEqualityComparer<MovieRecords>
        {
            public static MovieRecordReferenceComparer Instance { get; } = new();

            public bool Equals(MovieRecords x, MovieRecords y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(MovieRecords obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        /// <summary>
        /// ERROR 一覧もバインディングを壊さず中身だけ差し替える。
        /// </summary>
        public void ReplaceThumbnailErrorRecs(IEnumerable<ThumbnailErrorRecordViewModel> source)
        {
            ThumbnailErrorRecs.Clear();
            foreach (var movie in source)
            {
                ThumbnailErrorRecs.Add(movie);
            }
        }

        /// <summary>
        /// 検索キーワードという刃を振るって、膨大な獲物（コレクション）の中から条件に合う動画だけを容赦なく切り出す凄腕フィルター！⚔️
        /// </summary>
        public IEnumerable<MovieRecords> FilterMovies(
            IEnumerable<MovieRecords> source,
            string searchKeyword
        )
        {
            var query = source ?? Enumerable.Empty<MovieRecords>();
            // 単なる空入力なら絞り込みなしで全件返す
            if (string.IsNullOrWhiteSpace(searchKeyword))
            {
                return query;
            }

            var searchText = searchKeyword.Trim();

            // 1. フレーズ検索 (全体をダブルクォート " やシングルクォート ' で囲んでいる場合)
            if (
                (searchText.Length >= 2)
                && (
                    (searchText.StartsWith('"') && searchText.EndsWith('"'))
                    || (searchText.StartsWith('\'') && searchText.EndsWith('\''))
                )
            )
            {
                var exact = searchText[1..^1];
                return query.Where(item =>
                    BuildSearchFields(item).Any(field =>
                        field.Contains(exact, StringComparison.CurrentCultureIgnoreCase)
                    )
                );
            }

            // 2. 特殊コマンド検索 ({notag} や {dup} のような特定の文字列)
            if (searchText.StartsWith('{') && searchText.EndsWith('}'))
            {
                var inner = searchText[1..^1].Trim();

                // タグがひとつも設定されていない動画を探す
                if (inner.Equals("notag", StringComparison.CurrentCultureIgnoreCase))
                {
                    return query.Where(x => string.IsNullOrEmpty(x.Tags));
                }

                // ハッシュ値が重複している（同一ファイルの可能性がある）動画をまとて探す
                if (inner.Equals("dup", StringComparison.CurrentCultureIgnoreCase))
                {
                    var dupHashes = query
                        .GroupBy(x => x.Hash)
                        .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1)
                        .Select(g => g.Key)
                        .ToHashSet();
                    return query.Where(x => dupHashes.Contains(x.Hash));
                }
            }

            // 3. 通常のキーワード検索
            // " | " (OR検索)、半角スペース (AND検索)、"-" (NOT検索: 除外) を複合評価する。
            var orGroups = searchText.Split([" | "], StringSplitOptions.RemoveEmptyEntries);
            return query.Where(item =>
            {
                string[] fields = BuildSearchFields(item);

                // 各ORグループの「いずれか」を満たせばヒット
                return orGroups.Any(group =>
                {
                    var andTerms = group.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    // 該当ORグループ内の「すべて」の単語条件を満たす必要がある
                    return andTerms.All(term =>
                    {
                        // "-" から始まる単語が含まれていたら、その単語を「含まない」ことを条件とする
                        if (term.StartsWith('-'))
                        {
                            var keyword = term[1..];
                            return fields.All(f =>
                                !f.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                            );
                        }

                        // 通常の単語なら、いずれかのフィールドにその単語が「含まれる」ことを条件とする
                        return fields.Any(f =>
                            f.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                        );
                    });
                });
            });
        }

        // 通常検索、フレーズ検索、NOT 検索の対象フィールドを 1 か所へ寄せる。
        private static string[] BuildSearchFields(MovieRecords item)
        {
            string kana = ResolveSearchKana(item);
            string katakanaKana = JapaneseKanaProvider.ConvertToKatakana(kana);
            string roma = ResolveSearchRoma(item, kana);

            return
            [
                item?.Movie_Name ?? "",
                item?.Movie_Path ?? "",
                item?.Tags ?? "",
                item?.Comment1 ?? "",
                item?.Comment2 ?? "",
                item?.Comment3 ?? "",
                kana,
                katakanaKana,
                roma,
            ];
        }

        private static string ResolveSearchKana(MovieRecords item)
        {
            if (!string.IsNullOrWhiteSpace(item?.Kana))
            {
                return JapaneseKanaProvider.NormalizeToHiragana(item.Kana);
            }

            if (item == null)
            {
                return "";
            }

            return JapaneseKanaProvider.GetKana(item.Movie_Name, item.Movie_Path);
        }

        private static string ResolveSearchRoma(MovieRecords item, string kana)
        {
            if (!string.IsNullOrWhiteSpace(item?.Roma))
            {
                return item.Roma;
            }

            if (!string.IsNullOrWhiteSpace(kana))
            {
                return JapaneseKanaProvider.GetRomaFromKana(kana);
            }

            if (item == null)
            {
                return "";
            }

            return JapaneseKanaProvider.GetRoma(item.Movie_Name, item.Movie_Path);
        }

        /// <summary>
        /// SortListsで定義された「ソートの掟（ID）」に従って、絞り込み結果を美しく整列させる神の采配だ！⚡
        /// </summary>
        public IEnumerable<MovieRecords> SortMovies(IEnumerable<MovieRecords> source, string sortId)
        {
            var query = source ?? Enumerable.Empty<MovieRecords>();
            return sortId switch
            {
                "0" => query.OrderByDescending(x => x.Last_Date),
                "1" => query.OrderBy(x => x.Last_Date),
                "2" => query.OrderByDescending(x => x.File_Date),
                "3" => query.OrderBy(x => x.File_Date),
                "6" => query.OrderByDescending(x => x.Score),
                "7" => query.OrderBy(x => x.Score),
                "8" => query.OrderByDescending(x => x.View_Count),
                "9" => query.OrderBy(x => x.View_Count),
                "10" => query.OrderBy(x => x.Kana),
                "11" => query.OrderByDescending(x => x.Kana),
                "12" => query.OrderBy(x => x.Movie_Name),
                "13" => query.OrderByDescending(x => x.Movie_Name),
                "14" => query.OrderBy(x => x.Movie_Path),
                "15" => query.OrderByDescending(x => x.Movie_Path),
                "16" => query.OrderByDescending(x => x.Movie_Size),
                "17" => query.OrderBy(x => x.Movie_Size),
                "18" => query.OrderByDescending(x => x.Regist_Date),
                "19" => query.OrderBy(x => x.Regist_Date),
                "20" => query.OrderByDescending(x => x.Movie_Length),
                "21" => query.OrderBy(x => x.Movie_Length),
                "22" => query.OrderBy(x => x.Comment1),
                "23" => query.OrderByDescending(x => x.Comment1),
                "24" => query.OrderBy(x => x.Comment2),
                "25" => query.OrderByDescending(x => x.Comment2),
                "26" => query.OrderBy(x => x.Comment3),
                "27" => query.OrderByDescending(x => x.Comment3),
                "28" => query
                    .OrderByDescending(ResolveThumbnailErrorSortCount)
                    .ThenBy(x => x.Movie_Name ?? "", StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Movie_Path ?? "", StringComparer.CurrentCultureIgnoreCase),
                _ => query, // 万一未知のIDが来た場合はソートなしのまま返す
            };
        }

        // エラー順は、見えている placeholder と `. #ERROR.jpg` の両方をまとめて扱う。
        private static int ResolveThumbnailErrorSortCount(MovieRecords movie)
        {
            if (movie == null)
            {
                return 0;
            }

            return Math.Max(
                ThumbnailErrorPlaceholderHelper.CountPlaceholders(movie),
                movie.ThumbnailErrorMarkerCount
            );
        }

        // 表示用コンボボックスにバインドするための、ソート項目のキーバリュークラス
        public class SortItem(string id, string name)
        {
            public string Id { get; set; } = id;
            public string Name { get; set; } = name;
        }
    }
}
