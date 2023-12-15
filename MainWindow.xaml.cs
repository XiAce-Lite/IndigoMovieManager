using IndigoMovieManager.ModelView;
using IndigoMovieManager.Views;
using Microsoft.Win32;
using Notification.Wpf;
using OpenCvSharp;
using System.ComponentModel;
using System.Data;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Xml.Linq;
using static IndigoMovieManager.Tools;

namespace IndigoMovieManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private const string RECENT_OPEN_FILE_LABEL = "最近開いたファイル";
        private readonly int recentFileCount = 7;
        private Stack<string> recentFiles = new();

        private DataTable systemData;
        private DataTable movieData;
        private DataTable watchData;

        private static readonly Queue<QueueObj> queueThumb = [];

        private readonly MainWindowViewModel MainVM = new();
        private System.Windows.Point lbClickPoint = new();

        private DateTime _lastSliderTime = DateTime.MinValue;
        private readonly TimeSpan _timeSliderInterval = TimeSpan.FromSeconds(0.1);

        //結局、タイマー方式で動画とマニュアルサムネイルのスライダーを同期させた
        private readonly DispatcherTimer timer;
        private bool isDragging = false;

        //マニュアルサムネイル時の右クリックしたカラムの返却を受け取る変数
        private int manualPos = 0;

        private bool _imeFlag = false;

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
                        MainVM.DataBase.DBFullPath = Properties.Settings.Default.LastDoc;
                        MainVM.DataBase.DBName = Path.GetFileNameWithoutExtension(Properties.Settings.Default.LastDoc);

                        //Tabとソートを取得するだけの為に、MovieRecordsを取得する前にやってる。
                        //初回だけはMainWindow_ContentRenderedの処理と重複するかな。
                        GetSystemTable(Properties.Settings.Default.LastDoc);
                    }
                }
            }
            recentFileCount = Properties.Settings.Default.RecentFilesCount;
            recentFiles.Clear();

            InitializeComponent();

            ContentRendered += MainWindow_ContentRendered;
            Closing += MainWindow_Closing;
            TextCompositionManager.AddPreviewTextInputHandler(SearchBox, OnPreviewTextInput);
            TextCompositionManager.AddPreviewTextInputStartHandler(SearchBox, OnPreviewTextInputStart);
            TextCompositionManager.AddPreviewTextInputUpdateHandler(SearchBox, OnPreviewTextInputUpdate);

            var rootItem = new TreeSource() { Text = RECENT_OPEN_FILE_LABEL, IsExpanded = false };
            MainVM.TreeRoot.Add(rootItem);

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

            DataContext = MainVM;

            #region player
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
                ClearTempJpg();

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

                _ = CheckThumbAsync();
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
                Properties.Settings.Default.RecentFiles.AddRange(recentFiles.Reverse().ToArray());
                Properties.Settings.Default.Save();
            }
            catch (Exception)
            {
                throw;
            }
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

        //todo : フォルダ監視の実装（と言うか設計）タスクでぶん投げて、DBと比較とかかなぁ。
        //todo : タグクリックで検索機能
        //todo : 普通に検索機能
        //done : 高速化は出来たつもりだけど、あんなに仮想化の指定書かなきゃいけないもんかね。
        //todo : 検索機能の実装（と言うか設計）
        //todo : タグ周りの実装（と言うか設計）これが難儀しそう。今の改行区切りの文字列をまずはどう表示させるか？
        //todo : Youtubeのタグ設定みたいな風に、スペース区切りでどんどん入力、クリックで削除的なUI。

        private void OpenDatafile(string dbFullPath)
        {
            //強制的に-1にする。0のタブが前回だった場合の対応
            Tabs.SelectedIndex = -1;
            queueThumb.Clear();

            MainVM.DataBase.DBName = Path.GetFileNameWithoutExtension(dbFullPath);
            MainVM.DataBase.DBFullPath = dbFullPath;
            GetSystemTable(dbFullPath);
            GetWatchTable(dbFullPath);
            MainVM.MovieRecs.Clear();
            if (MainVM.DataBase.Sort != null)
            {
                SetSortData(MainVM.DataBase.Sort, true);
            }
            if (MainVM.DataBase.Skin != null)
            {
                SwitchTab(MainVM.DataBase.Skin);
            }
            _ = CheckFolderAsync(CheckMode.Auto);
            _ = CheckFolderAsync(CheckMode.Watch);
        }

        private string SelectSystemTable(string attr)
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

        private void GetSystemTable(string dbPath)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                string sql = @"SELECT * FROM system";
                systemData = SQLite.GetData(dbPath, sql);

                var skin = SelectSystemTable("skin");
                MainVM.DataBase.Skin = skin == "" ? "Default Small" : skin;

                var sort = SelectSystemTable("sort");
                MainVM.DataBase.Sort = sort == "" ? "1" : sort ;

                MainVM.DataBase.ThumbFolder = SelectSystemTable("thum");

                MainVM.DataBase.BookmarkFolder = SelectSystemTable("bookmark");
            }
            else
            {
                systemData.Clear();
            }
        }

        private void GetWatchTable(string dbPath)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                watchData = SQLite.GetData(dbPath, $"SELECT * FROM watch");
            }
        }

        private void UpdateSort()
        {
            if (!string.IsNullOrEmpty(MainVM.DataBase.Sort))
            {
                SQLite.UpdateSystemTable(Properties.Settings.Default.LastDoc, "sort", MainVM.DataBase.Sort);
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
            SQLite.UpdateSystemTable(Properties.Settings.Default.LastDoc, "skin", tabName);
        }

        private void SetSortData(string id, bool IsGetNew)
        {
            #region ソートキーワードの選択
            string sortWord = id switch
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
            #endregion

            if (movieData == null || IsGetNew)
            {
                movieData = SQLite.GetData(MainVM.DataBase.DBFullPath, $"SELECT * FROM movie order by {sortWord}");
                if (movieData == null) { return; }
            }

            var view = movieData.DefaultView;
            view.Sort = sortWord;
            DataTable dt2 = view.ToTable();
            movieData = dt2;
            _ = SetRecordsToSource(MainVM.DataBase.DBFullPath, false);
        }

        private Task SetRecordsToSource(string dbPath, bool IsGetNew = true)
        {
            if (IsGetNew)
            {
                string sql = @"SELECT * FROM movie";
                movieData = SQLite.GetData(dbPath, sql);
            }

            if (movieData != null)
            {
                var dbName = Path.GetFileNameWithoutExtension(dbPath);
                MainVM.MovieRecs.Clear();
                string[] thumbErrorPath = [@"errorSmall.jpg",@"errorBig.jpg",@"errorGrid.jpg",@"errorList.jpg",@"errorBig.jpg"];
                string[] thumbPath = new string[Tabs.Items.Count];

                var currentDir = Directory.GetCurrentDirectory();
                var list = movieData.AsEnumerable().ToArray();
                foreach (var row in list)
                {
                    var Hash = row["hash"].ToString();
                    var movieFullPath = row["movie_path"].ToString();
                    var fileExt = Path.GetExtension(movieFullPath);
                    var thumbFile = $"{row["movie_name"]}.#{Hash}.jpg";

                    for (int i = 0; i < Tabs.Items.Count; i++)
                    {
                        TabInfo tbi = new(i, dbName, MainVM.DataBase.ThumbFolder);

                        var tempPath = Path.Combine(tbi.OutPath, thumbFile);
                        if (Path.Exists(tempPath))
                        {
                            thumbPath[i] = tempPath;
                        }
                        else
                        {
                            thumbPath[i] = Path.Combine(Directory.GetCurrentDirectory(),"Images", thumbErrorPath[i]);
                        }
                    }

                    var tags = row["tag"].ToString();

                    List<string> tag = [];
                    if (!string.IsNullOrEmpty(tags))
                    {
                        var splitTags = tags.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                        foreach (var tagItem in splitTags)
                        {
                            tag.Add(tagItem);
                        }
                    }

                    var ext = Path.GetExtension (movieFullPath);

                    #region View用のデータにDBからぶち込む
                    var item = new MovieRecords
                    {
                        Movie_Id = (long)row["movie_id"],
                        Movie_Name = $"{row["movie_name"]}{ext}",
                        Movie_Body = $"{row["movie_name"]}",
                        Movie_Path = row["movie_path"].ToString(),
                        Movie_Length = new TimeSpan(0, 0, (int)(long)row["movie_length"]).ToString(@"hh\:mm\:ss"),
                        Movie_Size = (long)row["movie_size"],
                        Last_Date = ((DateTime)row["last_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                        File_Date = ((DateTime)row["file_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                        Regist_Date = ((DateTime)row["regist_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                        Score = (long)row["score"],
                        ViewCount = (long)row["view_count"],
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
                        Tags = row["tag"].ToString(),
                        Tag = tag,
                        Comment1 = row["comment1"].ToString(),
                        Comment2 = row["comment2"].ToString(),
                        Comment3 = row["comment3"].ToString(),
                        ThumbPathSmall = thumbPath[0],
                        ThumbPathBig = thumbPath[1],
                        ThumbPathGrid = thumbPath[2],
                        ThumbPathList = thumbPath[3],
                        ThumbPathBig10 = thumbPath[4],
                        Drive = Path.GetPathRoot(row["movie_path"].ToString()),
                        Dir = Path.GetDirectoryName(row["movie_path"].ToString())
                    };
                    #endregion

                    MainVM.MovieRecs.Add(item);
                }
            }
            return Task.CompletedTask;
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender as TabControl != null && e.OriginalSource is TabControl)
            {
                var tabControl = sender as TabControl;
                int index = tabControl.SelectedIndex;
                // Mainをレンダー後に、強制的に-1にしてるので（TabChangeイベントが発生せず。Index=0のタブが前回だった場合にここの処理が正常動作しない）
                if (index == -1) { return; }

                FilterView();

                #region LinqのWhereでErrorパスを持つレコードを絞り込む
                //todo : この書き方が何とかならんかなぁ。ダサいなぁ。思いつかないので放置で。
                MovieRecords[] query = [];
                switch (index)
                {
                    case 0:
                        query = MainVM.MovieRecs.Where(x => x.ThumbPathSmall.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable().ToArray();
                        break;
                    case 1:
                        query = MainVM.MovieRecs.Where(x => x.ThumbPathBig.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable().ToArray();
                        break;
                    case 2:
                        query = MainVM.MovieRecs.Where(x => x.ThumbPathGrid.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable().ToArray();
                        break;
                    case 3:
                        query = MainVM.MovieRecs.Where(x => x.ThumbPathList.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable().ToArray();
                        break;
                    case 4:
                        query = MainVM.MovieRecs.Where(x => x.ThumbPathBig10.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable().ToArray();
                        break;
                }
                #endregion

                //前の作成を終わったかどうか、判断したかったんだけども…プログレスバーが残ることがあるので、そのために。
                //一回分のサムネ作成の猶予があれば良いと言う事で。ここ以降はぶん投げるので、何秒待ってもいいのはいいんだけど、
                //中々次が始まらないのもあれだし、タブを切り替える度に通る所だし、こんなもんでどうだろうか。
                queueThumb.Clear();
                Task.Delay(4000);

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

        private void SwitchTab(string skin)
        {
            switch (skin)
            {
                case "DefaultSmall": TabSmall.IsSelected = true; break;
                case "DefaultBig": TabBig.IsSelected = true; break;
                case "DefaultGrid": TabGrid.IsSelected = true; break;
                case "DefaultList": TabList.IsSelected = true; break;
                default: TabSmall.IsSelected = true; break;
            }
        }

        private void DeleteMovieRecord_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) return;

            MovieRecords mv;
            mv = GetSelectedItemByTabIndex();
            if (mv == null) return;

            var dialogWindow = new DialogWindow()
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = "登録から削除します",
            };

            dialogWindow.checkBox.IsChecked = true;
            dialogWindow.radioArea.Visibility = Visibility.Collapsed;
            dialogWindow.message.Text = "登録からデータを削除します\n（監視対象の場合、再監視で復活します）";
            dialogWindow.checkBox.Content = "サムネイルも削除する";

            dialogWindow.ShowDialog();
            if (dialogWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            if (dialogWindow.checkBox.IsChecked == true)
            {
                //サムネも消す。
                var checkFileName = mv.Movie_Body;
                var thumbFolder = MainVM.DataBase.ThumbFolder;
                var defaultThumbFolder = Path.Combine(Directory.GetCurrentDirectory(), "Thumb", MainVM.DataBase.DBName);
                thumbFolder = thumbFolder == "" ? defaultThumbFolder : thumbFolder;

                if (Path.Exists(thumbFolder))
                {
                    // ファイルリスト
                    var di = new DirectoryInfo(thumbFolder);
                    EnumerationOptions enumOption = new()
                    {
                        RecurseSubdirectories = true
                    };
                    IEnumerable<FileInfo> ssFiles = di.EnumerateFiles($"*{checkFileName}*.jpg", enumOption);
                    foreach (var item in ssFiles)
                    {
                        item.Delete();
                    }
                }

            }
            SQLite.DeleteMovieRecord(MainVM.DataBase.DBFullPath, mv.Movie_Id);
            SetSortData(MainVM.DataBase.Sort, true);
        }

        private void BtnWatchManual_Click(object sender, RoutedEventArgs e)
        {
            MenuToggleButton.IsChecked = false;
            watchData.Clear();
            GetWatchTable(MainVM.DataBase.DBFullPath);
            _ = CheckFolderAsync(CheckMode.Manual);
        }

        private void BtnWatchSetting_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DataBase.DBFullPath))
            {
                MessageBox.Show("管理ファイルが選択されていません。", Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            MenuToggleButton.IsChecked = false;
            var watchWindow = new WatchWindow(MainVM.DataBase.DBFullPath)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            watchWindow.ShowDialog();
            watchData.Clear();
            GetWatchTable(MainVM.DataBase.DBFullPath);
            _= CheckFolderAsync(CheckMode.Watch);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            MenuToggleButton.IsChecked = false;
            var settingWindow = new SettingsWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            settingWindow.ShowDialog();
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
                Title = "設定ファイル(.wb）の選択"
            };

            var result = sfd.ShowDialog();
            if (result == true)
            {
                //todo: 新規ファイル作成処理を追加
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

            var result = ofd.ShowDialog();

            if (result == true)
            {
                var rootItem = MainVM.TreeRoot[0];

                if (rootItem.Children != null)
                {
                    if (rootItem.Children.Count > 0)
                    {
                        int i = 0;
                        foreach (var item in recentFiles.Reverse())
                        {
                            i++;
                            if (item == ofd.FileName)
                            {
                                MenuToggleButton.IsChecked = false;
                                recentFiles = new Stack<string>(recentFiles.Reverse().Skip(i));
                                break;
                            }
                        }
                    }
                }

                while (recentFiles.Count + 1 > recentFileCount)
                {
                    recentFiles = new Stack<string>(recentFiles.Reverse().Skip(1));
                }
                recentFiles.Push(ofd.FileName);

                rootItem.Children.Clear();
                foreach (var item in recentFiles)
                {
                    var childItem = new TreeSource() { Text = item, IsExpanded = false };
                    rootItem.Add(childItem);
                }

                Properties.Settings.Default.LastDoc = ofd.FileName;
                Properties.Settings.Default.Save();
            }
            MenuToggleButton.IsChecked = false;
            OpenDatafile(ofd.FileName);
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
        private void Test_Click(object sender, RoutedEventArgs e)
        {
            //
            var dialogWindow = new DialogWindow()
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = "登録から削除します",
            };

            dialogWindow.checkBox.IsChecked = true;
            dialogWindow.radioArea.Visibility = Visibility.Collapsed;
            dialogWindow.message.Text = "登録からデータを削除します\n（監視対象の場合、再監視で復活します）";
            dialogWindow.checkBox.Content = "サムネイルも削除する";

            dialogWindow.ShowDialog();
            if (dialogWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            if (dialogWindow.checkBox.IsChecked == true)
            {
                //サムネも消す。
            }
        }

        private void TreeLabel_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (sender is Label item)
                {
                    if (!string.IsNullOrEmpty(item.Content.ToString()))
                    {
                        if (item.Content.ToString() != RECENT_OPEN_FILE_LABEL)
                        {
                            UpdateSkin();
                            MenuToggleButton.IsChecked = false;
                            OpenDatafile(item.Content.ToString());
                            Properties.Settings.Default.LastDoc = item.Content.ToString();
                            Properties.Settings.Default.Save();
                        }
                    }
                }
            }
        }

        private void FilterView()
        {
            if (MainVM.MovieRecs.Count > 0)
            {
                var listView = new ListView();
                switch (Tabs.SelectedIndex)
                {
                    case 0: listView = SmallList; break;
                    case 1: listView = BigList; break;
                    case 2: listView = GridList; break;
                    case 3: break;
                    case 4: listView = BigList10; break;
                    default: listView = SmallList; break;
                }

                if (!string.IsNullOrEmpty(MainVM.DataBase.SearchKeyword))
                {
                    var filterList = MainVM.MovieRecs
                        .Where(
                            x => x.Movie_Name.Contains(MainVM.DataBase.SearchKeyword, StringComparison.CurrentCultureIgnoreCase) ||
                            x.Tags.Contains(MainVM.DataBase.SearchKeyword, StringComparison.CurrentCultureIgnoreCase) ||
                            x.Movie_Path.Contains(MainVM.DataBase.SearchKeyword, StringComparison.CurrentCultureIgnoreCase)
                        );

                    if (Tabs.SelectedIndex == 3)
                    {
                        ListDataGrid.ItemsSource = filterList;
                    }
                    else
                    {
                        listView.ItemsSource = filterList;
                    }

                    MainVM.DataBase.SearchCount = filterList.Count();
                }
                else
                {
                    MainVM.DataBase.SearchCount = MainVM.MovieRecs.Count;
                    if (Tabs.SelectedIndex == 3)
                    {
                        ListDataGrid.ItemsSource = MainVM.MovieRecs;
                    }
                    else
                    {
                        listView.ItemsSource = MainVM.MovieRecs;
                    }
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_imeFlag) return;
            if (e.Source is TextBox)
            {
                FilterView();
            }
        }

        private async void PlayMovie(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) return;

            MovieRecords mv;
            mv = GetSelectedItemByTabIndex();
            if (mv == null) return;

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
            if (sender is MenuItem senderObj)
            {
                if (senderObj.Name == "PlayFromThumb")
                {
                    msec = GetPlayPosition(Tabs.SelectedIndex, mv, ref secPos);
                }
            }

            if (!string.IsNullOrEmpty(playerParam))
            {
                playerParam = playerParam.Replace("<file>", $"{mv.Movie_Path}");
                playerParam = playerParam.Replace("<ms>", $"{msec}");
            }

            var moviePath = $"\"{mv.Movie_Path}\"";
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
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        private int GetPlayPosition(int tabIndex, MovieRecords mv, ref int returnPos)
        {
            int msec = 0;

            string currentThumbPath = "";
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
                    var thumbRow = thumbInfo.TotalHeight / thumbInfo.ThumbRows;
                    var thumbCol = thumbInfo.TotalWidth / thumbInfo.ThumbColumns;

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

        private void ComboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox senderObj)
            {
                if (MainVM.MovieRecs.Count > 0)
                {
                    if (senderObj.SelectedValue != null)
                    {
                        var id = senderObj.SelectedValue;
                        SetSortData(id.ToString(), false);
                        UpdateSort();
                    }
                }
            }
        }

        private MovieRecords GetSelectedItemByTabIndex()
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

        private void OpenParentFolder(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) return;

            MovieRecords mv;
            mv = GetSelectedItemByTabIndex();
            if (mv == null) return;

            if (Path.Exists(mv.Movie_Path))
            {
                if (Path.Exists(mv.Dir))
                {
                    Process.Start("explorer.exe", $"/select,{mv.Movie_Path}");
                }
            }
        }

        //todo : モード切り替え付けるべきだな。手動(とにかくチェック但しwatch=1のみ）、常時(watch=1)、一回のみ(auto=1)
        private enum CheckMode
        {
            Auto,
            Watch,
            Manual
        }
        private async Task CheckFolderAsync(CheckMode mode)
        {
            bool flg = false;
            List<QueueObj> addFiles = [];
            string checkExt = Properties.Settings.Default.CheckExt;

            var title = "フォルダ監視中";
            var Message = "";
            NotificationManager notificationManager = new();

            while (watchData != null)
            {
                if (mode == CheckMode.Manual)
                {
                    notificationManager.Show(title, "マニュアルで監視実施中…", NotificationType.Notification, "ProgressArea");
                }

                foreach (DataRow row in watchData.Rows)
                {
                    //存在しない監視フォルダは読み飛ばし。
                    if (!Path.Exists(row["dir"].ToString())) { continue; }

                    //Autoで呼ばれた＝初回＆設定が起動時チェックなしは読み飛ばし。
                    if ((mode == CheckMode.Auto) && ((long)row["auto"] != 1)) { continue; }

                    //Watchで呼ばれた＝起動中監視＆設定が起動中監視なしは読み飛ばし。
                    if ((mode == CheckMode.Watch) && ((long)row["watch"] != 1)) { continue; }

                    string checkFolder = row["dir"].ToString();
                    bool IsCheckSubFolder = ((long)row["sub"] == 1);
                    // ファイルリスト
                    var di = new DirectoryInfo(checkFolder);
                    EnumerationOptions enumOption = new()
                    {
                        RecurseSubdirectories = IsCheckSubFolder
                    };

                    try
                    {
                        IEnumerable<FileInfo> ssFiles = checkExt.Split(',').SelectMany(filter => di.EnumerateFiles(filter,enumOption));
                        foreach (var ssFile in ssFiles)
                        {
                            var searchKey = ssFile.FullName.Replace("'", "''");
                            var movies = movieData.Select($"movie_path = '{searchKey}'");
                            if (movies.Length == 0)
                            {
                                Message = checkFolder;
                                notificationManager.Show(title, $"{Message}に更新あり。", NotificationType.Notification ,"ProgressArea");

                                MovieInfo mvi = new(ssFile.FullName);
                                SQLite.InsertMovieTable(MainVM.DataBase.DBFullPath, mvi);

                                flg = true;

                                QueueObj temp = new()
                                {
                                    MovieId = mvi.MovieId,
                                    MovieFullPath = mvi.MoviePath,
                                    Tabindex = Tabs.SelectedIndex
                                };
                                addFiles.Add(temp);
                            }
                        }
                    }
                    catch (IOException)
                    {
                        //起動中に監視フォルダにファイルコピーされっと例外発生するんよね。
                        await Task.Delay(1000);
                    }
                }
                if (flg)
                {
                    SetSortData(MainVM.DataBase.Sort, true);

                    foreach (var item in addFiles)
                    {
                        queueThumb.Enqueue(item);
                    }
                    flg = false;
                    addFiles.Clear();
                }

                //呼び出しモードがWatchでない場合は、一回処理して抜ける
                //コピー中対応はループ内だな…。Autoの場合は監視フォルダに既にあるファイルでデータ未登録をチェック。
                //常時監視にすると、起動中にファイルが追加されても反応するとなる。
                if (mode != CheckMode.Watch)
                {
                    return;
                }
                await Task.Delay(3000);
            }
        }

        //サムネイル作成用に起動時にぶん投げるタスク。常時起動。終了条件はねぇ。
        private async Task CheckThumbAsync()
        {
            while (true)
            {
                if (queueThumb.Count > 0)
                {
                    QueueObj obj = queueThumb.Dequeue();
                    if (obj == null) { continue; }
                    await CreateThumbAsync(obj).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(2000);
                }
            }
        }

        /// <summary>
        /// サムネイル作成本体
        /// </summary>
        /// <param name="queueObj">取り出したQueueの中身</param>
        /// <param name="IsManual">マニュアル作成かどうか</param>
        /// <returns></returns>
        private async Task CreateThumbAsync(QueueObj queueObj, bool IsManual = false)
        {
            TabInfo tbi = new(queueObj.Tabindex, MainVM.DataBase.DBName, MainVM.DataBase.ThumbFolder);
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

            var title = $"サムネイル作成中({queueThumb.Count + 1})";
            var Message = $"{queueObj.MovieFullPath}";
            NotificationManager notificationManager = new();
            var progress = notificationManager.ShowProgressBar(title, false, true, "ProgressArea", false, 2);

            if (!Path.Exists(queueObj.MovieFullPath))
            {
                progress.Report((100, $"{Message} ファイルが存在しません。", title, true));
                return;
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

            // Stopwatchクラス生成
            var sw = new Stopwatch();

            try
            {
                double durationSec = 0;
                using var capture = new VideoCapture(queueObj.MovieFullPath);
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
                var oldTempFiles = Directory.GetFiles(tempPath, $"*{tempFileBody}*.jpg", SearchOption.TopDirectoryOnly);
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

                var progressCounter = (100 / (thumbInfo.ThumbCounts));
                int totalProgress = 0;

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
                            if (msecCounter > 100)
                            {
                                break;
                            }
                            msecCounter++;
                        }
                        // 計測開始
                        sw.Stop();

                        TimeSpan ts = sw.Elapsed;
                        if (ts.Seconds > 60)
                        {
                            return;
                        }

                        if (img == null) {
                            progress.Report((100, $"{Message} のイメージが取得できませんでした。", title, true));
                            IsSuccess = false;
                            return;
                        }
                        if (img.Width == 0) {
                            progress.Report((100, $"{Message} の幅が取得できませんでした。", title, true));
                            IsSuccess = false;
                            return;
                        }
                        if (img.Height == 0) {
                            progress.Report((100, $"{Message} の高さが取得できませんでした。", title, true));
                            IsSuccess = false;
                            return;
                        }

                        int w = img.Width;
                        int h = img.Height;
                        int wdiff = 0;
                        int hdiff = 0;

                        // アスペクト比の算出
                        float aspect = (float)img.Width / img.Height;
                        if (aspect > 1.34)
                        {
                            //横長だよね。
                            h = (int)Math.Floor((decimal)img.Height / 3);
                            w = (int)Math.Floor((decimal)h * 4);
                            h = img.Height;
                            wdiff = (img.Width - w) / 2;
                            hdiff = 0;
                        }
                        //縦長動画の場合はどうするよ？ 4:3の場合は何もしない。
                        if (aspect < 1.33)
                        {
                            //縦長かスクエアかな？
                            w = (int)Math.Floor((decimal)img.Width / 4);
                            h = (int)Math.Floor((decimal)w * 3);
                            w = img.Width;
                            hdiff = (img.Height - h) / 2;
                            wdiff = 0;
                        }

                        using Mat temp = new(img, new OpenCvSharp.Rect(wdiff, hdiff, w, h));

                        // サイズ変更した画像を保存する
                        var saveFile = Path.Combine(tempPath, $"tn_{tempFileBody}{i:D2}.jpg");
                        var sz = new OpenCvSharp.Size { Width = tbi.Width , Height = tbi.Height };

                        if (Properties.Settings.Default.IsResizeThumb)
                        {
                            using Mat dst = new();
                            Cv2.Resize(temp, dst, sz);
                            OpenCvSharp.Extensions.BitmapConverter.ToBitmap(dst).Save(saveFile,ImageFormat.Jpeg);
                        }
                        else
                        {
                            OpenCvSharp.Extensions.BitmapConverter.ToBitmap(temp).Save(saveFile,ImageFormat.Jpeg);
                        }

                        paths.Add(saveFile);

                        img.Dispose();
                        progress.Report((totalProgress += progressCounter, Message, title, false));
                    }
                });
                capture.Dispose();

                if (!IsSuccess) { return; }

                progress.Report((100, Message, title, false));

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
                progress.Dispose();
#if DEBUG == false
                // 既存テンプファイルの削除
                oldTempFiles = Directory.GetFiles(tempPath, $"*{tempFileBody}*.jpg", SearchOption.TopDirectoryOnly);
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

            foreach (var item in MainVM.MovieRecs.Where(x => x.Movie_Id == queueObj.MovieId))
            {
                switch (queueObj.Tabindex)
                {
                    case 0: item.ThumbPathSmall = saveThumbFileName; break;
                    case 1: item.ThumbPathBig = saveThumbFileName; break;
                    case 2: item.ThumbPathGrid = saveThumbFileName; break;
                    case 3: item.ThumbPathList = saveThumbFileName; break;
                    case 4: item.ThumbPathBig10 = saveThumbFileName; break;
                    default: break;
                }
            }
        }

        private void CreateThumb_EqualInterval(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) return;

            MovieRecords mv;
            mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            QueueObj tempObj = new()
            {
                MovieId = mv.Movie_Id,
                MovieFullPath = mv.Movie_Path,
                Tabindex = Tabs.SelectedIndex
            };
            queueThumb.Enqueue(tempObj);

            //割り込みたかったが… 割り込んだ後、何故か処理を再開してくれなくて。Queueに追加する事にした。
            //追加するだけなので、同期処理で良い（はず）
            //await CreateThumb(Tabs.SelectedIndex, mv.Movie_Path, mv.Movie_Id);
        }

        #region マニュアルサムネイル用のプレイヤー関連
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
        }

        private void UxVideoPlayer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            uxVideoPlayer.Pause();
        }

        /// <summary>
        /// ストップボタンクリック時のイベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            uxVideoPlayer.Stop();
            timer.Stop();
            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;
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

            if (Tabs.SelectedItem == null) return;

            MovieRecords mv;
            mv = GetSelectedItemByTabIndex();
            if (mv == null) return;

            timer.Stop();

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

            await CreateThumbAsync(queueObj, true);
        }

        private async void ManualThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) return;

            MovieRecords mv;
            mv = GetSelectedItemByTabIndex();
            if (mv == null) return;

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

            //動画の再生
            uxVideoPlayer.Volume = 0;
            uxVideoPlayer.Play();

            //再生位置の移動
            uxVideoPlayer.Position = new TimeSpan(0, 0, 0, 0, msec);
            await Task.Delay(500);
            uxVideoPlayer.Volume = (double)uxVolumeSlider.Value;
            uxVideoPlayer.Pause();
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