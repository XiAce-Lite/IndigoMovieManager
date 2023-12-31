using System.ComponentModel;

namespace IndigoMovieManager
{
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
        private int keepHistory = 30;

        /// <summary>
        /// SQLiteデータベースのフルパス
        /// </summary>
        public string DBFullPath
        {
            get => currentDbFullPath;
            set { currentDbFullPath = value; OnPropertyChanged(nameof(DBFullPath)); }
        }

        /// <summary>
        /// 拡張子なしのデータベースファイル名（既存のサムネファイルを開く為）
        /// </summary>
        public string DBName
        {
            get => currentDbName;
            set { currentDbName = value; OnPropertyChanged(nameof(DBName)); }
        }

        /// <summary>
        /// スキン名（今やデフォルト4種のみ対応）
        /// </summary>
        public string Skin
        {
            get => currentSkin;
            set { currentSkin = value; OnPropertyChanged(nameof(Skin)); }
        }

        public string ThumbFolder
        {
            get => thumbFolder;
            set { thumbFolder = value; OnPropertyChanged(nameof(ThumbFolder)); }
        }

        public string BookmarkFolder
        {
            get => bookmarkFolder;
            set { bookmarkFolder = value; OnPropertyChanged(nameof(BookmarkFolder)); }
        }

        public string SearchKeyword
        {
            get => searchKeyword;
            set { searchKeyword = value; OnPropertyChanged(nameof(SearchKeyword)); }
        }

        public int CurrentTabIndex
        {
            get => currentTabIndex; 
            set { currentTabIndex = value; OnPropertyChanged(nameof(CurrentTabIndex)); }
        }

        public string Sort
        {
            get => sort;
            set { sort = value; OnPropertyChanged(nameof(Sort)); }
        }

        public int SearchCount
        {
            get => searchCount;
            set { searchCount = value; OnPropertyChanged(nameof(SearchCount)); }
        }

        public int KeepHistory
        {
            get => keepHistory;
            set { keepHistory = value; OnPropertyChanged(nameof(KeepHistory)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
