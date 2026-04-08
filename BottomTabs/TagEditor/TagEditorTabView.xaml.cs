using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace IndigoMovieManager.BottomTabs.TagEditor
{
    public partial class TagEditorTabView : UserControl
    {
        public event EventHandler<TagEditorTagActionEventArgs> RegisteredTagSearchRequested;
        public event EventHandler<TagEditorTagActionEventArgs> RegisteredTagRemoveRequested;
        public event EventHandler<TagEditorTagActionEventArgs> PaletteTagToggleRequested;
        public event EventHandler<TagEditorTagActionEventArgs> PaletteTagAddRequested;

        private IReadOnlyList<TagEditorPaletteItem> _currentPaletteItems = Array.Empty<TagEditorPaletteItem>();
        private HashSet<string> _currentRegisteredTags = [];

        public TagEditorTabView()
        {
            InitializeComponent();
            ShowPlaceholder();
        }

        public void ShowRecord(MovieRecords record, IReadOnlyList<TagEditorPaletteItem> paletteItems)
        {
            DataContext = record;
            MovieNameTextBlock.Text = record?.Movie_Name ?? "";
            string[] registeredTags = record?.Tag?.ToArray() ?? Array.Empty<string>();
            _currentRegisteredTags = [.. registeredTags];
            RegisteredTagsItemsControl.ItemsSource = registeredTags;
            _currentPaletteItems = paletteItems ?? Array.Empty<TagEditorPaletteItem>();
            PaletteItemsControl.ItemsSource = _currentPaletteItems;
            ThumbnailPlaceholderTextBlock.Visibility = record == null ? Visibility.Visible : Visibility.Collapsed;
            DropHintTextBlock.Text = record == null
                ? "選択中動画はありません。"
                : "登録済みタグをここで確認できます";
            AddRegisteredTagButton.IsEnabled = record != null;
        }

        public void ShowPlaceholder(IReadOnlyList<TagEditorPaletteItem> paletteItems = null)
        {
            DataContext = null;
            MovieNameTextBlock.Text = "選択中動画はありません。";
            _currentRegisteredTags = [];
            RegisteredTagsItemsControl.ItemsSource = Array.Empty<string>();
            _currentPaletteItems = paletteItems ?? Array.Empty<TagEditorPaletteItem>();
            PaletteItemsControl.ItemsSource = _currentPaletteItems;
            ThumbnailPlaceholderTextBlock.Visibility = Visibility.Visible;
            DropHintTextBlock.Text = "登録済みタグをここで確認できます";
            AddRegisteredTagButton.IsEnabled = false;
        }

        private void PaletteButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not TagEditorPaletteItem item)
            {
                return;
            }

            PaletteTagToggleRequested?.Invoke(this, new TagEditorTagActionEventArgs(item.TagName));
        }

        private void RegisteredTagSearchButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseTagEvent(RegisteredTagSearchRequested, (sender as FrameworkElement)?.DataContext);
        }

        private void RegisteredTagRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseTagEvent(RegisteredTagRemoveRequested, (sender as FrameworkElement)?.DataContext);
        }

        private void AddRegisteredTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MovieRecords)
            {
                return;
            }

            ContextMenu contextMenu = new();
            foreach (TagEditorPaletteItem paletteItem in _currentPaletteItems)
            {
                MenuItem menuItem = new()
                {
                    Header = paletteItem.TagName,
                    IsEnabled = !_currentRegisteredTags.Contains(paletteItem.TagName),
                    Tag = paletteItem.TagName,
                };
                menuItem.Click += AddRegisteredTagMenuItem_Click;
                contextMenu.Items.Add(menuItem);
            }

            if (contextMenu.Items.Count == 0)
            {
                return;
            }

            contextMenu.PlacementTarget = AddRegisteredTagButton;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            contextMenu.IsOpen = true;
        }

        private void AddRegisteredTagMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string tagName = (sender as FrameworkElement)?.Tag?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            PaletteTagAddRequested?.Invoke(this, new TagEditorTagActionEventArgs(tagName));
        }

        private static void RaiseTagEvent(
            EventHandler<TagEditorTagActionEventArgs> handler,
            object dataContext
        )
        {
            string tagName = dataContext?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            handler?.Invoke(null, new TagEditorTagActionEventArgs(tagName));
        }
    }

    public sealed class TagEditorTagActionEventArgs : EventArgs
    {
        public TagEditorTagActionEventArgs(string tagName)
        {
            TagName = tagName ?? "";
        }

        public string TagName { get; }
    }
}
