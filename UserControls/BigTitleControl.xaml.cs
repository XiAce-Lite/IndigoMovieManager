using System.Windows.Controls;

namespace IndigoMovieManager.UserControls
{
    /// <summary>
    /// BigTitleControl.xaml の相互作用ロジック
    /// </summary>
    public partial class BigTitleControl : UserControl
    {
        // Bigタブ用のタイトル行コントロールを初期化する。
        // 再描画は親ListViewのバインディング更新に追従する。
        public BigTitleControl()
        {
            InitializeComponent();
        }
    }
}
