using IndigoMovieManager.ModelView;
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
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using static IndigoMovieManager.Tools;
using static IndigoMovieManager.SQLite;

namespace IndigoMovieManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        [GeneratedRegex(@"^\r\n+")]
        private static partial Regex MyRegex();

        private const string RECENT_OPEN_FILE_LABEL = "最近開いたファイル";
        private readonly int recentFileCount = 7;
        private Stack<string> recentFiles = new();

        private IEnumerable<MovieRecords> filterList = [];
        private static readonly Queue<QueueObj> queueThumb = [];

        private DataTable systemData;
        private DataTable movieData;
        private DataTable watchData;

        private readonly MainWindowViewModel MainVM = new();
        internal System.Windows.Point lbClickPoint = new();

        private DateTime _lastSliderTime = DateTime.MinValue;
        private readonly TimeSpan _timeSliderInterval = TimeSpan.FromSeconds(0.1);

        //結局、タイマー方式で動画とマニュアルサムネイルのスライダーを同期させた
        private readonly DispatcherTimer timer;
        private bool isDragging = false;

        //マニュアルサムネイル時の右クリックしたカラムの返却を受け取る変数
        private int manualPos = 0;

        //IME起動中的なフラグ。日本語入力時にインクリメンタルサーチさせない為。
        private bool _imeFlag = false;

        //フォルダ監視タスクのキャンセル用（FileSystemWatcherにしたらロジック変更で要らんかもだなぁｗ）
        private static CancellationTokenSource _cs = new();
        private static List<FileSystemWatcher> fileWatcher = [];

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
            recentFileCount = Properties.Settings.Default.RecentFilesCount;
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

            DataContext = MainVM;

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


        // <summary>
        /// ファイル変更
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileChanged(object sender, FileSystemEventArgs e)
        {
            string s = string.Format($"{DateTime.Now:yyyy/MM/dd HH:mm:ss} :");

            var ext = Path.GetExtension(e.FullPath);
            string checkExt = Properties.Settings.Default.CheckExt.Replace("*", "");
            string[] checkExts = checkExt.Split(',');

            if (checkExts.Contains(ext))
            {
                //変更があったときに結果を表示する
                switch (e.ChangeType)
                {
                    case WatcherChangeTypes.Changed:
                        s += $"【{e.ChangeType}】{e.FullPath}";
                        break;
                    case WatcherChangeTypes.Created:
                        s += $"【{e.ChangeType}】{e.FullPath}";
                        break;
                    case WatcherChangeTypes.Deleted:
                        s += $"【{e.ChangeType}】{e.FullPath}";
                        break;
                }
                Debug.WriteLine(s);
            }
        }

        /// <summary>
        /// ファイル名変更
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileRenamed(object sender, RenamedEventArgs e)
        {
            var ext = Path.GetExtension( e.FullPath );
            string checkExt = Properties.Settings.Default.CheckExt.Replace("*", "");
            string[] checkExts = checkExt.Split(',');

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
                foreach (var item in MainVM.MovieRecs.Where(x => x.Movie_Path == e.OldFullPath))
                {
                    item.Movie_Path = e.FullPath;
                    item.Movie_Name = Path.GetFileNameWithoutExtension(e.FullPath);
                    
                    Debug.WriteLine(item.Movie_Name);
                }
            }
        }



        private void RunWatcher(string watchFolder)
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
                IncludeSubdirectories = true,

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

            fileWatcher.Add(item);
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

        //todo : 新規データベース作成。
        //todo : 検索ボックスのヒストリ機能。データベースへ追加と、既定数のヒストリ読み込み、ボックスへのヒストリ追加。
        //todo : タグ編集、コピー、ペースト。コピペはコピーバッファ使わずに内部で専用でいいと思われ。
        //todo : タグ追加、タグ削除。タグ追加は編集の亜流として、タグ削除はちょっとI/F考えること。
        //todo : bookmark。ファイル[(フレーム)YY-MM-DD].jpg 640x480の様子。
        //todo : 個別設定の画面作成
        //todo : リネーム処理、そしてサムネのリネームも。
        //todo : 重複チェック。本家は恐らくファイル名もチェックで使ってる模様。
        //       こっちで登録しても再度本家に登録されるケースがあったのは、ファイル名の大文字小文字が違ってたから。
        //       本家のmovie_nameは小文字変換かけてる模様。合わせてみたら再登録されなかったので恐らく正解。

        private void OpenDatafile(string dbFullPath)
        {
            //強制的に-1にする。前回のタブが0だった場合の対応
            Tabs.SelectedIndex = -1;
            queueThumb.Clear();
            watchData?.Clear();
            fileWatcher?.Clear();
            _cs = new();

            MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbFullPath);
            MainVM.DbInfo.DBFullPath = dbFullPath;
            GetSystemTable(dbFullPath);
            GetWatchTable(dbFullPath);
            MainVM.MovieRecs.Clear();
            if (MainVM.DbInfo.Sort != null)
            {
                FilterAndSort(MainVM.DbInfo.Sort, true);
            }
            if (MainVM.DbInfo.Skin != null)
            {
                SwitchTab(MainVM.DbInfo.Skin);
            }

            //起動時のみなら、Autoで一回でいいんじゃね？とかの判断入れた方がいいか？
            //watchDataが複数行なのでなぁ。監視ループで読み飛ばすので良しとした。起動中監視がチェックされてない場合、
            //無駄なスレッドが動き続けることになるのよなぁ。ホントは監視対象の行ごとにスレッド立てるべき？
            //_ = CheckFolderAsync(CheckMode.Auto, _cs.Token);
            //_ = CheckFolderAsync(CheckMode.Watch, _cs.Token);

            foreach (DataRow row in watchData.Rows)
            {
                string checkFolder = row["dir"].ToString();
                RunWatcher(checkFolder);
            }
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

        private void GetSystemTable(string dbPath)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                string sql = @"SELECT * FROM system";
                systemData = GetData(dbPath, sql);

                var skin = SelectSystemTable("skin");
                MainVM.DbInfo.Skin = skin == "" ? "Default Small" : skin;

                var sort = SelectSystemTable("sort");
                MainVM.DbInfo.Sort = sort == "" ? "1" : sort ;

                MainVM.DbInfo.ThumbFolder = SelectSystemTable("thum");

                MainVM.DbInfo.BookmarkFolder = SelectSystemTable("bookmark");
            }
            else
            {
                systemData?.Clear();
            }
        }

        private void GetWatchTable(string dbPath)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                watchData = GetData(dbPath, $"SELECT * FROM watch");
            }
        }

        private void UpdateSort()
        {
            if (!string.IsNullOrEmpty(MainVM.DbInfo.Sort))
            {
                UpdateSystemTable(Properties.Settings.Default.LastDoc, "sort", MainVM.DbInfo.Sort);
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
            UpdateSystemTable(Properties.Settings.Default.LastDoc, "skin", tabName);
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

        private static string GetSortWordForLinq(string id)
        {
            #region ソートキーワードの選択
            string sortWord = id switch
            {
                "0" or "1" => "Last_Date",
                "2" or "3" => "File_Date",
                "6" or "7" => "Score",
                "8" or "9" => "View_Count",
                "10" or "11" => "Kana",
                "12" or "13" => "Movie_Name",
                "14" or "15" => "Movie_Path",
                "16" or "17" => "Movie_Size",
                "18" or "19" => "Regist_Date",
                "20" or "21" => "Movie_Length",
                "22" or "23" => "Comment1",
                "24" or "25" => "Comment2",
                "26" or "27" => "Comment3",
                _ => "Movie_Id",
            };
            #endregion
            return sortWord;
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
                _ = SetRecordsToSource(MainVM.DbInfo.DBFullPath, false);
            }

            //対象のカレントタブ＝Viewの取得
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

            //まずは絞り込み。
            filterList = MainVM.MovieRecs;
            if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword))
            {
                //todo : 検索のAnd機能や、Or機能、SQL直実行機能とかの検索機能強化。取りあえずAnd検索のみ。
                var searchKeyword = MainVM.DbInfo.SearchKeyword.ToLower();

                if ((searchKeyword.StartsWith('{') == true) && (searchKeyword.EndsWith('}') == true))
                {
                    //todo : SQL文で検索の場合。中身の精査までせにゃならんかね。
                    Debug.WriteLine($"SQL = {searchKeyword}");
                }
                else if ((searchKeyword.StartsWith('"') == true) && (searchKeyword.EndsWith('"') == true))
                {
                    //todo : ダブルコーテーションで括られてる場合は、ダイレクトにそのまま検索。
                    Debug.WriteLine($"Direct = {searchKeyword}");
                }
                else
                {
                    var searchKeywords = searchKeyword.Split(" ");
                    searchKeywords = searchKeyword.Split(" or ");
                    if (searchKeywords.Length > 1)
                    {
                        //todo : or区切りのor検索の場合。
                        Debug.WriteLine($"Or = {searchKeyword}");
                    }
                    else
                    {
                        searchKeywords = searchKeyword.Split(" ");
                        if (searchKeywords.Length > 1)
                        {
                            //todo : スペース区切りのAnd検索の場合。
                            Debug.WriteLine($"And = {searchKeyword}");

                            foreach (var item in searchKeywords)
                            {
                                filterList = filterList
                                    .Where(
                                        x => x.Movie_Name.Contains(item, StringComparison.CurrentCultureIgnoreCase) ||
                                        x.Tags.Contains(item, StringComparison.CurrentCultureIgnoreCase) ||
                                        x.Movie_Path.Contains(item, StringComparison.CurrentCultureIgnoreCase)
                                    );
                            }
                        }
                        else
                        {
                            filterList = MainVM.MovieRecs
                                .Where(
                                    x => x.Movie_Name.Contains(searchKeyword, StringComparison.CurrentCultureIgnoreCase) ||
                                    x.Tags.Contains(searchKeyword, StringComparison.CurrentCultureIgnoreCase) ||
                                    x.Movie_Path.Contains(searchKeyword, StringComparison.CurrentCultureIgnoreCase)
                                );
                        }
                    }
                }
                MainVM.DbInfo.SearchCount = filterList.Count();
            }
            else
            {
                MainVM.DbInfo.SearchCount = MainVM.MovieRecs.Count;
            }
#if DEBUG
            sw.Stop();
            ts = sw.Elapsed;
            Debug.WriteLine($"絞り込み経過時間：{ts.Milliseconds} ミリ秒");
#endif

#if DEBUG
            sw.Restart();
#endif
            var cv = CollectionViewSource.GetDefaultView(filterList);
            cv.SortDescriptions.Clear();
            ListSortDirection sortOption = new();
            var sortWordLinq = GetSortWordForLinq(MainVM.DbInfo.Sort);

            if (!int.TryParse(id, out int sortId)) { sortId = 0; }
            int[] conditionDescending = [0, 2, 6, 8, 11, 13, 15, 16, 18, 20, 23, 25, 27];
            int[] conditionAscending = [1, 3, 7, 9, 10, 12, 14, 17, 19, 21, 22, 24, 26];

            var matchASC = conditionAscending.Where(sortId.Equals);
            if (matchASC.Any()) {
                sortOption = ListSortDirection.Ascending;
            }
            else
            {
                var matchDSC = conditionDescending.Where(sortId.Equals);
                if (matchDSC.Any()) { sortOption = ListSortDirection.Descending; }
            }

            SortDescription sortDescription = new(sortWordLinq, sortOption);
            cv.SortDescriptions.Add(sortDescription);
            SmallList.ItemsSource = cv;

#if DEBUG
            sw.Stop();
            ts = sw.Elapsed;
            Debug.WriteLine($"ソート経過時間：{ts.Milliseconds} ミリ秒");
#endif

            if (Tabs.SelectedIndex == 3)
            {
                ListDataGrid.ItemsSource = filterList;
            }
            else
            {
                listView.ItemsSource = filterList;
            }
        }

        private Task SetRecordsToSource(string dbPath, bool IsGetNew = true)
        {
            if (IsGetNew)
            {
                string sql = @"SELECT * FROM movie";
                movieData = GetData(dbPath, sql);
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
                        TabInfo tbi = new(i, dbName, MainVM.DbInfo.ThumbFolder);

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
                        Drive = Path.GetPathRoot(row["movie_path"].ToString()),
                        Dir = Path.GetDirectoryName(row["movie_path"].ToString())
                    };
                    #endregion

                    MainVM.MovieRecs.Add(item);
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
                if (index == -1) { return; }

                if (!filterList.Any()) { return; }

                #region LinqのWhereでErrorパスを持つレコードを絞り込む
                //stack : この書き方が何とかならんかなぁ。ダサいなぁ。思いつかないので放置で。
                MovieRecords[] query = [];
                switch (index)
                {
                    case 0:
                        SmallList.ItemsSource = filterList;
                        query = MainVM.MovieRecs.Where(x => x.ThumbPathSmall.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable().ToArray();
                        break;
                    case 1:
                        BigList.ItemsSource = filterList;
                        query = MainVM.MovieRecs.Where(x => x.ThumbPathBig.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable().ToArray();
                        break;
                    case 2:
                        GridList.ItemsSource = filterList;
                        query = MainVM.MovieRecs.Where(x => x.ThumbPathGrid.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable().ToArray();
                        break;
                    case 3:
                        ListDataGrid.ItemsSource = filterList;
                        query = MainVM.MovieRecs.Where(x => x.ThumbPathList.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable().ToArray();
                        break;
                    case 4:
                        BigList10.ItemsSource = filterList;
                        query = MainVM.MovieRecs.Where(x => x.ThumbPathBig10.Contains("error", StringComparison.CurrentCultureIgnoreCase)).AsEnumerable().ToArray();
                        break;
                }
                #endregion

                if (query.Length < 1)
                {
                    return;
                }

                //前の作成を終わったかどうか、判断したかったんだけども…プログレスバーが残ることがあるので、そのために。
                //一回分のサムネ作成の猶予があれば良いと言う事で。ここ以降はぶん投げるので、何秒待ってもいいのはいいんだけど、
                //中々次が始まらないのもあれだし、タブを切り替える度に通る所だし、こんなもんでどうだろうか。
                //と思ってたけど、待ち受けほぼなしでもいいんじゃないかなぁと。
                //await Task.Delay(2000);
                await Task.Delay(50);

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

            if (Tabs.SelectedItem == null) return;

            MovieRecords mv;
            mv = GetSelectedItemByTabIndex();
            if (mv == null) return;

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

        private void DeleteMovieRecord_Click(object sender, RoutedEventArgs e)
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

            if (Tabs.SelectedItem == null) return;

            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null) return;

            if (keyName.ToLower() is "delete" or "deletemovie" or "deletefile")
            {

            }

            string msg = $"登録からデータを削除します\n（監視対象の場合、再監視で復活します）";
            string title = "登録から削除します";
            string radio1Content = "";
            string radio2Content = "";
            bool useRadio = false;

            if (keyName == "deletefile")
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
                        IEnumerable<FileInfo> ssFiles = di.EnumerateFiles($"*{checkFileName}*.jpg", enumOption);
                        foreach (var item in ssFiles)
                        {
                            item.Delete();
                        }
                    }
                }
                DeleteMovieRecord(MainVM.DbInfo.DBFullPath, rec.Movie_Id);
            }
            FilterAndSort(MainVM.DbInfo.Sort, true);
        }

        private void BtnReCreateThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                MessageBox.Show("管理ファイルが選択されていません。", Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            if (Tabs.SelectedItem == null) return;

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
                Title = "設定ファイル(.wb）の選択"
            };

            var result = sfd.ShowDialog();
            if (result == true)
            {
                //todo : 新規ファイル作成処理を追加予定地
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
                var rootItem = MainVM.RecentTreeRoot[0];

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
                OpenDatafile(ofd.FileName);
                MenuToggleButton.IsChecked = false;
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
        private void Test_Click(object sender, RoutedEventArgs e)
        {
            //_cs.Cancel(); // キャンセルの送信
            
        }

        private void TagEdit_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) return;

            MovieRecords mv;
            mv = GetSelectedItemByTabIndex();
            if (mv == null) return;

            var tagEditWindow = new TagEdit
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = mv
            };
            tagEditWindow.ShowDialog();

            if (tagEditWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }
            var dataContext = tagEditWindow.DataContext as MovieRecords;
            
            //リスト状態のタグと、改行付のタグを作る所
            var tagsEditedWithNewLine = dataContext.Tags;
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
            mv.Tag = tagArray;
            mv.Tags = tagsWithNewLine;

            //DBのタグを更新する。
            UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, mv.Movie_Id, "tag", mv.Tags);

            FilterAndSort(MainVM.DbInfo.Sort);
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
                                var settingWindow = new SettingsWindow
                                {
                                    Owner = this,
                                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                };
                                settingWindow.ShowDialog();
                                break;
                            case "個別設定":
                                //todo : 個別設定画面を呼び出す処理
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
                                watchData.Clear();
                                _cs = new();
                                GetWatchTable(MainVM.DbInfo.DBFullPath);
                                break;

                            case "監視フォルダ更新チェック":
                                //_ = CheckFolderAsync(CheckMode.Manual, _cs.Token);
                                break;

                            case "全ファイルサムネイル再作成":
                                if (Tabs.SelectedItem == null) return;

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

        private async void PlayMovie_Click(object sender, RoutedEventArgs e)
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
                mv.View_Count += 1;
                var now = DateTime.Now;
                var result = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                mv.Last_Date = result.ToString("yyyy-MM-dd HH:mm:ss");

                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, mv.Movie_Id, "view_count", mv.View_Count);
                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, mv.Movie_Id, "last_date", result);
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        private void TreeNode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button item)
            {
                if (!string.IsNullOrEmpty(item.Tag.ToString()))
                {
                    var tag = item.Tag.ToString();
                    if (tag != RECENT_OPEN_FILE_LABEL)
                    {
                        UpdateSkin();
                        UpdateSort();
                        OpenDatafile(tag);
                        Properties.Settings.Default.LastDoc = tag;
                        Properties.Settings.Default.Save();
                        MenuToggleButton.IsChecked = false;
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

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_imeFlag) return;
            if (e.Source is TextBox)
            {
                FilterAndSort(MainVM.DbInfo.Sort);
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

        private void Tab_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Tabs.SelectedIndex == -1) { return; }
            if (Tabs.SelectedItem == null) { return; }

            switch (e.Key)
            {
                case Key.Enter:                         //再生
                    PlayMovie_Click(sender,e);　break;
                case Key.F6:                            //タグ編集
                    TagEdit_Click(sender, e); break;
                case Key.C:                             //タグのコピー
                    break;
                case Key.V:                             //タグの貼り付け
                    break;
                case Key.Add:                           //スコアプラス
                case Key.Subtract:                      //スコアマイナス
                    MenuScore_Click(sender, e);
                    break;
                case Key.Delete:                        //登録の削除
                    DeleteMovieRecord_Click(sender,e);　
                    break;
                case Key.F2:                            //名前の変更
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
            if (sender is ComboBox senderObj)
            {
                if (MainVM.MovieRecs.Count > 0)
                {
                    if (senderObj.SelectedValue != null)
                    {
                        var id = senderObj.SelectedValue;
                        FilterAndSort(id.ToString(), false);
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
                    foreach(MovieRecords item in SmallList.SelectedItems) { mv.Add(item); }
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

        //モード切り替え付けるべきだな。手動(とにかくチェック但しwatch=1のみ）、常時(watch=1)、一回のみ(auto=1)
        private enum CheckMode
        {
            Auto,
            Watch,
            Manual
        }

        private async Task CheckFolderAsync(CheckMode mode, CancellationToken ct)
        {
            bool flg = false;
            List<QueueObj> addFiles = [];
            string checkExt = Properties.Settings.Default.CheckExt;

            var title = "フォルダ監視中";
            var Message = "";
            NotificationManager notificationManager = new();

            while (watchData.Rows.Count > 0)
            {
                if (mode == CheckMode.Manual)
                {
                    notificationManager.Show(title, "マニュアルで監視実施中…", NotificationType.Notification, "ProgressArea");
                }

                // 検出されたかどうかの判定
                if (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException("キャンセルを検出しました。");
                }

                foreach (DataRow row in watchData.Rows)
                {
                    // 検出されたかどうかの判定
                    if (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("キャンセルを検出しました。");
                    }

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
                        bool IsHit = false;
                        foreach (var ssFile in ssFiles)
                        {
                            // 検出されたかどうかの判定
                            if (ct.IsCancellationRequested)
                            {
                                throw new OperationCanceledException("キャンセルを検出しました。");
                            }

                            var searchFileName = ssFile.FullName.Replace("'", "''");
                            DataRow[] movies = movieData.Select($"movie_path = '{searchFileName}'");
                            if (movies.Length == 0)
                            {
                                Message = checkFolder;
                                if (IsHit == false)
                                {
                                    notificationManager.Show(title, $"{Message}に更新あり。", NotificationType.Notification, "ProgressArea");
                                    IsHit = true;
                                }

                                MovieInfo mvi = new(ssFile.FullName);
                                InsertMovieTable(MainVM.DbInfo.DBFullPath, mvi);

                                flg = true;

                                //ここでQueueの元ネタに入れてるのな。
                                //サムネイルファイルが存在するかどうかチェック。あればQueueに入れない。
                                TabInfo tbi = new(Tabs.SelectedIndex, MainVM.DbInfo.DBName, MainVM.DbInfo.ThumbFolder);
                                // ファイルハッシュ取得
                                var hash = GetHashCRC32(mvi.MoviePath);

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
                                    Tabindex = Tabs.SelectedIndex
                                };
                                addFiles.Add(temp);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if (e.GetType() == typeof(IOException))
                        {
                            //起動中に監視フォルダにファイルコピーされっと例外発生するんよね。
                            await Task.Delay(1000, ct);
                        }
                    }
                    await Task.Delay(1000, ct);
                }
                if (flg)
                {
                    FilterAndSort(MainVM.DbInfo.Sort, true);

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
                await Task.Delay(1000, ct);
            }
        }

        //サムネイル作成用に起動時にぶん投げるタスク。常時起動。終了条件はねぇ。
        private async Task CheckThumbAsync()
        {
            var title = "サムネイル作成中";
            NotificationManager notificationManager = new();
            bool IsHit = false;
            double progressCounter = 0;
            double totalProgress = 0;
            int totalCount = 0;

            while (true)
            {
                if (queueThumb.Count < 1)
                {
                    title = "サムネイル作成中";                  
                    totalCount = 0;
                    IsHit = false;
                    totalProgress = 0;
                    await Task.Delay(4000);
                    continue;
                }

                var progress = notificationManager.ShowProgressBar(title, false, true, "ProgressArea", false, 2, "");
                int i = 0;
                while (queueThumb.Count > 0)
                {
                    if (!IsHit)
                    {
                        totalCount = queueThumb.Count;
                        progressCounter = 100d / queueThumb.Count;
                        IsHit = true;
                    }

                    i++;
                    title = $"サムネイル作成中 ({i}/{totalCount})";

                    QueueObj queueObj = queueThumb.Dequeue();
                    if (queueObj == null) { continue; }

                    var Message = $"{queueObj.MovieFullPath}";
                    progress.Report((totalProgress += progressCounter, Message, title, false));
                    await CreateThumbAsync(queueObj).ConfigureAwait(false);
                }
                progress.Dispose();
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
                        _ => Path.Combine(noFileJpeg, "noFileSmall.jpg"),
                    };
                    File.Copy(noFileJpeg, saveThumbFileName, true);
                }
                else
                {
                    //todo : ムービーはないが、サムネイルはある場合の処理
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
                    });
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

        /// <summary>
        /// 手動等間隔サムネイル作成
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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

            if (Tabs.SelectedItem == null) return;

            MovieRecords mv;
            mv = GetSelectedItemByTabIndex();
            if (mv == null) return;

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

            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;

            uxVideoPlayer.Stop();
            IsPlaying = false;

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
            await Task.Delay(1000);

            //動画の再生
            uxVideoPlayer.Volume = 0;
            uxVideoPlayer.Play();

            //再生位置の移動
            uxVideoPlayer.Position = new TimeSpan(0, 0, 0, 0, msec);
            //await Task.Delay(500);
            uxVideoPlayer.Volume = (double)uxVolumeSlider.Value;
            //uxVideoPlayer.Pause();
            IsPlaying = true;
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