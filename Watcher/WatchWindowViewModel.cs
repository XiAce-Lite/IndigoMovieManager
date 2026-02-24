using System.Collections.ObjectModel;

namespace IndigoMovieManager.ModelView
{
    /// <summary>
    /// 監視フォルダ情報画面(WatchWindow)のUI(WPF)とやり取りするためのViewModelクラス。
    /// 現在登録されている監視対象フォルダのリストを保持・提供する。
    /// </summary>
    public class WatchWindowViewModel
    {
        // 監視フォルダの一覧管理用コレクション。
        // ListView等のItemsSourceにバインドされる。
        public ObservableCollection<WatchRecords> WatchRecs { get; set; }

        public WatchWindowViewModel()
        {
            // インスタンス化された段階で操作可能な空のコレクションを用意しておく。
            WatchRecs = [];
        }
    }
}
