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

        public TagEditorTabView()
        {
            InitializeComponent();
            ShowPlaceholder();
        }

        public void ShowRecord(MovieRecords record, IReadOnlyList<TagEditorPaletteItem> paletteItems)
        {
            DataContext = record;
            MovieNameTextBlock.Text = record?.Movie_Name ?? "";
            RegisteredTagsItemsControl.ItemsSource = record?.Tag?.ToArray() ?? Array.Empty<string>();
            PaletteItemsControl.ItemsSource = paletteItems ?? Array.Empty<TagEditorPaletteItem>();
            ThumbnailPlaceholderTextBlock.Visibility = record == null ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ShowPlaceholder(IReadOnlyList<TagEditorPaletteItem> paletteItems = null)
        {
            DataContext = null;
            MovieNameTextBlock.Text = "選択中動画はありません。";
            RegisteredTagsItemsControl.ItemsSource = Array.Empty<string>();
            PaletteItemsControl.ItemsSource = paletteItems ?? Array.Empty<TagEditorPaletteItem>();
            ThumbnailPlaceholderTextBlock.Visibility = Visibility.Visible;
        }

        private void RegisteredTagSearchButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseTagEvent(RegisteredTagSearchRequested, (sender as FrameworkElement)?.DataContext);
        }

        private void RegisteredTagRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseTagEvent(RegisteredTagRemoveRequested, (sender as FrameworkElement)?.DataContext);
        }

        private void PaletteButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not TagEditorPaletteItem item)
            {
                return;
            }

            PaletteTagToggleRequested?.Invoke(this, new TagEditorTagActionEventArgs(item.TagName));
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
