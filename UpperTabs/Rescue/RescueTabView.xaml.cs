using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IndigoMovieManager.UpperTabs.Rescue
{
    public partial class RescueTabView : UserControl
    {
        public RescueTabView()
        {
            InitializeComponent();
        }

        public event RoutedEventHandler RefreshRequested;
        public event RoutedEventHandler BulkNormalRetryRequested;
        public event RoutedEventHandler SelectedAutogenRequested;
        public event RoutedEventHandler SelectedFfmpegRequested;
        public event RoutedEventHandler SelectedFfmediaToolkitRequested;
        public event RoutedEventHandler SelectedOpenCvRequested;
        public event RoutedEventHandler SelectedBlackConfirmRequested;
        public event RoutedEventHandler SelectedBlackLiteRetryRequested;
        public event RoutedEventHandler SelectedBlackDeepRetryRequested;
        public event SelectionChangedEventHandler RescueListSelectionChangedRequested;
        public event MouseButtonEventHandler PlayRequested;

        public ComboBox TargetTabComboBoxControl => TargetTabComboBox;

        public DataGrid RescueListDataGridControl => RescueListDataGrid;

        public TextBlock HistoryTargetTextBlockControl => HistoryTargetTextBlock;

        public TextBlock HistoryEmptyTextBlockControl => HistoryEmptyTextBlock;

        public DataGrid RescueHistoryDataGridControl => RescueHistoryDataGrid;

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshRequested?.Invoke(sender, e);
        }

        private void BulkNormalRetryButton_Click(object sender, RoutedEventArgs e)
        {
            BulkNormalRetryRequested?.Invoke(sender, e);
        }

        private void SelectedAutogenButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedAutogenRequested?.Invoke(sender, e);
        }

        private void SelectedFfmpegButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedFfmpegRequested?.Invoke(sender, e);
        }

        private void SelectedFfmediaToolkitButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedFfmediaToolkitRequested?.Invoke(sender, e);
        }

        private void SelectedOpenCvButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedOpenCvRequested?.Invoke(sender, e);
        }

        private void SelectedBlackConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedBlackConfirmRequested?.Invoke(sender, e);
        }

        private void SelectedBlackLiteRetryButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedBlackLiteRetryRequested?.Invoke(sender, e);
        }

        private void SelectedBlackDeepRetryButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedBlackDeepRetryRequested?.Invoke(sender, e);
        }

        private void RescueListDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RescueListSelectionChangedRequested?.Invoke(sender, e);
        }

        private void RescueListDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            PlayRequested?.Invoke(sender, e);
        }
    }
}
