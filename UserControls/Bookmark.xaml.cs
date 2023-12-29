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
        public Bookmark()
        {
            InitializeComponent();
            DataContext = new MovieRecords();
        }

        private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);
                //todo : ただ開くだけじゃなくて、ブックマークのフレームからなんだよなぁ。
                //親のPlayMovieじゃなくて独自実装するべきか。
                ownerWindow.PlayMovie_Click(sender, e);
            }
        }

        private void FileNameLink_Click(object sender, RoutedEventArgs e)
        {
            MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);
            var item = (Hyperlink)sender;
            if (item != null)
            {
                MovieRecords mv = item.DataContext as MovieRecords;
                ownerWindow.SearchBox.Text = mv.Movie_Body;
            }
        }

        private void DeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            //todo : データベースからブックマーク削除とリフレッシュ処理だな。多分。
        }
    }
}
