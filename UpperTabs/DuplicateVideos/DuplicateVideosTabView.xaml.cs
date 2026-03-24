using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace IndigoMovieManager.UpperTabs.DuplicateVideos
{
    public partial class DuplicateVideosTabView : UserControl
    {
        private bool _syncingDetailSelection;

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

        public Selector DuplicateGroupSelectorControl => DuplicateGroupListBox;

        public ListBox DuplicatePreviewListBoxControl => DuplicatePreviewListBox;

        public DataGrid DuplicateDetailDataGridControl => DuplicateDetailDataGrid;

        public ComboBox GroupSortComboBoxControl => GroupSortComboBox;

        public TextBlock GroupCountTextBlockControl => GroupCountTextBlock;

        public TextBlock SelectedCountTextBlockControl => SelectedCountTextBlock;

        public TextBlock SelectedHashTextBlockControl => SelectedHashTextBlock;

        private void DetectButton_Click(object sender, RoutedEventArgs e)
        {
            DetectRequested?.Invoke(sender, e);
        }

        private void DuplicateGroupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            GroupSelectionChangedRequested?.Invoke(sender, e);
        }

        private void DuplicateDetailDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SyncPreviewSelectionFromDetail();
            DetailSelectionChangedRequested?.Invoke(sender, e);
        }

        private void DuplicatePreviewListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SyncDetailSelectionFromPreview();
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

        private void SyncPreviewSelectionFromDetail()
        {
            if (_syncingDetailSelection)
            {
                return;
            }

            try
            {
                _syncingDetailSelection = true;
                object selectedItem =
                    DuplicateDetailDataGrid.CurrentItem
                    ?? DuplicateDetailDataGrid.SelectedItem;
                DuplicatePreviewListBox.SelectedItem = selectedItem;
            }
            finally
            {
                _syncingDetailSelection = false;
            }
        }

        private void SyncDetailSelectionFromPreview()
        {
            if (_syncingDetailSelection)
            {
                return;
            }

            try
            {
                _syncingDetailSelection = true;
                object selectedItem = DuplicatePreviewListBox.SelectedItem;
                DuplicateDetailDataGrid.SelectedItem = selectedItem;
                if (selectedItem == null)
                {
                    return;
                }

                DuplicateDetailDataGrid.ScrollIntoView(selectedItem);
            }
            finally
            {
                _syncingDetailSelection = false;
            }
        }
    }
}
