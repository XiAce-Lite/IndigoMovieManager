using System.Windows.Controls;

namespace IndigoMovieManager.BottomTabs.SavedSearch
{
    public partial class SavedSearchTabView : UserControl
    {
        public SavedSearchTabView()
        {
            InitializeComponent();
        }

        // placeholder 文言の更新窓口を view 側へ閉じる。
        public void SetPlaceholderText(string text)
        {
            PlaceholderTextBlock.Text = text ?? "";
        }
    }
}
