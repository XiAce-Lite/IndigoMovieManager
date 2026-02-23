using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace IndigoMovieManager.UserControls
{
    /// <summary>
    /// SmallDetailControl.xaml の相互作用ロジック
    /// </summary>
    public partial class SmallDetailControl : UserControl
    {
        // Smallタブ用の明細表示コントロールを初期化する。
        // 表示内容はXAMLバインディング側で完結している。
        public SmallDetailControl()
        {
            InitializeComponent();
        }
    }
}
