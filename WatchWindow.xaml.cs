using IndigoMovieManager.ModelView;
using IndigoMovieManager.DB;
using Microsoft.Win32;
using System.ComponentModel;
using System.Data;
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

        // 監視フォルダ編集画面の初期化。
        // DBのwatch設定を読み込み、ViewModelへバインドする。
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
            // 画面の現在状態をそのまま watch テーブルへ保存し直す。
            SQLite.DeleteWatchTable(_dbFullPath);
            //データベースへ書き込む。
            foreach (WatchRecords item in WatchVM.WatchRecs)
            {
                if (string.IsNullOrEmpty(item.Dir)) { continue; }
                SQLite.InsertWatchTable(_dbFullPath, item);
            }
        }

        private void GetWatchTable(string dbPath)
        {
            // watchテーブルを読み込み、画面表示用の WatchRecs を再構築する。
            WatchVM.WatchRecs.Clear();
            if (!string.IsNullOrEmpty(dbPath))
            {
                watchData = SQLite.GetData(dbPath, $"SELECT * FROM watch");
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
            // 監視フォルダ編集画面を閉じる。
            Close();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            // 選択行の監視フォルダをダイアログで差し替える。
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
