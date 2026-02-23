using System.Windows.Controls;

namespace IndigoMovieManager.UserControls
{
    /// <summary>
    /// BigDetailControl.xaml の相互作用ロジック
    /// </summary>
    public partial class BigDetailControl : UserControl
    {
        // Bigタブ用の明細表示コントロールを初期化する。
        // 表示はDataContextのMovieRecordsをXAMLで描画する。
        public BigDetailControl()
        {
            InitializeComponent();
        }
    }
}
