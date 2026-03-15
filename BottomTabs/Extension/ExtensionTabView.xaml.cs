using System.Windows;
using System.Windows.Controls;
using IndigoMovieManager;

namespace IndigoMovieManager.BottomTabs.Extension
{
    public partial class ExtensionTabView : UserControl
    {
        public ExtensionTabView()
        {
            InitializeComponent();
            Visibility = Visibility.Collapsed;
        }

        // 選択中レコードを詳細表示へ流し込み、外側 host もまとめて見せる。
        public void ShowRecord(MovieRecords record)
        {
            if (record == null)
            {
                HideRecord();
                return;
            }

            ExtensionDetailView.DataContext = record;
            ExtensionDetailView.Visibility = Visibility.Visible;
            Visibility = Visibility.Visible;
        }

        // 検索結果がある時は、選択切替前でも詳細ペインの器だけ出せるようにする。
        public void ShowContainer()
        {
            ExtensionDetailView.DataContext = null;
            ExtensionDetailView.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Visible;
        }

        // 詳細ペインを閉じる時は、中身と外側 host をまとめて落とす。
        public void HideRecord()
        {
            ExtensionDetailView.DataContext = null;
            ExtensionDetailView.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
        }

        // タグ変更などで詳細表示だけ再描画したい時の窓口。
        public void RefreshDetail()
        {
            ExtensionDetailView.Refresh();
        }
    }
}
