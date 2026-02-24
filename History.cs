using System.Collections.ObjectModel;
using System.ComponentModel;

namespace IndigoMovieManager
{
    /// <summary>
    /// メイン画面での検索履歴（historyテーブル）の1レコードを表現するUI表示用モデル。
    /// INotifyPropertyChangedを実装し、WPFのDataGridやListView等へのデータバインディングに対応する。
    /// </summary>
    public class History : INotifyPropertyChanged
    {
        // 階層表示用の子要素リスト（ツリー上で履歴をグループ化する場合などに使用）
        private ObservableCollection<History> _Children = null;

        private long find_id = 0;
        private string find_text = "";
        private string find_date = "";

        /// <summary>
        /// 検索履歴のDB上の主キー列(ID)
        /// </summary>
        public long Find_Id
        {
            get { return find_id; }
            set
            {
                find_id = value;
                OnPropertyChanged(nameof(Find_Id));
            }
        }

        /// <summary>
        /// 実際に検索されたキーワード文字列
        /// </summary>
        public string Find_Text
        {
            get { return find_text; }
            set
            {
                find_text = value;
                OnPropertyChanged(nameof(Find_Text));
            }
        }

        /// <summary>
        /// 検索が実行された日時（UI表示用の文字列）
        /// </summary>
        public string Find_Date
        {
            get { return find_date; }
            set
            {
                find_date = value;
                OnPropertyChanged(nameof(Find_Date));
            }
        }

        /// <summary>
        /// ツリー表示などを行う場合の子要素リスト
        /// </summary>
        public ObservableCollection<History> Children
        {
            get { return _Children; }
            set
            {
                _Children = value;
                OnPropertyChanged(nameof(Children));
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
        public void Add(History child)
        {
            if (null == Children)
                Children = [];
            Children.Add(child);
        }
    }
}
