using IndigoMovieManager.ModelView;
using IndigoMovieManager.DB;
using Microsoft.Win32;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
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

        private void WatchWindow_PreviewDragOver(object sender, DragEventArgs e)
        {
            // フォルダを含むドロップだけ受け付け、見た目のカーソルも合わせる。
            string[] droppedPaths = GetDroppedPaths(e.Data);
            e.Effects = WatchFolderDropRegistrationPolicy.CanAccept(droppedPaths)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void WatchWindow_Drop(object sender, DragEventArgs e)
        {
            // 既存登録と重複しないフォルダだけ、新規監視フォルダとして末尾へ追加する。
            string[] droppedPaths = GetDroppedPaths(e.Data);
            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                droppedPaths,
                WatchVM.WatchRecs.Select(item => item.Dir)
            );

            foreach (string directoryPath in result.DirectoriesToAdd)
            {
                WatchVM.WatchRecs.Add(new WatchRecords
                {
                    Auto = true,
                    Watch = true,
                    Sub = true,
                    Dir = directoryPath,
                });
            }

            FocusLastAddedRow(result.DirectoriesToAdd.Count);
            ShowDropSummaryIfNeeded(result);
            e.Handled = true;
        }

        // Explorer から渡されるファイルドロップ配列を安全に取り出す。
        private static string[] GetDroppedPaths(IDataObject dataObject)
        {
            if (dataObject == null || !dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                return [];
            }

            return dataObject.GetData(DataFormats.FileDrop) as string[] ?? [];
        }

        // 追加できた最後の行へスクロールして、登録結果をすぐ見えるようにする。
        private void FocusLastAddedRow(int addedCount)
        {
            if (addedCount <= 0 || WatchVM.WatchRecs.Count <= 0)
            {
                return;
            }

            WatchRecords lastAddedItem = WatchVM.WatchRecs[^1];
            WatchDataGrid.SelectedItem = lastAddedItem;
            WatchDataGrid.ScrollIntoView(lastAddedItem);
        }

        // 重複や非フォルダが混ざった時だけ要約を出し、通常成功時はテンポを優先して静かに終える。
        private static void ShowDropSummaryIfNeeded(WatchFolderDropResult result)
        {
            if (result == null)
            {
                return;
            }

            if (result.DirectoriesToAdd.Count == 0)
            {
                MessageBox.Show(
                    "登録できるフォルダが見つかりませんでした。\nフォルダをそのままドロップしてください。",
                    "監視フォルダ登録",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            if (result.DuplicateCount == 0 && result.InvalidCount == 0)
            {
                return;
            }

            MessageBox.Show(
                $"監視フォルダを {result.DirectoriesToAdd.Count} 件追加しました。\n" +
                $"重複: {result.DuplicateCount} 件\n" +
                $"フォルダ以外: {result.InvalidCount} 件",
                "監視フォルダ登録",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }
}
