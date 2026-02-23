using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Data;

using IndigoMovieManager.DB;

namespace IndigoMovieManager.ModelViews
{
    public class MainWindowViewModel
    {
        public DBInfo DbInfo { get; set; }
        public ObservableCollection<TreeSource> RecentTreeRoot { get; set; }
        public ObservableCollection<TreeSource> ConfigTreeRoot { get; set; }
        public ObservableCollection<TreeSource> ToolTreeRoot { get; set; }
        public ObservableCollection<MovieRecords> MovieRecs { get; set; }
        public ObservableCollection<MovieRecords> FilteredMovieRecs { get; set; }
        public ObservableCollection<MovieRecords> BookmarkRecs { get; set; }
        public ObservableCollection<History> HistoryRecs { get; set; }
        public ObservableCollection<SortItem> SortLists { get; set; }

        public MainWindowViewModel()
        {
            DbInfo = new DBInfo();
            RecentTreeRoot = [];
            ConfigTreeRoot =
            [
                new TreeSource
                {
                    Text = "設定",
                    IconKind = MaterialDesignThemes.Wpf.PackIconKind.SettingsApplications,
                    IsExpanded = false,
                    Children =
                    [
                        new TreeSource { Text = "共通設定", IconKind = MaterialDesignThemes.Wpf.PackIconKind.Settings },
                        new TreeSource { Text = "個別設定", IconKind = MaterialDesignThemes.Wpf.PackIconKind.Cogs }
                    ]
                }
            ];
            ToolTreeRoot =
            [
                new TreeSource
                {
                    Text = "ツール",
                    IconKind = MaterialDesignThemes.Wpf.PackIconKind.Toolbox,
                    IsExpanded = false,
                    Children =
                    [
                        new TreeSource { Text = "監視フォルダ編集", IconKind = MaterialDesignThemes.Wpf.PackIconKind.Binoculars },
                        new TreeSource { Text = "監視フォルダ更新チェック", IconKind = MaterialDesignThemes.Wpf.PackIconKind.Reload },
                        new TreeSource { Text = "全ファイルサムネイル再作成", IconKind = MaterialDesignThemes.Wpf.PackIconKind.Image }
                    ]
                }
            ];
            MovieRecs = [];
            FilteredMovieRecs = [];
            BookmarkRecs = [];
            HistoryRecs = [];
            BindingOperations.EnableCollectionSynchronization(MovieRecs, new object());
            BindingOperations.EnableCollectionSynchronization(FilteredMovieRecs, new object());

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
                //new SortList("28", "ランダム")            //ランダムソートもかったるいので実装しない。要るか？
            ];
        }

        // 検索後の表示対象コレクションを差し替える。
        // XAML側を FilteredMovieRecs に統一して、code-behind の ItemsSource 再設定を減らす。
        public void ReplaceFilteredMovieRecs(IEnumerable<MovieRecords> source)
        {
            FilteredMovieRecs.Clear();
            foreach (var movie in source)
            {
                FilteredMovieRecs.Add(movie);
            }
        }

        // 検索テキストに応じて表示対象を絞り込む。
        public IEnumerable<MovieRecords> FilterMovies(IEnumerable<MovieRecords> source, string searchKeyword)
        {
            var query = source ?? Enumerable.Empty<MovieRecords>();
            if (string.IsNullOrWhiteSpace(searchKeyword))
            {
                return query;
            }

            var searchText = searchKeyword.Trim();

            // クォートで囲まれた場合はフレーズ検索として扱う。
            if ((searchText.Length >= 2) &&
                ((searchText.StartsWith('"') && searchText.EndsWith('"')) ||
                 (searchText.StartsWith('\'') && searchText.EndsWith('\''))))
            {
                var exact = searchText[1..^1];
                return query.Where(item =>
                    (item.Movie_Name ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase) ||
                    (item.Movie_Path ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase) ||
                    (item.Tags ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase) ||
                    (item.Comment1 ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase) ||
                    (item.Comment2 ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase) ||
                    (item.Comment3 ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase)
                );
            }

            // {notag}/{dup} のような特別コマンドを処理する。
            if (searchText.StartsWith('{') && searchText.EndsWith('}'))
            {
                var inner = searchText[1..^1].Trim();
                if (inner.Equals("notag", StringComparison.CurrentCultureIgnoreCase))
                {
                    return query.Where(x => string.IsNullOrEmpty(x.Tags));
                }

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

            // 通常検索は OR(" | ") / AND(半角スペース) / NOT("-") を評価する。
            var orGroups = searchText.Split([" | "], StringSplitOptions.RemoveEmptyEntries);
            return query.Where(item =>
            {
                return orGroups.Any(group =>
                {
                    var andTerms = group.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    return andTerms.All(term =>
                    {
                        var fields = new[]
                        {
                            item.Movie_Name ?? "",
                            item.Movie_Path ?? "",
                            item.Tags ?? "",
                            item.Comment1 ?? "",
                            item.Comment2 ?? "",
                            item.Comment3 ?? ""
                        };

                        if (term.StartsWith('-'))
                        {
                            var keyword = term[1..];
                            return fields.All(f => !f.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
                        }

                        return fields.Any(f => f.Contains(term, StringComparison.CurrentCultureIgnoreCase));
                    });
                });
            });
        }

        // 絞り込み結果へソートを適用する。
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
                _ => query
            };
        }

        public class SortItem(string id, string name)
        {
            public string Id { get; set; } = id;
            public string Name { get; set; } = name;
        }
    }
}
