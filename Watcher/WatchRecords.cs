using System.Collections.ObjectModel;
using System.ComponentModel;

namespace IndigoMovieManager
{
    /// <summary>
    /// 設定画面や監視フォルダ画面において、監視対象フォルダ（Watchテーブル）の1レコードを表現するUI表示・編集用モデル。
    /// INotifyPropertyChangedを実装し、WPFのDataGridやListView等へのデータバインディングに対応する。
    /// </summary>
    public class WatchRecords : INotifyPropertyChanged
    {
        // ツリー表示など、階層構造を持たせる必要がある場合の子要素リスト
        private ObservableCollection<WatchRecords> _Children = null;

        private string dir = "";
        private bool auto = true;
        private bool watch = true;
        private bool sub = true;

        public WatchRecords() { }

        /// <summary>
        /// 監視対象となるフォルダのパス
        /// </summary>
        public string Dir
        {
            get => dir;
            set
            {
                dir = value;
                OnPropertyChanged(nameof(Dir));
            }
        }

        /// <summary>
        /// 動画追加時に、サムネイルを自動生成するかどうか
        /// </summary>
        public bool Auto
        {
            get => auto;
            set
            {
                auto = value;
                OnPropertyChanged(nameof(Auto));
            }
        }

        /// <summary>
        /// このフォルダの監視自体を有効にするかどうか
        /// </summary>
        public bool Watch
        {
            get => watch;
            set
            {
                watch = value;
                OnPropertyChanged(nameof(Watch));
            }
        }

        /// <summary>
        /// サブフォルダも監視対象に含めるかどうか
        /// </summary>
        public bool Sub
        {
            get => sub;
            set
            {
                sub = value;
                OnPropertyChanged(nameof(Sub));
            }
        }

        /// <summary>
        /// ディレクトリ階層などでグループ化・ツリー表示する際の子要素リスト
        /// </summary>
        public ObservableCollection<WatchRecords> Children
        {
            get { return _Children; }
            set
            {
                _Children = value;
                OnPropertyChanged("Children");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 値の変更をWPFのUI(View)へ通知する
        /// </summary>
        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>
        /// 子要素をリストに追加するヘルパーメソッド
        /// </summary>
        public void Add(WatchRecords child)
        {
            if (null == Children)
                Children = [];
            Children.Add(child);
        }
    }
}
