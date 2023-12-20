using IndigoMovieManager.ModelView;
using Microsoft.Win32;
using System.ComponentModel;
using System.Data;
using static IndigoMovieManager.SQLite;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace IndigoMovieManager
{
    /// <summary>
    /// WatchWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class WatchWindow : Window
    {
        private readonly WatchWindowViewModel WatchVM = new();
        private DataTable watchData;
        private readonly string _dbFullPath;

        public WatchWindow(string dbFullPath)
        {
            InitializeComponent();
            Closing += WatchWindowClosing;

            GetWatchTable(dbFullPath);
            DataContext = WatchVM;

            _dbFullPath = dbFullPath;
        }

        private void WatchWindowClosing(object sender, CancelEventArgs e)
        {
            DeleteWatchTable(_dbFullPath);
            //データベースへ書き込む。
            foreach (WatchRecords item in WatchVM.WatchRecs)
            {
                if (string.IsNullOrEmpty(item.Dir)) { continue; }
                InsertWatchTable(_dbFullPath, item);
            }
        }

        private void GetWatchTable(string dbPath)
        {
            WatchVM.WatchRecs.Clear();
            if (!string.IsNullOrEmpty(dbPath))
            {
                watchData = GetData(dbPath, $"SELECT * FROM watch");
                var list = watchData.AsEnumerable().ToArray();
                foreach (var row in list)
                {
                    var item = new WatchRecords
                    {
                        Auto = (long)row["auto"] == 1,
                        Watch = (long)row["watch"] == 1,
                        Sub = (long)row["sub"] == 1,
                        Dir = row["dir"].ToString()
                    };
                    WatchVM.WatchRecs.Add(item);
                }
            }
        }

        private void BtnReturn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {

            try
            {

                WatchRecords item = (WatchRecords)WatchDataGrid.SelectedItem;

                var ofd = new OpenFolderDialog
                {
                    InitialDirectory = item.Dir.ToString() ?? Directory.GetCurrentDirectory(),
                    Multiselect = false,
                    Title = "監視フォルダの選択",
                };

                var result = ofd.ShowDialog();
                if (result == true)
                {
                    item.Dir = ofd.FolderName;
                }

            }
            catch (InvalidCastException)
            {
            }
        }
    }
}
