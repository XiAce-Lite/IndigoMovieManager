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
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using IndigoMovieManager.DB;
using IndigoMovieManager.ModelViews;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueuePipeline;
using IndigoMovieManager.UpperTabs.Common;
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
        /// <summary>
        /// QueueDBに怒涛の勢いで書き込むためのバッチ窓口（100〜300ms）！ここでまとめてドカンと流す！🔥
        /// </summary>
        private const int ThumbnailQueuePersistBatchWindowMs = 150;
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
        //マニュアルサムネイル時の右クリックしたカラムの返却を受け取る変数
        private int manualPos = 0;

        //IME起動中的なフラグ。日本語入力中（未変換）にインクリメンタルサーチさせない為。
        private bool _imeFlag = false;

        private static readonly List<FileSystemWatcher> fileWatchers = [];

        //private bool _searchBoxItemSelectedByMouse = false;
        private bool _searchBoxItemSelectedByUser = false;

        public MainWindow()
        {
            MainVM = new MainWindowViewModel(); // ← 追加
            _thumbnailQueuePersister = new ThumbnailQueuePersister(
                queueRequestChannel.Reader,
                ThumbnailQueuePersistBatchWindowMs,
                message => DebugRuntimeLog.Write("queue-db", message),
                request =>
                    IsQueueRequestAcceptedForSession(
                        request,
                        ReadCurrentMainDbQueueRequestSessionStamp()
                    )
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
            Loaded += (_, _) =>
            {
                EnsureThumbnailProgressUiTimerRunning();
                SyncThumbnailProgressSettingControls();
            };
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
            InitializeExtensionTabSupport();
            InitializeBookmarkTabSupport();
            InitializeSavedSearchTabSupport();
            InitializeDebugTabSupport();
            ApplyDebugTabVisibility();
            InitializeThumbnailErrorUiSupport();
            InitializeThumbnailProgressUiSupport();
            InitializeUpperTabViewportSupport();

            #region Player Initialize
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
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
                                _ = TrySwitchMainDb(
                                    Properties.Settings.Default.LastDoc,
                                    MainDbSwitchSource.StartupAutoOpen
                                );
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
                UpdateThumbnailProgressSnapshotUi();
                TryStartInitialThumbnailFailureSync();
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
                // 閉じ際に動画再生とUIタイマーを先に止め、追加のハンドル消費を抑える。
                uxVideoPlayer.Stop();
                timer.Stop();
                _thumbnailProgressUiTimer.Stop();
                _debugTabRefreshTimer?.Stop();
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

                if (
                    !layoutText.Contains(
                        $"ContentId=\"{ThumbnailErrorBottomTabContentId}\"",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    BackupLegacyDockLayout("missing-thumbnail-error-bottom-tab");
                    return;
                }

                // Debug 構成では開発用タブも必須扱いにして、古いレイアウトを引きずらない。
                if (
                    ShouldShowDebugTab
                    && !layoutText.Contains(
                        $"ContentId=\"{DebugToolContentId}\"",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    BackupLegacyDockLayout("missing-debug-tool");
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

        // 複数 worker を持つ系は個別に短時間待機し、終了処理を引き延ばさない。
        private static void WaitBackgroundTasksForShutdown(IEnumerable<Task> tasks, string taskName)
        {
            if (tasks == null)
            {
                return;
            }

            int index = 0;
            foreach (Task task in tasks)
            {
                index++;
                WaitBackgroundTaskForShutdown(task, $"{taskName}[{index}]");
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

            // Bookmarkタブのデータ再構築は専用窓口へ寄せる。
            ReloadBookmarkTabData();

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

                string dbName = string.IsNullOrWhiteSpace(MainVM.DbInfo.DBName)
                    ? Path.GetFileNameWithoutExtension(dbPath) ?? ""
                    : MainVM.DbInfo.DBName;
                string configuredThumbFolder = SelectSystemTable("thum");
                MainVM.DbInfo.ThumbFolder = Thumbnail.TabInfo.ResolveRuntimeThumbRoot(
                    dbPath,
                    dbName,
                    configuredThumbFolder
                );

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
            UpdateSort(MainVM?.DbInfo?.DBFullPath ?? "");
        }

        private void UpdateSort(string dbFullPath)
        {
            if (
                !string.IsNullOrWhiteSpace(dbFullPath)
                && !string.IsNullOrEmpty(MainVM.DbInfo.Sort)
            )
            {
                UpsertSystemTable(dbFullPath, "sort", MainVM.DbInfo.Sort);
            }
        }

        /// <summary>
        /// 今表示してるタブ（スキン）を、過去の互換性を守りつつsystemテーブルにそっと保存する優しい処理！🥰
        /// </summary>
        private void UpdateSkin()
        {
            UpdateSkin(MainVM?.DbInfo?.DBFullPath ?? "");
        }

        private void UpdateSkin(string dbFullPath)
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
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            UpsertSystemTable(dbFullPath, "skin", tabName);
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
                    string thumbFolder = Thumbnail.TabInfo.ResolveRuntimeThumbRoot(
                        MainVM.DbInfo.DBFullPath,
                        MainVM.DbInfo.DBName,
                        MainVM.DbInfo.ThumbFolder
                    );

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

                    string bookmarkFolder = ResolveBookmarkFolderPath();

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
                        ReloadBookmarkTabData();
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
                RefreshThumbnailErrorRecords();
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
            FilteredMovieRecsUpdateResult applyResult = MainVM.ReplaceFilteredMovieRecs(
                sorted,
                updateMode: UpperTabCollectionUpdatePolicy.ResolveUpdateMode(
                    Tabs?.SelectedIndex,
                    isSortOnly: false
                )
            );
            filterSortStopwatch.Stop();
            filterSortElapsedMs = filterSortStopwatch.ElapsedMilliseconds;

            UpdateExtensionDetailVisibilityBySearchCount();

            Stopwatch refreshStopwatch = Stopwatch.StartNew();
            if (applyResult.HasChanges)
            {
                Refresh();
            }
            refreshStopwatch.Stop();
            refreshElapsedMs = refreshStopwatch.ElapsedMilliseconds;
            if (applyResult.HasChanges)
            {
                RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "filter");
            }

            totalStopwatch.Stop();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"filter end: revision={requestRevision} sort={id} is_get_new={isGetNew} count={MainVM.DbInfo.SearchCount} changed={applyResult.HasChanges} prefix={applyResult.RetainedPrefixCount} suffix={applyResult.RetainedSuffixCount} removed={applyResult.RemovedCount} inserted={applyResult.InsertedCount} moved={applyResult.MovedCount} db_reload_ms={dbLoadElapsedMs} source_apply_ms={sourceApplyElapsedMs} filter_sort_ms={filterSortElapsedMs} refresh_ms={refreshElapsedMs} total_ms={totalStopwatch.ElapsedMilliseconds}"
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
                FilteredMovieRecsUpdateResult applyResult = MainVM.ReplaceFilteredMovieRecs(
                    sorted,
                    updateMode: UpperTabCollectionUpdatePolicy.ResolveUpdateMode(
                        Tabs?.SelectedIndex,
                        isSortOnly: true
                    )
                );
                MainVM.DbInfo.SearchCount = sorted.Length;
                if (applyResult.HasChanges)
                {
                    Refresh();
                    RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "sort");
                }
                sw.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"sort end: sort={id} tab={Tabs?.SelectedIndex} changed={applyResult.HasChanges} prefix={applyResult.RetainedPrefixCount} suffix={applyResult.RetainedSuffixCount} removed={applyResult.RemovedCount} inserted={applyResult.InsertedCount} moved={applyResult.MovedCount} update_mode={UpperTabCollectionUpdatePolicy.ResolveUpdateMode(Tabs?.SelectedIndex, true)} count={sorted.Length} total_ms={sw.ElapsedMilliseconds}"
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
            string[] thumbPath = new string[thumbErrorPath.Length];
            var Hash = row["hash"].ToString();
            var movieFullPath = row["movie_path"].ToString();
            var movieName = row["movie_name"].ToString();

            for (int i = 0; i < thumbErrorPath.Length; i++)
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

    }
}
