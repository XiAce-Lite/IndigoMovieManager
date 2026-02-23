using IndigoMovieManager.Thumbnail;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 一覧選択時に詳細表示と不足サムネイルの再作成キュー投入を行う。
        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                viewExtDetail.Visibility = Visibility.Collapsed;
                return;
            }
            viewExtDetail.DataContext = mv;
            viewExtDetail.Visibility = Visibility.Visible;

            if (!string.IsNullOrEmpty(mv.ThumbDetail) &&
                mv.ThumbDetail.Contains("error", StringComparison.CurrentCultureIgnoreCase))
            {
                QueueObj tempObj = new()
                {
                    MovieId = mv.Movie_Id,
                    MovieFullPath = mv.Movie_Path,
                    Tabindex = 99
                };
                _ = TryEnqueueThumbnailJob(tempObj);
            }
        }

        // DataGridは直接編集させず、専用操作経由に寄せる。
        private void ListDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            e.Cancel = true;
        }

        // サムネイルクリック時に選択行とクリック位置を同期する。
        private void Label_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Label label && label.DataContext is MovieRecords record)
            {
                ListDataGrid.SelectedItem = record;
                lbClickPoint = e.GetPosition(label);
            }
        }

        // SmallListのアイテム内要素クリック時に選択状態にするイベントハンドラ
        private void SmallListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    return;
                }

                if (!item.IsSelected)
                {
                    item.IsSelected = true;
                    SmallList.SelectedItem = item.DataContext;
                }
            }
        }

        // BigListのアイテム内要素クリック時に選択状態にするイベントハンドラ
        private void BigListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    return;
                }

                if (!item.IsSelected)
                {
                    item.IsSelected = true;
                    BigList.SelectedItem = item.DataContext;
                }
            }
        }

        // BigList10のアイテム内要素クリック時に選択状態にするイベントハンドラ
        private void BigList10Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    return;
                }

                if (!item.IsSelected)
                {
                    item.IsSelected = true;
                    BigList10.SelectedItem = item.DataContext;
                }
            }
        }
    }
}
