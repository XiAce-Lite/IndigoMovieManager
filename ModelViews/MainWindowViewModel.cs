using System.Collections.ObjectModel;

namespace IndigoMovieManager.ModelView
{
    public class MainWindowViewModel 
    {
        public DBInfo DbInfo { get; set; }
        public ObservableCollection<TreeSource> RecentTreeRoot { get; set; }
        public ObservableCollection<TreeSource> ConfigTreeRoot { get; set; }
        public ObservableCollection<TreeSource> ToolTreeRoot { get; set; }
        public ObservableCollection<MovieRecords> MovieRecs { get; set; }
        public ObservableCollection<SortItem> SortLists { get; set; }

        public MainWindowViewModel() {
            DbInfo = new DBInfo();
            RecentTreeRoot = [];
            ConfigTreeRoot = [];
            ToolTreeRoot = [];
            MovieRecs = [];

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

        public class SortItem(string id, string name)
        {
            public string Id { get; set; } = id;
            public string Name { get; set; } = name;
        }
    }
}
