using IndigoMovieManager.ModelViews;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);

            var item = (Hyperlink)sender;
            if (item != null)
            {
                if (ownerWindow.Tabs.SelectedItem == null) return;
                MovieRecords mv;
                mv = ownerWindow.GetSelectedItemByTabIndex();
                if (mv == null) return;

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
                        //サムネイル作成中にタグを消すと例外起こるので握りつぶす。あんま良くねぇけど。
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
