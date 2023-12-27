using System.Windows;
using System.Windows.Controls;
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
    }
}
