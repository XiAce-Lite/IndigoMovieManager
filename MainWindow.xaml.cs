using AvalonDock;
using AvalonDock.Layout.Serialization;
using IndigoMovieManager.DB;
using IndigoMovieManager.ModelViews;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;
using static IndigoMovieManager.Thumbnail.Tools;

namespace IndigoMovieManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        //監視モード
        private enum CheckMode
        {
            Auto,
            Watch,
            Manual
        }
        private Task _thumbCheckTask;
        private CancellationTokenSource _thumbCheckCts = new();

        [GeneratedRegex(@"^\r\n+")]
        private static partial Regex MyRegex();

        private const string RECENT_OPEN_FILE_LABEL = "最近開いたファイル";
        // サムネイルキュー監視の待機間隔（ミリ秒）。
        private const int ThumbnailQueuePollIntervalMs = 3000;
        private Stack<string> recentFiles = new();

        private IEnumerable<MovieRecords> filterList = [];
        private static readonly ConcurrentQueue<QueueObj> queueThumb = [];
        private readonly ThumbnailCreationService _thumbnailCreationService = new();
        private readonly ThumbnailQueueProcessor _thumbnailQueueProcessor = new();

        private DataTable systemData;
        private DataTable movieData;
        private DataTable historyData;
        private DataTable watchData;
        private DataTable bookmarkData;

        // MainWindow クラス内の MainVM フィールドまたはプロパティの宣言を public に変更
        public readonly MainWindowViewModel MainVM;
        internal System.Windows.Point lbClickPoint = new();

        private DateTime _lastSliderTime = DateTime.MinValue;
        private readonly TimeSpan _timeSliderInterval = TimeSpan.FromSeconds(0.1);

        //private DateTime _lastInputTime = DateTime.MinValue;  //インクリメントサーチで使用。一旦オミット。
        private readonly TimeSpan _timeInputInterval = TimeSpan.FromSeconds(0.5);

        //結局、タイマー方式で動画とマニュアルサムネイルのスライダーを同期させた
        private readonly DispatcherTimer timer;
        private bool isDragging = false;

        //マニュアルサムネイル時の右クリックしたカラムの返却を受け取る変数
        private int manualPos = 0;

        //IME起動中的なフラグ。日本語入力中（未変換）にインクリメンタルサーチさせない為。
        private bool _imeFlag = false;

        private static readonly List<FileSystemWatcher> fileWatchers = [];

        //private bool _searchBoxItemSelectedByMouse = false;
        private bool _searchBoxItemSelectedByUser = false;
        private const string ThumbGpuDecodeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string ThumbGpuDecodeCudaValue = "cuda";
        private const string ThumbGpuDecodeOffValue = "off";

        // 設定画面の値を使って、サムネイル並列数を安全な範囲で返す。
        private static int GetThumbnailQueueMaxParallelism()
        {
            int parallelism = Properties.Settings.Default.ThumbnailParallelism;
            if (parallelism < 1) { return 1; }
            if (parallelism > 24) { return 24; }
            return parallelism;
        }

        // 共通設定のGPUデコード有効/無効を実行環境変数へ反映する。
        private static void ApplyThumbnailGpuDecodeSetting()
        {
            string mode = Properties.Settings.Default.ThumbnailGpuDecodeEnabled
                ? ThumbGpuDecodeCudaValue
                : ThumbGpuDecodeOffValue;

            Environment.SetEnvironmentVariable(ThumbGpuDecodeEnvName, mode);
        }

        public MainWindow()
        {
            MainVM = new MainWindowViewModel(); // ← 追加
            
            //前のバージョンのプロパティを引き継ぐぜ。
            Properties.Settings.Default.Upgrade();
            ApplyThumbnailGpuDecodeSetting();

            //イニシャライズの前に、systemテーブルを読み込んで、前回スキン(タブ)を取得する。
            if (Properties.Settings.Default.AutoOpen)
            {
                if (Properties.Settings.Default.LastDoc != null)
                {
                    if (Path.Exists(Properties.Settings.Default.LastDoc))
                    {
                        //前回のデータベースフルパス
                        MainVM.DbInfo.DBFullPath = Properties.Settings.Default.LastDoc;
                        MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(Properties.Settings.Default.LastDoc);

                        //Tabとソートを取得するだけの為に、MovieRecordsを取得する前にやってる。
                        //初回だけはMainWindow_ContentRenderedの処理と重複するかな。
                        GetSystemTable(Properties.Settings.Default.LastDoc);
                    }
                }
            }
            recentFiles.Clear();

            InitializeComponent();

            // アセンブリのファイルバージョンを取得
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

            this.Title = $"Indigo Movie Manager v{version}";

            ContentRendered += MainWindow_ContentRendered;
            Closing += MainWindow_Closing;
            TextCompositionManager.AddPreviewTextInputHandler(SearchBox, OnPreviewTextInput);
            TextCompositionManager.AddPreviewTextInputStartHandler(SearchBox, OnPreviewTextInputStart);
            TextCompositionManager.AddPreviewTextInputUpdateHandler(SearchBox, OnPreviewTextInputUpdate);

            var rootItem = new TreeSource() { Text = RECENT_OPEN_FILE_LABEL, IsExpanded = false };
            MainVM.RecentTreeRoot.Add(rootItem);

            if (Properties.Settings.Default.RecentFiles != null)
            {
                foreach (var item in Properties.Settings.Default.RecentFiles)
                {
                    if (item == null) { continue; }
                    if (string.IsNullOrEmpty(item.ToString())) { continue; }
                    recentFiles.Push(item);
                }
                foreach (var item in recentFiles)
                {
                    var childItem = new TreeSource() { Text = item, IsExpanded = false };
                    rootItem.Add(childItem);
                }
            }

            #region ツリーメニューベタ設定部
            //stack : ダサ杉ダサ蔵。しょうがねぇかなぁ。こればかりは。
            //        判断するところでも、Tagにぶっ込んだラベル文字列で判断してるしなぁ。
            //        最近開いたファイルと見た目を合わせてたかった＆トップノードの1クリックで開きたかったので合わせている。
            /*
            rootItem = new TreeSource() { Text = "設定", IsExpanded = false, IconKind = MaterialDesignThemes.Wpf.PackIconKind.SettingsApplications };
            var childitem = new TreeSource() { Text = "共通設定", IsExpanded = false, IconKind = MaterialDesignThemes.Wpf.PackIconKind.Settings };
            rootItem.Add(childitem);
            childitem = new TreeSource() { Text = "個別設定", IsExpanded = false, IconKind = MaterialDesignThemes.Wpf.PackIconKind.Cogs };
            rootItem.Add(childitem);
            MainVM.ConfigTreeRoot.Add(rootItem);

            rootItem = new TreeSource() { Text = "ツール", IsExpanded = false, IconKind = MaterialDesignThemes.Wpf.PackIconKind.Toolbox };
            childitem = new TreeSource() { Text = "監視フォルダ編集", IsExpanded = false, IconKind = MaterialDesignThemes.Wpf.PackIconKind.Binoculars };
            rootItem.Add(childitem);
            childitem = new TreeSource() { Text = "監視フォルダ更新チェック", IsExpanded = false, IconKind = MaterialDesignThemes.Wpf.PackIconKind.Reload };
            rootItem.Add(childitem);
            childitem = new TreeSource() { Text = "全ファイルサムネイル再作成", IsExpanded = false, IconKind = MaterialDesignThemes.Wpf.PackIconKind.Image };
            rootItem.Add(childitem);
            MainVM.ToolTreeRoot.Add(rootItem);
            */

            #endregion

            DataContext = MainVM;

            if (Path.Exists("layout.xml"))
            {
                XmlLayoutSerializer layoutSerializer = new(uxDockingManager);
                using var reader = new StreamReader("layout.xml");
                layoutSerializer.Deserialize(reader);
            }

            #region Player Initialize
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            timer.Tick += new EventHandler(Timer_Tick);

            //ボリュームと再生速度のスライダー初期値をセット
            uxVideoPlayer.Volume = (double)uxVolumeSlider.Value;

            uxTime.Text = "00:00:00";
            uxVolume.Text = ((int)(uxVolumeSlider.Value * 100)).ToString();
            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;
            #endregion
        }

        // 画面描画完了後の初期復元と、初回タスク起動を行う。
        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            try
            {
                ClearTempJpg(); //一時ファイルの削除

                //ロケーションとサイズの復元
                Left = Properties.Settings.Default.MainLocation.X;
                Top = Properties.Settings.Default.MainLocation.Y;
                Width = Properties.Settings.Default.MainSize.Width;
                Height = Properties.Settings.Default.MainSize.Height;

                //前回起動時のファイルを開く処理
                if (Properties.Settings.Default.AutoOpen)
                {
                    if (Properties.Settings.Default.LastDoc != null)
                    {
                        if (Path.Exists(Properties.Settings.Default.LastDoc))
                        {
                            if (Properties.Settings.Default.AutoOpen)
                            {
                                OpenDatafile(Properties.Settings.Default.LastDoc);
                            }
                        }
                    }
                }

                // サムネイル監視タスクを一度だけ起動
                if (_thumbCheckTask == null || _thumbCheckTask.IsCompleted)
                {
                    _thumbCheckTask = CheckThumbAsync(_thumbCheckCts.Token);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        // 終了時の確認・設定保存・後片付けを行う。
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (Properties.Settings.Default.ConfirmExit)
            {
                var result = MessageBox.Show(this, "本当に終了しますか？", "終了確認", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (result != MessageBoxResult.OK)
                {
                    e.Cancel = true;
                    MenuToggleButton.IsChecked = false;
                    return;
                }
            }

            try
            {
                Properties.Settings.Default.MainLocation = new System.Drawing.Point((int)Left, (int)Top);
                Properties.Settings.Default.MainSize = new System.Drawing.Size((int)Width, (int)Height);
                UpdateSkin();
                UpdateSort();

                Properties.Settings.Default.RecentFiles.Clear();
                Properties.Settings.Default.RecentFiles.AddRange([.. recentFiles.Reverse()]);
                Properties.Settings.Default.Save();

                XmlLayoutSerializer layoutSerializer = new(uxDockingManager);
                using var writer = new StreamWriter("layout.xml");
                layoutSerializer.Serialize(writer);

                if (!string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
                {
                    var keepHistoryData = SelectSystemTable("keepHistory");
                    int keepHistoryCount = Convert.ToInt32(keepHistoryData == "" ? "30" : keepHistoryData);
                    DeleteHistoryTable(MainVM.DbInfo.DBFullPath, keepHistoryCount);
                }
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                _thumbCheckCts.Cancel();
            }
        }

        // IME確定時に検索入力フラグを通常状態へ戻す。
        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            _imeFlag = false;
        }
        // IME変換開始を検知して検索の即時実行を抑制する。
        private void OnPreviewTextInputStart(object sender, TextCompositionEventArgs e)
        {
            _imeFlag = true;
        }
        // IME変換文字が空になったら検索入力フラグを解除する。
        private void OnPreviewTextInputUpdate(object sender, TextCompositionEventArgs e)
        {
            if (e.TextComposition.CompositionText.Length == 0) { _imeFlag = false; }
        }

        //todo : And以外の検索の実装。せめてNOT検索ぐらいまでは…
        //todo : 検索履歴の保管条件（おそらくヒット：ゼロ件超で保管）確認＆修正
        //todo : タグバー代替（保管済み検索条件）の実装
        //stack : プロパティ表示ウィンドウの作成。
        //todo : 重複チェック。本家は恐らくファイル名もチェックで使ってる模様。
        //       こっちで登録しても再度本家に登録されるケースがあったのは、ファイル名の大文字小文字が違ってたから。
        //       movie_name と Hash で重複チェックかなぁ。
        //       本家のmovie_nameは小文字変換かけてる模様。合わせてみたら再登録されなかったので恐らく正解。

        // DBを開いて、画面表示・履歴・監視を現在DBに切り替える。
        private void OpenDatafile(string dbFullPath)
        {
            //強制的に-1にする。前回のタブが0だった場合の対応
            Tabs.SelectedIndex = -1;
            ClearThumbnailQueue();
            watchData?.Clear();
            fileWatchers?.Clear();
            MainVM.DbInfo.SearchKeyword = "";

            MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbFullPath);
            MainVM.DbInfo.DBFullPath = dbFullPath;
            GetSystemTable(dbFullPath);
            MainVM.MovieRecs.Clear();

            GetHistoryTable(dbFullPath);

            if (MainVM.DbInfo.Sort != null)
            {
                FilterAndSort(MainVM.DbInfo.Sort, true);    //ここは両方。オープン時なので。
            }
            if (MainVM.DbInfo.Skin != null)
            {
                SwitchTab(MainVM.DbInfo.Skin);
            }

            //bookmarkのデータ詰める。あとはブックマーク追加時とブックマーク削除時の対応はイベントで。
            GetBookmarkTable();

            _ = CheckFolderAsync(CheckMode.Auto);   //一回きりの追加ファイルがないかのチェック。
            CreateWatcher();                        //FileSystemWatcherの作成。
        }

        // systemテーブルから指定属性値を取り出す。
        public string SelectSystemTable(string attr)
        {
            if (systemData != null)
            {
                DataRow[] drs = systemData.Select($"attr='{attr}'");
                if (drs.Length > 0)
                {
                    return drs[0]["value"].ToString();
                }
            }
            return "";
        }

        // bookmarkテーブルを読み込み、ブックマーク表示用コレクションを再構築する。
        private void GetBookmarkTable()
        {
            bookmarkData = GetData(MainVM.DbInfo.DBFullPath, "select * from bookmark");
            if (bookmarkData != null)
            {
                MainVM.BookmarkRecs.Clear();
                var bookmarkFolder = MainVM.DbInfo.BookmarkFolder;
                var defaultBookmarkFolder = Path.Combine(Directory.GetCurrentDirectory(), "bookmark", MainVM.DbInfo.DBName);
                bookmarkFolder = bookmarkFolder == "" ? defaultBookmarkFolder : bookmarkFolder;

                var list = bookmarkData.AsEnumerable().ToArray();
                foreach (var row in list)
                {
                    var movieFullPath = row["movie_path"].ToString();
                    var ext = Path.GetExtension(movieFullPath);
                    var thumbFile = Path.Combine(bookmarkFolder, movieFullPath);
                    var thumbBody = movieFullPath.Split('[')[0];
                    var frameS = movieFullPath.Split('(')[1];
                    frameS = frameS.Split(')')[0];
                    long frame = 0;
                    if (frameS != "")
                    {
                        frame = Convert.ToInt64(frameS);   //Scoreにフレームぶっ込む。
                    }
                    var item = new MovieRecords
                    {
                        Movie_Id = (long)row["movie_id"],
                        Movie_Name = $"{row["movie_name"]}{ext}",
                        Movie_Body = thumbBody,
                        Last_Date = ((DateTime)row["last_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                        File_Date = ((DateTime)row["file_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                        Regist_Date = ((DateTime)row["regist_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                        View_Count = (long)row["view_count"],
                        Score = frame,
                        Kana = row["kana"].ToString(),
                        Roma = row["roma"].ToString(),
                        IsExists = true, //Path.Exists(thumbFile),
                        Ext = ext,
                        ThumbDetail = thumbFile
                    };
                    MainVM.BookmarkRecs.Add(item);
                }
            }
        }

        // historyテーブルを重複排除して読み込み、検索候補を更新する。
        private void GetHistoryTable(string dbFullPath)
        {
            // 現在のテキストを一時保存
            var currentText = SearchBox.Text;

            // find_textごとに最新の1件のみ取得
            string sql = @"SELECT find_id, find_text, find_date
                            FROM (
                                SELECT *,
                                       ROW_NUMBER() OVER (PARTITION BY find_text ORDER BY find_date DESC) AS rn
                                FROM history
                                )
                            WHERE rn = 1
                            ORDER BY find_date DESC";

            historyData = GetData(dbFullPath, sql);
            if (historyData != null)
            {
                MainVM.HistoryRecs.Clear();
                var list = historyData.AsEnumerable().ToArray();
                var oldtext = new List<string>();
                foreach (var row in list)
                {
                    //重複チェック。履歴は、同じ文字列があったら、上書きしない。
                    if (oldtext.Contains(row["find_text"].ToString())) { continue; }
                    var item = new History
                    {
                        Find_Id = (long)row["find_id"],
                        Find_Text = row["find_text"].ToString(),
                        Find_Date = ((DateTime)row["find_date"]).ToString("yyyy-MM-dd HH:mm:ss")
                    };
                    oldtext.Add(row["find_text"].ToString());
                    MainVM.HistoryRecs.Add(item);
                }
            }
            // テキストを復元
            SearchBox.Text = currentText;
        }

        // systemテーブルからスキン・ソート・各フォルダ設定を反映する。
        private void GetSystemTable(string dbPath)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                string sql = @"SELECT * FROM system";
                systemData = GetData(dbPath, sql);

                var skin = SelectSystemTable("skin");
                MainVM.DbInfo.Skin = skin == "" ? "Default Small" : skin;

                var sort = SelectSystemTable("sort");
                MainVM.DbInfo.Sort = sort == "" ? "1" : sort;

                MainVM.DbInfo.ThumbFolder = SelectSystemTable("thum");

                MainVM.DbInfo.BookmarkFolder = SelectSystemTable("bookmark");
            }
            else
            {
                systemData?.Clear();
            }
        }

        // watchテーブルを指定条件で読み込む。
        private void GetWatchTable(string dbPath, string sql)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                watchData = GetData(dbPath, sql);
            }
        }

        // ソートIDをSQLのORDER BY句へ変換する。
        private static string GetSortWordForSQL(string id)
        {
            string sortWordSQL = id switch
            {
                "0" => "last_date desc",
                "1" => "last_date",
                "2" => "file_date desc",
                "3" => "file_date",
                "6" => "Score desc",
                "7" => "Score",
                "8" => "view_count desc",
                "9" => "view_count",
                "10" => "kana",
                "11" => "kana desc",
                "12" => "movie_name",
                "13" => "movie_name desc",
                "14" => "movie_path",
                "15" => "movie_path desc",
                "16" => "movie_size desc",
                "17" => "movie_size",
                "18" => "regist_date desc",
                "19" => "regist_date",
                "20" => "movie_length desc",
                "21" => "movie_length",
                "22" => "comment1",
                "23" => "comment1 desc",
                "24" => "comment2",
                "25" => "comment2 desc",
                "26" => "comment3",
                "27" => "comment3 desc",
                _ => "",
            };
            return sortWordSQL;
        }

        // 現在ソート条件をsystemテーブルへ保存する。
        private void UpdateSort()
        {
            if (!string.IsNullOrEmpty(MainVM.DbInfo.Sort))
            {
                UpsertSystemTable(Properties.Settings.Default.LastDoc, "sort", MainVM.DbInfo.Sort);
            }
        }

        // 現在タブを互換性を保ってsystemテーブルへ保存する。
        private void UpdateSkin()
        {
            //5x2はあえて書き込まない。互換性の関係で。
            string tabName = Tabs.SelectedIndex switch
            {
                0 => "DefaultSmall",
                1 => "DefaultBig",
                2 => "DefaultGrid",
                3 => "DefaultList",
                _ => "DefaultSmall",
            };
            UpsertSystemTable(Properties.Settings.Default.LastDoc, "skin", tabName);
        }

        // 保存済みスキン名に応じて表示タブを切り替える。
        private void SwitchTab(string skin)
        {
            switch (skin)
            {
                case "DefaultSmall":
                    TabSmall.IsSelected = true;
                    if (SmallList.Items.Count > 0)
                    {
                        SmallList.SelectedIndex = 0;
                    }
                    break;
                case "DefaultBig":
                    TabBig.IsSelected = true;
                    if (BigList.Items.Count > 0)
                    {
                        BigList.SelectedIndex = 0;
                    }
                    break;
                case "DefaultGrid":
                    TabGrid.IsSelected = true;
                    if (GridList.Items.Count > 0)
                    {
                        GridList.SelectedIndex = 0;
                    }
                    break;
                case "DefaultList":
                    TabList.IsSelected = true;
                    if (ListDataGrid.Items.Count > 0)
                    {
                        ListDataGrid.SelectedIndex = 0;
                    }
                    break;
                default:
                    TabSmall.IsSelected = true;
                    if (SmallList.Items.Count > 0)
                    {
                        SmallList.SelectedIndex = 0;
                    }
                    break;
            }
        }

        // 現在タブの先頭アイテムを選択状態にする。
        public void SelectFirstItem()
        {
            switch (Tabs.SelectedIndex)
            {
                case 0:
                    TabSmall.IsSelected = true;
                    if (SmallList.Items.Count > 0)
                    {
                        SmallList.SelectedIndex = 0;
                    }
                    break;
                case 1:
                    TabBig.IsSelected = true;
                    if (BigList.Items.Count > 0)
                    {
                        BigList.SelectedIndex = 0;
                    }
                    break;
                case 2:
                    TabGrid.IsSelected = true;
                    if (GridList.Items.Count > 0)
                    {
                        GridList.SelectedIndex = 0;
                    }
                    break;
                case 3:
                    TabList.IsSelected = true;
                    if (ListDataGrid.Items.Count > 0)
                    {
                        ListDataGrid.SelectedIndex = 0;
                    }
                    break;
                case 4:
                    TabBig10.IsSelected = true;
                    if (BigList10.Items.Count > 0)
                    {
                        BigList10.SelectedIndex = 0;
                    }
                    break;
                default:
                    TabSmall.IsSelected = true;
                    if (SmallList.Items.Count > 0)
                    {
                        SmallList.SelectedIndex = 0;
                    }
                    break;
            }
            //viewExtDetail.Visibility = Visibility.Hidden;
        }

        // 各一覧の再描画と詳細表示のDataContext再設定を行う。
        private void Refresh()
        {
            SmallList.Items.Refresh();
            BigList.Items.Refresh();
            GridList.Items.Refresh();
            ListDataGrid.Items.Refresh();
            BigList10.Items.Refresh();

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }
            viewExtDetail.DataContext = mv;
        }

        // ファイルリネームに追従してDB・サムネイル・ブックマーク名を更新する。
        private async void RenameThumb(string eFullPath, string oldFullPath)
        {
            try
            {
                foreach (var item in MainVM.MovieRecs.Where(x => x.Movie_Path == oldFullPath))
                {
                    item.Movie_Path = eFullPath;
                    item.Movie_Name = Path.GetFileNameWithoutExtension(eFullPath).ToLower();

                    //DB内のデータ更新＆サムネイルのファイル名変更処理
                    UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, item.Movie_Id, "movie_path", item.Movie_Path);
                    UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, item.Movie_Id, "movie_name", item.Movie_Name);

                    //サムネイルのリネーム
                    var checkFileName = Path.GetFileNameWithoutExtension(oldFullPath);
                    var thumbFolder = MainVM.DbInfo.ThumbFolder;
                    var defaultThumbFolder = Path.Combine(Directory.GetCurrentDirectory(), "Thumb", MainVM.DbInfo.DBName);
                    thumbFolder = thumbFolder == "" ? defaultThumbFolder : thumbFolder;

                    if (Path.Exists(thumbFolder))
                    {
                        // ファイルリスト
                        var di = new DirectoryInfo(thumbFolder);
                        EnumerationOptions enumOption = new()
                        {
                            RecurseSubdirectories = true
                        };
                        IEnumerable<FileInfo> ssFiles = di.EnumerateFiles($"*{checkFileName}.#{item.Hash}*.jpg", enumOption);
                        foreach (var thumbFile in ssFiles)
                        {
                            var oldFilePath = thumbFile.FullName;
                            var newFilePath = oldFilePath.Replace(checkFileName, item.Movie_Name, StringComparison.CurrentCultureIgnoreCase);
                            if (item.ThumbPathSmall == oldFilePath) { item.ThumbPathSmall = newFilePath; }
                            if (item.ThumbPathBig == oldFilePath) { item.ThumbPathBig = newFilePath; }
                            if (item.ThumbPathGrid == oldFilePath) { item.ThumbPathGrid = newFilePath; }
                            if (item.ThumbPathList == oldFilePath) { item.ThumbPathList = newFilePath; }
                            if (item.ThumbPathBig10 == oldFilePath) { item.ThumbPathBig10 = newFilePath; }

                            thumbFile.MoveTo(newFilePath, true);
                        }
                    }

                    var bookmarkFolder = MainVM.DbInfo.BookmarkFolder;
                    var defaultBookmarkFolder = Path.Combine(Directory.GetCurrentDirectory(), "bookmark", MainVM.DbInfo.DBName);
                    bookmarkFolder = bookmarkFolder == "" ? defaultBookmarkFolder : bookmarkFolder;

                    if (Path.Exists(bookmarkFolder))
                    {
                        // ファイルリスト
                        var di = new DirectoryInfo(bookmarkFolder);
                        EnumerationOptions enumOption = new()
                        {
                            RecurseSubdirectories = true
                        };
                        IEnumerable<FileInfo> ssFiles = di.EnumerateFiles($"*{checkFileName}*.jpg", enumOption);
                        foreach (var bookMarkJpg in ssFiles)
                        {
                            var dstFile = bookMarkJpg.FullName.Replace(checkFileName, item.Movie_Name, StringComparison.CurrentCultureIgnoreCase);
                            try
                            {
                                File.Move(bookMarkJpg.FullName, dstFile, true);
                            }
                            catch (Exception)
                            {
                                throw;
                            }
                        }
                        //Bookmarkデータの更新
                        UpdateBookmarkRename(MainVM.DbInfo.DBFullPath, checkFileName, item.Movie_Name);
                    }
                }
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    GetBookmarkTable();
                    BookmarkList.Items.Refresh();
                    FilterAndSort(MainVM.DbInfo.Sort, true);
                    Refresh();
                }));
            }
            catch (Exception)
            {
            }
        }

        // DB再取得・検索絞り込み・並び替え・各一覧反映を一括で行う。
        public void FilterAndSort(string id, bool IsGetNew = false)
        {
#if DEBUG
            // Stopwatchクラス生成
            var sw = new Stopwatch();
            TimeSpan ts;
#endif
            //データが取れてない、あるいは強制的に取る場合は、まずDB見に行く。
            if (movieData == null || IsGetNew)
            {
#if DEBUG
                sw.Start();
#endif
                movieData = GetData(MainVM.DbInfo.DBFullPath, $"SELECT * FROM movie order by {GetSortWordForSQL(id)}");
                if (movieData == null) { return; }
#if DEBUG
                sw.Stop();
                ts = sw.Elapsed;
                Debug.WriteLine($"レコード取得経過時間：{ts.Milliseconds} ミリ秒");
#endif
                //データ詰める。
                _ = SetRecordsToSource();
            }

#if DEBUG
            sw.Restart();
#endif
            //まずは絞り込み。MainVMにはオープン時のDBからのデータと、監視で追加されたデータが入っている(最新状態)
            //一旦フィルタリストを最新化する。ここを通ったあとの各タブのデータソースは、このフィルターされたリストとなる（はず）
            filterList = new ObservableCollection<MovieRecords>(MainVM.MovieRecs);

            if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword))
            {
                var searchText = MainVM.DbInfo.SearchKeyword.Trim();

                // クォーテーションで囲まれている場合は、そのまま完全一致検索
                if ((searchText.Length >= 2) &&
                    ((searchText.StartsWith('"') && searchText.EndsWith('"')) ||
                     (searchText.StartsWith('\'') && searchText.EndsWith('\''))))
                {
                    var exact = searchText[1..^1];
                    filterList = filterList.Where(item =>
                        (item.Movie_Name ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase) ||
                        (item.Movie_Path ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase) ||
                        (item.Tags ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase) ||
                        (item.Comment1 ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase) ||
                        (item.Comment2 ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase) ||
                        (item.Comment3 ?? "").Contains(exact, StringComparison.CurrentCultureIgnoreCase)
                    );
                    MainVM.DbInfo.SearchCount = filterList.Count();
                }
                // { ... } 形式の特別処理
                else if (searchText.StartsWith('{') && searchText.EndsWith('}'))
                {
                    var inner = searchText[1..^1].Trim();

                    // notag 特別処理
                    if (inner.Equals("notag", StringComparison.CurrentCultureIgnoreCase))
                    {
                        filterList = filterList.Where(x => string.IsNullOrEmpty(x.Tags));
                        MainVM.DbInfo.SearchCount = filterList.Count();
                    }
                    // dup 特別処理
                    else if (inner.Equals("dup", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // Hashが重複しているものを抽出
                        var dupHashes = filterList
                            .GroupBy(x => x.Hash)
                            .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1)
                            .Select(g => g.Key)
                            .ToHashSet();

                        filterList = filterList.Where(x => dupHashes.Contains(x.Hash));
                        MainVM.DbInfo.SearchCount = filterList.Count();
                    }
                }
                else
                {
                    // " | " でORグループ分割
                    var orGroups = searchText.Split([" | "], StringSplitOptions.RemoveEmptyEntries);

                    filterList = filterList.Where(item =>
                    {
                        // 各ORグループのいずれかにマッチすればOK
                        return orGroups.Any(group =>
                        {
                            // AND条件（半角スペース区切り）
                            var andTerms = group.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                            // 各AND条件をすべて満たすか
                            return andTerms.All(term =>
                            {
                                // 検索対象フィールド
                                var fields = new[]
                                {
                                    item.Movie_Name ?? "",
                                    item.Movie_Path ?? "",
                                    item.Tags ?? "",
                                    item.Comment1 ?? "",
                                    item.Comment2 ?? "",
                                    item.Comment3 ?? ""
                                };

                                if (term.StartsWith('-'))
                                {
                                    // NOT条件（除外）
                                    var keyword = term[1..];
                                    return fields.All(f => !f.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
                                }
                                else
                                {
                                    // AND条件
                                    return fields.Any(f => f.Contains(term, StringComparison.CurrentCultureIgnoreCase));
                                }
                            });
                        });
                    });
                    MainVM.DbInfo.SearchCount = filterList.Count();
                }
            }
            else
            {
                //検索キーワードが入ってないときは、生データの件数を表示する。
                MainVM.DbInfo.SearchCount = MainVM.MovieRecs.Count;
            }

            if (MainVM.DbInfo.SearchCount == 0)
            {
                viewExtDetail.Visibility = Visibility.Collapsed;
            }
            else
            {
                viewExtDetail.Visibility = Visibility.Visible;
            }

            SetSortData(id);

            SmallList.ItemsSource = filterList;
            BigList.ItemsSource = filterList;
            GridList.ItemsSource = filterList;
            ListDataGrid.ItemsSource = filterList;
            BigList10.ItemsSource = filterList;
            Refresh();
#if DEBUG
            sw.Stop();
            ts = sw.Elapsed;
            Debug.WriteLine($"絞り込み経過時間 FilterAndSort：{ts.Milliseconds} ミリ秒");
#endif
        }

        // 現在のfilterListに対してソート順だけを適用する。
        private void SetSortData(string id)
        {
            //ベタ書きの方が分かりやすいっちゃぁ分かりやすいよなぁ。ほんのちょっと早い。
            var query = filterList; // from x in filterList select x;
            switch (id)
            {
                case "0": query = from x in filterList orderby x.Last_Date descending select x; break;
                case "1": query = from x in filterList orderby x.Last_Date select x; break;
                case "2": query = from x in filterList orderby x.File_Date descending select x; break;
                case "3": query = from x in filterList orderby x.File_Date select x; break;
                case "6": query = from x in filterList orderby x.Score descending select x; break;
                case "7": query = from x in filterList orderby x.Score select x; break;
                case "8": query = from x in filterList orderby x.View_Count descending select x; break;
                case "9": query = from x in filterList orderby x.View_Count select x; break;
                case "10": query = from x in filterList orderby x.Kana select x; break;
                case "11": query = from x in filterList orderby x.Kana descending select x; break;
                case "12": query = from x in filterList orderby x.Movie_Name select x; break;
                case "13": query = from x in filterList orderby x.Movie_Name descending select x; break;
                case "14": query = from x in filterList orderby x.Movie_Path select x; break;
                case "15": query = from x in filterList orderby x.Movie_Path descending select x; break;
                case "16": query = from x in filterList orderby x.Movie_Size descending select x; break;
                case "17": query = from x in filterList orderby x.Movie_Size select x; break;
                case "18": query = from x in filterList orderby x.Regist_Date descending select x; break;
                case "19": query = from x in filterList orderby x.Regist_Date select x; break;
                case "20": query = from x in filterList orderby x.Movie_Length descending select x; break;
                case "21": query = from x in filterList orderby x.Movie_Length select x; break;
                case "22": query = from x in filterList orderby x.Comment1 select x; break;
                case "23": query = from x in filterList orderby x.Comment1 descending select x; break;
                case "24": query = from x in filterList orderby x.Comment2 select x; break;
                case "25": query = from x in filterList orderby x.Comment2 descending select x; break;
                case "26": query = from x in filterList orderby x.Comment3 select x; break;
                case "27": query = from x in filterList orderby x.Comment3 descending select x; break;
            }
            filterList = query;
        }

        // 既存filterListを並び替えて画面へ反映する。
        private void SortData(string id)
        {
#if DEBUG
            // Stopwatchクラス生成
            var sw = new Stopwatch();
            TimeSpan ts;
            sw.Start();
#endif
            //ここ以降がソート処理（のはず）
            try
            {
                SetSortData(id);
                SmallList.ItemsSource = filterList;
                BigList.ItemsSource = filterList;
                GridList.ItemsSource = filterList;
                ListDataGrid.ItemsSource = filterList;
                BigList10.ItemsSource = filterList;
                Refresh();
#if DEBUG
                sw.Stop();
                ts = sw.Elapsed;
                Debug.WriteLine($"ソート経過時間：{ts.Milliseconds} ミリ秒");
#endif
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        // DBレコード1件をMovieRecordsへ変換して画面用コレクションへ追加する。
        private void DataRowToViewData(DataRow row)
        {
            string[] thumbErrorPath = [@"errorSmall.jpg", @"errorBig.jpg", @"errorGrid.jpg", @"errorList.jpg", @"errorBig.jpg"];
            string[] thumbPath = new string[Tabs.Items.Count];
            var Hash = row["hash"].ToString();
            var movieFullPath = row["movie_path"].ToString();
            var thumbFile = $"{row["movie_name"]}.#{Hash}.jpg";

            for (int i = 0; i < Tabs.Items.Count; i++)
            {
                TabInfo tbi = new(i, MainVM.DbInfo.DBName, MainVM.DbInfo.ThumbFolder);

                var tempPath = Path.Combine(tbi.OutPath, thumbFile);
                if (Path.Exists(tempPath))
                {
                    thumbPath[i] = tempPath;
                }
                else
                {
                    thumbPath[i] = Path.Combine(Directory.GetCurrentDirectory(), "Images", thumbErrorPath[i]);
                }
            }

            //エクステンションの詳細用サムネ特別処理
            //(5つ目のタブ扱いにする手もあるけど、そうするとタブ増やすときに面倒かなと)
            //だもんでCase 99の所に入れておいた。で、ブックマークの場合のフルパスもここを使う。
            //オブジェクトは、MovieとBookmarkと違うので問題ねぇはず。
            TabInfo tbiExtensionDetail = new(99, MainVM.DbInfo.DBName, MainVM.DbInfo.ThumbFolder);
            var tempPathExtensionDetail = Path.Combine(tbiExtensionDetail.OutPath, thumbFile);
            string thumbPathDetail;
            if (Path.Exists(tempPathExtensionDetail))
            {
                thumbPathDetail = tempPathExtensionDetail;
            }
            else
            {
                //エラー時のサムネはGridと同じタイプを流用
                thumbPathDetail = Path.Combine(Directory.GetCurrentDirectory(), "Images", thumbErrorPath[2]);
            }


            var tags = row["tag"].ToString();
            List<string> tagArray = [];
            if (!string.IsNullOrEmpty(tags))
            {
                var splitTags = tags.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                foreach (var tagItem in splitTags)
                {
                    tagArray.Add(tagItem);
                }
            }
            var tag = MyRegex().Replace(tags, "");

            var ext = Path.GetExtension(movieFullPath);
            var movie_body = Path.GetFileNameWithoutExtension(movieFullPath);

            #region View用のデータにDBからぶち込む
            var item = new MovieRecords
            {
                Movie_Id = (long)row["movie_id"],
                Movie_Name = $"{row["movie_name"]}{ext}",
                Movie_Body = movie_body, // $"{row["movie_name"]}",
                Movie_Path = row["movie_path"].ToString(),
                Movie_Length = new TimeSpan(0, 0, (int)(long)row["movie_length"]).ToString(@"hh\:mm\:ss"),
                Movie_Size = (long)row["movie_size"],
                Last_Date = ((DateTime)row["last_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                File_Date = ((DateTime)row["file_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                Regist_Date = ((DateTime)row["regist_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                Score = (long)row["score"],
                View_Count = (long)row["view_count"],
                Hash = row["hash"].ToString(),
                Container = row["container"].ToString(),
                Video = row["video"].ToString(),
                Audio = row["audio"].ToString(),
                Extra = row["extra"].ToString(),
                Title = row["title"].ToString(),
                Album = row["album"].ToString(),
                Artist = row["artist"].ToString(),
                Grouping = row["grouping"].ToString(),
                Writer = row["writer"].ToString(),
                Genre = row["genre"].ToString(),
                Track = row["track"].ToString(),
                Camera = row["camera"].ToString(),
                Create_Time = row["create_time"].ToString(),
                Kana = row["kana"].ToString(),
                Roma = row["roma"].ToString(),
                Tags = tag, //row["tag"].ToString(),
                Tag = tagArray,
                Comment1 = row["comment1"].ToString(),
                Comment2 = row["comment2"].ToString(),
                Comment3 = row["comment3"].ToString(),
                ThumbPathSmall = thumbPath[0],
                ThumbPathBig = thumbPath[1],
                ThumbPathGrid = thumbPath[2],
                ThumbPathList = thumbPath[3],
                ThumbPathBig10 = thumbPath[4],
                ThumbDetail = thumbPathDetail,
                Drive = Path.GetPathRoot(row["movie_path"].ToString()),
                Dir = Path.GetDirectoryName(row["movie_path"].ToString()),
                IsExists = Path.Exists(movieFullPath),
                Ext = ext
            };
            #endregion
            MainVM.MovieRecs.Add(item);
        }

        // movieData全件を表示用コレクションへ再投入する。
        private Task SetRecordsToSource()
        {
            if (movieData != null)
            {
                MainVM.MovieRecs.Clear();

                var list = movieData.AsEnumerable().ToArray();
                foreach (var row in list)
                {
                    DataRowToViewData(row);
                }
            }
            return Task.CompletedTask;
        }
        /*
        // タブ切替時に不足サムネイルを検出し、必要な再作成キューを積む。
        private async void Tabs_SelectionChangedAsync(object sender, SelectionChangedEventArgs e)
        {
            if (sender as TabControl != null && e.OriginalSource is TabControl)
            {
                ClearThumbnailQueue();

                var tabControl = sender as TabControl;
                int index = tabControl.SelectedIndex;
                // Mainをレンダー後に、強制的に-1にしてるので（TabChangeイベントが発生せず。Index=0のタブが前回だった場合にここの処理が正常動作しない）
                if (index == -1)
                {
#if DEBUG
                    Debug.WriteLine("タブインデックス＝-1");
#endif
                    return;
                }

                MainVM.DbInfo.CurrentTabIndex = index;

                if (!filterList.Any())
                {
#if DEBUG
                    Debug.WriteLine("フィルターリストが空と思われ");
#endif
                    return;
                }
                else
                {
#if DEBUG
                    Debug.WriteLine($"{index}, {filterList.Count()}");
#endif
                }   

                #region LinqのWhereでErrorパスを持つレコードを絞り込む
                //stack : この書き方が何とかならんかなぁ。ダサいなぁ。思いつかないので放置で。
                MovieRecords[] query = [];
                switch (index)
                {
                    case 0:
                        SmallList.ItemsSource = filterList;
                        query = [.. MainVM.MovieRecs.Where(x => x.ThumbPathSmall.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable()];
                        break;
                    case 1:
                        BigList.ItemsSource = filterList;
                        query = [.. MainVM.MovieRecs.Where(x => x.ThumbPathBig.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable()];
                        break;
                    case 2:
                        GridList.ItemsSource = filterList;
                        query = [.. MainVM.MovieRecs.Where(x => x.ThumbPathGrid.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable()];
                        break;
                    case 3:
                        ListDataGrid.ItemsSource = filterList;
                        query = [.. MainVM.MovieRecs.Where(x => x.ThumbPathList.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable()];
                        break;
                    case 4:
                        BigList10.ItemsSource = filterList;
                        query = [.. MainVM.MovieRecs.Where(x => x.ThumbPathBig10.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable()];
                        break;
                }
                #endregion

                SelectFirstItem();

                //query > 0 ってことは、サムネファイルにErrorファイルが割り当てられた＝サムネがねぇデータがあるってこと。
                if (query.Length > 0)
                {
                    //いくらか待たないと、プログレスバーが残ってしまうので、Delayを入れておく。
                    await Task.Delay(1000);

                    //なので、サムネ追加Queueに追加していく
                    foreach (var item in query)
                    {
                        QueueObj tempObj = new()
                        {
                            MovieId = item.Movie_Id,
                            MovieFullPath = item.Movie_Path,
                            Tabindex = index
                        };
                        queueThumb.Enqueue(tempObj);
                    }
                }
            }

            //ここは、タブの中の画像をクリックした時に、詳細表示用の特別なサムネイルを生成するところ。
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }
            if (mv.ThumbDetail.Contains("error"))
            {
                QueueObj tempObj = new()
                {
                    MovieId = mv.Movie_Id,
                    MovieFullPath = mv.Movie_Path,
                    Tabindex = 99
                };
                queueThumb.Enqueue(tempObj);
            }

            //エクステンションの詳細にデータをセットしているところ。
            //リネーム後にもこのようにセットしてやりゃ、反映する。
            viewExtDetail.DataContext = mv;
            viewExtDetail.Visibility = Visibility.Visible;
        }
        */

        private async void Tabs_SelectionChangedAsync(object sender, SelectionChangedEventArgs e)
        {
            if (sender as TabControl != null && e.OriginalSource is TabControl)
            {
                ClearThumbnailQueue();

                var tabControl = sender as TabControl;
                int index = tabControl.SelectedIndex;
                if (index == -1) return;

                MainVM.DbInfo.CurrentTabIndex = index;

                if (!filterList.Any()) return;

                // サムネイルプロパティ名配列
                string[] thumbProps = [
                    nameof(MovieRecords.ThumbPathSmall),
                    nameof(MovieRecords.ThumbPathBig),
                    nameof(MovieRecords.ThumbPathGrid),
                    nameof(MovieRecords.ThumbPathList),
                    nameof(MovieRecords.ThumbPathBig10)
                ];

                // 対応するリストコントロール
                object[] listControls = [
                    SmallList,
                    BigList,
                    GridList,
                    ListDataGrid,
                    BigList10
                ];

                // ItemsSourceを設定
                if (index >= 0 && index < listControls.Length)
                {
                    if (listControls[index] is ItemsControl itemsControl)
                    {
                        itemsControl.ItemsSource = filterList;
                    }

                    // サムネイルパスのプロパティを取得
                    var thumbProp = typeof(MovieRecords).GetProperty(thumbProps[index]);

                    // 検索結果(filterList)から"error"を含むものだけ抽出
                    var query = filterList
                        .Where(x => thumbProp?.GetValue(x)?.ToString()?.Contains("error", StringComparison.CurrentCultureIgnoreCase) == true)
                        .ToArray();

                    SelectFirstItem();

                    if (query.Length > 0)
                    {
                        await Task.Delay(1000);

                        foreach (var item in query)
                        {
                            QueueObj tempObj = new()
                            {
                                MovieId = item.Movie_Id,
                                MovieFullPath = item.Movie_Path,
                                Tabindex = index
                            };
                            _ = TryEnqueueThumbnailJob(tempObj);
                        }
                    }
                }

                // 詳細サムネイル（ThumbDetail）が error の場合も追加
                MovieRecords mv = GetSelectedItemByTabIndex();
                if (mv == null) return;
                if (mv.ThumbDetail.Contains("error", StringComparison.CurrentCultureIgnoreCase))
                {
                    QueueObj tempObj = new()
                    {
                        MovieId = mv.Movie_Id,
                        MovieFullPath = mv.Movie_Path,
                        Tabindex = 99
                    };
                    _ = TryEnqueueThumbnailJob(tempObj);
                }

                viewExtDetail.DataContext = mv;
                viewExtDetail.Visibility = Visibility.Visible;
            }
        }

        // クリック位置から対象サムネイルの秒位置を計算して返す。
        private int GetPlayPosition(int tabIndex, MovieRecords mv, ref int returnPos)
        {
            int msec = 0;

            string currentThumbPath;
            switch (tabIndex)
            {
                case 0: currentThumbPath = mv.ThumbPathSmall; break;
                case 1: currentThumbPath = mv.ThumbPathBig; break;
                case 2: currentThumbPath = mv.ThumbPathGrid; break;
                case 3: currentThumbPath = mv.ThumbPathList; break;
                case 4: currentThumbPath = mv.ThumbPathBig10; break;
                default: return 0;
            }

            if (Path.Exists(currentThumbPath))
            {
                ThumbInfo thumbInfo = new();
                thumbInfo.GetThumbInfo(currentThumbPath);
                if (thumbInfo.IsThumbnail == true)
                {
                    List<System.Drawing.Point> points = [];
                    for (int j = 1; j < thumbInfo.ThumbRows + 1; j++)
                        for (int i = 1; i < thumbInfo.ThumbColumns + 1; i++)
                        {
                            {
                                var pt = new System.Drawing.Point
                                {
                                    X = i * thumbInfo.ThumbWidth,
                                    Y = j * thumbInfo.ThumbHeight
                                };
                                points.Add(pt);
                            }
                        }

                    int secPos = points.Count;
                    for (int i = 0; i < points.Count; i++)
                    {
                        if ((lbClickPoint.X < points[i].X) && (lbClickPoint.Y < points[i].Y))
                        {
                            secPos = i;
                            break;
                        }
                    }
                    msec = thumbInfo.ThumbSec[secPos] * 1000;
                    returnPos = secPos;
                }
            }
            return msec;
        }

        // 一覧タブ上のショートカットキーを各機能へ振り分ける。
        private void Tab_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Tabs.SelectedIndex == -1) { return; }
            if (Tabs.SelectedItem == null) { return; }

            switch (e.Key)
            {
                case Key.Enter:                         //再生
                    PlayMovie_Click(sender, e); break;
                case Key.F6:                            //タグ編集
                    TagEdit_Click(sender, e); break;
                case Key.C:                             //タグのコピー
                    TagCopy_Click(sender, e);
                    break;
                case Key.V:                             //タグの貼り付け
                    TagPaste_Click(sender, e);
                    break;
                case Key.Add:                           //スコアプラス
                case Key.Subtract:                      //スコアマイナス
                    MenuScore_Click(sender, e);
                    break;
                case Key.Delete:                        //登録の削除
                    DeleteMovieRecord_Click(sender, e);
                    break;
                case Key.F2:                            //名前の変更
                    RenameFile_Click(sender, e);
                    break;
                case Key.F12:                           //親フォルダ
                    OpenParentFolder_Click(sender, e);
                    break;
                case Key.P:                             //プロパティ
                    break;
                default:
                    return;
            }
        }

        // ソートコンボ変更時に並び替えと先頭選択を実行する。
        private void ComboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath)) { return; }
            if (sender is ComboBox senderObj)
            {
                if (MainVM.MovieRecs.Count > 0)
                {
                    if (senderObj.SelectedValue != null)
                    {
                        var id = senderObj.SelectedValue;
                        //FilterAndSort(id.ToString(), false);    //ソート順変更時。
                        SortData(id.ToString());
                        SelectFirstItem();
                    }
                }
            }
        }

        // 現在タブから選択中の1件を取得する。
        public MovieRecords GetSelectedItemByTabIndex()
        {
            MovieRecords mv = null;
            switch (Tabs.SelectedIndex)
            {
                case 0: mv = SmallList.SelectedItem as MovieRecords; break;
                case 1: mv = BigList.SelectedItem as MovieRecords; break;
                case 2: mv = GridList.SelectedItem as MovieRecords; break;
                case 3: mv = ListDataGrid.SelectedItem as MovieRecords; break;
                case 4: mv = BigList10.SelectedItem as MovieRecords; break;

                //default: return null;
            }
            return mv;
        }

        // 現在タブから複数選択中のレコード一覧を取得する。
        private List<MovieRecords> GetSelectedItemsByTabIndex()
        {
            List<MovieRecords> mv = [];
            switch (Tabs.SelectedIndex)
            {
                case 0:
                    foreach (MovieRecords item in SmallList.SelectedItems) { mv.Add(item); }
                    break;
                case 1:
                    foreach (MovieRecords item in BigList.SelectedItems) { mv.Add(item); }
                    break;
                case 2:
                    foreach (MovieRecords item in GridList.SelectedItems) { mv.Add(item); }
                    break;
                case 3:
                    foreach (MovieRecords item in ListDataGrid.SelectedItems) { mv.Add(item); }
                    break;
                case 4:
                    foreach (MovieRecords item in BigList10.SelectedItems) { mv.Add(item); }
                    break;
                default: return null;
            }
            return mv;
        }

    }
}
