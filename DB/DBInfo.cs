using System.ComponentModel;

namespace IndigoMovieManager.DB
{
    /// <summary>
    /// アプリの「今」の息吹（DBファイルパス、検索キーワード、お洒落なスキン等）を全てこの身に宿し、
    /// 変化があった瞬間にUI(WPF)へ「変わったぞォーッ！」と叫び声を上げる熱きViewModel的クラス！🗣️🔥
    /// </summary>
    public class DBInfo : INotifyPropertyChanged
    {
        private string currentDbFullPath = "";
        private string currentSkin = "";
        private string currentDbName = "";
        private string searchKeyword = "";
        private string sort = "";
        private string thumbFolder = "";
        private string bookmarkFolder = "";
        private int searchCount = 0;
        private int registeredMovieCount = 0;
        private int currentTabIndex = -1;

        /// <summary>
        /// 命の源！SQLiteデータベースのフルパスだ！📂
        /// </summary>
        public string DBFullPath
        {
            get => currentDbFullPath;
            set
            {
                currentDbFullPath = value;
                OnPropertyChanged(nameof(DBFullPath));
            }
        }

        /// <summary>
        /// 拡張子を脱ぎ捨てた純粋なデータベースファイル名！（既存のサムネファイルを召喚するための鍵🔑）
        /// </summary>
        public string DBName
        {
            get => currentDbName;
            set
            {
                currentDbName = value;
                OnPropertyChanged(nameof(DBName));
            }
        }

        /// <summary>
        /// UIのイケメン度を決定づけるスキン名！（現在デフォで4種の顔を持ってるぜ😎）
        /// </summary>
        public string Skin
        {
            get => currentSkin;
            set
            {
                currentSkin = value;
                OnPropertyChanged(nameof(Skin));
            }
        }

        /// <summary>
        /// 夢がいっぱい詰まったサムネイル画像の保存先フォルダパス！🖼️✨
        /// </summary>
        public string ThumbFolder
        {
            get => thumbFolder;
            set
            {
                thumbFolder = value;
                OnPropertyChanged(nameof(ThumbFolder));
            }
        }

        /// <summary>
        /// お気に入り達を飾るブックマーク用画像の保存先フォルダパス！🔖💖
        /// </summary>
        public string BookmarkFolder
        {
            get => bookmarkFolder;
            set
            {
                bookmarkFolder = value;
                OnPropertyChanged(nameof(BookmarkFolder));
            }
        }

        /// <summary>
        /// 欲望のままに打ち込まれた検索キーワード！🔍
        /// </summary>
        public string SearchKeyword
        {
            get => searchKeyword;
            set
            {
                searchKeyword = value;
                OnPropertyChanged(nameof(SearchKeyword));
            }
        }

        /// <summary>
        /// 今まさに君が見つめているタブのインデックス番号！👀
        /// </summary>
        public int CurrentTabIndex
        {
            get => currentTabIndex;
            set
            {
                currentTabIndex = value;
                OnPropertyChanged(nameof(CurrentTabIndex));
            }
        }

        /// <summary>
        /// リストを支配する並び順！（SQLのORDER BY句相当の呪文だ！🌀）
        /// </summary>
        public string Sort
        {
            get => sort;
            set
            {
                sort = value;
                OnPropertyChanged(nameof(Sort));
            }
        }

        /// <summary>
        /// 検索実行後に叩き出された大興奮のヒット件数！🎯
        /// </summary>
        public int SearchCount
        {
            get => searchCount;
            set
            {
                searchCount = value;
                OnPropertyChanged(nameof(SearchCount));
            }
        }

        /// <summary>
        /// movieテーブルへ正式登録されている総件数。段階ロード中でもここだけはDB基準の値を持つ。
        /// </summary>
        public int RegisteredMovieCount
        {
            get => registeredMovieCount;
            set
            {
                registeredMovieCount = value;
                OnPropertyChanged(nameof(RegisteredMovieCount));
            }
        }

        /// <summary>
        /// プロパティの変化をUI側へ爆音で報せるための熱きイベント！🎸⚡
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            // UIバインディングに対して「おい！このプロパティが変わったぞ！再描画だァーッ！」と叫ぶ！📢
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
