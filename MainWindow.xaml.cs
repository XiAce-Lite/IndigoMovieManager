using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Layout.Serialization;
using IndigoMovieManager.DB;
using IndigoMovieManager.ModelViews;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueuePipeline;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
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
            Manual,
        }

        private Task _thumbCheckTask;
        private CancellationTokenSource _thumbCheckCts = new();

        [GeneratedRegex(@"^\r\n+")]
        private static partial Regex MyRegex();

        private const string RECENT_OPEN_FILE_LABEL = "最近開いたファイル";

        /// <summary>
        /// サムネイルキューを血眼で見張る待機間隔（ミリ秒）だぜ！👀
        /// </summary>
        private const int ThumbnailQueuePollIntervalMs = 3000;

        /// <summary>
        /// Everything先生に差分を尋ねるポーリング間隔（ミリ秒）！爆速の秘訣！🚀
        /// </summary>
        private const int EverythingWatchPollIntervalMs = 3000;
        private const int EverythingWatchPollIntervalBusyMs = 15000;
        private const int EverythingWatchPollIntervalMediumMs = 6000;
        private const int EverythingWatchPollBusyThreshold = 200;
        private const int EverythingWatchPollMediumThreshold = 50;
        private const string DockLayoutFileName = "layout.xml";
        private const string ThumbnailProgressContentId = "ToolThumbnailProgress";
        // 一時対応: サムネイル作成中ダイアログ表示を止める。
        private static readonly bool TemporaryPauseThumbnailProgressDialog = true;
        // 一時対応: 進捗タブのDB登録待ち/DB総数表示を止める。
        private static readonly bool TemporaryPauseThumbnailProgressDbCount = true;
        // 一時対応: 進捗タブのCPU/GPU/HDDメーター値更新を止める。
        private static readonly bool TemporaryPauseThumbnailProgressMeters = true;

        /// <summary>
        /// QueueDBに怒涛の勢いで書き込むためのバッチ窓口（100〜300ms）！ここでまとめてドカンと流す！🔥
        /// </summary>
        private const int ThumbnailQueuePersistBatchWindowMs = 150;
        private const int ThumbnailProgressUiIntervalMs = 500;
        private const int ThumbnailProgressSnapshotFallbackIntervalMs = 3000;
        private Stack<string> recentFiles = new();

        private IEnumerable<MovieRecords> filterList = [];
        private int _filterAndSortRequestRevision;

        /// <summary>
        /// ワーカー達が容赦なく投げ込んでくるジョブを受け止めるチャネル！Persister（単一Reader）が一人で捌き切ってDB化する最強の盾！盾🛡️
        /// </summary>
        private static readonly Channel<QueueRequest> queueRequestChannel =
            Channel.CreateUnbounded<QueueRequest>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                }
            );

        private readonly ThumbnailCreationService _thumbnailCreationService = new(
            new AppVideoMetadataProvider(),
            new AppThumbnailLogger()
        );
        private readonly ThumbnailQueueProcessor _thumbnailQueueProcessor = new();
        private readonly IThumbnailQueueProgressPresenter _thumbnailQueueProgressPresenter =
            TemporaryPauseThumbnailProgressDialog
                ? NoOpThumbnailQueueProgressPresenter.Instance
                : new AppThumbnailQueueProgressPresenter();
        private readonly ThumbnailProgressRuntime _thumbnailProgressRuntime = new();
        private readonly ThumbnailQueuePersister _thumbnailQueuePersister;

        /// <summary>
        /// Persister本体じゃなく「監視タスク」を握っておくぜ！もし例外で死んでも不死鳥の如く蘇らせるための命綱だ！🐦‍🔥
        /// </summary>
        private Task _thumbnailQueuePersisterTask;
        private CancellationTokenSource _thumbnailQueuePersisterCts = new();

        /// <summary>
        /// Everything先生による監視ポーリングの完全常駐タスク！こいつが休むことはない！👁️
        /// </summary>
        private Task _everythingWatchPollTask;
        private CancellationTokenSource _everythingWatchPollCts = new();
        private int _lastEverythingPollDelayMs = EverythingWatchPollIntervalMs;

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
        private readonly DispatcherTimer _thumbnailProgressUiTimer;
        private int _thumbnailProgressUiTickAccumulatedMs;
        // 進捗スナップショット更新要求はここで集約し、UI反映の連打を抑える。
        private int _thumbnailProgressSnapshotRefreshQueued;
        private int _thumbnailProgressSnapshotRefreshRequested;
        private long _thumbnailProgressLastAppliedSnapshotVersion = -1;
        private int _thumbnailProgressLastAppliedDbPendingCount = -1;
        private int _thumbnailProgressLastAppliedDbTotalCount = -1;
        private int _thumbnailProgressLastAppliedLogicalCoreCount = -1;
        private bool isDragging = false;

        private PerformanceCounter _cpuUsageCounter;
        private bool _cpuCounterInitialized;
        private bool _cpuCounterAvailable = true;

        private readonly List<PerformanceCounter> _gpuUsageCounters = [];
        private bool _gpuCounterInitialized;
        private bool _gpuCounterAvailable = true;

        private PerformanceCounter _hddUsageCounter;
        private bool _hddCounterInitialized;
        private bool _hddCounterAvailable = true;

        //マニュアルサムネイル時の右クリックしたカラムの返却を受け取る変数
        private int manualPos = 0;

        //IME起動中的なフラグ。日本語入力中（未変換）にインクリメンタルサーチさせない為。
        private bool _imeFlag = false;

        private static readonly List<FileSystemWatcher> fileWatchers = [];

        //private bool _searchBoxItemSelectedByMouse = false;
        private bool _searchBoxItemSelectedByUser = false;

        /// <summary>
        /// 設定画面の欲望（並列数）を読み取りつつ、安全な範囲（1〜24）に制御して返すぜ！PCを燃やさないためのリミッターだ！🚥
        /// </summary>
        private static int GetThumbnailQueueMaxParallelism()
        {
            int parallelism = Properties.Settings.Default.ThumbnailParallelism;
            if (parallelism < 1)
            {
                return 1;
            }
            if (parallelism > 24)
            {
                return 24;
            }
            return parallelism;
        }

        // サムネイル並列数を設定範囲（1〜24）へ丸める。
        private static int ClampThumbnailParallelismSetting(int parallelism)
        {
            if (parallelism < 1)
            {
                return 1;
            }
            if (parallelism > 24)
            {
                return 24;
            }
            return parallelism;
        }

        // 共通設定のサムネイル並列数を更新し、進捗UIへ即時反映要求を出す。
        private void ApplyThumbnailParallelismSetting(
            int nextParallelism,
            string source,
            bool prioritizeUi = false
        )
        {
            int current = ClampThumbnailParallelismSetting(Properties.Settings.Default.ThumbnailParallelism);
            int next = ClampThumbnailParallelismSetting(nextParallelism);
            if (current == next)
            {
                return;
            }

            Properties.Settings.Default.ThumbnailParallelism = next;
            UpdateThumbnailProgressConfiguredParallelism(next);

            if (prioritizeUi)
            {
                ForceThumbnailProgressSnapshotRefreshNow();
            }
            else
            {
                RequestThumbnailProgressSnapshotRefresh();
            }

            DebugRuntimeLog.Write(
                "thumbnail",
                $"parallel setting updated: {current} -> {next} source={source}"
            );
        }

        // Ctrl + +/- でサムネイル並列数を即時変更する。
        private bool TryHandleThumbnailParallelismShortcut(KeyEventArgs e)
        {
            if (e == null)
            {
                return false;
            }

            ModifierKeys modifiers = Keyboard.Modifiers;
            if ((modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                return false;
            }
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                return false;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            int delta = key switch
            {
                Key.Add => 1,
                Key.OemPlus => 1,
                Key.Subtract => -1,
                Key.OemMinus => -1,
                _ => 0,
            };
            if (delta == 0)
            {
                return false;
            }

            int current = ClampThumbnailParallelismSetting(Properties.Settings.Default.ThumbnailParallelism);
            ApplyThumbnailParallelismSetting(current + delta, "shortcut");
            e.Handled = true;
            return true;
        }

        // Window全体でショートカットを先に処理し、各コントロールの個別キー処理へ誤爆させない。
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            _ = TryHandleThumbnailParallelismShortcut(e);
        }

        // 設定変更直後に「最大並列数」表示が先に反応するよう、Runtimeの設定値だけ先行更新する。
        private void UpdateThumbnailProgressConfiguredParallelism(int configuredParallelism)
        {
            ThumbnailProgressRuntimeSnapshot snapshot = _thumbnailProgressRuntime.CreateSnapshot();
            _thumbnailProgressRuntime.UpdateSessionProgress(
                snapshot.SessionCompletedCount,
                snapshot.SessionTotalCount,
                snapshot.CurrentParallelism,
                ClampThumbnailParallelismSetting(configuredParallelism)
            );
        }

        // ボタン操作時は最優先で進捗UIを更新し、押下反応を体感できるようにする。
        private void ForceThumbnailProgressSnapshotRefreshNow()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                if (Dispatcher.CheckAccess())
                {
                    UpdateThumbnailProgressSnapshotUi();
                    return;
                }

                _ = Dispatcher.InvokeAsync(
                    UpdateThumbnailProgressSnapshotUi,
                    DispatcherPriority.Send
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-progress",
                    $"force snapshot refresh failed: {ex.Message}"
                );
            }
        }

        // 進捗タブの「-」ボタンで並列数を1段下げる。
        private void ThumbnailParallelMinusButton_Click(object sender, RoutedEventArgs e)
        {
            int current = ClampThumbnailParallelismSetting(Properties.Settings.Default.ThumbnailParallelism);
            ApplyThumbnailParallelismSetting(
                current - 1,
                "panel-button-minus",
                prioritizeUi: true
            );
        }

        // 進捗タブの「+」ボタンで並列数を1段上げる。
        private void ThumbnailParallelPlusButton_Click(object sender, RoutedEventArgs e)
        {
            int current = ClampThumbnailParallelismSetting(Properties.Settings.Default.ThumbnailParallelism);
            ApplyThumbnailParallelismSetting(
                current + 1,
                "panel-button-plus",
                prioritizeUi: true
            );
        }

        /// <summary>
        /// 共通設定の「GPUデコード」の意思を、実行環境変数（IMM_THUMB_GPU_DECODE）にブチ込む！ここで力が解放されるぞ！💥
        /// </summary>
        private static void ApplyThumbnailGpuDecodeSetting()
        {
            // 起動時に1回だけモードを確定し、以後は同じ値を使い続ける。
            string mode = ThumbnailEnvConfig.InitializeGpuDecodeModeAtStartup(
                Properties.Settings.Default.ThumbnailGpuDecodeEnabled,
                message => DebugRuntimeLog.Write("thumbnail", message)
            );
            DebugRuntimeLog.Write("thumbnail", $"gpu decode mode applied: {mode}");
        }

        public MainWindow()
        {
            MainVM = new MainWindowViewModel(); // ← 追加
            _thumbnailQueuePersister = new ThumbnailQueuePersister(
                queueRequestChannel.Reader,
                ThumbnailQueuePersistBatchWindowMs,
                message => DebugRuntimeLog.Write("queue-db", message)
            );

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
                        // ここでは表示設定だけ先読みし、DB本体の切替はOpenDatafile成功時にだけ行う。
                        if (
                            TryValidateMainDatabaseSchema(
                                Properties.Settings.Default.LastDoc,
                                out _
                            )
                        )
                        {
                            //Tabとソートを取得するだけの為に、MovieRecordsを取得する前にやってる。
                            //初回だけはMainWindow_ContentRenderedの処理と重複するかな。
                            GetSystemTable(Properties.Settings.Default.LastDoc);
                        }
                    }
                }
            }
            recentFiles.Clear();

            InitializeComponent();

            // アセンブリのファイルバージョンを取得
            var version = Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyFileVersionAttribute>()
                ?.Version;

            this.Title = $"Indigo Movie Manager v{version}";

            ContentRendered += MainWindow_ContentRendered;
            Closing += MainWindow_Closing;
            Loaded += (_, _) => EnsureThumbnailProgressUiTimerRunning();
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            TextCompositionManager.AddPreviewTextInputHandler(SearchBox, OnPreviewTextInput);
            TextCompositionManager.AddPreviewTextInputStartHandler(
                SearchBox,
                OnPreviewTextInputStart
            );
            TextCompositionManager.AddPreviewTextInputUpdateHandler(
                SearchBox,
                OnPreviewTextInputUpdate
            );

            var rootItem = new TreeSource() { Text = RECENT_OPEN_FILE_LABEL, IsExpanded = false };
            MainVM.RecentTreeRoot.Add(rootItem);

            if (Properties.Settings.Default.RecentFiles != null)
            {
                foreach (var item in Properties.Settings.Default.RecentFiles)
                {
                    if (item == null)
                    {
                        continue;
                    }
                    if (string.IsNullOrEmpty(item.ToString()))
                    {
                        continue;
                    }
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

            TryRestoreDockLayout();

            #region Player Initialize
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            timer.Tick += new EventHandler(Timer_Tick);
            _thumbnailProgressUiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ThumbnailProgressUiIntervalMs),
            };
            _thumbnailProgressUiTimer.Tick += ThumbnailProgressUiTimer_Tick;

            //ボリュームと再生速度のスライダー初期値をセット
            uxVideoPlayer.Volume = (double)uxVolumeSlider.Value;

            uxTime.Text = "00:00:00";
            uxVolume.Text = ((int)(uxVolumeSlider.Value * 100)).ToString();
            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;
            #endregion
        }

        /// <summary>
        /// 画面の描画完了後に走る最初の儀式！ウィンドウの復元と、裏で動く常駐タスクたちを一斉に叩き起こすぜ！🌅
        /// </summary>
        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            try
            {
                DebugRuntimeLog.TaskStart(nameof(MainWindow_ContentRendered));
                // 念のため起動時に入力を有効化してから、各常駐タスクを起動する。
                SetThumbnailQueueInputEnabled(true);
                ClearTempJpg(); //一時ファイルの削除

                // 画面外へ飛んだ設定値を補正しつつロケーションとサイズを復元する。
                RestoreWindowBoundsSafely();

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
                    DebugRuntimeLog.TaskStart(nameof(CheckThumbAsync), "trigger=ContentRendered");
                    _thumbCheckTask = CheckThumbAsync(_thumbCheckCts.Token);
                }

                // QueueDB Persisterはアプリ生存中は常駐させ、Producer入力を短周期で永続化する。
                if (
                    _thumbnailQueuePersisterTask == null
                    || _thumbnailQueuePersisterTask.IsCompleted
                )
                {
                    DebugRuntimeLog.TaskStart(
                        nameof(RunThumbnailQueuePersisterSupervisorAsync),
                        "trigger=ContentRendered"
                    );
                    _thumbnailQueuePersisterTask = RunThumbnailQueuePersisterSupervisorAsync(
                        _thumbnailQueuePersisterCts.Token
                    );
                }

                // Everything連携が有効な場合は短周期ポーリングで差分同期を回す。
                if (_everythingWatchPollTask == null || _everythingWatchPollTask.IsCompleted)
                {
                    DebugRuntimeLog.TaskStart(
                        nameof(RunEverythingWatchPollLoopAsync),
                        "trigger=ContentRendered"
                    );
                    _everythingWatchPollTask = RunEverythingWatchPollLoopAsync(
                        _everythingWatchPollCts.Token
                    );
                }

                EnsureThumbnailProgressUiTimerRunning();
                UpdateThumbnailProgressMetersUi();
                UpdateThumbnailProgressSnapshotUi();
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                DebugRuntimeLog.TaskEnd(nameof(MainWindow_ContentRendered));
            }
        }

        /// <summary>
        /// アプリ終了時の大掃除！確認ダイアログから設定の保存、そしてタスク群への「止まれ！」の号令まで一手に引き受ける終末の処理だ！⏳
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (Properties.Settings.Default.ConfirmExit)
            {
                var result = MessageBox.Show(
                    this,
                    "本当に終了しますか？",
                    "終了確認",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question
                );
                if (result != MessageBoxResult.OK)
                {
                    e.Cancel = true;
                    MenuToggleButton.IsChecked = false;
                    return;
                }
            }

            try
            {
                Properties.Settings.Default.MainLocation = new System.Drawing.Point(
                    (int)Left,
                    (int)Top
                );
                Properties.Settings.Default.MainSize = new System.Drawing.Size(
                    (int)Width,
                    (int)Height
                );
                UpdateSkin();
                UpdateSort();

                Properties.Settings.Default.RecentFiles.Clear();
                Properties.Settings.Default.RecentFiles.AddRange([.. recentFiles.Reverse()]);
                Properties.Settings.Default.Save();

                XmlLayoutSerializer layoutSerializer = new(uxDockingManager);
                using var writer = new StreamWriter(DockLayoutFileName);
                layoutSerializer.Serialize(writer);

                if (!string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
                {
                    var keepHistoryData = SelectSystemTable("keepHistory");
                    int keepHistoryCount = Convert.ToInt32(
                        keepHistoryData == "" ? "30" : keepHistoryData
                    );
                    DeleteHistoryTable(MainVM.DbInfo.DBFullPath, keepHistoryCount);
                }
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                _thumbnailProgressUiTimer.Stop();
                DisposeSystemUsageCounters();

                // まず入力を止め、以降の監視イベントからの投入を遮断する。
                SetThumbnailQueueInputEnabled(false);
                queueRequestChannel.Writer.TryComplete();
                DebugRuntimeLog.Write(
                    "lifecycle",
                    "MainWindow closing: thumbnail token cancel requested."
                );
                _thumbCheckCts.Cancel();
                _thumbnailQueuePersisterCts.Cancel();
                _everythingWatchPollCts.Cancel();

                // 即終了優先を守るため、各タスク待機は最大500msで打ち切る。
                WaitBackgroundTaskForShutdown(_thumbCheckTask, "thumbnail-consumer");
                WaitBackgroundTaskForShutdown(_thumbnailQueuePersisterTask, "thumbnail-persister");
                WaitBackgroundTaskForShutdown(_everythingWatchPollTask, "everything-poll");
            }
        }

        private void TryRestoreDockLayout()
        {
            if (!Path.Exists(DockLayoutFileName))
            {
                return;
            }

            try
            {
                // 新しいツールタブを含まない古いレイアウトは互換外として退避し、XAML既定レイアウトで起動する。
                string layoutText = File.ReadAllText(DockLayoutFileName);
                if (
                    !layoutText.Contains(
                        $"ContentId=\"{ThumbnailProgressContentId}\"",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    BackupLegacyDockLayout("missing-thumbnail-progress");
                    return;
                }

                XmlLayoutSerializer layoutSerializer = new(uxDockingManager);
                using var reader = new StreamReader(DockLayoutFileName);
                layoutSerializer.Deserialize(reader);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write("layout", $"layout restore failed. reason={ex.Message}");
                BackupLegacyDockLayout("deserialize-failed");
            }
        }

        private static void BackupLegacyDockLayout(string reason)
        {
            try
            {
                string suffix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = $"layout.{reason}.{suffix}.xml";
                File.Move(DockLayoutFileName, backupPath, true);
            }
            catch
            {
                try
                {
                    File.Delete(DockLayoutFileName);
                }
                catch
                {
                    // 退避失敗時は何もしない。次回起動時も復元は試みない前提で進める。
                }
            }
        }

        private void RestoreWindowBoundsSafely()
        {
            const double minWindowWidth = 640;
            const double minWindowHeight = 480;

            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            double targetWidth = Math.Max(minWindowWidth, Properties.Settings.Default.MainSize.Width);
            double targetHeight = Math.Max(minWindowHeight, Properties.Settings.Default.MainSize.Height);
            targetWidth = Math.Min(targetWidth, Math.Max(minWindowWidth, virtualRight - virtualLeft));
            targetHeight = Math.Min(targetHeight, Math.Max(minWindowHeight, virtualBottom - virtualTop));

            double targetLeft = Properties.Settings.Default.MainLocation.X;
            double targetTop = Properties.Settings.Default.MainLocation.Y;

            bool outOfScreen =
                targetLeft + targetWidth < virtualLeft
                || targetLeft > virtualRight
                || targetTop + targetHeight < virtualTop
                || targetTop > virtualBottom;

            if (outOfScreen)
            {
                targetLeft = virtualLeft + Math.Max(0, (virtualRight - virtualLeft - targetWidth) / 2);
                targetTop = virtualTop + Math.Max(0, (virtualBottom - virtualTop - targetHeight) / 2);
            }
            else
            {
                targetLeft = Math.Min(Math.Max(targetLeft, virtualLeft), virtualRight - targetWidth);
                targetTop = Math.Min(Math.Max(targetTop, virtualTop), virtualBottom - targetHeight);
            }

            Left = targetLeft;
            Top = targetTop;
            Width = targetWidth;
            Height = targetHeight;
        }

        private void ThumbnailProgressUiTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                UpdateThumbnailProgressMetersUi();
                // DB登録待ち/DB総数はイベント更新を主軸にしつつ、
                // キュー無変化時間帯の表示古さを防ぐため低頻度フォールバックを入れる。
                _thumbnailProgressUiTickAccumulatedMs += ThumbnailProgressUiIntervalMs;
                if (_thumbnailProgressUiTickAccumulatedMs >= ThumbnailProgressSnapshotFallbackIntervalMs)
                {
                    _thumbnailProgressUiTickAccumulatedMs = 0;
                    UpdateThumbnailProgressSnapshotUi();
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write("thumbnail-progress", $"ui update failed: {ex.Message}");
            }
        }

        // 進捗の構造情報（キュー数/スレッド/パネル）を反映する。
        private void UpdateThumbnailProgressSnapshotUi()
        {
            ThumbnailProgressRuntimeSnapshot runtimeSnapshot =
                _thumbnailProgressRuntime.CreateSnapshot();
            ThumbnailProgressViewState thumbnailProgress = MainVM?.ThumbnailProgress;
            if (thumbnailProgress == null)
            {
                return;
            }

            int dbPendingCount = TemporaryPauseThumbnailProgressDbCount
                ? 0
                : MainVM?.PendingMovieRecs?.Count ?? 0;
            int dbTotalCount = TemporaryPauseThumbnailProgressDbCount
                ? 0
                : MainVM?.MovieRecs?.Count ?? 0;
            int logicalCoreCount = Environment.ProcessorCount;

            // 同一バージョンかつ表示値が同じならUI反映を省略し、負荷時の詰まりを避ける。
            if (
                runtimeSnapshot.Version == _thumbnailProgressLastAppliedSnapshotVersion
                && dbPendingCount == _thumbnailProgressLastAppliedDbPendingCount
                && dbTotalCount == _thumbnailProgressLastAppliedDbTotalCount
                && logicalCoreCount == _thumbnailProgressLastAppliedLogicalCoreCount
            )
            {
                return;
            }

            Stopwatch applyStopwatch = Stopwatch.StartNew();
            thumbnailProgress.ApplySnapshot(
                runtimeSnapshot,
                dbPendingCount,
                dbTotalCount,
                logicalCoreCount
            );
            if (TemporaryPauseThumbnailProgressDbCount)
            {
                thumbnailProgress.ApplyDbPendingPaused();
            }
            applyStopwatch.Stop();

            _thumbnailProgressLastAppliedSnapshotVersion = runtimeSnapshot.Version;
            _thumbnailProgressLastAppliedDbPendingCount = dbPendingCount;
            _thumbnailProgressLastAppliedDbTotalCount = dbTotalCount;
            _thumbnailProgressLastAppliedLogicalCoreCount = logicalCoreCount;
            _thumbnailProgressUiTickAccumulatedMs = 0;

            ThumbnailProgressUiMetricsLogger.RecordSnapshotApply(
                runtimeSnapshot.Version,
                dbPendingCount,
                dbTotalCount,
                runtimeSnapshot.ActiveWorkers.Count,
                applyStopwatch.Elapsed.TotalMilliseconds
            );
        }

        // CPU/GPU/HDDメーターはタイマーで低頻度更新する。
        private void UpdateThumbnailProgressMetersUi()
        {
            if (TemporaryPauseThumbnailProgressMeters)
            {
                MainVM?.ThumbnailProgress?.ApplyMetersPaused();
                return;
            }

            double cpuPercent = ReadSystemCpuUsagePercent();
            double? gpuPercent = ReadGpuUsagePercent();
            double? hddPercent = ReadHddUsagePercent();

            MainVM?.ThumbnailProgress?.ApplyMeters(cpuPercent, gpuPercent, hddPercent);
        }

        // 他スレッドからの進捗反映要求を1本に束ね、UIスレッドの連打を避ける。
        private void RequestThumbnailProgressSnapshotRefresh()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            _ = Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshRequested, 1);
            if (Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshQueued, 1) == 1)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(ProcessThumbnailProgressSnapshotRefreshQueue)
            );
        }

        private void ProcessThumbnailProgressSnapshotRefreshQueue()
        {
            try
            {
                if (Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshRequested, 0) == 1)
                {
                    UpdateThumbnailProgressSnapshotUi();
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-progress",
                    $"snapshot refresh failed: {ex.Message}"
                );
            }
            finally
            {
                _ = Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshQueued, 0);
                if (Interlocked.CompareExchange(ref _thumbnailProgressSnapshotRefreshRequested, 0, 0) == 1)
                {
                    RequestThumbnailProgressSnapshotRefresh();
                }
            }
        }

        // 進捗タイマーが止まっていた場合だけ再起動する。
        private void EnsureThumbnailProgressUiTimerRunning()
        {
            if (_thumbnailProgressUiTimer.IsEnabled)
            {
                return;
            }

            _thumbnailProgressUiTimer.Start();
        }

        // CPUはシステム全体使用率を取得して0〜100へクランプする。
        private double ReadSystemCpuUsagePercent()
        {
            if (!_cpuCounterAvailable)
            {
                return 0;
            }

            EnsureCpuCounter();
            if (!_cpuCounterAvailable || _cpuUsageCounter == null)
            {
                return 0;
            }

            try
            {
                return ClampMeterPercent(_cpuUsageCounter.NextValue());
            }
            catch (Exception ex)
            {
                _cpuCounterAvailable = false;
                DebugRuntimeLog.Write("thumbnail-progress", $"cpu counter read failed: {ex.Message}");
                return 0;
            }
        }

        private void EnsureCpuCounter()
        {
            if (_cpuCounterInitialized || !_cpuCounterAvailable)
            {
                return;
            }

            try
            {
                // 新しめ環境ではこちらがより実態に近い。
                _cpuUsageCounter = new PerformanceCounter(
                    "Processor Information",
                    "% Processor Utility",
                    "_Total",
                    true
                );
                _ = _cpuUsageCounter.NextValue();
            }
            catch
            {
                try
                {
                    // 互換環境向けフォールバック。
                    _cpuUsageCounter = new PerformanceCounter(
                        "Processor",
                        "% Processor Time",
                        "_Total",
                        true
                    );
                    _ = _cpuUsageCounter.NextValue();
                }
                catch (Exception ex)
                {
                    _cpuCounterAvailable = false;
                    DebugRuntimeLog.Write(
                        "thumbnail-progress",
                        $"cpu counter init failed: {ex.Message}"
                    );
                }
            }
            finally
            {
                _cpuCounterInitialized = true;
            }
        }

        private double? ReadGpuUsagePercent()
        {
            if (!_gpuCounterAvailable)
            {
                return null;
            }

            EnsureGpuCounters();
            if (!_gpuCounterAvailable || _gpuUsageCounters.Count < 1)
            {
                return null;
            }

            try
            {
                double total = 0;
                foreach (PerformanceCounter counter in _gpuUsageCounters)
                {
                    total += counter.NextValue();
                }
                return ClampMeterPercent(total);
            }
            catch (Exception ex)
            {
                _gpuCounterAvailable = false;
                DebugRuntimeLog.Write("thumbnail-progress", $"gpu counter read failed: {ex.Message}");
                return null;
            }
        }

        private void EnsureGpuCounters()
        {
            if (_gpuCounterInitialized || !_gpuCounterAvailable)
            {
                return;
            }

            try
            {
                PerformanceCounterCategory category = new("GPU Engine");
                string[] instanceNames = category.GetInstanceNames();
                var targetNames = instanceNames
                    .Where(x =>
                        x.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)
                        || x.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase)
                    )
                    .Take(32)
                    .ToArray();

                foreach (string instanceName in targetNames)
                {
                    PerformanceCounter counter = new(
                        "GPU Engine",
                        "Utilization Percentage",
                        instanceName,
                        true
                    );
                    _ = counter.NextValue(); // 初回呼び出しは測定準備用
                    _gpuUsageCounters.Add(counter);
                }

                if (_gpuUsageCounters.Count < 1)
                {
                    _gpuCounterAvailable = false;
                    DebugRuntimeLog.Write("thumbnail-progress", "gpu counter unavailable.");
                }
            }
            catch (Exception ex)
            {
                _gpuCounterAvailable = false;
                DebugRuntimeLog.Write("thumbnail-progress", $"gpu counter init failed: {ex.Message}");
            }
            finally
            {
                _gpuCounterInitialized = true;
            }
        }

        private double? ReadHddUsagePercent()
        {
            if (!_hddCounterAvailable)
            {
                return null;
            }

            EnsureHddCounter();
            if (!_hddCounterAvailable || _hddUsageCounter == null)
            {
                return null;
            }

            try
            {
                return ClampMeterPercent(_hddUsageCounter.NextValue());
            }
            catch (Exception ex)
            {
                _hddCounterAvailable = false;
                DebugRuntimeLog.Write("thumbnail-progress", $"hdd counter read failed: {ex.Message}");
                return null;
            }
        }

        private void EnsureHddCounter()
        {
            if (_hddCounterInitialized || !_hddCounterAvailable)
            {
                return;
            }

            try
            {
                _hddUsageCounter = new PerformanceCounter(
                    "PhysicalDisk",
                    "% Disk Time",
                    "_Total",
                    true
                );
                _ = _hddUsageCounter.NextValue(); // 初回呼び出しは測定準備用
            }
            catch (Exception ex)
            {
                _hddCounterAvailable = false;
                DebugRuntimeLog.Write("thumbnail-progress", $"hdd counter init failed: {ex.Message}");
            }
            finally
            {
                _hddCounterInitialized = true;
            }
        }

        private void DisposeSystemUsageCounters()
        {
            _cpuUsageCounter?.Dispose();
            _cpuUsageCounter = null;

            foreach (PerformanceCounter counter in _gpuUsageCounters)
            {
                counter.Dispose();
            }
            _gpuUsageCounters.Clear();

            _hddUsageCounter?.Dispose();
            _hddUsageCounter = null;
        }

        private static double ClampMeterPercent(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }
            if (value < 0)
            {
                return 0;
            }
            if (value > 100)
            {
                return 100;
            }
            return value;
        }

        /// <summary>
        /// Persisterが過労で吹っ飛んでも、アプリが生きている限り何度でも蘇らせる地獄の無限監視ループ！🧟‍♂️
        /// </summary>
        private async Task RunThumbnailQueuePersisterSupervisorAsync(CancellationToken cts)
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await _thumbnailQueuePersister.RunAsync(cts).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write("queue-db", $"persister restart scheduled: {ex.Message}");
                    try
                    {
                        // 連続障害時の過剰再起動を避けるため、短い待機を挟んで再試行する。
                        await Task.Delay(500, cts).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Everything連携向けの爆速短周期ポーリング！🚀
        /// ローカルの監視フォルダがある時だけ本気を出し、無い時はエコに待機する賢いヤツだ！🧠
        /// </summary>
        private async Task RunEverythingWatchPollLoopAsync(CancellationToken cts)
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    if (ShouldRunEverythingWatchPoll())
                    {
                        await QueueCheckFolderAsync(CheckMode.Watch, "EverythingPoll");
                    }

                    int delayMs = ResolveEverythingWatchPollDelayMs();
                    await Task.Delay(delayMs, cts);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"everything poll restart scheduled: {ex.Message}"
                    );
                    try
                    {
                        await Task.Delay(1000, cts);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        // サムネイルキュー負荷に応じてEverythingポーリング間隔を調整し、空振り連打を抑える。
        private int ResolveEverythingWatchPollDelayMs()
        {
            int delayMs = EverythingWatchPollIntervalMs;
            try
            {
                var queueDbService = ResolveCurrentQueueDbService();
                if (queueDbService != null)
                {
                    int activeCount = queueDbService.GetActiveQueueCount(
                        thumbnailQueueOwnerInstanceId
                    );
                    if (activeCount >= EverythingWatchPollBusyThreshold)
                    {
                        delayMs = EverythingWatchPollIntervalBusyMs;
                    }
                    else if (activeCount >= EverythingWatchPollMediumThreshold)
                    {
                        delayMs = EverythingWatchPollIntervalMediumMs;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"everything poll delay resolve failed: {ex.Message}"
                );
                delayMs = EverythingWatchPollIntervalMs;
            }

            if (delayMs != _lastEverythingPollDelayMs)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"everything poll interval changed: {_lastEverythingPollDelayMs} -> {delayMs}"
                );
                _lastEverythingPollDelayMs = delayMs;
            }
            return delayMs;
        }

        /// <summary>
        /// 今Everythingポーリングをぶん回すべきか？をクールにジャッジするぜ！😎
        /// </summary>
        private bool ShouldRunEverythingWatchPoll()
        {
            var mode = GetEverythingIntegrationMode();
            if (!_indexProviderFacade.IsIntegrationConfigured(mode))
            {
                return false;
            }

            var availability = _indexProviderFacade.CheckAvailability(mode);
            // OnモードはEverything停止中でも、filesystem fallback走査のためポーリングを止めない。
            bool keepPollingForFallback = (int)mode == 2;
            if (!availability.CanUse && !keepPollingForFallback)
            {
                return false;
            }

            string dbPath = MainVM.DbInfo.DBFullPath;
            if (string.IsNullOrWhiteSpace(dbPath) || !Path.Exists(dbPath))
            {
                return false;
            }

            try
            {
                DataTable watchTable = GetData(dbPath, "select dir from watch where watch = 1");
                if (watchTable == null)
                {
                    return false;
                }

                foreach (DataRow row in watchTable.Rows)
                {
                    string watchFolder = row["dir"]?.ToString() ?? "";
                    if (!Path.Exists(watchFolder))
                    {
                        continue;
                    }

                    if (IsEverythingEligiblePath(watchFolder, out _))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"everything poll eligibility failed: {ex.Message}"
                );
            }

            return false;
        }

        /// <summary>
        /// アプリ終了時、バックグラウンドタスクがグダグダ粘るのを許さない！最大500msで強制シャットダウンする完全処刑窓口だ！⚡
        /// </summary>
        private static void WaitBackgroundTaskForShutdown(Task task, string taskName)
        {
            if (task == null)
            {
                return;
            }
            try
            {
                Task completed = Task.WhenAny(task, Task.Delay(500)).GetAwaiter().GetResult();
                if (!ReferenceEquals(completed, task))
                {
                    DebugRuntimeLog.Write("lifecycle", $"{taskName} wait timeout: 500ms status={task.Status}");
                    return;
                }

                if (task.IsFaulted)
                {
                    string message = task.Exception?.GetBaseException()?.Message ?? "unknown";
                    DebugRuntimeLog.Write("lifecycle", $"{taskName} faulted: {message}");
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write("lifecycle", $"{taskName} wait failed: {ex.Message}");
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
            if (e.TextComposition.CompositionText.Length == 0)
            {
                _imeFlag = false;
            }
        }

        //todo : And以外の検索の実装。せめてNOT検索ぐらいまでは…
        //todo : 検索履歴の保管条件（おそらくヒット：ゼロ件超で保管）確認＆修正
        //todo : タグバー代替（保管済み検索条件）の実装
        //stack : プロパティ表示ウィンドウの作成。
        //todo : 重複チェック。本家は恐らくファイル名もチェックで使ってる模様。
        //       こっちで登録しても再度本家に登録されるケースがあったのは、ファイル名の大文字小文字が違ってたから。
        //       movie_name と Hash で重複チェックかなぁ。
        //       本家のmovie_nameは小文字変換かけてる模様。合わせてみたら再登録されなかったので恐らく正解。

        /// <summary>
        /// データベースをパカッと開き、画面表示から履歴、監視モードまですべてを今のDB色に染め上げる超重要メソッド！🎨
        /// 内部は「旧DBの完全シャットダウン」→「新DBの起動」の2フェーズ構成で安全に切り替える！🛡️
        /// </summary>
        private bool OpenDatafile(string dbFullPath)
        {
            Stopwatch sw = Stopwatch.StartNew();
            DebugRuntimeLog.TaskStart(nameof(OpenDatafile), $"db='{dbFullPath}'");
            bool isOpened = false;

            try
            {
                // 先にスキーマ検証し、NGなら現DBを維持したまま中断する。
                if (!TryValidateMainDatabaseSchema(dbFullPath, out string schemaError))
                {
                    DebugRuntimeLog.Write(
                        "db",
                        $"open canceled: schema validation failed. db='{dbFullPath}', reason='{schemaError}'"
                    );
                    MessageBox.Show(
                        this,
                        $"メインDBのスキーマ不一致を検知したため、開く処理を中止しました。\n\n{schemaError}",
                        Assembly.GetExecutingAssembly().GetName().Name,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return false;
                }

                // === Phase 1: 旧DBの完全シャットダウン ===
                ShutdownCurrentDb();

                // === Phase 2: 新DBの起動 ===
                BootNewDb(dbFullPath);
                isOpened = true;
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "db",
                    $"open failed: db='{dbFullPath}', err='{ex.GetType().Name}: {ex.Message}'"
                );
                MessageBox.Show(
                    this,
                    $"データベースを開けませんでした。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return false;
            }
            finally
            {
                sw.Stop();
                DebugRuntimeLog.TaskEnd(
                    nameof(OpenDatafile),
                    $"db='{dbFullPath}' opened={isOpened} elapsed_ms={sw.ElapsedMilliseconds}"
                );
            }
        }

        /// <summary>
        /// 旧DBに紐づくリソースを完全に後始末する！Watcher停止・キュークリア・データクリアを漏れなく実行！🧹
        /// </summary>
        private void ShutdownCurrentDb()
        {
            // タブを強制リセット（前回のタブが0だった場合の対応）
            Tabs.SelectedIndex = -1;

            // 旧FileSystemWatcherを全停止＆Dispose（イベントリーク防止！🛡️）
            StopAndClearFileWatchers();

            // サムネイルキューのデバウンス情報をリセット
            ClearThumbnailQueue();

            // 旧DBの監視フォルダデータをクリア
            watchData?.Clear();

            // Everything通知フラグをリセット（新DBで再表示させるため）
            _hasShownEverythingModeNotice = false;
            _hasShownEverythingFallbackNotice = false;
            _hasShownFolderMonitoringNotice = false;

            // 検索キーワードをリセット
            MainVM.DbInfo.SearchKeyword = "";
        }

        /// <summary>
        /// 新DBをガッツリ読み込んで、画面もWatcherも全部新しいDB色に染め上げる！🎨
        /// </summary>
        private void BootNewDb(string dbFullPath)
        {
            MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbFullPath);
            MainVM.DbInfo.DBFullPath = dbFullPath;
            GetSystemTable(dbFullPath);
            MainVM.MovieRecs.Clear();

            GetHistoryTable(dbFullPath);

            if (MainVM.DbInfo.Sort != null)
            {
                FilterAndSort(MainVM.DbInfo.Sort, true); // オープン時なので全件再描画。
            }
            if (MainVM.DbInfo.Skin != null)
            {
                SwitchTab(MainVM.DbInfo.Skin);
            }

            // bookmarkのデータ詰める。あとはブックマーク追加時とブックマーク削除時の対応はイベントで。
            GetBookmarkTable();

            DebugRuntimeLog.TaskStart(nameof(CheckFolderAsync), "mode=Auto trigger=OpenDatafile");
            _ = QueueCheckFolderAsync(CheckMode.Auto, "OpenDatafile"); // 追加ファイルがないかのチェック。
            CreateWatcher(); // 新DBの監視フォルダに対してFileSystemWatcherを作成。
        }

        /// <summary>
        /// 全FileSystemWatcherを停止＆Disposeし、リストもクリアする！旧DBのイベントリークを完全封殺！🔒
        /// </summary>
        private static void StopAndClearFileWatchers()
        {
            foreach (var w in fileWatchers)
            {
                try
                {
                    w.EnableRaisingEvents = false;
                    w.Dispose();
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write("watch", $"watcher dispose failed: {ex.GetType().Name}");
                }
            }
            fileWatchers.Clear();
        }

        /// <summary>
        /// DBのsystemテーブルから、欲しい属性の値をピンポイントで引っこ抜いてくるぜ！🎣
        /// </summary>
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

        /// <summary>
        /// bookmarkテーブルを読み込み、画面表示用のコレクションを爆速で再構築！お気に入りを蘇らせる！💖
        /// </summary>
        private void GetBookmarkTable()
        {
            bookmarkData = GetData(MainVM.DbInfo.DBFullPath, "select * from bookmark");
            if (bookmarkData != null)
            {
                MainVM.BookmarkRecs.Clear();
                var bookmarkFolder = MainVM.DbInfo.BookmarkFolder;
                var defaultBookmarkFolder = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "bookmark",
                    MainVM.DbInfo.DBName
                );
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
                        frame = Convert.ToInt64(frameS); //Scoreにフレームぶっ込む。
                    }
                    var item = new MovieRecords
                    {
                        Movie_Id = (long)row["movie_id"],
                        Movie_Name = $"{row["movie_name"]}{ext}",
                        Movie_Body = thumbBody,
                        Last_Date = ((DateTime)row["last_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                        File_Date = ((DateTime)row["file_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                        Regist_Date = ((DateTime)row["regist_date"]).ToString(
                            "yyyy-MM-dd HH:mm:ss"
                        ),
                        View_Count = (long)row["view_count"],
                        Score = frame,
                        Kana = row["kana"].ToString(),
                        Roma = row["roma"].ToString(),
                        IsExists = true, //Path.Exists(thumbFile),
                        Ext = ext,
                        ThumbDetail = thumbFile,
                    };
                    MainVM.BookmarkRecs.Add(item);
                }
            }
        }

        /// <summary>
        /// historyテーブルから過去の検索歴を引っぱり出し、重複を消し飛ばしてスマートな検索候補を作るぜ！🧠
        /// </summary>
        private void GetHistoryTable(string dbFullPath)
        {
            // 現在のテキストを一時保存
            var currentText = SearchBox.Text;

            // find_textごとに最新の1件のみ取得
            string sql =
                @"SELECT find_id, find_text, find_date
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
                    if (oldtext.Contains(row["find_text"].ToString()))
                    {
                        continue;
                    }
                    var item = new History
                    {
                        Find_Id = (long)row["find_id"],
                        Find_Text = row["find_text"].ToString(),
                        Find_Date = ((DateTime)row["find_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                    };
                    oldtext.Add(row["find_text"].ToString());
                    MainVM.HistoryRecs.Add(item);
                }
            }
            // テキストを復元
            SearchBox.Text = currentText;
        }

        /// <summary>
        /// systemテーブルに眠るスキン・ソート・フォルダ設定を呼び覚まし、アプリの見た目と挙動に魂を吹き込む！✨
        /// </summary>
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

        /// <summary>
        /// watch（監視）テーブルを、指定の条件でガッツリ読み込んでくるぜ！👁️
        /// </summary>
        private void GetWatchTable(string dbPath, string sql)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                watchData = GetData(dbPath, sql);
            }
        }

        /// <summary>
        /// 単なるソートIDを、SQLiteが震え上がる最強の ORDER BY 呪文へと変換する魔導書だ！📜
        /// </summary>
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

        /// <summary>
        /// 今のイケてるソート条件をsystemテーブルに焼き付ける！次回起動時もこの並び順だぜ！🔥
        /// </summary>
        private void UpdateSort()
        {
            if (!string.IsNullOrEmpty(MainVM.DbInfo.Sort))
            {
                UpsertSystemTable(Properties.Settings.Default.LastDoc, "sort", MainVM.DbInfo.Sort);
            }
        }

        /// <summary>
        /// 今表示してるタブ（スキン）を、過去の互換性を守りつつsystemテーブルにそっと保存する優しい処理！🥰
        /// </summary>
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

        /// <summary>
        /// 読み込んだスキン名に合わせて、表示するタブを華麗に切り替えるチェンジャー・メソッド！🔀
        /// </summary>
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

        /// <summary>
        /// 今開いてるタブの先頭アイテムにカーソルを合わせる！これが俺のスマートなエスコートだ！😎
        /// </summary>
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

        /// <summary>
        /// 画面の全リストを強制アップデート！詳細情報のDataContextもガッツリ再設定して最新の顔を見せるぜ！✨
        /// </summary>
        private void Refresh()
        {
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }
            viewExtDetail.DataContext = mv;
        }

        /// <summary>
        /// リネームイベントを検知！DB・サムネ・ブックマークの全方位に「名前変わったぞ！」と号令をかけて回る怒涛の追従処理！🏃‍♂️💨
        /// </summary>
        private async void RenameThumb(string eFullPath, string oldFullPath)
        {
            try
            {
                foreach (var item in MainVM.MovieRecs.Where(x => x.Movie_Path == oldFullPath))
                {
                    item.Movie_Path = eFullPath;
                    item.Movie_Name = Path.GetFileNameWithoutExtension(eFullPath).ToLower();

                    //DB内のデータ更新＆サムネイルのファイル名変更処理
                    UpdateMovieSingleColumn(
                        MainVM.DbInfo.DBFullPath,
                        item.Movie_Id,
                        "movie_path",
                        item.Movie_Path
                    );
                    UpdateMovieSingleColumn(
                        MainVM.DbInfo.DBFullPath,
                        item.Movie_Id,
                        "movie_name",
                        item.Movie_Name
                    );

                    //サムネイルのリネーム
                    var checkFileName = Path.GetFileNameWithoutExtension(oldFullPath);
                    var thumbFolder = MainVM.DbInfo.ThumbFolder;
                    var defaultThumbFolder = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "Thumb",
                        MainVM.DbInfo.DBName
                    );
                    thumbFolder = thumbFolder == "" ? defaultThumbFolder : thumbFolder;

                    if (Path.Exists(thumbFolder))
                    {
                        // ファイルリスト
                        var di = new DirectoryInfo(thumbFolder);
                        EnumerationOptions enumOption = new() { RecurseSubdirectories = true };
                        IEnumerable<FileInfo> ssFiles = di.EnumerateFiles(
                            $"*{checkFileName}.#{item.Hash}*.jpg",
                            enumOption
                        );
                        foreach (var thumbFile in ssFiles)
                        {
                            var oldFilePath = thumbFile.FullName;
                            var newFilePath = oldFilePath.Replace(
                                checkFileName,
                                item.Movie_Name,
                                StringComparison.CurrentCultureIgnoreCase
                            );
                            if (item.ThumbPathSmall == oldFilePath)
                            {
                                item.ThumbPathSmall = newFilePath;
                            }
                            if (item.ThumbPathBig == oldFilePath)
                            {
                                item.ThumbPathBig = newFilePath;
                            }
                            if (item.ThumbPathGrid == oldFilePath)
                            {
                                item.ThumbPathGrid = newFilePath;
                            }
                            if (item.ThumbPathList == oldFilePath)
                            {
                                item.ThumbPathList = newFilePath;
                            }
                            if (item.ThumbPathBig10 == oldFilePath)
                            {
                                item.ThumbPathBig10 = newFilePath;
                            }

                            thumbFile.MoveTo(newFilePath, true);
                        }
                    }

                    var bookmarkFolder = MainVM.DbInfo.BookmarkFolder;
                    var defaultBookmarkFolder = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "bookmark",
                        MainVM.DbInfo.DBName
                    );
                    bookmarkFolder = bookmarkFolder == "" ? defaultBookmarkFolder : bookmarkFolder;

                    if (Path.Exists(bookmarkFolder))
                    {
                        // ファイルリスト
                        var di = new DirectoryInfo(bookmarkFolder);
                        EnumerationOptions enumOption = new() { RecurseSubdirectories = true };
                        IEnumerable<FileInfo> ssFiles = di.EnumerateFiles(
                            $"*{checkFileName}*.jpg",
                            enumOption
                        );
                        foreach (var bookMarkJpg in ssFiles)
                        {
                            var dstFile = bookMarkJpg.FullName.Replace(
                                checkFileName,
                                item.Movie_Name,
                                StringComparison.CurrentCultureIgnoreCase
                            );
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
                        UpdateBookmarkRename(
                            MainVM.DbInfo.DBFullPath,
                            checkFileName,
                            item.Movie_Name
                        );
                    }
                }
                await Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        GetBookmarkTable();
                        BookmarkList.Items.Refresh();
                        FilterAndSort(MainVM.DbInfo.Sort, true);
                        Refresh();
                    })
                );
            }
            catch (Exception) { }
        }

        /// <summary>
        /// DB再取得から検索・並び替え・画面反映まで、すべてをワンボタンでフルコース提供する超最強の総合フィルターメソッド！🍔🍟🥤
        /// </summary>
        public void FilterAndSort(string id, bool IsGetNew = false)
        {
            _ = FilterAndSortAsync(id, IsGetNew);
        }

        private async Task FilterAndSortAsync(string id, bool isGetNew)
        {
            Stopwatch totalStopwatch = Stopwatch.StartNew();
            int requestRevision = Interlocked.Increment(ref _filterAndSortRequestRevision);
            DataTable latestMovieData = movieData;
            long dbLoadElapsedMs = 0;
            long sourceApplyElapsedMs = 0;
            long filterSortElapsedMs = 0;
            long refreshElapsedMs = 0;

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"filter start: revision={requestRevision} sort={id} is_get_new={isGetNew} keyword='{MainVM.DbInfo.SearchKeyword}'"
            );

            if (latestMovieData == null || isGetNew)
            {
                Stopwatch dbLoadStopwatch = Stopwatch.StartNew();
                string dbFullPath = MainVM.DbInfo.DBFullPath;
                string sql = $"SELECT * FROM movie order by {GetSortWordForSQL(id)}";
                latestMovieData = await Task.Run(() => GetData(dbFullPath, sql));
                dbLoadStopwatch.Stop();
                dbLoadElapsedMs = dbLoadStopwatch.ElapsedMilliseconds;
                if (latestMovieData == null)
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"filter abort: revision={requestRevision} reason=db_reload_returned_null elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                    );
                    return;
                }
                if (requestRevision != _filterAndSortRequestRevision)
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"filter skip stale reload: revision={requestRevision} current_revision={_filterAndSortRequestRevision} db_reload_ms={dbLoadElapsedMs}"
                    );
                    return;
                }
                movieData = latestMovieData;
                Stopwatch sourceApplyStopwatch = Stopwatch.StartNew();
                await SetRecordsToSource(latestMovieData);
                sourceApplyStopwatch.Stop();
                sourceApplyElapsedMs = sourceApplyStopwatch.ElapsedMilliseconds;
            }

            if (requestRevision != _filterAndSortRequestRevision)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"filter skip stale apply: revision={requestRevision} current_revision={_filterAndSortRequestRevision} elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                );
                return;
            }

            Stopwatch filterSortStopwatch = Stopwatch.StartNew();
            var filtered = MainVM
                .FilterMovies(MainVM.MovieRecs, MainVM.DbInfo.SearchKeyword)
                .ToArray();
            MainVM.DbInfo.SearchCount = string.IsNullOrWhiteSpace(MainVM.DbInfo.SearchKeyword)
                ? MainVM.MovieRecs.Count
                : filtered.Length;

            var sorted = MainVM.SortMovies(filtered, id).ToArray();
            filterList = sorted;
            MainVM.ReplaceFilteredMovieRecs(sorted);
            filterSortStopwatch.Stop();
            filterSortElapsedMs = filterSortStopwatch.ElapsedMilliseconds;

            if (MainVM.DbInfo.SearchCount == 0)
            {
                viewExtDetail.Visibility = Visibility.Collapsed;
            }
            else
            {
                viewExtDetail.Visibility = Visibility.Visible;
            }

            Stopwatch refreshStopwatch = Stopwatch.StartNew();
            Refresh();
            refreshStopwatch.Stop();
            refreshElapsedMs = refreshStopwatch.ElapsedMilliseconds;

            totalStopwatch.Stop();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"filter end: revision={requestRevision} sort={id} is_get_new={isGetNew} count={MainVM.DbInfo.SearchCount} db_reload_ms={dbLoadElapsedMs} source_apply_ms={sourceApplyElapsedMs} filter_sort_ms={filterSortElapsedMs} refresh_ms={refreshElapsedMs} total_ms={totalStopwatch.ElapsedMilliseconds}"
            );
        }

        /// <summary>
        /// 絞り込み済みのリスト(filterList)に対して、指定されたソートの魔法だけをサクッとかけるぜ！🪄
        /// </summary>
        private void SetSortData(string id)
        {
            //ベタ書きの方が分かりやすいっちゃぁ分かりやすいよなぁ。ほんのちょっと早い。
            var query = filterList; // from x in filterList select x;
            switch (id)
            {
                case "0":
                    query = from x in filterList orderby x.Last_Date descending select x;
                    break;
                case "1":
                    query = from x in filterList orderby x.Last_Date select x;
                    break;
                case "2":
                    query = from x in filterList orderby x.File_Date descending select x;
                    break;
                case "3":
                    query = from x in filterList orderby x.File_Date select x;
                    break;
                case "6":
                    query = from x in filterList orderby x.Score descending select x;
                    break;
                case "7":
                    query = from x in filterList orderby x.Score select x;
                    break;
                case "8":
                    query = from x in filterList orderby x.View_Count descending select x;
                    break;
                case "9":
                    query = from x in filterList orderby x.View_Count select x;
                    break;
                case "10":
                    query = from x in filterList orderby x.Kana select x;
                    break;
                case "11":
                    query = from x in filterList orderby x.Kana descending select x;
                    break;
                case "12":
                    query = from x in filterList orderby x.Movie_Name select x;
                    break;
                case "13":
                    query = from x in filterList orderby x.Movie_Name descending select x;
                    break;
                case "14":
                    query = from x in filterList orderby x.Movie_Path select x;
                    break;
                case "15":
                    query = from x in filterList orderby x.Movie_Path descending select x;
                    break;
                case "16":
                    query = from x in filterList orderby x.Movie_Size descending select x;
                    break;
                case "17":
                    query = from x in filterList orderby x.Movie_Size select x;
                    break;
                case "18":
                    query = from x in filterList orderby x.Regist_Date descending select x;
                    break;
                case "19":
                    query = from x in filterList orderby x.Regist_Date select x;
                    break;
                case "20":
                    query = from x in filterList orderby x.Movie_Length descending select x;
                    break;
                case "21":
                    query = from x in filterList orderby x.Movie_Length select x;
                    break;
                case "22":
                    query = from x in filterList orderby x.Comment1 select x;
                    break;
                case "23":
                    query = from x in filterList orderby x.Comment1 descending select x;
                    break;
                case "24":
                    query = from x in filterList orderby x.Comment2 select x;
                    break;
                case "25":
                    query = from x in filterList orderby x.Comment2 descending select x;
                    break;
                case "26":
                    query = from x in filterList orderby x.Comment3 select x;
                    break;
                case "27":
                    query = from x in filterList orderby x.Comment3 descending select x;
                    break;
            }
            filterList = query;
        }

        /// <summary>
        /// 今表示中の一覧だけを並べ直し、XAML バインディングを壊さず中身だけ更新する。
        /// </summary>
        private void SortData(string id)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                var sorted = MainVM.SortMovies(MainVM.FilteredMovieRecs, id).ToArray();
                filterList = sorted;
                MainVM.ReplaceFilteredMovieRecs(sorted);
                MainVM.DbInfo.SearchCount = sorted.Length;
                Refresh();
                sw.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"sort end: sort={id} count={sorted.Length} total_ms={sw.ElapsedMilliseconds}"
                );
            }
            catch (Exception err)
            {
                MessageBox.Show(
                    err.Message,
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                throw;
            }
            return;
        }

        /// <summary>
        /// DBから拾った無骨なレコード1件を、キラキラな表示用（MovieRecords）に変換してコレクションの中に迎え入れるぜ！ようこそ！🎉
        /// </summary>
        private void DataRowToViewData(DataRow row)
        {
            string[] thumbErrorPath =
            [
                @"errorSmall.jpg",
                @"errorBig.jpg",
                @"errorGrid.jpg",
                @"errorList.jpg",
                @"errorBig.jpg",
            ];
            string[] thumbPath = new string[Tabs.Items.Count];
            var Hash = row["hash"].ToString();
            var movieFullPath = row["movie_path"].ToString();
            var movieName = row["movie_name"].ToString();

            for (int i = 0; i < Tabs.Items.Count; i++)
            {
                TabInfo tbi = new(i, MainVM.DbInfo.DBName, MainVM.DbInfo.ThumbFolder);

                // 生成側と同じ規則でまず探索し、旧命名が残っている環境はフォールバックで拾う。
                var tempPath = ThumbnailPathResolver.BuildThumbnailPath(tbi, movieFullPath, Hash);
                if (!Path.Exists(tempPath) && !string.IsNullOrWhiteSpace(movieName))
                {
                    tempPath = ThumbnailPathResolver.BuildThumbnailPath(tbi, movieName, Hash);
                }
                if (Path.Exists(tempPath))
                {
                    thumbPath[i] = tempPath;
                }
                else
                {
                    thumbPath[i] = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "Images",
                        thumbErrorPath[i]
                    );
                }
            }

            //エクステンションの詳細用サムネ特別処理
            //(5つ目のタブ扱いにする手もあるけど、そうするとタブ増やすときに面倒かなと)
            //だもんでCase 99の所に入れておいた。で、ブックマークの場合のフルパスもここを使う。
            //オブジェクトは、MovieとBookmarkと違うので問題ねぇはず。
            TabInfo tbiExtensionDetail = new(99, MainVM.DbInfo.DBName, MainVM.DbInfo.ThumbFolder);
            var tempPathExtensionDetail = ThumbnailPathResolver.BuildThumbnailPath(
                tbiExtensionDetail,
                movieFullPath,
                Hash
            );
            if (!Path.Exists(tempPathExtensionDetail) && !string.IsNullOrWhiteSpace(movieName))
            {
                tempPathExtensionDetail = ThumbnailPathResolver.BuildThumbnailPath(
                    tbiExtensionDetail,
                    movieName,
                    Hash
                );
            }
            string thumbPathDetail;
            if (Path.Exists(tempPathExtensionDetail))
            {
                thumbPathDetail = tempPathExtensionDetail;
            }
            else
            {
                //エラー時のサムネはGridと同じタイプを流用
                thumbPathDetail = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Images",
                    thumbErrorPath[2]
                );
            }

            var tags = row["tag"].ToString();
            List<string> tagArray = [];
            if (!string.IsNullOrEmpty(tags))
            {
                var splitTags = tags.Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries
                );
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
                Movie_Length = new TimeSpan(0, 0, (int)(long)row["movie_length"]).ToString(
                    @"hh\:mm\:ss"
                ),
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
                Ext = ext,
            };
            #endregion
            MainVM.MovieRecs.Add(item);
        }

        /// <summary>
        /// 取得済みの生データ（movieData）から、表示用のコレクションへ怒涛の勢いで全員放り込んでいく一斉投入メソッド！🔥
        /// </summary>
        private Task SetRecordsToSource(DataTable sourceData = null)
        {
            var targetData = sourceData ?? movieData;
            if (targetData != null)
            {
                MainVM.MovieRecs.Clear();

                var list = targetData.AsEnumerable().ToArray();
                foreach (var row in list)
                {
                    DataRowToViewData(row);
                }
            }
            return Task.CompletedTask;
        }

        // タブ切替時に不足サムネイルを検出し、必要な再作成キューを積む。
        private async void Tabs_SelectionChangedAsync(object sender, SelectionChangedEventArgs e)
        {
            if (sender as TabControl != null && e.OriginalSource is TabControl)
            {
                Stopwatch selectionStopwatch = Stopwatch.StartNew();
                ClearThumbnailQueue();

                var tabControl = sender as TabControl;
                int index = tabControl.SelectedIndex;
                if (index == -1)
                    return;

                MainVM.DbInfo.CurrentTabIndex = index;

                if (MainVM.FilteredMovieRecs.Count == 0)
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"tab change skip: tab={index} reason=no_filtered_items total_ms={selectionStopwatch.ElapsedMilliseconds}"
                    );
                    return;
                }

                string[] thumbProps =
                [
                    nameof(MovieRecords.ThumbPathSmall),
                    nameof(MovieRecords.ThumbPathBig),
                    nameof(MovieRecords.ThumbPathGrid),
                    nameof(MovieRecords.ThumbPathList),
                    nameof(MovieRecords.ThumbPathBig10),
                ];

                int queuedErrorCount = 0;
                if (index >= 0 && index < thumbProps.Length)
                {
                    var thumbProp = typeof(MovieRecords).GetProperty(thumbProps[index]);
                    var query = MainVM.FilteredMovieRecs
                        .Where(x =>
                            thumbProp
                                ?.GetValue(x)
                                ?.ToString()
                                ?.Contains("error", StringComparison.CurrentCultureIgnoreCase)
                            == true
                        )
                        .ToArray();

                    SelectFirstItem();

                    if (query.Length > 0)
                    {
                        queuedErrorCount = query.Length;
                        _ = EnqueueTabThumbnailErrorsAsync(index, query);
                    }
                }

                MovieRecords mv = GetSelectedItemByTabIndex();
                if (mv == null)
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"tab change end: tab={index} selected=none queued_error={queuedErrorCount} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                    );
                    return;
                }
                if (mv.ThumbDetail.Contains("error", StringComparison.CurrentCultureIgnoreCase))
                {
                    QueueObj tempObj = new()
                    {
                        MovieId = mv.Movie_Id,
                        MovieFullPath = mv.Movie_Path,
                        Hash = mv.Hash,
                        Tabindex = 99,
                    };
                    _ = TryEnqueueThumbnailJob(tempObj);
                }

                viewExtDetail.DataContext = mv;
                viewExtDetail.Visibility = Visibility.Visible;
                selectionStopwatch.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change end: tab={index} selected='{mv.Movie_Name}' queued_error={queuedErrorCount} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
            }
        }

        // タブ切替直後の体感を優先し、不足サムネ再投入は少し遅らせて裏で流す。
        private async Task EnqueueTabThumbnailErrorsAsync(int tabIndex, MovieRecords[] query)
        {
            if (query == null || query.Length == 0)
            {
                return;
            }

            await Task.Delay(1000);

            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(() => { });
            }

            if (Tabs.SelectedIndex != tabIndex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab enqueue skip: tab={tabIndex} reason=tab_changed queued_error={query.Length}"
                );
                return;
            }

            int queuedCount = 0;
            foreach (var item in query)
            {
                queuedCount++;
                QueueObj tempObj = new()
                {
                    MovieId = item.Movie_Id,
                    MovieFullPath = item.Movie_Path,
                    Hash = item.Hash,
                    Tabindex = tabIndex,
                };
                _ = TryEnqueueThumbnailJob(tempObj);
            }

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"tab enqueue end: tab={tabIndex} queued_error={queuedCount}"
            );
        }

        // クリック位置から対象サムネイルの秒位置を計算して返す。
        private int GetPlayPosition(int tabIndex, MovieRecords mv, ref int returnPos)
        {
            int msec = 0;

            string currentThumbPath;
            switch (tabIndex)
            {
                case 0:
                    currentThumbPath = mv.ThumbPathSmall;
                    break;
                case 1:
                    currentThumbPath = mv.ThumbPathBig;
                    break;
                case 2:
                    currentThumbPath = mv.ThumbPathGrid;
                    break;
                case 3:
                    currentThumbPath = mv.ThumbPathList;
                    break;
                case 4:
                    currentThumbPath = mv.ThumbPathBig10;
                    break;
                default:
                    return 0;
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
                                Y = j * thumbInfo.ThumbHeight,
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
            if (Tabs.SelectedIndex == -1)
            {
                return;
            }
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter: //再生
                    PlayMovie_Click(sender, e);
                    break;
                case Key.F6: //タグ編集
                    TagEdit_Click(sender, e);
                    break;
                case Key.C: //タグのコピー
                    TagCopy_Click(sender, e);
                    break;
                case Key.V: //タグの貼り付け
                    TagPaste_Click(sender, e);
                    break;
                case Key.Add: //スコアプラス
                case Key.Subtract: //スコアマイナス
                    MenuScore_Click(sender, e);
                    break;
                case Key.Delete: //登録の削除
                    // 共通設定に合わせてDelキーの挙動を切り替える。
                    // 0: 登録解除（従来）、1: 登録解除＋動画をゴミ箱へ移動。
                    int deleteKeyActionMode = Properties.Settings.Default.DeleteKeyActionMode;
                    if (deleteKeyActionMode == 1)
                    {
                        DeleteMovieRecord_Click(new MenuItem { Name = "DeleteWithRecycle" }, e);
                    }
                    else
                    {
                        DeleteMovieRecord_Click(sender, e);
                    }
                    break;
                case Key.F2: //名前の変更
                    RenameFile_Click(sender, e);
                    break;
                case Key.F12: //親フォルダ
                    OpenParentFolder_Click(sender, e);
                    break;
                case Key.P: //プロパティ
                    break;
                default:
                    return;
            }
        }

        // ソートコンボ変更時に並び替えと先頭選択を実行する。
        private void ComboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }
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
                case 0:
                    mv = SmallList.SelectedItem as MovieRecords;
                    break;
                case 1:
                    mv = BigList.SelectedItem as MovieRecords;
                    break;
                case 2:
                    mv = GridList.SelectedItem as MovieRecords;
                    break;
                case 3:
                    mv = ListDataGrid.SelectedItem as MovieRecords;
                    break;
                case 4:
                    mv = BigList10.SelectedItem as MovieRecords;
                    break;

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
                    foreach (MovieRecords item in SmallList.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case 1:
                    foreach (MovieRecords item in BigList.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case 2:
                    foreach (MovieRecords item in GridList.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case 3:
                    foreach (MovieRecords item in ListDataGrid.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                case 4:
                    foreach (MovieRecords item in BigList10.SelectedItems)
                    {
                        mv.Add(item);
                    }
                    break;
                default:
                    return null;
            }
            return mv;
        }
    }
}
