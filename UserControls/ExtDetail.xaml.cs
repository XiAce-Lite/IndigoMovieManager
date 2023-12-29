using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace IndigoMovieManager.UserControls
{
    /// <summary>
    /// ExtDetail.xaml の相互作用ロジック
    /// </summary>
    public partial class ExtDetail : UserControl
    {
        public ExtDetail()
        {
            InitializeComponent();
            DataContext = new MovieRecords();
        }

        private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);
                ownerWindow.PlayMovie_Click(sender, e);
            }
        }

        public void Refresh()
        {
            ExtDetailTags.Items.Refresh();
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            var item = (Hyperlink)sender;
            if (item != null)
            {
                MovieRecords mv= item.DataContext as MovieRecords;
                if (Path.Exists(mv.Movie_Path))
                {
                    Process.Start("explorer.exe", $"/select,{mv.Movie_Path}");
                }
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

        private void Ext_Click(object sender, RoutedEventArgs e)
        {
            MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);
            var item = (Hyperlink)sender;
            if (item != null)
            {
                MovieRecords mv = item.DataContext as MovieRecords;
                ownerWindow.SearchBox.Text = mv.Ext;
            }
        }
    }
}
