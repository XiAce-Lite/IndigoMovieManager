﻿using AvalonDock;
using AvalonDock.Layout.Serialization;
using IndigoMovieManager.ModelView;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using Notification.Wpf;
using Notification.Wpf.Classes;
using OpenCvSharp;
using System.ComponentModel;
using System.Data;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using static IndigoMovieManager.SQLite;
using static IndigoMovieManager.Tools;

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
        private readonly CancellationTokenSource _thumbCheckCts = new();

        [GeneratedRegex(@"^\r\n+")]
        private static partial Regex MyRegex();

        private const string RECENT_OPEN_FILE_LABEL = "最近開いたファイル";
        private Stack<string> recentFiles = new();

        private IEnumerable<MovieRecords> filterList = [];
        private static readonly Queue<QueueObj> queueThumb = [];

        private DataTable systemData;
        private DataTable movieData;
        private DataTable historyData;
        private DataTable watchData;
        private DataTable bookmarkData;

        private readonly MainWindowViewModel MainVM = new();
        internal System.Windows.Point lbClickPoint = new();

        private DateTime _lastSliderTime = DateTime.MinValue;
        private readonly TimeSpan _timeSliderInterval = TimeSpan.FromSeconds(0.1);

        private DateTime _lastInputTime = DateTime.MinValue;
        private readonly TimeSpan _timeInputInterval = TimeSpan.FromSeconds(1.5);

        //結局、タイマー方式で動画とマニュアルサムネイルのスライダーを同期させた
        private readonly DispatcherTimer timer;
        private bool isDragging = false;

        //マニュアルサムネイル時の右クリックしたカラムの返却を受け取る変数
        private int manualPos = 0;

        //IME起動中的なフラグ。日本語入力中（未変換）にインクリメンタルサーチさせない為。
        private bool _imeFlag = false;

        private static readonly List<FileSystemWatcher> fileWatchers = [];

        public MainWindow()
        {
            //前のバージョンのプロパティを引き継ぐぜ。
            Properties.Settings.Default.Upgrade();

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

        /// <summary>
        /// ファイル追加
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileChanged(object sender, FileSystemEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath);
            string checkExt = Properties.Settings.Default.CheckExt.Replace("*", "");
            string[] checkExts = checkExt.Split(',');

            if (checkExts.Contains(ext))
            {
                //追加があった場合のみ対応。削除と更新は無視。
                if (e.ChangeType == WatcherChangeTypes.Created)
                {
                    MovieInfo mvi = new(e.FullPath);
                    _ = InsertMovieTable(MainVM.DbInfo.DBFullPath, mvi);
                    DataTable dt = GetData(MainVM.DbInfo.DBFullPath, "select * from movie order by movie_id desc");
                    DataRowToViewData(dt.Rows[0]);

                    QueueObj newFileForThumb = new()
                    {
                        MovieId = mvi.MovieId,
                        MovieFullPath = mvi.MoviePath,
                        Tabindex = MainVM.DbInfo.CurrentTabIndex
                    };
                    queueThumb.Enqueue(newFileForThumb);
                }
            }
        }

        /// <summary>
        /// ファイル名変更
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileRenamed(object sender, RenamedEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath);
            string checkExt = Properties.Settings.Default.CheckExt.Replace("*", "");
            string[] checkExts = checkExt.Split(',');
            var eFullPath = e.FullPath;
            var oldFullPath = e.OldFullPath;

            if (checkExts.Contains(ext))
            {
#if DEBUG
                string s = string.Format($"{DateTime.Now:yyyy/MM/dd HH:mm:ss} :");
                s += $"【{e.ChangeType}】{e.OldName} → {e.FullPath}";
                Debug.WriteLine(s);
#endif
                //本家では、Renameは即反映してる様子。
                //このタイミングでは、新旧のファイル名がフルパスで取得可能。
                //旧ファイル名でDB検索、対象がヒットしたら、新ファイル名に変更。
                RenameThumb(eFullPath, oldFullPath);
            }
        }

        private void RunWatcher(string watchFolder, bool sub)
        {
            if (!Path.Exists(watchFolder))
            {
                return;
            }

            // パクリ元：https://dxo.co.jp/blog/archives/3323
            FileSystemWatcher item = new()
            {
                // 監視対象ディレクトリを指定する
                Path = watchFolder,

                // 監視対象の拡張子を指定する（全てを指定する場合は空にする）
                Filter = "",

                // 監視する変更を指定する
                NotifyFilter = NotifyFilters.LastAccess |
                                NotifyFilters.LastWrite |
                                NotifyFilters.FileName |
                                NotifyFilters.DirectoryName,

                // サブディレクトリ配下も含めるか指定する
                IncludeSubdirectories = sub,

                // 通知を格納する内部バッファ 既定値は 8192 (8 KB)  4 KB ～ 64 KB
                InternalBufferSize = 1024 * 32
            };

            // ファイル変更、作成、削除のイベントをファイル変更メソッドにあげる
            item.Changed += new FileSystemEventHandler(FileChanged);
            item.Created += new FileSystemEventHandler(FileChanged);
            //item.Deleted += new FileSystemEventHandler(FileChanged);

            // ファイル名変更のイベントをファイル名変更メソッドにあげる
            item.Renamed += new RenamedEventHandler(FileRenamed);
            item.EnableRaisingEvents = true;

            fileWatchers.Add(item);
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            _imeFlag = false;
        }
        private void OnPreviewTextInputStart(object sender, TextCompositionEventArgs e)
        {
            _imeFlag = true;
        }
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

        private void OpenDatafile(string dbFullPath)
        {
            //強制的に-1にする。前回のタブが0だった場合の対応
            Tabs.SelectedIndex = -1;
            queueThumb.Clear();
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

        private void GetHistoryTable(string dbFullPath)
        {
            //string sql = @"select * from history order by find_date desc";
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
        }

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

        private void GetWatchTable(string dbPath, string sql)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                watchData = GetData(dbPath, sql);
            }
        }

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

        private void UpdateSort()
        {
            if (!string.IsNullOrEmpty(MainVM.DbInfo.Sort))
            {
                UpsertSystemTable(Properties.Settings.Default.LastDoc, "sort", MainVM.DbInfo.Sort);
            }
        }

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

        private void SelectFirstItem()
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

        private void FilterAndSort(string id, bool IsGetNew = false)
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
            filterList = MainVM.MovieRecs;

            if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword))
            {
                var searchText = MainVM.DbInfo.SearchKeyword.Trim();

                // { ... } 形式の特別処理
                if (searchText.StartsWith('{') && searchText.EndsWith('}'))
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

        private async void Tabs_SelectionChangedAsync(object sender, SelectionChangedEventArgs e)
        {
            if (sender as TabControl != null && e.OriginalSource is TabControl)
            {
                queueThumb.Clear();
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

        private void TagCopy_Click(object sender, RoutedEventArgs e)
        {
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            if (mv.Tags == null) { return; }
            if (mv.Tags.Length == 0) { return; }

            Clipboard.SetData(DataFormats.Text, mv.Tags);
        }

        private void TagPaste_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText(TextDataFormat.Text)) { return; }

            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null) { return; }

            foreach (var rec in mv)
            {
                rec.Tags = Clipboard.GetText(TextDataFormat.Text);

                List<string> tagArray = [];
                var splitTags = rec.Tags.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                foreach (var tagItem in splitTags.Distinct())
                {
                    tagArray.Add(tagItem);
                }
                rec.Tag = tagArray;

                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, rec.Movie_Id, "tag", rec.Tags);
            }

            Refresh();
        }

        private void TagAdd_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) { return; }

            MovieRecords dt = new();
            var tagEditWindow = new TagEdit
            {
                Title = "選択全ファイルにタグを追加",
                Owner = this,
                DataContext = dt,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            tagEditWindow.ShowDialog();

            if (tagEditWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null) { return; }

            var dataContext = tagEditWindow.DataContext as MovieRecords;
            //リスト状態のタグと、改行付のタグを作る所
            var tagsEditedWithNewLine = dataContext.Tags;

            foreach (var rec in mv)
            {
                tagsEditedWithNewLine += Environment.NewLine + rec.Tags;

                string tagsWithNewLine = "";
                List<string> tagArray = [];
                if (!string.IsNullOrEmpty(tagsEditedWithNewLine))
                {
                    var splitTags = tagsEditedWithNewLine.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                    //意味ないように見えるけど（だって改行付データだもん）改行を除いて、重複も除く
                    tagsWithNewLine = ConvertTagsWithNewLine([.. splitTags]);

                    foreach (var tagItem in splitTags.Distinct())
                    {
                        tagArray.Add(tagItem);
                    }
                }
                rec.Tag = tagArray;
                rec.Tags = tagsWithNewLine;

                //DBのタグを更新する。
                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, rec.Movie_Id, "tag", rec.Tags);
            }
            Refresh();
        }

        private void TagDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mvSelected = GetSelectedItemByTabIndex();
            if (mvSelected == null) { return; }

            var tagEditWindow = new TagEdit
            {
                Title = "選択全ファイルからタグを削除",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = mvSelected
            };
            tagEditWindow.ShowDialog();

            if (tagEditWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null) { return; }

            var dataContext = tagEditWindow.DataContext as MovieRecords;
            //リスト状態のタグと、改行付のタグを作る所
            var tagsEditedWithNewLine = dataContext.Tags;

            foreach (var rec in mv)
            {
                List<string> tagArray = rec.Tag;
                if (!string.IsNullOrEmpty(tagsEditedWithNewLine))
                {
                    var splitTags = tagsEditedWithNewLine.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var tagItem in splitTags.Distinct())
                    {
                        tagArray.Remove(tagItem);
                    }
                    var tagsWithNewLine = ConvertTagsWithNewLine([.. tagArray]);
                    rec.Tag = tagArray;
                    rec.Tags = tagsWithNewLine;

                    //DBのタグを更新する。
                    UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, rec.Movie_Id, "tag", rec.Tags);
                }
            }

            Refresh();
        }

        private void TagEdit_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            var tagEditWindow = new TagEdit
            {
                Title = "タグ編集",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = mv
            };
            tagEditWindow.ShowDialog();

            if (tagEditWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            var dc = tagEditWindow.DataContext as MovieRecords;

            //リスト状態のタグと、改行付のタグを作る所
            var tagsEditedWithNewLine = dc.Tags;
            List<string> tagArray = [];
            if (!string.IsNullOrEmpty(tagsEditedWithNewLine))
            {
                var splitTags = tagsEditedWithNewLine.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                //意味ないように見えるけど（だって改行付データだもん）改行を除いて、重複も除く
                string tagsWithNewLine = ConvertTagsWithNewLine([.. splitTags]);

                foreach (var tagItem in splitTags.Distinct())
                {
                    tagArray.Add(tagItem);
                }
                mv.Tag = tagArray;
                mv.Tags = tagsWithNewLine;

                //DBのタグを更新する。
                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, mv.Movie_Id, "tag", mv.Tags);

                Refresh();
            }
        }

        private void MenuCopyAndMove_Click(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;

            if (!(item.Name is "FileCopy" or "FileMove"))
            {
                return;
            }

            var dlgTitle = item.Name == "FileCopy" ? "コピー先の選択" : "移動先の選択";
            var dlg = new OpenFolderDialog
            {
                Title = dlgTitle,
                Multiselect = false,
                AddToRecent = true
            };

            var ret = dlg.ShowDialog();

            if (ret == true)
            {
                if (Tabs.SelectedItem == null) { return; }

                List<MovieRecords> mv;
                mv = GetSelectedItemsByTabIndex();
                if (mv == null) { return; }

                var destFolder = dlg.FolderName;
                foreach (var watcher in fileWatchers)
                {
                    if (watcher.Path == destFolder)
                    {
                        watcher.EnableRaisingEvents = false;
                    }
                }

                foreach (var rec in mv)
                {
                    var destName = Path.Combine(dlg.FolderName, Path.GetFileName(rec.Movie_Path));


                    if (item.Name == "FileCopy")
                    {
                        File.Copy(rec.Movie_Path, destName, true);
                    }
                    else
                    {
                        File.Move(rec.Movie_Path, destName, true);
                        rec.Movie_Path = destName;
                        rec.Dir = destFolder;
                        UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, rec.Movie_Id, "movie_path", destName);
                        Refresh();
                    }

                }

                foreach (var watcher in fileWatchers)
                {
                    if (watcher.Path == destFolder)
                    {
                        watcher.EnableRaisingEvents = true;
                    }
                }
            }
        }

        private void MenuScore_Click(object sender, RoutedEventArgs e)
        {
            string keyName = "";
            if (sender is not MenuItem menuItem)
            {
                if (e is KeyEventArgs key)
                {
                    keyName = key.Key.ToString();
                }
            }
            else
            {
                keyName = menuItem.Name;
            }

            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            if (keyName.ToLower() is "add" or "scoreplus")
            {
                mv.Score += 1;
            }
            else if (keyName.ToLower() is "subtract" or "scoreminus")
            {
                mv.Score -= 1;
            }

            //DBのスコアを更新する。
            UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, mv.Movie_Id, "score", mv.Score);
        }

        private void OpenParentFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            if (Path.Exists(mv.Movie_Path))
            {
                if (Path.Exists(mv.Dir))
                {
                    Process.Start("explorer.exe", $"/select,{mv.Movie_Path}");
                }
            }
        }

        private void RenameFile_Click(object sender, RoutedEventArgs e)
        {
            string keyName = "";
            if (sender is not MenuItem menuItem)
            {
                if (e is KeyEventArgs keyEvent)
                {
                    keyName = keyEvent.Key.ToString();
                }
            }
            else
            {
                keyName = menuItem.Name;
            }

            if (!(keyName.ToLower() is "f2" or "renamefile"))
            {
                return;
            }

            if (Tabs.SelectedItem == null) { return; }
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            //mv送っちゃうと、エクステンションの詳細も連動するのよね。当たり前だけど。
            //なので地味に使うところだけコピー。
            var body = Path.GetFileNameWithoutExtension(mv.Movie_Path);
            MovieRecords dt = new()
            {
                Movie_Id = mv.Movie_Id,
                Movie_Body = body,
                Movie_Path = mv.Movie_Path,
                Movie_Name = mv.Movie_Name,
                Ext = mv.Ext
            };

            var renameWindow = new RenameFile
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = dt
            };
            renameWindow.ShowDialog();

            if (renameWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            if (dt.Movie_Body == mv.Movie_Body && dt.Ext == mv.Ext)
            {
                return;
            }

            //リネーム。
            var checkFileName = mv.Movie_Body;
            var newFilePath = dt.Movie_Body;
            var checkExt = mv.Ext;
            var newExt = dt.Ext;

            //実態ファイルのリネームと新旧ファイルパス作成
            FileInfo mvFile = new(mv.Movie_Path);
            var destMoveFile = mv.Movie_Path.Replace(checkFileName, newFilePath);
            var destFolder = Path.GetDirectoryName(destMoveFile);
            destMoveFile = destMoveFile.Replace(checkExt, newExt);
            mvFile.MoveTo(destMoveFile, true);

            //監視の一時停止（あれば）
            foreach (var watcher in fileWatchers)
            {
                if (watcher.Path == destFolder)
                {
                    watcher.EnableRaisingEvents = false;
                }
            }

            //監視時のリネーム処理の実態を呼び出す。
            RenameThumb(destMoveFile, mv.Movie_Path);

            //監視の再開（あれば）
            foreach (var watcher in fileWatchers)
            {
                if (watcher.Path == destFolder)
                {
                    watcher.EnableRaisingEvents = true;
                }
            }

            //stack : ここでもやっぱりエクステンションの詳細名称が追従しない。
            //タブの中をクリックしたとき、最後にデータをセットしてるんだけども、
            //ListViewのSelectedIndexを再設定してデータ入れても更新されなかったんだよねぇ。
        }

        private void DeleteMovieRecord_Click(object sender, RoutedEventArgs e)
        {
            string keyName = "";
            if (sender is not MenuItem menuItem)
            {
                if (e is KeyEventArgs keyEvent)
                {
                    keyName = keyEvent.Key.ToString();
                }
            }
            else
            {
                keyName = menuItem.Name;
            }

            if (!(keyName.ToLower() is "delete" or "deletemovie" or "deletefile"))
            {
                return;
            }

            if (Tabs.SelectedItem == null) { return; }

            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null) { return; }

            string msg = $"登録からデータを削除します\n（監視対象の場合、再監視で復活します）";
            string title = "登録から削除します";
            string radio1Content = "";
            string radio2Content = "";
            bool useRadio = false;

            if (keyName.Equals("deletefile", StringComparison.CurrentCultureIgnoreCase))
            {
                msg = "登録元のファイルを削除します。";
                title = "ファイル削除";
                useRadio = true;
                radio1Content = "ゴミ箱に移動して削除";
                radio2Content = "ディスクから完全に削除";
            }

            var dialogWindow = new MessageBoxEx(this)
            {
                CheckBoxContent = "サムネイルも削除する",
                UseRadioButton = useRadio,
                UseCheckBox = true,
                CheckBoxIsChecked = true,
                DlogMessage = msg,
                DlogTitle = title,
                Radio1Content = radio1Content,
                Radio2Content = radio2Content,
                PackIconKind = MaterialDesignThemes.Wpf.PackIconKind.ExclamationBold
            };

            dialogWindow.ShowDialog();
            if (dialogWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            foreach (var rec in mv)
            {
                if (dialogWindow.checkBox.IsChecked == true)
                {
                    //サムネも消す。
                    var checkFileName = rec.Movie_Body;
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
                        IEnumerable<FileInfo> ssFiles = di.EnumerateFiles($"*{checkFileName}.#{rec.Hash}*.jpg", enumOption);
                        foreach (var item in ssFiles)
                        {
                            item.Delete();
                        }
                    }
                }
                DeleteMovieTable(MainVM.DbInfo.DBFullPath, rec.Movie_Id);

                //実ファイルの削除、2パターン
                if (keyName.Equals("deletefile", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (dialogWindow.radioButton1.IsChecked == true)
                    {
                        //ゴミ箱送り。
                        FileSystem.DeleteFile(rec.Movie_Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    }
                    else
                    {
                        //実削除
                        File.Delete(rec.Movie_Path);
                    }
                }

            }
            FilterAndSort(MainVM.DbInfo.Sort, true);    //登録からの削除。これは読み直しで良いっぽい。
        }

        private void BtnReCreateThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                MessageBox.Show("管理ファイルが選択されていません。", Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            if (Tabs.SelectedItem == null) { return; }

            var dialogWindow = new MessageBoxEx(this)
            {
                DlogTitle = "サムネイルの再作成",
                DlogMessage = $"サムネイルを再作成します。よろしいですか？",
                PackIconKind = MaterialDesignThemes.Wpf.PackIconKind.EventQuestion
            };

            dialogWindow.ShowDialog();
            if (dialogWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            MenuToggleButton.IsChecked = false;
            foreach (var item in MainVM.MovieRecs)
            {
                QueueObj tempObj = new()
                {
                    MovieId = item.Movie_Id,
                    MovieFullPath = item.Movie_Path,
                    Tabindex = Tabs.SelectedIndex
                };
                queueThumb.Enqueue(tempObj);
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                InitialDirectory = Directory.GetCurrentDirectory(),
                RestoreDirectory = true,
                Filter = "設定ファイル(*.wb)|*.wb|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Title = "設定ファイル(.wb）の選択",
                OverwritePrompt = false
            };

            var result = sfd.ShowDialog();
            if (result == true)
            {
                if (Path.Exists(sfd.FileName))
                {
                    MessageBox.Show($"{sfd.FileName}は既に存在します。", "新規作成", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                MenuToggleButton.IsChecked = false;
                CreateDatabase(sfd.FileName);
                ReStackRecentTree(sfd.FileName);
                OpenDatafile(sfd.FileName);
                Properties.Settings.Default.LastDoc = sfd.FileName;
                Properties.Settings.Default.Save();
            }
        }

        private void ReStackRecentTree(string newItem)
        {
            var rootItem = MainVM.RecentTreeRoot[0];

            Stack<string> temp = new();

            //古い履歴から順に。
            foreach (var item in recentFiles.Reverse())
            {
                //今開いたファイルが直近履歴で開いていた場合、一番上に上げたいのよね。
                //なのでそれ以外は普通に入れ直し。
                if (item != newItem)
                {
                    temp.Push(item);
                }
            }
            recentFiles.Clear();
            recentFiles = temp;

            //最近開いたファイル数を超えた＝古いのを除いて再度作り直し。
            while (recentFiles.Count + 1 > Properties.Settings.Default.RecentFilesCount)
            {
                recentFiles = new Stack<string>(recentFiles.Reverse().Skip(1));
            }

            //最新を上に乗せる。
            recentFiles.Push(newItem);

            rootItem.Children?.Clear();

            foreach (var item in recentFiles)
            {
                var childItem = new TreeSource() { Text = item, IsExpanded = false };
                rootItem.Add(childItem);
            }
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                InitialDirectory = Directory.GetCurrentDirectory(),
                RestoreDirectory = true,
                Filter = "設定ファイル(*.wb)|*.wb|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Multiselect = false,
                Title = "設定ファイル(.wb）の選択"
            };

            MenuToggleButton.IsChecked = false;

            var result = ofd.ShowDialog();

            if (result == true)
            {
                ReStackRecentTree(ofd.FileName);
                Properties.Settings.Default.LastDoc = ofd.FileName;
                Properties.Settings.Default.Save();
                OpenDatafile(ofd.FileName);
            }
        }

        //
        //
        //
        //
        //テストボタン。色々使う。
        //
        //
        //
        //
        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            GetBookmarkTable();
            BookmarkList.Items.Refresh();
            FilterAndSort(MainVM.DbInfo.Sort, true);
            Refresh();
        }

        private void MenuBtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button item)
            {
                if (!string.IsNullOrEmpty(item.Tag.ToString()))
                {
                    var tag = item.Tag.ToString();
                    if (tag != "設定")
                    {

                        switch (tag)
                        {
                            case "共通設定":
                                MenuToggleButton.IsChecked = false;
                                var CommonSettingsWindow = new CommonSettingsWindow
                                {
                                    Owner = this,
                                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                };
                                CommonSettingsWindow.ShowDialog();
                                break;
                            case "個別設定":
                                if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
                                {
                                    MessageBox.Show("管理ファイルが選択されていません。", Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                                    return;
                                }

                                MenuToggleButton.IsChecked = false;
                                var sysData = new Settings(MainVM.DbInfo.DBFullPath);
                                var settingsWindow = new SettingsWindow
                                {
                                    Owner = this,
                                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                    DataContext = sysData
                                };
                                settingsWindow.ShowDialog();

                                UpsertSystemTable(MainVM.DbInfo.DBFullPath, "thum", settingsWindow.ThumbFolder.Text);
                                UpsertSystemTable(MainVM.DbInfo.DBFullPath, "bookmark", settingsWindow.BookmarkFolder.Text);
                                UpsertSystemTable(MainVM.DbInfo.DBFullPath, "keepHistory", settingsWindow.KeepHistory.Text);
                                UpsertSystemTable(MainVM.DbInfo.DBFullPath, "playerPrg", settingsWindow.PlayerPrg.Text);
                                var param = settingsWindow.PlayerParam.Text == null ? "" : settingsWindow.PlayerParam.Text.ToString();
                                UpsertSystemTable(MainVM.DbInfo.DBFullPath, "playerParam", param);

                                GetSystemTable(MainVM.DbInfo.DBFullPath);
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        if (MenuConfig.Items.Count > 0)
                        {
                            if (MenuConfig.Items[0] is TreeSource topNode)
                            {
                                topNode.IsExpanded = !topNode.IsExpanded;
                            }
                        }
                    }
                }
            }
        }

        private void MenuBtnTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button item)
            {
                if (!string.IsNullOrEmpty(item.Tag.ToString()))
                {
                    var tag = item.Tag.ToString();
                    if (tag != "ツール")
                    {
                        if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
                        {
                            MessageBox.Show("管理ファイルが選択されていません。", Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                            return;
                        }

                        MenuToggleButton.IsChecked = false;

                        switch (tag)
                        {
                            case "監視フォルダ編集":
                                var watchWindow = new WatchWindow(MainVM.DbInfo.DBFullPath)
                                {
                                    Owner = this,
                                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                };
                                watchWindow.ShowDialog();
                                break;

                            case "監視フォルダ更新チェック":
                                _ = CheckFolderAsync(CheckMode.Manual);
                                break;

                            case "全ファイルサムネイル再作成":
                                if (Tabs.SelectedItem == null) { return; }

                                var dialogWindow = new MessageBoxEx(this)
                                {
                                    DlogTitle = "サムネイルの再作成",
                                    DlogMessage = $"サムネイルを再作成します。よろしいですか？",
                                    PackIconKind = MaterialDesignThemes.Wpf.PackIconKind.EventQuestion
                                };

                                dialogWindow.ShowDialog();
                                if (dialogWindow.CloseStatus() == MessageBoxResult.Cancel)
                                {
                                    return;
                                }

                                foreach (var rec in MainVM.MovieRecs)
                                {
                                    QueueObj tempObj = new()
                                    {
                                        MovieId = rec.Movie_Id,
                                        MovieFullPath = rec.Movie_Path,
                                        Tabindex = Tabs.SelectedIndex
                                    };
                                    queueThumb.Enqueue(tempObj);
                                }
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        if (MenuTool.Items.Count > 0)
                        {
                            if (MenuTool.Items[0] is TreeSource topNode)
                            {
                                topNode.IsExpanded = !topNode.IsExpanded;
                            }
                        }
                    }
                }
            }
        }

        private void MenuRecentTree_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button item)
            {
                if (!string.IsNullOrEmpty(item.Tag.ToString()))
                {
                    var tag = item.Tag.ToString();
                    if (tag != RECENT_OPEN_FILE_LABEL)
                    {
                        MenuToggleButton.IsChecked = false;
                        if (!string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
                        {
                            UpdateSkin();
                            UpdateSort();
                        }
                        ReStackRecentTree(tag);
                        OpenDatafile(tag);
                        Properties.Settings.Default.LastDoc = tag;
                        Properties.Settings.Default.Save();
                    }
                    else
                    {
                        if (MenuRecent.Items.Count > 0)
                        {
                            if (MenuRecent.Items[0] is TreeSource topNode)
                            {
                                topNode.IsExpanded = !topNode.IsExpanded;
                            }
                        }
                    }
                }
            }
        }

        public async void PlayMovie_Click(object sender, RoutedEventArgs e)
        {
            var playerPrg = SelectSystemTable("playerPrg");
            var playerParam = SelectSystemTable("playerParam");

            //設定DBごとのプレイヤーが空
            if (string.IsNullOrEmpty(playerPrg))
            {
                //全体設定のプレイヤーを設定
                playerPrg = Properties.Settings.Default.DefaultPlayerPath;
            }

            //設定DBごとのプレイヤーパラメータが空
            if (string.IsNullOrEmpty(playerParam))
            {
                //全体設定のプレイヤーパラメータを設定
                playerParam = Properties.Settings.Default.DefaultPlayerParam;
            }

            int msec = 0;
            int secPos = 0; //ここでは渡す為だけに使ってる。
            string moviePath = "";
            MovieRecords mv = new();
            bool notBookmark = true;

            if (sender is Label labelObj)
            {
                if (labelObj.Name == "LabelBookMark")
                {
                    var item = (Label)sender;
                    if (item != null)
                    {
                        notBookmark = false;
                        mv = item.DataContext as MovieRecords;
                        //実ムービーファイルのパスを取得する。Movie_Bodyに入っているファイル名の一部で検索する。
                        MovieRecords bookmarkedMv = MainVM.MovieRecs.Where(
                                x => x.Movie_Name.Contains(mv.Movie_Body, StringComparison.CurrentCultureIgnoreCase)).First();
                        var BookMarkedFilePath = bookmarkedMv.Movie_Path;
                        MovieInfo mvi = new(BookMarkedFilePath, true);   //Hashの取得が重いのでオプション付けた。ブックマークには不要。
                        msec = (int)mv.Score / (int)mvi.FPS * 1000;
                        moviePath = $"\"{BookMarkedFilePath}\"";
                        UpdateBookmarkViewCount(MainVM.DbInfo.DBFullPath, mv.Movie_Id);
                    }
                }
            }

            if (notBookmark)
            {
                if (Tabs.SelectedItem == null) { return; }

                mv = GetSelectedItemByTabIndex();
                if (mv == null) { return; }

                moviePath = $"\"{mv.Movie_Path}\"";

                if (!Path.Exists(mv.Movie_Path))
                {
                    return;
                }

                if (sender is MenuItem senderObj)
                {
                    if (senderObj.Name == "PlayFromThumb")
                    {
                        msec = GetPlayPosition(Tabs.SelectedIndex, mv, ref secPos);
                    }
                }
            }

            if (!string.IsNullOrEmpty(playerParam))
            {
                playerParam = playerParam.Replace("<file>", $"{mv.Movie_Path}");
                playerParam = playerParam.Replace("<ms>", $"{msec}");
            }

            var arg = $"{moviePath} {playerParam}";

            try
            {
                using Process ps1 = new();
                //設定ファイルのプログラムも既定のプログラムも空だった場合にはここのはず。
                if (string.IsNullOrEmpty(playerPrg))
                {
                    ps1.StartInfo.UseShellExecute = true;
                    ps1.StartInfo.FileName = moviePath;
                }
                else
                {
                    ps1.StartInfo.Arguments = arg;
                    ps1.StartInfo.FileName = playerPrg;
                }
                ps1.Start();

                var psName = ps1.ProcessName;
                Process ps2 = Process.GetProcessById(ps1.Id);
                foreach (Process p in Process.GetProcessesByName(psName))
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        if (p.MainWindowTitle.Contains(mv.Movie_Name, StringComparison.CurrentCultureIgnoreCase))
                        {
                            p.Kill();
                            await p.WaitForExitAsync();
                        }
                    }
                }
                mv.View_Count += 1;
                mv.Score += 1;
                var now = DateTime.Now;
                var result = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                mv.Last_Date = result.ToString("yyyy-MM-dd HH:mm:ss");

                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, mv.Movie_Id, "score", mv.Score);
                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, mv.Movie_Id, "view_count", mv.View_Count);
                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, mv.Movie_Id, "last_date", result);
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        private void SearchBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath)) { return; }

            if (e.Source is ComboBox)
            {
                FilterAndSort(MainVM.DbInfo.Sort);  //サーチのコンボチェンジイベント。
                SelectFirstItem();
                if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword))
                {
                    //セレクションが変わってもHistoryに書いてるかも。
                    InsertHistoryTable(MainVM.DbInfo.DBFullPath, MainVM.DbInfo.SearchKeyword);
                }
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath)) { return; }

            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword))
            {
                //FindFactの方は、カーソルがある間は書き込んでないのかもと思って。
                InsertFindFactTable(MainVM.DbInfo.DBFullPath, MainVM.DbInfo.SearchKeyword);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath)) { return; }
            if (_imeFlag) { return; }
            if (e.Source is ComboBox combo)
            {

                // 入力文字列の末尾が -, |, { のいずれかならサーチしない。}は終了なので、サーチスタート。
                var text = combo.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    // すでに{があり、}がまだ無い場合はreturn
                    int openIdx = text.IndexOf('{');
                    int closeIdx = text.IndexOf('}');
                    if (openIdx >= 0 && (closeIdx < 0 || closeIdx < openIdx))
                    {
                        return;
                    }

                    char lastChar = text[^1];
                    if (lastChar == '-' || lastChar == '|' || lastChar == '{')
                    {
                        return;
                    }
                }

                //インクリメンタルサーチがなぁ。ちょっと間隔で調整的な。美しくない。
                DateTime now = DateTime.Now;
                TimeSpan timeSinceLastUpdate = now - _lastInputTime;

                if (timeSinceLastUpdate >= _timeInputInterval)
                {
                    _lastInputTime = now;
                    FilterAndSort(MainVM.DbInfo.Sort);  //サーチのテキストチェンジイベント。
                    SelectFirstItem();
                }
            }
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath)) { return; }
            if (_imeFlag) { return; }
            if (e.Source is ComboBox)
            {
                //history への追加処理。どうも本家もサーチボックス上でエンターキーを押したときに
                //history へ追加してる気がする。→検索結果がヒットしたら？かも？
                if (e.Key == Key.Return)
                {
                    if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword) && (MainVM.DbInfo.SearchCount > 0))
                    {
                        InsertHistoryTable(MainVM.DbInfo.DBFullPath, MainVM.DbInfo.SearchKeyword);
                        GetHistoryTable(MainVM.DbInfo.DBFullPath);
                    }
                }
            }
        }

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

        private void Label_MouseDown(object sender, MouseButtonEventArgs e)
        {
            lbClickPoint = e.GetPosition(sender as Label);
        }

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

        public MovieRecords GetSelectedItemByTabIndex()
        {
            MovieRecords mv;
            switch (Tabs.SelectedIndex)
            {
                case 0: mv = SmallList.SelectedItem as MovieRecords; break;
                case 1: mv = BigList.SelectedItem as MovieRecords; break;
                case 2: mv = GridList.SelectedItem as MovieRecords; break;
                case 3: mv = ListDataGrid.SelectedItem as MovieRecords; break;
                case 4: mv = BigList10.SelectedItem as MovieRecords; break;

                default: return null;
            }
            return mv;
        }

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

        private void CreateWatcher()
        {
            string sql = $"SELECT * FROM watch where watch = 1";
            GetWatchTable(MainVM.DbInfo.DBFullPath, sql);

            foreach (DataRow row in watchData.Rows)
            {
                //存在しない監視フォルダは読み飛ばし。
                if (!Path.Exists(row["dir"].ToString())) { continue; }

                string checkFolder = row["dir"].ToString();
                bool sub = (long)row["sub"] == 1;

                RunWatcher(checkFolder, sub);
            }
        }

        /// <summary>
        /// 起動時と手動時のフォルダチェック。
        /// DB内レコードとフォルダ内対象ファイルの差分比較し、差分があれば追加。
        /// リネームや削除には対応出来ず。
        /// </summary>
        /// <returns></returns>
        /// <exception cref="OperationCanceledException"></exception>
        private async Task CheckFolderAsync(CheckMode mode)
        {
            bool FolderCheckflg = false;
            List<QueueObj> addFiles = [];
            string checkExt = Properties.Settings.Default.CheckExt;

            var title = "フォルダ監視中";
            var Message = "";
            NotificationManager notificationManager = new();

            string sql = mode switch
            {
                CheckMode.Auto => $"SELECT * FROM watch where auto = 1",
                CheckMode.Watch => $"SELECT * FROM watch where watch = 1",
                _ => $"SELECT * FROM watch",
            };
            GetWatchTable(MainVM.DbInfo.DBFullPath, sql);

            foreach (DataRow row in watchData.Rows)
            {
                //存在しない監視フォルダは読み飛ばし。
                if (!Path.Exists(row["dir"].ToString())) { continue; }
                string checkFolder = row["dir"].ToString();

                notificationManager.Show(title, $"{checkFolder} 監視実施中…", NotificationType.Notification, "ProgressArea");

                bool sub = ((long)row["sub"] == 1);

                // ファイルリスト
                var di = new DirectoryInfo(checkFolder);
                EnumerationOptions enumOption = new()
                {
                    RecurseSubdirectories = sub
                };

                try
                {
                    IEnumerable<FileInfo> ssFiles = checkExt.Split(',').SelectMany(filter => di.EnumerateFiles(filter, enumOption));
                    bool IsHit = false;
                    foreach (var ssFile in ssFiles)
                    {
                        var searchFileName = ssFile.FullName.Replace("'", "''");
                        DataRow[] movies = movieData.Select($"movie_path = '{searchFileName}'");
                        if (movies.Length == 0)
                        {
                            Message = checkFolder;
                            if (IsHit == false)
                            {
                                notificationManager.Show(title, $"{Message}に更新あり。", NotificationType.Notification, "ProgressArea");
                                //MessageBox.Show("更新しています。","更新あり",MessageBoxButton.OK,MessageBoxImage.Information);
                                IsHit = true;
                            }

                            MovieInfo mvi = new(ssFile.FullName);
                            await InsertMovieTable(MainVM.DbInfo.DBFullPath, mvi);

                            FolderCheckflg = true;

                            //ここでQueueの元ネタに入れてるのな。
                            //サムネイルファイルが存在するかどうかチェック。あればQueueに入れない。
                            TabInfo tbi = new(MainVM.DbInfo.CurrentTabIndex, MainVM.DbInfo.DBName, MainVM.DbInfo.ThumbFolder);

                            // ファイルハッシュ取得
                            var hash = mvi.Hash;

                            // 拡張子なしのファイル名取得。
                            var fileBody = Path.GetFileNameWithoutExtension(mvi.MoviePath);

                            // 結合したサムネイルのファイル名作成
                            var saveThumbFileName = Path.Combine(tbi.OutPath, $"{fileBody}.#{hash}.jpg");

                            if (Path.Exists(saveThumbFileName))
                            {
                                continue;
                            }

                            QueueObj temp = new()
                            {
                                MovieId = mvi.MovieId,
                                MovieFullPath = mvi.MoviePath,
                                Tabindex = MainVM.DbInfo.CurrentTabIndex
                            };
                            addFiles.Add(temp);

                            DataTable dt = GetData(MainVM.DbInfo.DBFullPath, "select * from movie order by movie_id desc");
                            DataRowToViewData(dt.Rows[0]);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (e.GetType() == typeof(IOException))
                    {
                        //起動中に監視フォルダにファイルコピーされっと例外発生するんよね。
                        await Task.Delay(1000);
                    }
                }
                await Task.Delay(100);
            }

            //stack : ファイル名を外部から変更したときに、エクステンションのファイル名が追従してなかった。強制チェックで反応はした。
            //再クリックで表示はリロードしたので、内部は変わってる。リフレッシュも漏れてる可能性あり。
            //と言うかですね。これは外部からのリネームでも、アプリでのリネームでも同じで。クリックすりゃ反映する（そりゃそうだ）
            //ここは仕様と言う事で… リネーム処理後にどこを選択してたか覚えてればなんとかなるんだろうけども。
            if (FolderCheckflg)
            {
                FilterAndSort(MainVM.DbInfo.Sort, true);    //チェックフォルダ時。監視対象があった場合の処理やな。

                foreach (var item in addFiles)
                {
                    queueThumb.Enqueue(item);
                }
            }
        }

        /// <summary>
        /// CheckThumbAsync サムネイル作成用に起動時にぶん投げるタスク。常時起動。終了条件はねぇ。
        /// </summary>
        /// <returns></returns>
        private async Task CheckThumbAsync(CancellationToken cts = default)
        {
            var title = "サムネイル作成中";
            NotificationManager notificationManager = new();

            try {
                while (true)
                {
                    double totalProgress = 0;
                    await Task.Delay(3000, cts);
                    if (queueThumb.Count < 1) { continue; }

                    var progress = notificationManager.ShowProgressBar(title, false, true, "ProgressArea", false, 2, "");

                    int i = 0;
                    int totalCount = queueThumb.Count;
                    double progressCounter = 100d / totalCount;

                    while (totalCount > 0)
                    {
                        i++;
                        if (totalCount < i) { totalCount = i; }

                        if (queueThumb.Count < 1) {
                            progress.Dispose();
                            break; 
                        }
                        QueueObj queueObj = queueThumb.Dequeue();
                        if (queueObj == null) { continue; }

                        string tabName = queueObj.Tabindex switch
                        {
                            0 => "サムネイル作成中(Small)",
                            1 => "サムネイル作成中(Big)",
                            2 => "サムネイル作成中(Grid)",
                            3 => "サムネイル作成中(List)",
                            4 => "サムネイル作成中(Big10)",
                            _ => "サムネイル作成中",
                        };
                        title = $"{tabName} ({i}/{totalCount})";

                        var Message = $"{queueObj.MovieFullPath}";
                        progress.Report((totalProgress += progressCounter, Message, title, false));
                        await CreateThumbAsync(queueObj,false,cts).ConfigureAwait(false);
                    }
                    progress.Dispose();
                }
            }
            catch (Exception e)
            {
                string s = string.Format($"{DateTime.Now:yyyy/MM/dd HH:mm:ss} :");
                // 何かしらのエラーが発生した場合、スルーする。
                Debug.WriteLine($"{s} {e.Message} ");
            }
        }

        private async Task CreateBookmarkThumbAsync(string movieFullPath, string saveThumbPath, int capturePos)
        {
            if (!Path.Exists(movieFullPath)) { return; }

            await Task.Run(() =>
            {
                using var capture = new VideoCapture(movieFullPath);
                //なんか、Grabしないと遅いって話をどっかで見たので。
                capture.Grab();

                var img = new Mat();
                capture.PosMsec = capturePos * 1000;
                int msecCounter = 0;
                while (capture.Read(img) == false)
                {
                    capture.PosMsec += 100;
                    if (msecCounter > 100) { break; }
                    msecCounter++;
                }

                if (img == null) { return; }
                if (img.Width == 0) { return; }
                if (img.Height == 0) { return; }

                using Mat temp = new(img, GetAspect(img.Width, img.Height));

                // サイズ変更した画像を保存する
                using Mat dst = new();
                OpenCvSharp.Size sz = new(640, 480);
                Cv2.Resize(temp, dst, sz);
                OpenCvSharp.Extensions.BitmapConverter.ToBitmap(dst).Save(saveThumbPath, ImageFormat.Jpeg);

                img.Dispose();
                capture.Dispose();
            });
            await Task.Delay(1000);
            BookmarkList.Items.Refresh();
        }

        private static OpenCvSharp.Rect GetAspect(int imgWidth, int imgHeight)
        {
            int w = imgWidth;
            int h = imgHeight;
            int wdiff = 0;
            int hdiff = 0;

            // アスペクト比の算出
            float aspect = (float)imgWidth / imgHeight;
            if (aspect > 1.34)
            {
                //横長だよね。
                h = (int)Math.Floor((decimal)imgHeight / 3);
                w = (int)Math.Floor((decimal)h * 4);
                h = imgHeight;
                wdiff = (imgWidth - w) / 2;
                hdiff = 0;
            }
            //縦長動画の場合はどうするよ？ 4:3の場合は何もしない。
            if (aspect < 1.33)
            {
                //縦長かスクエアかな？
                w = (int)Math.Floor((decimal)imgWidth / 4);
                h = (int)Math.Floor((decimal)w * 3);
                w = imgWidth;
                hdiff = (imgHeight - h) / 2;
                wdiff = 0;
            }
            return new OpenCvSharp.Rect(wdiff, hdiff, w, h);
        }

        /// <summary>
        /// サムネイル作成本体
        /// </summary>
        /// <param name="queueObj">取り出したQueueの中身</param>
        /// <param name="IsManual">マニュアル作成かどうか</param>
        /// <returns></returns>
        private async Task CreateThumbAsync(QueueObj queueObj, bool IsManual = false, CancellationToken cts = default)
        {
            TabInfo tbi = new(queueObj.Tabindex, MainVM.DbInfo.DBName, MainVM.DbInfo.ThumbFolder);
            var movieFullPath = queueObj.MovieFullPath;

            // ファイルハッシュ取得
            var hash = GetHashCRC32(movieFullPath);

            // 拡張子なしのファイル名取得。
            var fileBody = Path.GetFileNameWithoutExtension(movieFullPath);

            // 結合したサムネイルのファイル名作成
            var saveThumbFileName = Path.Combine(tbi.OutPath, $"{fileBody}.#{hash}.jpg");

            // マニュアル作成の場合、既存のファイルが存在しないと何もしない。
            if (IsManual)
            {
                if (!Path.Exists(saveThumbFileName)) { return; }
            }

            // テンプファイル名のボディを作る
            var tempFileBody = $"{fileBody}_{hash}_temp";

            // テンプフォルダ取得
            var currentPath = Directory.GetCurrentDirectory();
            var tempPath = Path.Combine(currentPath, "temp");
            if (!Path.Exists(tempPath))
            {
                Directory.CreateDirectory(tempPath);
            }

            // 出力先ディレクトリを取得する
            if (Path.Exists(tbi.OutPath) == false)
            {
                Directory.CreateDirectory(tbi.OutPath);
            }

            //実体の動画ファイルが存在しない
            if (!Path.Exists(queueObj.MovieFullPath))
            {
                //サムネイルのファイルも存在しない
                if (!Path.Exists(saveThumbFileName))
                {
                    var noFileJpeg = Path.Combine(Directory.GetCurrentDirectory(), "Images");

                    noFileJpeg = queueObj.Tabindex switch
                    {
                        0 => Path.Combine(noFileJpeg, "noFileSmall.jpg"),
                        1 => Path.Combine(noFileJpeg, "noFileBig.jpg"),
                        2 => Path.Combine(noFileJpeg, "noFileGrid.jpg"),
                        3 => Path.Combine(noFileJpeg, "noFileList.jpg"),
                        4 => Path.Combine(noFileJpeg, "noFileBig.jpg"),
                        99 => Path.Combine(noFileJpeg, "noFileGrid.jpg"),
                        _ => Path.Combine(noFileJpeg, "noFileSmall.jpg"),
                    };
                    File.Copy(noFileJpeg, saveThumbFileName, true);
                }
            }
            else
            {
                OpenCvSharp.Size sz = new(0, 0);

                // Stopwatchクラス生成
                var sw = new Stopwatch();
                try
                {
                    double durationSec = 0;
                    using var capture = new VideoCapture(queueObj.MovieFullPath);
                    //なんか、Grabしないと遅いって話をどっかで見たので。
                    capture.Grab();

                    var frameCount = capture.Get(VideoCaptureProperties.FrameCount);
                    var fps = capture.Get(VideoCaptureProperties.Fps);

                    durationSec = frameCount / fps;

                    // 分割する秒数を算出
                    int divideSec = (int)(durationSec / ((tbi.Columns * tbi.Rows) + 1));

                    ThumbInfo thumbInfo = new()
                    {
                        ThumbWidth = tbi.Width,
                        ThumbHeight = tbi.Height,
                        ThumbRows = tbi.Rows,
                        ThumbColumns = tbi.Columns,
                        ThumbCounts = tbi.Columns * tbi.Rows
                    };

                    if (IsManual)
                    {
                        //既存のファイルの秒数情報を取得する。
                        thumbInfo.GetThumbInfo(saveThumbFileName);
                        if (thumbInfo.IsThumbnail == false) { return; }

                        //ListのthumbSecの特定場所（secPos）に秒数を設定する。
                        if ((queueObj.ThumbPanelPos != null) && (queueObj.ThumbTimePos != null))
                        {
                            thumbInfo.ThumbSec[(int)queueObj.ThumbPanelPos] = (int)queueObj.ThumbTimePos;
                        }
                    }
                    else
                    {
                        //ファイルの後ろに書き込むバイトデータを生成
                        for (int i = 1; i < (thumbInfo.ThumbCounts) + 1; i++)
                        {
                            thumbInfo.Add(i * divideSec);
                        }
                    }
                    thumbInfo.NewThumbInfo();

                    // 既存テンプファイルの削除
                    var oldTempFiles = Directory.GetFiles(tempPath, $"*{tempFileBody}*.jpg", System.IO.SearchOption.TopDirectoryOnly);
                    foreach (var oldFile in oldTempFiles)
                    {
                        if (File.Exists(oldFile))
                        {
                            File.Delete(oldFile);
                        }
                    }

                    // ファイルリスト
                    var di = new DirectoryInfo(tempPath);
                    EnumerationOptions enumOption = new()
                    {
                        MaxRecursionDepth = 0,
                        RecurseSubdirectories = false
                    };
                    IEnumerable<FileInfo> ssFiles = di.EnumerateFiles($"{tempFileBody}*.jpg", enumOption);

                    List<string> paths = [];

                    bool IsSuccess = true;
                    await Task.Run(() =>
                    {
                        // スナップショットの作成（切り出し＆縮小）
                        for (int i = 0; i < thumbInfo.ThumbSec.Count; i++)
                        {
                            // 計測開始
                            sw.Restart();

                            var img = new Mat();
                            capture.PosMsec = thumbInfo.ThumbSec[i] * 1000;

                            int msecCounter = 0;
                            while (capture.Read(img) == false)
                            {
                                capture.PosMsec += 100;
                                if (msecCounter > 100) { break; }
                                msecCounter++;
                            }
                            // 計測開始
                            sw.Stop();

                            TimeSpan ts = sw.Elapsed;
                            if (ts.Seconds > 60) { IsSuccess = false; return; }

                            if (img == null) { IsSuccess = false; return; }
                            if (img.Width == 0) { IsSuccess = false; return; }
                            if (img.Height == 0) { IsSuccess = false; return; }

                            using Mat temp = new(img, GetAspect(img.Width, img.Height));

                            // サイズ変更した画像を保存する
                            var saveFile = Path.Combine(tempPath, $"tn_{tempFileBody}{i:D2}.jpg");

                            if (Properties.Settings.Default.IsResizeThumb)
                            {
                                sz = new OpenCvSharp.Size { Width = tbi.Width, Height = tbi.Height };
                            }
                            else
                            {
                                if (sz.Width == 0)
                                {
                                    sz = new OpenCvSharp.Size { Width = temp.Width < 320 ? temp.Width : 320, Height = temp.Height < 240 ? temp.Height : 240 };
                                }
                            }

                            using Mat dst = new();
                            Cv2.Resize(temp, dst, sz);
                            OpenCvSharp.Extensions.BitmapConverter.ToBitmap(dst).Save(saveFile, ImageFormat.Jpeg);

                            paths.Add(saveFile);

                            img.Dispose();
                        }
                    }, cts);
                    capture.Dispose();

                    if (!IsSuccess) { return; }

                    // サムネイルの横並び結合
                    Bitmap bmp = ConcatImages(paths, tbi.Columns, tbi.Rows);
                    if (bmp != null)
                    {
                        if (Path.Exists(saveThumbFileName))
                        {
                            File.Delete(saveThumbFileName);
                        }
                        bmp.Save(saveThumbFileName, ImageFormat.Jpeg);
                        bmp.Dispose();

                        using FileStream dest = new(saveThumbFileName, FileMode.Append, FileAccess.Write);
                        dest.Seek(0, SeekOrigin.End);
                        dest.Write(thumbInfo.SecBuffer);
                        dest.Write(thumbInfo.InfoBuffer);
                    }
#if DEBUG == false
                    // 既存テンプファイルの削除
                    oldTempFiles = Directory.GetFiles(tempPath, $"*{tempFileBody}*.jpg", System.IO.SearchOption.TopDirectoryOnly);
                    Parallel.ForEach(oldTempFiles, oldFile =>
                    {
                        if (File.Exists(oldFile))
                        {
                            File.Delete(oldFile);
                        }
                    });
#endif
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"err = {e.Message} Movie = {queueObj.MovieFullPath}");
                }
            }

            foreach (var item in MainVM.MovieRecs.Where(x => x.Movie_Id == queueObj.MovieId))
            {
                switch (queueObj.Tabindex)
                {
                    case 0: item.ThumbPathSmall = saveThumbFileName; break;
                    case 1: item.ThumbPathBig = saveThumbFileName; break;
                    case 2: item.ThumbPathGrid = saveThumbFileName; break;
                    case 3: item.ThumbPathList = saveThumbFileName; break;
                    case 4: item.ThumbPathBig10 = saveThumbFileName; break;
                    case 99: item.ThumbDetail = saveThumbFileName; break;
                    default: break;
                }
            }
        }

        /// <summary>
        /// 手動等間隔サムネイル作成
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CreateThumb_EqualInterval(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            QueueObj tempObj = new()
            {
                MovieId = mv.Movie_Id,
                MovieFullPath = mv.Movie_Path,
                Tabindex = Tabs.SelectedIndex
            };
            queueThumb.Enqueue(tempObj);
        }

        #region マニュアルサムネイル用のプレイヤー関連

        private bool IsPlaying = false;
        /// <summary>
        /// 再生ボタンクリック時のイベントハンドラ
        /// パクリ元：https://resanaplaza.com/2023/06/24/%e3%80%90%e3%82%b5%e3%83%b3%e3%83%97%e3%83%ab%e6%ba%80%e8%bc%89%e3%80%91c%e3%81%a7%e5%8b%95%e7%94%bb%e5%86%8d%e7%94%9f%e3%81%97%e3%82%88%e3%81%86%e3%82%88%ef%bc%81%ef%bc%88mediaelement%ef%bc%89/
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Start_Click(object sender, RoutedEventArgs e)
        {
            PlayerArea.Visibility = Visibility.Visible;
            PlayerController.Visibility = Visibility.Visible;
            uxVideoPlayer.Visibility = Visibility.Visible;
            uxVideoPlayer.Play();
            IsPlaying = true;
            uxTimeSlider.Value = uxVideoPlayer.Position.TotalMilliseconds;
            timer.Start();
        }

        /// <summary>
        /// 一時停止ボタンクリック時のイベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            uxVideoPlayer.Pause();
            IsPlaying = false;
        }

        private void UxVideoPlayer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (IsPlaying == true)
            {
                uxVideoPlayer.Pause();
                IsPlaying = false;
            }
            else
            {
                uxVideoPlayer.Play();
                IsPlaying = true;
            }
        }

        /// <summary>
        /// ストップボタンクリック時のイベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Stop();
            IsPlaying = false;
            timer.Stop();
        }

        /// <summary>
        /// タイムラインスライダーのイベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UxTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            DateTime now = DateTime.Now;
            TimeSpan timeSinceLastUpdate = now - _lastSliderTime;

            if (timeSinceLastUpdate >= _timeSliderInterval)
            {
                uxVideoPlayer.Position = new TimeSpan(0, 0, 0, 0, (int)uxTimeSlider.Value);
                _lastSliderTime = now;
                uxTime.Text = uxVideoPlayer.Position.ToString()[..8];
            }
        }

        /// <summary>
        /// 動画ファイル再生開始のイベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UxVideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            uxTimeSlider.Maximum = uxVideoPlayer.NaturalDuration.TimeSpan.TotalMilliseconds;
        }

        /// <summary>
        /// ボリュームスライダーのイベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UxVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            uxVideoPlayer.Volume = (double)uxVolumeSlider.Value;
            if (uxVolume != null)
            {
                uxVolume.Text = ((int)(uxVideoPlayer.Volume * 100)).ToString();
            }
        }

        /// <summary>
        /// キャプチャボタンのイベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Capture_Click(object sender, RoutedEventArgs e)
        {
            //QueueObj 作って、サムネ作成する。どのパネルか、秒数はどこか、差し替える画像はどれか。
            //その辺は、サムネ作成側の処理で判断。

            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            timer.Stop();
            uxVideoPlayer.Pause();

            QueueObj queueObj = new()
            {
                MovieId = mv.Movie_Id,
                MovieFullPath = mv.Movie_Path,
                Tabindex = Tabs.SelectedIndex,
                ThumbPanelPos = manualPos,
                ThumbTimePos = (int)uxVideoPlayer.Position.TotalSeconds
            };
            uxVideoPlayer.Stop();

            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;

            IsPlaying = false;

            await Task.Delay(10);
            _ = CreateThumbAsync(queueObj, true);
        }

        public void DeleteBookmark(object sender, RoutedEventArgs e)
        {
            if (sender is Button deleteButton)
            {
                var item = deleteButton.DataContext as MovieRecords;
                DeleteBookmarkTable(MainVM.DbInfo.DBFullPath, item.Movie_Id);
                GetBookmarkTable();
                BookmarkList.Items.Refresh();
            }
        }

        private async void AddBookmark_Click(object sender, RoutedEventArgs e)
        {
            //QueueObj 作って、サムネ作成する。どのパネルか、秒数はどこか、差し替える画像はどれか。
            //その辺は、サムネ作成側の処理で判断。

            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            timer.Stop();
            uxVideoPlayer.Pause();

            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;

            MovieInfo mvi = new(mv.Movie_Path, true);        //Hashの取得が重いのでオプション付けた。ブックマークには不要。

            int pos = (int)uxVideoPlayer.Position.TotalSeconds;
            var targetFrame = pos * (int)mvi.FPS;
            var timestamp = string.Format($"{DateTime.Now:HH-mm-ss}");
            var thumbBody = $"{mv.Movie_Body}[({targetFrame}){timestamp}]";
            var thumbFileName = $"{thumbBody}.jpg";
            var thumbFolder = MainVM.DbInfo.BookmarkFolder;
            var defaultThumbFolder = Path.Combine(Directory.GetCurrentDirectory(), "bookmark", MainVM.DbInfo.DBName);
            thumbFolder = thumbFolder == "" ? defaultThumbFolder : thumbFolder;
            thumbFileName = Path.Combine(thumbFolder, thumbFileName);
            if (!Path.Exists(thumbFolder))
            {
                Directory.CreateDirectory(thumbFolder);
            }

            await Task.Delay(10);
            //bookmark用サムネイル作成処理。通常と重複は多いんだけども。
            _ = CreateBookmarkThumbAsync(mv.Movie_Path, thumbFileName, pos);

            uxVideoPlayer.Stop();
            IsPlaying = false;

            //Bookmarkテーブルへのレコード書き込み処理追加
            mvi.MovieName = thumbBody;
            mvi.MoviePath = $"{thumbBody}.jpg";
            InsertBookmarkTable(MainVM.DbInfo.DBFullPath, mvi);
            GetBookmarkTable();
            BookmarkList.Items.Refresh();
        }

        private async void ManualThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            int msec = 0;
            if (sender is MenuItem senderObj)
            {
                if (senderObj.Name == "ManualThumbnail")
                {
                    msec = GetPlayPosition(Tabs.SelectedIndex, mv, ref manualPos);
                }
            }

            //動画ファイルの指定
            uxVideoPlayer.Source = new Uri(mv.Movie_Path);
            await Task.Delay(1000);

            //動画の再生
            uxVideoPlayer.Volume = 0;
            uxVideoPlayer.Play();

            //再生位置の移動
            uxVideoPlayer.Position = new TimeSpan(0, 0, 0, 0, msec);
            //uxVideoPlayer.Volume = (double)uxVolumeSlider.Value;
            uxVideoPlayer.Pause();
            await Task.Delay(100);
            IsPlaying = false;
            PlayerArea.Visibility = Visibility.Visible;
            uxVideoPlayer.Visibility = Visibility.Visible;
            PlayerController.Visibility = Visibility.Visible;
            uxTimeSlider.Focus();

            timer.Start();
        }

        private void FR_Click(object sender, RoutedEventArgs e)
        {
            var tempSlider = (int)uxTimeSlider.Value - 100;
            if (tempSlider < 0) { tempSlider = 0; }
            FF_FR(tempSlider);
        }
        private void FF_Click(object sender, RoutedEventArgs e)
        {
            var tempSlider = (int)uxTimeSlider.Value + 100;
            if (tempSlider > uxTimeSlider.Maximum) { tempSlider = (int)uxTimeSlider.Maximum; }
            FF_FR(tempSlider);
        }
        private void FF_FR(int tempSlider)
        {
            uxTimeSlider.Value = tempSlider;
            uxVideoPlayer.Position = new TimeSpan(0, 0, 0, 0, tempSlider);
            uxTime.Text = uxVideoPlayer.Position.ToString()[..8];
        }

        /// <summary>
        /// ドラッグしてなければ、スライダーの値を定期的に、動画のポジションにする。
        /// パクリ元：https://www.c-sharpcorner.com/UploadFile/dpatra/seek-bar-for-media-element-in-wpf/
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!isDragging)
            {
                uxTimeSlider.Value = uxVideoPlayer.Position.TotalMilliseconds;
            }
        }

        private void UxTimeSlider_DragEnter(object sender, DragEventArgs e)
        {
            isDragging = true;
        }

        private void UxTimeSlider_DragLeave(object sender, DragEventArgs e)
        {
            isDragging = false;
            uxVideoPlayer.Position = TimeSpan.FromSeconds(uxTimeSlider.Value);
        }

        #endregion
    }
}