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
using IndigoMovieManager.Data;
using IndigoMovieManager.DB;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.ViewModels;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueuePipeline;
using IndigoMovieManager.UpperTabs.Common;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    /// <summary>
    /// アプリ全体の「司令塔」となるメインウィンドウの View 層。
    ///
    /// 【全体の流れでの位置づけ】
    ///   App.xaml → ★ここ★ MainWindow
    ///     → コンストラクタで常駐タスク（サムネキュー/Persister/Everythingポーリング）を配線
    ///     → ContentRendered でDB自動復元・常駐タスク開始
    ///     → Closing で全タスク停止・設定永続化・リソース解放
    ///
    /// partial class として Thumbnail・Queue・RescueLane・FailureSync・Paths 等の
    /// 責務別ファイルに分割されており、このファイルはライフサイクル管理とDB操作を担当する。
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
        private const string DefaultDockLayoutFileName = "layout.default.xml";
        private const string ThumbnailProgressContentId = "ToolThumbnailProgress";
        private const string TagEditorBottomTabContentId = "ToolTagEditor";
        /// <summary>
        /// QueueDBに怒涛の勢いで書き込むためのバッチ窓口（100〜300ms）！ここでまとめてドカンと流す！🔥
        /// </summary>
        private const int ThumbnailQueuePersistBatchWindowMs = 150;
        private Stack<string> recentFiles = new();

        private IEnumerable<MovieRecords> filterList = [];
        private int _filterAndSortRequestRevision;
        private int _movieExistsRefreshRevision;
        private int _registeredMovieCountRevision;
        private bool _registeredMovieCountInitialized;

        /// <summary>
        /// メインヘッダーの検索件数・登録総数を一括リセットし、前DBの残像表示を防ぐ。
        /// DB切替（ShutdownCurrentDb）時に呼ばれる初期化メソッド。
        /// </summary>
        private void ResetMainHeaderCounts()
        {
            MainVM.DbInfo.SearchCount = 0;
            MainVM.DbInfo.RegisteredMovieCount = 0;
            _registeredMovieCountInitialized = false;
            Interlocked.Increment(ref _registeredMovieCountRevision);
        }

        /// <summary>
        /// 登録動画の総件数をバックグラウンドで取得し、完了後にUIへ反映する。
        /// first-page 表示を止めずに正確値を後追いで確定する。
        /// </summary>
        private void QueueRegisteredMovieCountRefresh(string dbFullPath)
        {
            string targetDbPath = dbFullPath ?? "";
            int revision = Interlocked.Increment(ref _registeredMovieCountRevision);
            _registeredMovieCountInitialized = false;

            if (string.IsNullOrWhiteSpace(targetDbPath))
            {
                MainVM.DbInfo.RegisteredMovieCount = 0;
                return;
            }

            _ = RefreshRegisteredMovieCountAsync(targetDbPath, revision);
        }

        // 初回の正確値取得後は差分だけ加減算し、未確定なら正確値再取得へ逃がす。
        private void TryAdjustRegisteredMovieCount(string dbFullPath, int delta)
        {
            if (delta == 0)
            {
                return;
            }

            void apply()
            {
                if (
                    !string.Equals(
                        MainVM?.DbInfo?.DBFullPath,
                        dbFullPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return;
                }

                if (!_registeredMovieCountInitialized)
                {
                    QueueRegisteredMovieCountRefresh(dbFullPath);
                    return;
                }

                if (MainVM?.DbInfo == null)
                {
                    return;
                }

                MainVM.DbInfo.RegisteredMovieCount = Math.Max(0, MainVM.DbInfo.RegisteredMovieCount + delta);
                Interlocked.Increment(ref _registeredMovieCountRevision);
            }

            if (Dispatcher == null)
            {
                apply();
                return;
            }

            _ = Dispatcher.InvokeAsync(apply, DispatcherPriority.Background);
        }

        // バックグラウンドで数えた結果は、現在選択中のDBに対する最新値だけ反映する。
        private async Task RefreshRegisteredMovieCountAsync(string dbFullPath, int revision)
        {
            try
            {
                int registeredMovieCount = await Task.Run(
                    () => _mainDbMovieReadFacade.ReadRegisteredMovieCount(dbFullPath)
                );
                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (revision != Volatile.Read(ref _registeredMovieCountRevision))
                        {
                            return;
                        }

                        if (
                            !string.Equals(
                                MainVM?.DbInfo?.DBFullPath,
                                dbFullPath,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            return;
                        }

                        MainVM.DbInfo.RegisteredMovieCount = registeredMovieCount;
                        _registeredMovieCountInitialized = true;
                    },
                    DispatcherPriority.Background
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"registered count refresh failed: db='{dbFullPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

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

        private readonly IThumbnailCreationService _thumbnailCreationService =
            AppThumbnailCreationServiceFactory.Create(AppLocalDataPaths.LogsPath);
        private readonly ThumbnailQueueProcessor _thumbnailQueueProcessor = new();
        private readonly ThumbnailQueuePersister _thumbnailQueuePersister;
        private readonly IMainDbMovieReadFacade _mainDbMovieReadFacade =
            new MainDbMovieReadFacade();

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
        // 実起動 UI 統合テストでは、設定保存などの永続化だけを避けて window 局所の後始末は通す。
        internal bool SkipMainWindowClosingSideEffectsForTesting { get; set; }
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
            InitializeDetailThumbnailModeRuntime();
            ApplyThumbnailGpuDecodeSetting();
            ApplyThumbnailFfmpegEcoSetting();

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
            _uiHangActivityTracker = new UiHangActivityTracker();
            _uiHangNotificationCoordinator = new UiHangNotificationCoordinator(
                Dispatcher,
                _uiHangActivityTracker,
                IsUiHangDangerState,
                ShouldDisplayUiHangNotification
            );
            InitializeUpperTabDisplayOrder();
            InitializeUpperTabRescueTab();
            InitializeUpperTabDuplicateVideosTab();
            // 起動直後の一時Small選択が残らないよう、まずは未選択へ戻しておく。
            Tabs.SelectedIndex = -1;
            MainVM.DbInfo.CurrentTabIndex = -1;
            SourceInitialized += (_, _) => App.ApplyWindowTitleBarTheme(this);
            SourceInitialized += (_, _) =>
            {
                UpdateUiHangWindowStateSnapshot();
                UpdateUiHangNotificationPlacement();
            };
            LocationChanged += (_, _) => UpdateUiHangNotificationPlacement();
            SizeChanged += (_, _) => UpdateUiHangNotificationPlacement();
            StateChanged += (_, _) =>
            {
                UpdateUiHangWindowStateSnapshot();
                UpdateUiHangNotificationPlacement();
                UpdateUiHangNotificationVisibilityPolicy();
            };
            Activated += (_, _) =>
            {
                UpdateUiHangWindowStateSnapshot();
                UpdateUiHangNotificationVisibilityPolicy();
            };
            Deactivated += (_, _) =>
            {
                UpdateUiHangWindowStateSnapshot();
                UpdateUiHangNotificationVisibilityPolicy();
            };

            // アセンブリのファイルバージョンを取得
            var version = Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyFileVersionAttribute>()
                ?.Version;

            this.Title = $"Indigo Movie Manager v{version}";

            ContentRendered += MainWindow_ContentRendered;
            ContentRendered += (_, _) => UpdateUiHangNotificationPlacement();
            Closing += MainWindow_Closing;
            Loaded += (_, _) =>
            {
                StartUiHangNotificationSupport();
                EnsureThumbnailProgressUiTimerRunning();
                SyncThumbnailProgressSettingControls();
                PrimeThumbnailProgressWorkerPanels();
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
            InitializeTagEditorTabSupport();
            InitializeDebugTabSupport();
            InitializeLogTabSupport();
            ApplyDebugTabVisibility();
            ApplyLogTabVisibility();
            ApplyThumbnailErrorBottomTabVisibility();
            InitializeThumbnailErrorUiSupport();
            InitializeThumbnailProgressUiSupport();
            InitializeUpperTabViewportSupport();
            InitializeWebViewSkinIntegration();

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

        // 左ドロワー表示中だけ、watch の新規流入を抑えて操作テンポを守る。
        private void MenuToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            BeginWatchUiSuppression("left-drawer");
        }

        // 左ドロワーを閉じた時だけ、保留があれば watch を1回 catch-up させる。
        private void MenuToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            EndWatchUiSuppression("left-drawer");
        }

        /// <summary>
        /// 画面の描画完了後に走る最初の儀式！ウィンドウの復元と、裏で動く常駐タスクたちを一斉に叩き起こすぜ！🌅
        /// </summary>
        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            try
            {
                DebugRuntimeLog.TaskStart(nameof(MainWindow_ContentRendered));
                LogStartupWindowShownOnce();
                // 念のため起動時に入力を有効化してから、各常駐タスクを起動する。
                SetThumbnailQueueInputEnabled(true);
                ThumbnailTempFileCleaner.ClearCurrentWorkingTempJpg(); //一時ファイルの削除

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
                                // 起動直後の初回描画を先に通し、その後でDB切替を流す。
                                _ = Dispatcher.BeginInvoke(
                                    DispatcherPriority.Background,
                                    new Action(() =>
                                        TrySwitchMainDb(
                                            Properties.Settings.Default.LastDoc,
                                            MainDbSwitchSource.StartupAutoOpen
                                        )
                                    )
                                );
                            }
                        }
                    }
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

            bool skipProcessWideShutdownSideEffects = SkipMainWindowClosingSideEffectsForTesting;

            try
            {
                if (!skipProcessWideShutdownSideEffects)
                {
                    ShowUiHangShutdownStatus("終了処理: 設定を保存中");
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

                    ShowUiHangShutdownStatus("終了処理: レイアウトを保存中");
                    SaveDockLayoutToFile(DockLayoutFileName);
                    SaveDockLayoutToFile(DefaultDockLayoutFileName);

                    if (!string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
                    {
                        ShowUiHangShutdownStatus("終了処理: 履歴を整理中");
                        var keepHistoryData = SelectSystemTable("keepHistory");
                        int keepHistoryCount = Convert.ToInt32(
                            keepHistoryData == "" ? "30" : keepHistoryData
                        );
                        DeleteHistoryTable(MainVM.DbInfo.DBFullPath, keepHistoryCount);
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                ShowUiHangShutdownStatus("終了処理: UI停止準備中");
                // 閉じ際に動画再生とUIタイマーを先に止め、追加のハンドル消費を抑える。
                uxVideoPlayer.Stop();
                StopDispatcherTimerSafely(timer, nameof(timer));
                StopDispatcherTimerSafely(
                    _thumbnailProgressUiTimer,
                    nameof(_thumbnailProgressUiTimer)
                );
                StopDispatcherTimerSafely(_debugTabRefreshTimer, nameof(_debugTabRefreshTimer));
                StopDispatcherTimerSafely(_logTabRefreshTimer, nameof(_logTabRefreshTimer));

                ShowUiHangShutdownStatus("終了処理: 入力受付を停止中");
                // まず入力を止め、以降の監視イベントからの投入を遮断する。
                ShowUiHangShutdownStatus("終了処理: バックグラウンド処理を停止中");
                SetThumbnailQueueInputEnabled(false);
                queueRequestChannel.Writer.TryComplete();
                DebugRuntimeLog.Write(
                    "lifecycle",
                    "shutdown: input stop requested and thumbnail queue input disabled."
                );
                DebugRuntimeLog.Write(
                    "lifecycle",
                    "MainWindow closing: thumbnail token cancel requested."
                );
                _thumbCheckCts.Cancel();
                _thumbnailQueuePersisterCts.Cancel();
                _everythingWatchPollCts.Cancel();
                CancelKanaBackfill("window-closing");

                // 即終了優先を守るため、各タスク待機は最大500msで打ち切る。
                ShowUiHangShutdownStatus("終了処理: 後始末を実行中(1/4): サムネイル消費タスク停止待機");
                WaitBackgroundTaskForShutdown(_thumbCheckTask, "thumbnail-consumer");

                ShowUiHangShutdownStatus("終了処理: 後始末を実行中(2/4): サムネイル保存タスク停止待機");
                WaitBackgroundTaskForShutdown(_thumbnailQueuePersisterTask, "thumbnail-persister");

                ShowUiHangShutdownStatus("終了処理: 後始末を実行中(3/4): 監視ポーリング停止待機");
                WaitBackgroundTaskForShutdown(_everythingWatchPollTask, "everything-poll");

                ShowUiHangShutdownStatus("終了処理: 後始末を実行中(4/4): rescue worker を停止中");
                DebugRuntimeLog.Write(
                    "lifecycle",
                    "shutdown: starting rescue worker cleanup."
                );
                DisposeThumbnailRescueWorkerLaunchers();

                DebugRuntimeLog.Write(
                    "lifecycle",
                    "shutdown: stopping ui hang notification support."
                );
                ShowUiHangShutdownStatus("終了処理: 後始末を実行中: オーバーレイ停止中");
                HideUiHangShutdownStatus();
                StopUiHangNotificationSupport();
            }
        }

        /// <summary>
        /// 前回保存した AvalonDock レイアウト（layout.xml）の復元を試みる。
        /// 現在の配置を default(layout.default.xml) にも保存し、
        /// 通常レイアウトが無い・壊れている時は default から復元する。
        /// </summary>
        private void TryRestoreDockLayout()
        {
            if (TryRestoreDockLayoutFromFile(DockLayoutFileName, backupInvalidLayout: true))
            {
                return;
            }

            _ = TryRestoreDockLayoutFromFile(DefaultDockLayoutFileName, backupInvalidLayout: false);
        }

        /// <summary>
        /// 指定ファイルのレイアウト復元を試みる。
        /// 通常 layout.xml は互換外時に退避し、default 側は壊れていても静かに無視する。
        /// </summary>
        private bool TryRestoreDockLayoutFromFile(string layoutFilePath, bool backupInvalidLayout)
        {
            if (!Path.Exists(layoutFilePath))
            {
                return false;
            }

            try
            {
                // 新しいツールタブを含まない古いレイアウトは互換外として扱う。
                string layoutText = File.ReadAllText(layoutFilePath);
                string invalidReason = ValidateDockLayoutText(layoutText);
                if (!string.IsNullOrEmpty(invalidReason))
                {
                    if (backupInvalidLayout)
                    {
                        BackupLegacyDockLayout(layoutFilePath, invalidReason);
                    }
                    return false;
                }

                XmlLayoutSerializer layoutSerializer = new(uxDockingManager);
                using var reader = new StreamReader(layoutFilePath);
                layoutSerializer.Deserialize(reader);
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "layout",
                    $"layout restore failed. file='{layoutFilePath}' reason={ex.Message}"
                );
                if (backupInvalidLayout)
                {
                    BackupLegacyDockLayout(layoutFilePath, "deserialize-failed");
                }
                return false;
            }
        }

        /// <summary>
        /// 互換性のない旧レイアウトファイルを日時付きで退避し、次回は既定レイアウトで起動させる。
        /// </summary>
        private string ValidateDockLayoutText(string layoutText)
        {
            if (
                !layoutText.Contains(
                    $"ContentId=\"{ThumbnailProgressContentId}\"",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "missing-thumbnail-progress";
            }

            if (
                ShouldRequireThumbnailErrorBottomTabInLayoutRestore(
                    layoutText,
                    ShouldShowThumbnailErrorBottomTab
                )
            )
            {
                return "missing-thumbnail-error-bottom-tab";
            }

            if (
                !layoutText.Contains(
                    $"ContentId=\"{TagEditorBottomTabContentId}\"",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "missing-tag-editor-bottom-tab";
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
                return "missing-debug-tool";
            }

            if (
                ShouldShowDebugTab
                && !layoutText.Contains(
                    $"ContentId=\"{LogToolContentId}\"",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "missing-log-tool";
            }

            return "";
        }

        /// <summary>
        /// 現在のタブ配置を通常保存用と default 保存用の両方へ書き出す。
        /// これにより、ユーザーが整えた配置を次回以降の既定値としても再利用できる。
        /// </summary>
        private void SaveDockLayoutToFile(string layoutFilePath)
        {
            XmlLayoutSerializer layoutSerializer = new(uxDockingManager);
            using var writer = new StreamWriter(layoutFilePath);
            layoutSerializer.Serialize(writer);
        }

        private static void BackupLegacyDockLayout(string layoutFilePath, string reason)
        {
            try
            {
                string suffix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string directoryPath = Path.GetDirectoryName(layoutFilePath);
                string fileName = Path.GetFileNameWithoutExtension(layoutFilePath);
                string extension = Path.GetExtension(layoutFilePath);
                string backupFileName = $"{fileName}.{reason}.{suffix}{extension}";
                string backupPath = string.IsNullOrWhiteSpace(directoryPath)
                    ? backupFileName
                    : Path.Combine(directoryPath, backupFileName);
                File.Move(layoutFilePath, backupPath, true);
            }
            catch
            {
                try
                {
                    File.Delete(layoutFilePath);
                }
                catch
                {
                    // 退避失敗時は何もしない。次回起動時も復元は試みない前提で進める。
                }
            }
        }

        /// <summary>
        /// マルチモニタ切断や解像度変更で画面外に飛んだウィンドウ位置を安全に補正して復元する。
        /// </summary>
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
            // 起動直後の呼び出し元を止めないよう、まず非同期境界へ出る。
            await Task.Yield();

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
            // 起動直後の初回描画を優先するため、同期前半をUIスレッドへ残さない。
            await Task.Yield();

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    if (IsWatchSuppressedByUi())
                    {
                        MarkWatchWorkDeferredWhileSuppressed("everything-poll");
                    }
                    else if (ShouldRunEverythingWatchPoll())
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

        /// <summary>
        /// サムネイルキュー負荷に応じてEverythingポーリング間隔を動的に調整する。
        /// キュー残量が多い時はポーリングを15秒に延ばし、CPUの空振り消費を抑える。
        /// </summary>
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
            if (IsStartupFeedPartialActive)
            {
                return false;
            }

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
            string previousMainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";

            try
            {
                // 先にスキーマ検証し、NGなら現DBを維持したまま中断する。
                ShowUiHangDbSwitchStatus("DB切替: スキーマを確認中");
                if (!TryValidateMainDatabaseSchema(dbFullPath, out string schemaError))
                {
                    DebugRuntimeLog.Write(
                        "db",
                        $"open canceled: schema validation failed. db='{dbFullPath}', reason='{schemaError}'"
                    );
                    MessageBox.Show(
                        this,
                        BuildMainDbValidationFailureMessage(schemaError),
                        Assembly.GetExecutingAssembly().GetName().Name,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return false;
                }

                ShowUiHangDbSwitchStatus("DB切替: rescue worker を停止中");
                StopThumbnailRescueWorkersForDbSwitch(previousMainDbFullPath, dbFullPath);

                // === Phase 1: 旧DBの完全シャットダウン ===
                ShowUiHangDbSwitchStatus("DB切替: 旧DBを停止中");
                ShutdownCurrentDb();

                // === Phase 2: 新DBの起動 ===
                ShowUiHangDbSwitchStatus("DB切替: 新DBを起動中");
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
            CancelDeferredWatchUiReload("shutdown-current-db");
            ResetStartupFeedState("shutdown-current-db");
            CancelKanaBackfill("shutdown-current-db");

            // タブを強制リセット（前回のタブが0だった場合の対応）
            Tabs.SelectedIndex = -1;
            MainVM.DbInfo.CurrentTabIndex = -1;

            // 旧FileSystemWatcherを全停止＆Dispose（イベントリーク防止！🛡️）
            StopAndClearFileWatchers();
            ClearDeferredWatchScanStates();
            ClearDeferredWatchWorkByUiSuppression();

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
            ResetMainHeaderCounts();
            movieData = null;
            filterList = [];
            MainVM.ReplaceMovieRecs([]);
            MainVM.ReplaceFilteredMovieRecs([], FilteredMovieRecsUpdateMode.Reset);
        }

        /// <summary>
        /// 新DBをガッツリ読み込んで、画面もWatcherも全部新しいDB色に染め上げる！🎨
        /// </summary>
        private void BootNewDb(string dbFullPath)
        {
            MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbFullPath);
            MainVM.DbInfo.DBFullPath = dbFullPath;
            ShowUiHangDbSwitchStatus("DB切替: system 設定を読込中");
            GetSystemTable(dbFullPath);
            MainVM.ReplaceMovieRecs([]);
            MainVM.ReplaceFilteredMovieRecs([], FilteredMovieRecsUpdateMode.Reset);
            filterList = [];
            movieData = null;
            ResetMainHeaderCounts();
            QueueRegisteredMovieCountRefresh(dbFullPath);

            ShowUiHangDbSwitchStatus("DB切替: 履歴を読込中");
            GetHistoryTable(dbFullPath);
            ReloadSavedSearchItems();

            if (MainVM.DbInfo.Skin != null)
            {
                ShowUiHangDbSwitchStatus("DB切替: タブ状態を復元中");
                SwitchTab(MainVM.DbInfo.Skin);
            }

            // 起動時のDB復元では Skin/DBFullPath の PropertyChanged だけに頼ると、
            // タイミング次第で外部 skin host refresh が見た目へ出ないことがある。
            // 新DB起動完了時に 1 回明示的に積み、起動復元経路でも host 切替を確実に走らせる。
            QueueExternalSkinHostRefresh("boot-new-db");

            UpdateExtensionDetailVisibilityBySearchCount();
            ShowUiHangDbSwitchStatus("DB切替: 初期表示を準備中");
            BeginStartupDbOpen();
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
            string currentText = SearchBox?.Text ?? "";

            bool previousSuppressState = _suppressSearchBoxTextChangedHandling;
            _suppressSearchBoxTextChangedHandling = true;
            try
            {
                historyData = null;
                MainVM.HistoryRecs.Clear();
                foreach (History item in SearchHistoryService.LoadLatestHistory(dbFullPath))
                {
                    MainVM.HistoryRecs.Add(item);
                }

                // 履歴再読込で編集中テキストが消えないように戻す。
                if (SearchBox != null)
                {
                    SearchBox.Text = currentText;
                }
            }
            finally
            {
                _suppressSearchBoxTextChangedHandling = previousSuppressState;
            }
        }

        /// <summary>
        /// systemテーブルに眠るスキン・ソート・フォルダ設定を呼び覚まし、アプリの見た目と挙動に魂を吹き込む！✨
        /// </summary>
        private void GetSystemTable(string dbPath)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                // system 読みは facade へ寄せ、UI 側は反映だけに絞る。
                systemData = _mainDbMovieReadFacade.LoadSystemTable(dbPath);

                var skin = SelectSystemTable("skin");
                // 永続値は raw skin 名を残し、表示側だけで安全に built-in へフォールバックする。
                MainVM.DbInfo.Skin = string.IsNullOrWhiteSpace(skin) ? "DefaultGrid" : skin;

                var sort = SelectSystemTable("sort");
                MainVM.DbInfo.Sort = sort == "" ? "1" : sort;

                string dbName = string.IsNullOrWhiteSpace(MainVM.DbInfo.DBName)
                    ? Path.GetFileNameWithoutExtension(dbPath) ?? ""
                    : MainVM.DbInfo.DBName;
                string configuredThumbFolder = SelectSystemTable("thum");
                MainVM.DbInfo.ThumbFolder = ThumbRootResolver.ResolveRuntimeThumbRoot(
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
        /// systemテーブルのスキン名の表記ゆれ（大文字小文字・全角空白等）を正規化する。
        /// 不明な値は "DefaultGrid" へフォールバックし、起動時の迷子を防ぐ。
        /// </summary>
        private static string NormalizeSkinName(string skin)
        {
            if (string.IsNullOrWhiteSpace(skin))
            {
                return "DefaultGrid";
            }

            string compactSkin = skin.Trim().Replace(" ", "").Replace("　", "");
            if (string.Equals(compactSkin, "DefaultBig", StringComparison.OrdinalIgnoreCase))
            {
                return "DefaultBig";
            }

            if (string.Equals(compactSkin, "DefaultGrid", StringComparison.OrdinalIgnoreCase))
            {
                return "DefaultGrid";
            }

            if (string.Equals(compactSkin, "DefaultList", StringComparison.OrdinalIgnoreCase))
            {
                return "DefaultList";
            }

            if (string.Equals(compactSkin, "DefaultBig10", StringComparison.OrdinalIgnoreCase))
            {
                return "DefaultBig10";
            }

            return "DefaultGrid";
        }

        /// <summary>
        /// watch（監視）テーブルを、指定の条件でガッツリ読み込んでくるぜ！👁️
        /// </summary>
        private void GetWatchTable(string dbPath, string sql)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                watchData = GetData(dbPath, sql);
                WatchTableRowNormalizer.Normalize(watchData);
            }
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
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            PersistCurrentSkinState(dbFullPath);
        }

        /// <summary>
        /// 読み込んだスキン名に合わせて、表示するタブを華麗に切り替えるチェンジャー・メソッド！🔀
        /// </summary>
        private void SwitchTab(string skin)
        {
            if (!ApplySkinByName(skin, persistToCurrentDb: false))
            {
                SelectUpperTabDefaultViewBySkinName("DefaultGrid");
            }
        }

        /// <summary>
        /// DB再取得から検索・並び替え・画面反映まで、すべてをワンボタンでフルコース提供する超最強の総合フィルターメソッド！🍔🍟🥤
        /// </summary>
        public void FilterAndSort(string id, bool IsGetNew = false)
        {
            CancelStartupFeed("filter-sort");
            _ = FilterAndSortAsync(id, IsGetNew);
        }

        private async Task FilterAndSortAsync(string id, bool isGetNew)
        {
            using IDisposable uiHangScope = TrackUiHangActivity(UiHangActivityKind.Database);
            Stopwatch totalStopwatch = Stopwatch.StartNew();
            int requestRevision = Interlocked.Increment(ref _filterAndSortRequestRevision);
            DataTable latestMovieData = movieData;
            MovieRecords[] latestMovieRecords = null;
            long dbLoadElapsedMs = 0;
            long sourceApplyElapsedMs = 0;
            long filterSortElapsedMs = 0;
            long refreshElapsedMs = 0;

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"filter start: revision={requestRevision} sort={id} is_get_new={isGetNew} keyword='{MainVM.DbInfo.SearchKeyword}'"
            );

            if ((latestMovieData == null && !_startupFeedLoadedAllPages) || isGetNew)
            {
                Stopwatch dbLoadStopwatch = Stopwatch.StartNew();
                string dbFullPath = MainVM.DbInfo.DBFullPath;
                // full reload の movie 読みは facade へ寄せ、並び順の SQL を UI から剥がす。
                latestMovieData = await Task.Run(
                    () => _mainDbMovieReadFacade.LoadMovieTableForSort(dbFullPath, id)
                );
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
                latestMovieRecords = await SetRecordsToSource(latestMovieData, requestRevision);
                // DB読み込みと変換が完了したので、rawなDataTable参照を残さずに解放する。
                movieData = null;
                if (requestRevision != _filterAndSortRequestRevision)
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"filter skip stale source set: revision={requestRevision} current_revision={_filterAndSortRequestRevision} source_apply_ms={sourceApplyStopwatch.ElapsedMilliseconds}"
                    );
                    return;
                }
                InvalidateThumbnailErrorRecords(refreshIfVisible: true);
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
            string searchKeyword = MainVM.DbInfo.SearchKeyword;
            IEnumerable<MovieRecords> filterSource = (latestMovieRecords?.AsEnumerable() ?? MainVM.MovieRecs)
                .Where(movie => movie != null);
            (MovieRecords[] sorted, int searchCount) = await Task.Run(() =>
            {
                MovieRecords[] filtered = MainVM
                    .FilterMovies(filterSource, searchKeyword)
                    .ToArray();
                int resolvedSearchCount = filtered.Length;
                MovieRecords[] sortedMovies = MainVM.SortMovies(filtered, id).ToArray();
                return (sortedMovies, resolvedSearchCount);
            });
            if (requestRevision != _filterAndSortRequestRevision)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"filter skip stale filter-sort: revision={requestRevision} current_revision={_filterAndSortRequestRevision} elapsed_ms={totalStopwatch.ElapsedMilliseconds}"
                );
                return;
            }
            MainVM.DbInfo.SearchCount = searchCount;
            filterList = sorted;
            int currentTabIndex = TryGetCurrentUpperTabFixedIndex(out int resolvedTabIndex)
                ? resolvedTabIndex
                : UpperTabGridFixedIndex;
            FilteredMovieRecsUpdateResult applyResult = MainVM.ReplaceFilteredMovieRecs(
                sorted,
                updateMode: UpperTabCollectionUpdatePolicy.ResolveUpdateMode(
                    currentTabIndex,
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
                NotifyUpperTabViewportSourceChanged();
                RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "filter");
            }

            if (id == "28")
            {
                RefreshThumbnailErrorRecords(force: true);
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
            // 並び替えロジックは ViewModel に寄せ、追加ソートの差分を 1 箇所へ閉じ込める。
            filterList = MainVM.SortMovies(filterList ?? [], id).ToArray();
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
                int currentTabIndex = TryGetCurrentUpperTabFixedIndex(out int resolvedTabIndex)
                    ? resolvedTabIndex
                    : UpperTabGridFixedIndex;
                FilteredMovieRecsUpdateResult applyResult = MainVM.ReplaceFilteredMovieRecs(
                    sorted,
                    updateMode: UpperTabCollectionUpdatePolicy.ResolveUpdateMode(
                        currentTabIndex,
                        isSortOnly: true
                    )
                );
                MainVM.DbInfo.SearchCount = sorted.Length;
                if (applyResult.HasChanges)
                {
                    NotifyUpperTabViewportSourceChanged();
                    Refresh();
                    RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "sort");
                }
                sw.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"sort end: sort={id} tab={currentTabIndex} changed={applyResult.HasChanges} prefix={applyResult.RetainedPrefixCount} suffix={applyResult.RetainedSuffixCount} removed={applyResult.RemovedCount} inserted={applyResult.InsertedCount} moved={applyResult.MovedCount} update_mode={UpperTabCollectionUpdatePolicy.ResolveUpdateMode(currentTabIndex, true)} count={sorted.Length} total_ms={sw.ElapsedMilliseconds}"
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
        /// 起動時全件変換で共有するサムネイル出力パス群のスナップショット。
        /// 変換ループの途中でパスが変わらないように、開始前に1回だけキャプチャする。
        /// </summary>
        private readonly record struct MovieRecordBulkBuildContext(
            string[] ThumbnailOutPaths,
            string DetailThumbnailOutPath,
            string ImagesDirectoryPath
        );

        /// <summary>
        /// 全レイアウトタブのサムネイル既存ファイル名をメモリ上にキャッシュし、
        /// 起動時全件変換で File.Exists の N×5 回呼び出しを HashSet.Contains に置き換える高速化用。
        /// </summary>
        private sealed class MovieRecordBulkBuildCache
        {
            public required HashSet<string>[] ThumbnailFileNamesByTab { get; init; }
            public required HashSet<string> DetailThumbnailFileNames { get; init; }
        }

        /// <summary>
        /// DBから拾った無骨なレコード1件を、キラキラな表示用（MovieRecords）へ変換する。
        /// 単発追加では従来どおり実ファイル確認も行い、起動時全件変換だけは別の高速経路へ逃がす。
        /// </summary>
        private void DataRowToViewData(DataRow row)
        {
            MovieRecords item = CreateMovieRecordFromDataRow(row);
            if (item == null)
            {
                return;
            }

            MainVM.MovieRecs.Add(item);
        }

        /// <summary>
        /// DataRow 1行を表示用の MovieRecords に変換する。
        /// 単発追加では実ファイル存在確認あり、起動時全件変換では BulkBuildCache 経由の高速経路を使う。
        /// </summary>
        private MovieRecords CreateMovieRecordFromDataRow(
            DataRow row,
            MovieRecordBulkBuildContext? bulkContext = null,
            MovieRecordBulkBuildCache bulkCache = null,
            bool resolveMovieExists = true
        )
        {
            if (row == null)
            {
                return null;
            }

            string[] thumbErrorPath =
            [
                @"errorSmall.jpg",
                @"errorBig.jpg",
                @"errorGrid.jpg",
                @"errorList.jpg",
                @"errorBig.jpg",
            ];
            string[] thumbPath = new string[thumbErrorPath.Length];
            string hash = row["hash"]?.ToString() ?? "";
            string movieFullPath = row["movie_path"]?.ToString() ?? "";
            string movieName = row["movie_name"]?.ToString() ?? "";
            string imagesDirectoryPath = bulkContext?.ImagesDirectoryPath
                ?? Path.Combine(AppContext.BaseDirectory, "Images");

            for (int i = 0; i < thumbErrorPath.Length; i++)
            {
                string fallbackPath = Path.Combine(imagesDirectoryPath, thumbErrorPath[i]);
                if (bulkContext.HasValue && bulkCache != null)
                {
                    thumbPath[i] = ResolveThumbnailDisplayPath(
                        bulkContext.Value.ThumbnailOutPaths[i],
                        bulkCache.ThumbnailFileNamesByTab[i],
                        movieFullPath,
                        movieName,
                        hash,
                        fallbackPath
                    );
                    continue;
                }

                // 生成側と同じ規則でまず探索し、旧命名が残っている環境はフォールバックで拾う。
                string tempPath = BuildCurrentThumbnailPath(i, movieFullPath, hash);
                if (!Path.Exists(tempPath) && !string.IsNullOrWhiteSpace(movieName))
                {
                    tempPath = BuildCurrentThumbnailPath(i, movieName, hash);
                }

                thumbPath[i] = Path.Exists(tempPath) ? tempPath : fallbackPath;
            }

            string thumbPathDetail;
            if (bulkContext.HasValue && bulkCache != null)
            {
                thumbPathDetail = ResolveThumbnailDisplayPath(
                    bulkContext.Value.DetailThumbnailOutPath,
                    bulkCache.DetailThumbnailFileNames,
                    movieFullPath,
                    movieName,
                    hash,
                    Path.Combine(imagesDirectoryPath, thumbErrorPath[2])
                );
            }
            else
            {
                // エクステンション詳細用も、本体と同じく旧命名をフォールバックで拾う。
                string tempPathExtensionDetail = BuildCurrentThumbnailPath(99, movieFullPath, hash);
                if (!Path.Exists(tempPathExtensionDetail) && !string.IsNullOrWhiteSpace(movieName))
                {
                    tempPathExtensionDetail = BuildCurrentThumbnailPath(99, movieName, hash);
                }

                thumbPathDetail = Path.Exists(tempPathExtensionDetail)
                    ? tempPathExtensionDetail
                    : Path.Combine(imagesDirectoryPath, thumbErrorPath[2]);
            }

            string tags = row["tag"]?.ToString() ?? "";
            List<string> tagArray = [];
            if (!string.IsNullOrEmpty(tags))
            {
                string[] splitTags = tags.Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries
                );
                foreach (string tagItem in splitTags)
                {
                    tagArray.Add(tagItem);
                }
            }

            string tag = MyRegex().Replace(tags, "");
            string ext = Path.GetExtension(movieFullPath);
            string movieBody = Path.GetFileNameWithoutExtension(movieFullPath);

            return new MovieRecords
            {
                Movie_Id = (long)row["movie_id"],
                Movie_Name = $"{movieName}{ext}",
                Movie_Body = movieBody,
                Movie_Path = movieFullPath,
                Movie_Length = new TimeSpan(0, 0, (int)(long)row["movie_length"]).ToString(
                    @"hh\:mm\:ss"
                ),
                Movie_Size = (long)row["movie_size"],
                Last_Date = ReadDbDateTimeTextOrEmpty(row["last_date"]),
                File_Date = ReadDbDateTimeTextOrEmpty(row["file_date"]),
                Regist_Date = ReadDbDateTimeTextOrEmpty(row["regist_date"]),
                Score = (long)row["score"],
                View_Count = (long)row["view_count"],
                Hash = hash,
                Container = row["container"]?.ToString() ?? "",
                Video = row["video"]?.ToString() ?? "",
                Audio = row["audio"]?.ToString() ?? "",
                Extra = row["extra"]?.ToString() ?? "",
                Title = row["title"]?.ToString() ?? "",
                Album = row["album"]?.ToString() ?? "",
                Artist = row["artist"]?.ToString() ?? "",
                Grouping = row["grouping"]?.ToString() ?? "",
                Writer = row["writer"]?.ToString() ?? "",
                Genre = row["genre"]?.ToString() ?? "",
                Track = row["track"]?.ToString() ?? "",
                Camera = row["camera"]?.ToString() ?? "",
                Create_Time = row["create_time"]?.ToString() ?? "",
                Kana = row["kana"]?.ToString() ?? "",
                Roma = row["roma"]?.ToString() ?? "",
                Tags = tag,
                Tag = tagArray,
                Comment1 = row["comment1"]?.ToString() ?? "",
                Comment2 = row["comment2"]?.ToString() ?? "",
                Comment3 = row["comment3"]?.ToString() ?? "",
                ThumbPathSmall = thumbPath[0],
                ThumbPathBig = thumbPath[1],
                ThumbPathGrid = thumbPath[2],
                ThumbPathList = thumbPath[3],
                ThumbPathBig10 = thumbPath[4],
                ThumbDetail = thumbPathDetail,
                Drive = Path.GetPathRoot(movieFullPath),
                Dir = Path.GetDirectoryName(movieFullPath),
                // 起動時全件変換では存在確認を後段へ逃がし、まず一覧を出す。
                IsExists = resolveMovieExists ? Path.Exists(movieFullPath) : true,
                Ext = ext,
            };
        }

        /// <summary>
        /// 全件変換開始前にサムネイル出力パス群を1回だけ採取し、変換中のパス揺れを防ぐ。
        /// </summary>
        private MovieRecordBulkBuildContext CaptureMovieRecordBulkBuildContext()
        {
            string[] thumbnailOutPaths = new string[5];
            for (int index = 0; index < thumbnailOutPaths.Length; index++)
            {
                thumbnailOutPaths[index] = ResolveCurrentThumbnailOutPath(index);
            }

            return new MovieRecordBulkBuildContext(
                thumbnailOutPaths,
                ResolveCurrentThumbnailOutPath(99),
                Path.Combine(AppContext.BaseDirectory, "Images")
            );
        }

        /// <summary>
        /// 各レイアウトフォルダから既存 jpg ファイル名を一括収集し、HashSet 化して返す。
        /// 全件変換時の高速サムネイルパス解決に使う。
        /// </summary>
        private static MovieRecordBulkBuildCache BuildMovieRecordBulkBuildCache(
            MovieRecordBulkBuildContext context
        )
        {
            HashSet<string>[] thumbnailFileNamesByTab = new HashSet<string>[context.ThumbnailOutPaths.Length];
            for (int index = 0; index < context.ThumbnailOutPaths.Length; index++)
            {
                thumbnailFileNamesByTab[index] = BuildThumbnailFileNameLookup(
                    context.ThumbnailOutPaths[index]
                );
            }

            return new MovieRecordBulkBuildCache
            {
                ThumbnailFileNamesByTab = thumbnailFileNamesByTab,
                DetailThumbnailFileNames = BuildThumbnailFileNameLookup(context.DetailThumbnailOutPath),
            };
        }

        internal static HashSet<string> BuildThumbnailFileNameLookup(string thumbnailOutPath)
        {
            return ThumbnailPathResolver.BuildThumbnailFileNameLookup(thumbnailOutPath);
        }

        /// <summary>
        /// HashSet キャッシュを使って最速でサムネイル表示パスを解決する。
        /// 現在の命名規則 → 旧命名規則 → 同名画像 fallback の順で探索する。
        /// </summary>
        private static string ResolveThumbnailDisplayPath(
            string thumbnailOutPath,
            HashSet<string> existingFileNames,
            string movieFullPath,
            string movieName,
            string hash,
            string fallbackPath
        )
        {
            if (!string.IsNullOrWhiteSpace(thumbnailOutPath) && existingFileNames != null)
            {
                string currentFileName = ThumbnailPathResolver.BuildThumbnailFileName(movieFullPath, hash);
                if (existingFileNames.Contains(currentFileName))
                {
                    return Path.Combine(thumbnailOutPath, currentFileName);
                }

                if (!string.IsNullOrWhiteSpace(movieName))
                {
                    string legacyFileName = ThumbnailPathResolver.BuildThumbnailFileName(movieName, hash);
                    if (existingFileNames.Contains(legacyFileName))
                    {
                        return Path.Combine(thumbnailOutPath, legacyFileName);
                    }
                }
            }

            if (
                ThumbnailSourceImagePathResolver.TryResolveSameNameThumbnailSourceImagePath(
                    movieFullPath,
                    out string sourceImagePath
                )
            )
            {
                return sourceImagePath;
            }
            return fallbackPath;
        }

        /// <summary>
        /// 取得済みの生データ（movieData）から、表示用コレクションを背景で組み立てて一気に差し替える。
        /// </summary>
        private async Task<MovieRecords[]> SetRecordsToSource(
            DataTable sourceData,
            int requestRevision
        )
        {
            DataTable targetData = sourceData ?? movieData;
            if (targetData == null)
            {
                return [];
            }

            int rowCount = targetData.Rows.Count;
            MovieRecordBulkBuildContext bulkContext = CaptureMovieRecordBulkBuildContext();
            MovieRecords[] items = await Task.Run(() =>
            {
                MovieRecordBulkBuildCache bulkCache = BuildMovieRecordBulkBuildCache(bulkContext);
                MovieRecords[] loadedItems = new MovieRecords[rowCount];
                for (int index = 0; index < rowCount; index++)
                {
                    loadedItems[index] = CreateMovieRecordFromDataRow(
                        targetData.Rows[index],
                        bulkContext,
                        bulkCache,
                        resolveMovieExists: false
                    );
                }

                return loadedItems;
            });

            if (requestRevision != _filterAndSortRequestRevision)
            {
                return items;
            }

            MainVM.ReplaceMovieRecs(items);
            QueueMovieExistsRefresh(items, requestRevision);
            return items;
        }

        /// <summary>
        /// 起動時全件変換で省略したファイル存在チェックをバックグラウンドで後追い実行し、
        /// 見つからないファイルの IsExists を false に更新する。128件ずつバッチでUIスレッドへ反映。
        /// </summary>
        private void QueueMovieExistsRefresh(
            IReadOnlyList<MovieRecords> items,
            int requestRevision
        )
        {
            if (items == null || items.Count < 1)
            {
                return;
            }

            int refreshRevision = Interlocked.Increment(ref _movieExistsRefreshRevision);
            _ = Task.Run(async () =>
            {
                try
                {
                    List<(MovieRecords Record, bool Exists)> pending = [];
                    for (int index = 0; index < items.Count; index++)
                    {
                        if (
                            refreshRevision != Volatile.Read(ref _movieExistsRefreshRevision)
                            || requestRevision != Volatile.Read(ref _filterAndSortRequestRevision)
                        )
                        {
                            return;
                        }

                        MovieRecords item = items[index];
                        bool exists = Path.Exists(item?.Movie_Path ?? "");
                        if (item != null && item.IsExists != exists)
                        {
                            pending.Add((item, exists));
                        }

                        if (pending.Count >= 128)
                        {
                            await ApplyMovieExistsRefreshBatchAsync(
                                pending.ToArray(),
                                refreshRevision,
                                requestRevision
                            );
                            pending.Clear();
                        }
                    }

                    if (pending.Count > 0)
                    {
                        await ApplyMovieExistsRefreshBatchAsync(
                            pending.ToArray(),
                            refreshRevision,
                            requestRevision
                        );
                    }
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"movie exists refresh failed: revision={requestRevision} err='{ex.GetType().Name}: {ex.Message}'"
                    );
                }
            });
        }

        private Task ApplyMovieExistsRefreshBatchAsync(
            (MovieRecords Record, bool Exists)[] batch,
            int refreshRevision,
            int requestRevision
        )
        {
            return Dispatcher
                .InvokeAsync(
                    () =>
                    {
                        if (
                            refreshRevision != _movieExistsRefreshRevision
                            || requestRevision != _filterAndSortRequestRevision
                        )
                        {
                            return;
                        }

                        for (int index = 0; index < batch.Length; index++)
                        {
                            batch[index].Record.IsExists = batch[index].Exists;
                        }
                    },
                    DispatcherPriority.Background
                )
                .Task;
        }

        /// <summary>
        /// サムネイル画像上のクリック位置から、対応する動画の再生開始秒（ミリ秒）を計算する。
        /// サムネイルのグリッド構造（行×列）から、どのフレームがクリックされたかを逆算する。
        /// </summary>
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

        /// <summary>
        /// 一覧タブ上のショートカットキー（Enter/F6/C/V/+/-/F2/F12/Delete等）を
        /// 各機能ハンドラへ振り分けるキーディスパッチャ。
        /// </summary>
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

            if (TryHandleUpperTabPageScroll(e))
            {
                return;
            }

            if (e.Key == Key.Delete)
            {
                // Delete系ショートカットは修飾キーごとに別設定へ振り分ける。
                if (TryHandleDeleteShortcut(e))
                {
                    return;
                }
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

        /// <summary>
        /// ソートコンボボックスの選択変更ハンドラ。
        /// 段階ロード中は全件再取得付き FilterAndSort、通常時はインメモリ SortData で並び替えて先頭を選択する。
        /// </summary>
        private void ComboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }
            if (_suppressSortComboSelectionChangedHandling)
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
                        if (IsStartupFeedPartialActive)
                        {
                            FilterAndSort(id.ToString(), true);
                        }
                        else
                        {
                            SortData(id.ToString());
                        }
                        if (id.ToString() == "28")
                        {
                            RefreshThumbnailErrorRecords(force: true);
                        }
                        SelectFirstItem();
                    }
                }
            }
        }

    }
}
