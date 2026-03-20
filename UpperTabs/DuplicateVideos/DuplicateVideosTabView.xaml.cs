using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IndigoMovieManager.UpperTabs.DuplicateVideos
{
    public partial class DuplicateVideosTabView : UserControl
    {
        public DuplicateVideosTabView()
        {
            InitializeComponent();
        }

        public event RoutedEventHandler DetectRequested;
        public event SelectionChangedEventHandler GroupSelectionChangedRequested;
        public event SelectionChangedEventHandler DetailSelectionChangedRequested;
        public event MouseButtonEventHandler DetailPlayRequested;
        public event SelectionChangedEventHandler GroupSortSelectionChangedRequested;
        public event EventHandler<DataGridCellEditEndingEventArgs> DetailCellEditEndingRequested;

        public DataGrid DuplicateGroupDataGridControl => DuplicateGroupDataGrid;

        public DataGrid DuplicateDetailDataGridControl => DuplicateDetailDataGrid;

        public ComboBox GroupSortComboBoxControl => GroupSortComboBox;

        public TextBlock GroupCountTextBlockControl => GroupCountTextBlock;

        public TextBlock SelectedCountTextBlockControl => SelectedCountTextBlock;

        public TextBlock SelectedHashTextBlockControl => SelectedHashTextBlock;

        private void DetectButton_Click(object sender, RoutedEventArgs e)
        {
            DetectRequested?.Invoke(sender, e);
        }

        private void DuplicateGroupDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            GroupSelectionChangedRequested?.Invoke(sender, e);
        }

        private void DuplicateDetailDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DetailSelectionChangedRequested?.Invoke(sender, e);
        }

        private void DuplicateDetailDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DetailPlayRequested?.Invoke(sender, e);
        }

        private void GroupSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            GroupSortSelectionChangedRequested?.Invoke(sender, e);
        }

        private void DuplicateDetailDataGrid_CellEditEnding(
            object sender,
            DataGridCellEditEndingEventArgs e
        )
        {
            DetailCellEditEndingRequested?.Invoke(sender, e);
        }
    }
}
