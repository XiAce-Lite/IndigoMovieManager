using IndigoMovieManager.ModelView;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using static IndigoMovieManager.Tools;
using static IndigoMovieManager.SQLite;
using System.Diagnostics;
using IndigoMovieManager.ModelViews;

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
                if (ctrlFlg)
                {
                    ownerWindow.SearchBox.Text += " " + item.DataContext.ToString();
                }
                else
                {
                    ownerWindow.SearchBox.Text = item.DataContext.ToString();
                }
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
