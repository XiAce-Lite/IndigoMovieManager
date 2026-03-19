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
        public event SelectionChangedEventHandler RescueListSelectionChangedRequested;
        public event MouseButtonEventHandler PlayRequested;

        public ComboBox TargetTabComboBoxControl => TargetTabComboBox;

        public DataGrid RescueListDataGridControl => RescueListDataGrid;

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        public TextBlock HistoryTargetTextBlockControl => HistoryTargetTextBlock;

        public TextBlock HistoryEmptyTextBlockControl => HistoryEmptyTextBlock;

        public DataGrid RescueHistoryDataGridControl => RescueHistoryDataGrid;

        {
            RefreshRequested?.Invoke(sender, e);
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
