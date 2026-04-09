using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace IndigoMovieManager.BottomTabs.TagEditor
{
    public partial class TagEditorTabView : UserControl
    {
        private const double VerticalLayoutThreshold = 1180;

        public event EventHandler<TagEditorTagActionEventArgs> RegisteredTagSearchRequested;
        public event EventHandler<TagEditorTagActionEventArgs> RegisteredTagRemoveRequested;
        public event EventHandler<TagEditorTagActionEventArgs> RegisteredTagToggleRequested;
        public event EventHandler<TagEditorTagActionEventArgs> PaletteTagToggleRequested;
        public event EventHandler<TagEditorTagActionEventArgs> PaletteTagAddRequested;
        public event EventHandler<TagEditorTagActionEventArgs> CustomTagAddRequested;

        private IReadOnlyList<TagEditorPaletteItem> _currentPaletteItems = Array.Empty<TagEditorPaletteItem>();
        private HashSet<string> _currentRegisteredTags = [];
        private HashSet<string> _currentActiveTags = [];
        private bool? _isVerticalLayout;

        public TagEditorTabView()
        {
            InitializeComponent();
            Loaded += TagEditorTabView_Loaded;
            SizeChanged += TagEditorTabView_SizeChanged;
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
            CustomTagTextBox.Text = "";
            CustomTagTextBox.IsEnabled = record != null;
            AddCustomTagButton.IsEnabled = record != null;
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
            CustomTagTextBox.Text = "";
            CustomTagTextBox.IsEnabled = false;
            AddCustomTagButton.IsEnabled = false;
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

        private void RegisteredTagRemoveConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseTagEvent(RegisteredTagRemoveRequested, (sender as FrameworkElement)?.DataContext);
            CloseParentPopupBox(sender as DependencyObject);
        }

        private void RegisteredTagRemoveCancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseParentPopupBox(sender as DependencyObject);
        }

        private void RegisteredTagToggleButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseTagEvent(RegisteredTagToggleRequested, (sender as FrameworkElement)?.DataContext);
        }

        private void AddCustomTagButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseCustomTagAddRequested();
        }

        private void CustomTagTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            RaiseCustomTagAddRequested();
            e.Handled = true;
        }

        private void RaiseCustomTagAddRequested()
        {
            string tagName = NormalizeCustomTagText(CustomTagTextBox.Text);
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            CustomTagAddRequested?.Invoke(this, new TagEditorTagActionEventArgs(tagName));
            CustomTagTextBox.Text = "";
        }

        private static string NormalizeCustomTagText(string text)
        {
            return (text ?? "").Trim().Replace("\r", "").Replace("\n", " ");
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

        private static void CloseParentPopupBox(DependencyObject source)
        {
            PopupBox popupBox = FindAncestor<PopupBox>(source);
            if (popupBox != null)
            {
                popupBox.IsPopupOpen = false;
            }
        }

        private static T FindAncestor<T>(DependencyObject source)
            where T : DependencyObject
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (current is T typed)
                {
                    return typed;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void TagEditorTabView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateResponsiveLayout();
        }

        private void TagEditorTabView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout();
        }

        private void UpdateResponsiveLayout()
        {
            if (ResponsiveLayoutRoot == null || ActualWidth <= 0)
            {
                return;
            }

            bool shouldUseVerticalLayout = ActualWidth < VerticalLayoutThreshold;
            if (_isVerticalLayout == shouldUseVerticalLayout)
            {
                return;
            }

            if (shouldUseVerticalLayout)
            {
                ApplyVerticalLayout();
            }
            else
            {
                ApplyHorizontalLayout();
            }

            _isVerticalLayout = shouldUseVerticalLayout;
        }

        private void ApplyHorizontalLayout()
        {
            ResponsiveLayoutRoot.ColumnDefinitions.Clear();
            ResponsiveLayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
            ResponsiveLayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            ResponsiveLayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.85, GridUnitType.Star) });
            ResponsiveLayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            ResponsiveLayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });

            ResponsiveLayoutRoot.RowDefinitions.Clear();
            ResponsiveLayoutRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            PlacePanel(SelectedTagsDropHost, column: 0, row: 0);
            PlacePanel(RegisteredTagsHost, column: 2, row: 0);
            PlacePanel(SearchTagsHost, column: 4, row: 0);
        }

        private void ApplyVerticalLayout()
        {
            ResponsiveLayoutRoot.ColumnDefinitions.Clear();
            ResponsiveLayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            ResponsiveLayoutRoot.RowDefinitions.Clear();
            ResponsiveLayoutRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.35, GridUnitType.Star) });
            ResponsiveLayoutRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            ResponsiveLayoutRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.5, GridUnitType.Star) });
            ResponsiveLayoutRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            ResponsiveLayoutRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star) });

            PlacePanel(SelectedTagsDropHost, column: 0, row: 0);
            PlacePanel(RegisteredTagsHost, column: 0, row: 2);
            PlacePanel(SearchTagsHost, column: 0, row: 4);
        }

        private static void PlacePanel(FrameworkElement element, int column, int row)
        {
            if (element == null)
            {
                return;
            }

            Grid.SetColumn(element, column);
            Grid.SetRow(element, row);
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
