using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IndigoMovieManager.BottomTabs.SavedSearch
{
    public partial class SavedSearchTabView : UserControl
    {
        public event EventHandler<SavedSearchRequestedEventArgs> SearchRequested;

        public SavedSearchTabView()
        {
            InitializeComponent();
        }

        // placeholder 文言の更新窓口を view 側へ閉じる。
        public void SetPlaceholderText(string text)
        {
            PlaceholderTextBlock.Text = text ?? "";
            PlaceholderTextBlock.Visibility = Visibility.Visible;
            SavedSearchListBox.Visibility = Visibility.Collapsed;
        }

        public void SetItems(IReadOnlyList<SavedSearchItem> items, string placeholderText = null)
        {
            SavedSearchItem[] normalizedItems = items?.ToArray() ?? [];
            SavedSearchListBox.ItemsSource = normalizedItems;

            bool hasItems = normalizedItems.Length > 0;
            SavedSearchListBox.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            PlaceholderTextBlock.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            PlaceholderTextBlock.Text = placeholderText ?? "保存済み検索条件はありません。";
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseSearchRequested((sender as FrameworkElement)?.DataContext as SavedSearchItem);
        }

        private void SavedSearchListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            RaiseSearchRequested(SavedSearchListBox.SelectedItem as SavedSearchItem);
        }

        private void RaiseSearchRequested(SavedSearchItem item)
        {
            if (item == null || !item.CanExecute)
            {
                return;
            }

            SearchRequested?.Invoke(this, new SavedSearchRequestedEventArgs(item));
        }
    }

    public sealed class SavedSearchRequestedEventArgs : EventArgs
    {
        public SavedSearchRequestedEventArgs(SavedSearchItem item)
        {
            Item = item;
        }

        public SavedSearchItem Item { get; }
    }
}
