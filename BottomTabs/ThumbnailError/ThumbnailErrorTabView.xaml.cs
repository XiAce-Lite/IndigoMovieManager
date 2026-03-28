using System.Windows;
using System.Windows.Controls;

namespace IndigoMovieManager.BottomTabs.ThumbnailError
{
    public partial class ThumbnailErrorTabView : UserControl
    {
        public ThumbnailErrorTabView()
        {
            InitializeComponent();
        }

        public event RoutedEventHandler ReloadRequested;
        public event RoutedEventHandler ClearListRequested;
        public event RoutedEventHandler RescueSelectedRequested;
        public event RoutedEventHandler RescueAllRequested;
        public event SelectionChangedEventHandler ErrorListSelectionChangedRequested;

        public DataGrid ErrorListDataGridControl => ErrorListDataGrid;

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            ReloadRequested?.Invoke(sender, e);
        }

        private void RescueSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            RescueSelectedRequested?.Invoke(sender, e);
        }

        private void ClearListButton_Click(object sender, RoutedEventArgs e)
        {
            ClearListRequested?.Invoke(sender, e);
        }

        private void RescueAllButton_Click(object sender, RoutedEventArgs e)
        {
            RescueAllRequested?.Invoke(sender, e);
        }

        private void ErrorListDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ErrorListSelectionChangedRequested?.Invoke(sender, e);
        }
    }
}
