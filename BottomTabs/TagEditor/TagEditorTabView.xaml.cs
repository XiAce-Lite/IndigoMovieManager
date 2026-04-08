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
        public event EventHandler<TagEditorTagActionEventArgs> RegisteredTagToggleRequested;
        public event EventHandler<TagEditorTagActionEventArgs> PaletteTagToggleRequested;
        public event EventHandler<TagEditorTagActionEventArgs> PaletteTagAddRequested;

        private IReadOnlyList<TagEditorPaletteItem> _currentPaletteItems = Array.Empty<TagEditorPaletteItem>();
        private HashSet<string> _currentRegisteredTags = [];
        private HashSet<string> _currentActiveTags = [];

        public TagEditorTabView()
        {
            InitializeComponent();
            ShowPlaceholder();
        }

        public void ShowRecord(
            MovieRecords record,
            IReadOnlyList<TagEditorPaletteItem> paletteItems,
            IReadOnlyCollection<string> activeTags
        )
        {
            DataContext = record;
            MovieNameTextBlock.Text = record?.Movie_Name ?? "";
            string[] registeredTags = record?.Tag?.ToArray() ?? Array.Empty<string>();
            _currentRegisteredTags = [.. registeredTags];
            _currentActiveTags = activeTags != null ? [.. activeTags] : [];
            RegisteredTagsItemsControl.ItemsSource = registeredTags
                .Select(x => new TagEditorRegisteredTagItem
                {
                    TagName = x,
                    IsActive = _currentActiveTags.Contains(x),
                })
                .ToArray();
            _currentPaletteItems = paletteItems ?? Array.Empty<TagEditorPaletteItem>();
            PaletteItemsControl.ItemsSource = _currentPaletteItems;
            ThumbnailPlaceholderTextBlock.Visibility = record == null ? Visibility.Visible : Visibility.Collapsed;
            DropHintTextBlock.Text = record == null
                ? "選択中動画はありません。"
                : "登録済みタグをここで確認できます";
        }

        public void ShowPlaceholder(IReadOnlyList<TagEditorPaletteItem> paletteItems = null)
        {
            DataContext = null;
            MovieNameTextBlock.Text = "選択中動画はありません。";
            _currentRegisteredTags = [];
            _currentActiveTags = [];
            RegisteredTagsItemsControl.ItemsSource = Array.Empty<string>();
            _currentPaletteItems = paletteItems ?? Array.Empty<TagEditorPaletteItem>();
            PaletteItemsControl.ItemsSource = _currentPaletteItems;
            ThumbnailPlaceholderTextBlock.Visibility = Visibility.Visible;
            DropHintTextBlock.Text = "登録済みタグをここで確認できます";
        }

        private void PaletteButton_Click(object sender, RoutedEventArgs e)
        {
            string tagName =
                ((sender as FrameworkElement)?.Tag?.ToString())
                ?? ((sender as FrameworkElement)?.DataContext as TagEditorPaletteItem)?.TagName
                ?? "";
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            PaletteTagToggleRequested?.Invoke(this, new TagEditorTagActionEventArgs(tagName));
        }

        private void PaletteAddButton_Click(object sender, RoutedEventArgs e)
        {
            string tagName = (sender as FrameworkElement)?.Tag?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            PaletteTagAddRequested?.Invoke(this, new TagEditorTagActionEventArgs(tagName));
        }

        private void RegisteredTagSearchButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseTagEvent(RegisteredTagSearchRequested, (sender as FrameworkElement)?.DataContext);
        }

        private void RegisteredTagRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseTagEvent(RegisteredTagRemoveRequested, (sender as FrameworkElement)?.DataContext);
        }

        private void RegisteredTagToggleButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseTagEvent(RegisteredTagToggleRequested, (sender as FrameworkElement)?.DataContext);
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

    public sealed class TagEditorRegisteredTagItem
    {
        public string TagName { get; init; } = "";

        public bool IsActive { get; init; }

        public override string ToString()
        {
            return TagName;
        }
    }
}
