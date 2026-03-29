using IndigoMovieManager.ModelView;
using IndigoMovieManager.DB;
using Microsoft.Win32;
using Notification.Wpf;
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
        private const string WatchDropToastAreaName = "WatchNotificationArea";
        private readonly NotificationManager _watchDropNotificationManager = new();
        private readonly WatchWindowViewModel WatchVM = new();
        private DataTable watchData;
        private readonly string _dbFullPath;
        private readonly string[] _initialDroppedPaths;
        private bool _initialDropApplied;

        // 監視フォルダ編集画面の初期化。
        // DBのwatch設定を読み込み、ViewModelへバインドする。
        public WatchWindow(string dbFullPath, IEnumerable<string> initialDroppedPaths = null)
        {
            InitializeComponent();
            Closing += WatchWindowClosing;
            Loaded += WatchWindow_Loaded;

            GetWatchTable(dbFullPath);
            DataContext = WatchVM;

            _dbFullPath = dbFullPath;
            _initialDroppedPaths = initialDroppedPaths?.ToArray() ?? [];
        }

        private void WatchWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // メイン画面から渡された初期ドロップ候補は、画面表示後に1回だけ流し込む。
            if (_initialDropApplied || _initialDroppedPaths.Length == 0)
            {
                return;
            }

            _initialDropApplied = true;
            ApplyDroppedDirectories(_initialDroppedPaths, showSummary: true);
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
            ApplyDroppedDirectories(droppedPaths, showSummary: true);
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

        // メイン画面経由でも本画面への直接ドロップでも、同じ登録ロジックへ寄せる。
        private void ApplyDroppedDirectories(
            IEnumerable<string> droppedPaths,
            bool showSummary
        )
        {
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
            if (showSummary)
            {
                ShowDropSummary(result);
            }
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

        // ドロップ結果はモーダルで止めず、右下トーストで短く返す。
        private void ShowDropSummary(WatchFolderDropResult result)
        {
            if (result == null)
            {
                return;
            }

            (string title, string message, NotificationType type) = BuildDropSummaryToast(result);
            ShowDropToast(title, message, type);
        }

        // 追加成功とスキップ理由を、トースト向けの短い文面へまとめる。
        internal static (string Title, string Message, NotificationType Type) BuildDropSummaryToast(
            WatchFolderDropResult result
        )
        {
            if (result == null)
            {
                return ("監視フォルダ登録", "", NotificationType.Information);
            }

            if (result.DirectoriesToAdd.Count == 0)
            {
                return (
                    "監視フォルダ登録",
                    "登録できるフォルダが見つかりませんでした。フォルダをそのままドロップしてください。",
                    NotificationType.Information
                );
            }

            if (result.DuplicateCount == 0 && result.InvalidCount == 0)
            {
                return (
                    "監視フォルダ登録",
                    $"監視フォルダを {result.DirectoriesToAdd.Count} 件追加しました。",
                    NotificationType.Success
                );
            }

            return (
                "監視フォルダ登録",
                $"監視フォルダを {result.DirectoriesToAdd.Count} 件追加しました。 重複: {result.DuplicateCount} 件 / フォルダ以外: {result.InvalidCount} 件",
                NotificationType.Information
            );
        }

        private void ShowDropToast(string title, string message, NotificationType type)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                _watchDropNotificationManager.Show(
                    title,
                    message,
                    type,
                    WatchDropToastAreaName,
                    TimeSpan.FromSeconds(4)
                );
            }
            catch
            {
                // トースト表示に失敗してもドロップ結果自体は保持されているので、ここでは黙って継続する。
            }
        }
    }
}
