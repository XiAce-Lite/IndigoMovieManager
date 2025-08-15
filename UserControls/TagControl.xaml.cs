using IndigoMovieManager.ModelViews;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static IndigoMovieManager.SQLite;
using static IndigoMovieManager.Tools;

namespace IndigoMovieManager.UserControls
{
    /// <summary>
    /// TagControl.xaml の相互作用ロジック
    /// </summary>
    public partial class TagControl : UserControl
    {
        private bool ctrlFlg = false;
        public TagControl()
        {
            InitializeComponent();
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);
            var item = (Hyperlink)sender;
            if (item != null)
            {
                string keyword;
                if (ctrlFlg)
                {
                    // 既存のテキストにスペース区切りで追加
                    keyword = ownerWindow.SearchBox.Text + " " + item.DataContext.ToString();
                }
                else
                {
                    // 単独クリック時はそのタグのみ
                    keyword = item.DataContext.ToString();
                }

                // 検索キーワードをSearchBoxとViewModelにセット
                ownerWindow.SearchBox.Text = keyword;
                ownerWindow.MainVM.DbInfo.SearchKeyword = keyword;

                // 検索処理を実行
                ownerWindow.FilterAndSort(ownerWindow.MainVM.DbInfo.Sort, true);
                ownerWindow.SelectFirstItem();

                // SearchBoxにフォーカスを当てる
                ownerWindow.SearchBox.Focus();
            }
        }

        // ビジュアルツリーを上にたどって親を探す
        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            // まずDataGridRow/ListViewItem/ListBoxItemを探す
            var container = FindParent<DataGridRow>(this)
                         ?? (DependencyObject)FindParent<ListViewItem>(this)
                         ?? FindParent<ListBoxItem>(this);

            // そこからItemsControl（DataGrid/ListView/ListBox）を探す
            ItemsControl parent = null;
            if (container is DataGridRow dgr)
                parent = ItemsControl.ItemsControlFromItemContainer(dgr);
            else if (container is ListViewItem lvi)
                parent = ItemsControl.ItemsControlFromItemContainer(lvi);
            else if (container is ListBoxItem lbi)
                parent = ItemsControl.ItemsControlFromItemContainer(lbi);

            // 本来の行データ（MovieRecords）を取得
            object itemData = null;
            // DataGridRow/ListViewItem/ListBoxItemのDataContextがMovieRecords
            if (container is FrameworkElement fe && fe.DataContext is MovieRecords rec)
                itemData = rec;

            // 選択状態をitemDataのみに
            if (parent != null && itemData != null)
            {
                if (parent is DataGrid dg)
                {
                    dg.SelectedItems.Clear();
                    dg.SelectedItem = itemData;
                    dg.ScrollIntoView(itemData);
                }
                else if (parent is ListView lv)
                {
                    lv.SelectedItems.Clear();
                    lv.SelectedItem = itemData;
                    lv.ScrollIntoView(itemData);
                }
                else if (parent is ListBox lb)
                {
                    lb.SelectedItems.Clear();
                    lb.SelectedItem = itemData;
                    lb.ScrollIntoView(itemData);
                }
            }

            // 既存のタグ削除処理
            MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);
            var item = (Hyperlink)sender;
            if (item != null)
            {
                if (ownerWindow.Tabs.SelectedItem == null) return;
                if (itemData is not MovieRecords mv) return;

                if (mv.Tag.Contains(item.DataContext))
                {
                    mv.Tag.Remove(item.DataContext.ToString());
                    mv.Tags = ConvertTagsWithNewLine(mv.Tag);
                    int index = ownerWindow.Tabs.SelectedIndex;

                    //タグをDBに入れる仕掛け。
                    var dt = (MainWindowViewModel)ownerWindow.DataContext;
                    UpdateMovieSingleColumn(dt.DbInfo.DBFullPath, mv.Movie_Id, "tag", mv.Tags);

                    try
                    {
                        switch (index)
                        {
                            case 0: ownerWindow.SmallList.Items.Refresh(); break;
                            case 1: ownerWindow.BigList.Items.Refresh(); break;
                            case 2: ownerWindow.GridList.Items.Refresh(); break;
                            case 3: ownerWindow.ListDataGrid.Items.Refresh(); break;
                            case 4: ownerWindow.BigList10.Items.Refresh(); break;
                            default: break;
                        }
                        ownerWindow.viewExtDetail.Refresh();
                    }
                    catch (Exception)
                    {
                        //サムネイル作成中にタグを消すと例外起こるので握りつぶす。
                    }
                }
            }
        }

        private void TagGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl)
            {
                ctrlFlg = true;
            }
        }

        private void TagGrid_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl)
            {
                ctrlFlg = false;
            }
        }
    }
}
