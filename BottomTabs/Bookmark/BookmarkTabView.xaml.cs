using System.Windows.Controls;

namespace IndigoMovieManager.BottomTabs.Bookmark
{
    public partial class BookmarkTabView : UserControl
    {
        public BookmarkTabView()
        {
            InitializeComponent();
        }

        // MainWindow からの見た目更新要求はこの窓口だけへ寄せる。
        public void RefreshItems()
        {
            BookmarkList?.Items.Refresh();
        }
    }
}
