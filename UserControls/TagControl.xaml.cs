﻿using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace IndigoMovieManager.UserControls
{
    /// <summary>
    /// TagControl.xaml の相互作用ロジック
    /// </summary>
    public partial class TagControl : UserControl
    {
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
                ownerWindow.SearchBox.Text = item.DataContext.ToString();
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
                    int index = ownerWindow.Tabs.SelectedIndex;

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

                    }
                    catch (Exception)
                    {
                        //サムネイル作成中にタグを消すと例外起こるので握りつぶす。あんま良くねぇけど。
                    }
                }
            }
        }
    }
}