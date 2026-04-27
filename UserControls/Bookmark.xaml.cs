using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace IndigoMovieManager.UserControls
{
    /// <summary>
    /// Bookmark.xaml の相互作用ロジック
    /// </summary>
    public partial class Bookmark : UserControl
    {
        // ブックマーク表示セルの初期化。
        public Bookmark()
        {
            InitializeComponent();
        }

        private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // ブックマークサムネのダブルクリックは、親Windowの再生処理へ委譲する。
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                //以外と親側に丸投げ（処理は追加したが）でいけるようで。
                MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);

                var item = (Label)sender;
                if (item != null)
                {
                    ownerWindow.PlayMovie_Click(sender, e);
                }
            }
        }

        private async void FileNameLink_Click(object sender, RoutedEventArgs e)
        {
            // ブックマーク名クリック時も検索正本へ合流させ、
            // SearchBox直代入由来の余計なイベント連鎖を減らす。
            MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);
            var item = (Hyperlink)sender;
            if (item != null)
            {
                MovieRecords mv = item.DataContext as MovieRecords;
                if (mv == null)
                {
                    return;
                }

                await ownerWindow.ApplySearchKeywordFromLinkAsync(mv.Movie_Body ?? "");
            }
        }

        private void DeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            // 削除ボタンは、親Windowの削除処理へ委譲する。
            MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);
            var item = (Button)sender;
            if (item != null)
            {
                ownerWindow.DeleteBookmark(sender, e);
            }
        }
    }
}
