using System.ComponentModel;

namespace IndigoMovieManager.DB
{
    /// <summary>
    /// アプリケーション内で現在の状態（DBファイル、検索キーワード、スキンなど）を保持し、
    /// プロパティ変更時にUI(WPF)へ通知するためのViewModel的なクラス。
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
        private int currentTabIndex = -1;

        /// <summary>
        /// SQLiteデータベースのフルパス
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
        /// 拡張子なしのデータベースファイル名（既存のサムネファイルを開く為）
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
        /// UIの見た目を決定するスキン名（現在デフォルト4種を設定可能）
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
        /// サムネイル画像の保存先フォルダパス
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
        /// ブックマーク用画像の保存先フォルダパス
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
        /// 現在入力されている検索キーワード
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
        /// 現在選択されているタブのインデックス番号
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
        /// 一覧表示の並び順（SQLのORDER BY句相当の文字列など）
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
        /// 検索実行後のヒット件数
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
        /// プロパティの値が変更されたことをUI側に通知するためのイベント
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            // UIバインディングに対して「このプロパティが更新されたよ」と知らせる
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
