using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IndigoMovieManager.BottomTabs.ThumbnailProgress;
using IndigoMovieManager.ModelViews;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 一時対応: サムネイル作成中ダイアログ表示を止める。
        private static readonly bool TemporaryPauseThumbnailProgressDialog = true;
        // 一時対応: 進捗タブのDB登録待ち/DB総数表示を止める。
        private static readonly bool TemporaryPauseThumbnailProgressDbCount = true;
        private const int ThumbnailProgressUiIntervalMs = 500;
        private const int ThumbnailProgressSnapshotFallbackIntervalMs = 3000;

        private bool _isThumbnailProgressSettingsSyncing;
        private readonly IThumbnailQueueProgressPresenter _thumbnailQueueProgressPresenter =
            TemporaryPauseThumbnailProgressDialog
                ? NoOpThumbnailQueueProgressPresenter.Instance
                : new AppThumbnailQueueProgressPresenter();
        private readonly ThumbnailProgressRuntime _thumbnailProgressRuntime = new();
        private DispatcherTimer _thumbnailProgressUiTimer;
        private int _thumbnailProgressUiTickAccumulatedMs;
        // 進捗スナップショット更新要求はここで集約し、UI反映の連打を抑える。
        private int _thumbnailProgressSnapshotRefreshQueued;
        private int _thumbnailProgressSnapshotRefreshRequested;
        private int _thumbnailProgressUiDirtyWhileHidden;
        private int _thumbnailProgressTabVisibleOrSelected;
        private long _thumbnailProgressLastAppliedSnapshotVersion = -1;
        private int _thumbnailProgressLastAppliedDbPendingCount = -1;
        private int _thumbnailProgressLastAppliedDbTotalCount = -1;
        private int _thumbnailProgressLastAppliedLogicalCoreCount = -1;
        private bool _thumbnailProgressTabMonitoringInitialized;
        private CheckBox ThumbnailProgressResizeThumbCheckBox =>
            ThumbnailProgressTabViewHost?.ResizeThumbCheckBox;
        private CheckBox ThumbnailProgressGpuDecodeEnabled =>
            ThumbnailProgressTabViewHost?.GpuDecodeEnabledCheckBox;
        private Slider sliderThumbnailProgressParallelism =>
            ThumbnailProgressTabViewHost?.ParallelismSlider;
        private Slider sliderThumbnailProgressSlowLaneMinGb =>
            ThumbnailProgressTabViewHost?.SlowLaneMinGbSlider;

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

        // 巨大動画判定は進捗タブ側でしか使わないため、ここへ寄せる。
        private static int ClampThumbnailSlowLaneMinGb(int value)
        {
            if (value < 1)
            {
                return 1;
            }
            if (value > 1024)
            {
                return 1024;
            }
            return value;
        }

        private static int ResolveThumbnailProgressPresetParallelism(
            int? divisor,
            int? parallelCount
        )
        {
            if (parallelCount.HasValue)
            {
                return ClampThumbnailParallelismSetting(parallelCount.Value);
            }

            int safeDivisor = divisor.GetValueOrDefault();
            if (safeDivisor < 1)
            {
                safeDivisor = 1;
            }

            int resolved = System.Environment.ProcessorCount / safeDivisor;
            if (resolved < 1)
            {
                resolved = 1;
            }
            return ClampThumbnailParallelismSetting(resolved);
        }

        // サムネ進捗タブの可視状態監視とタイマー初期化をまとめる。
        private void InitializeThumbnailProgressUiSupport()
        {
            InitializeThumbnailProgressTabVisibilityMonitoring();
            InitializeThumbnailProgressUiTimer();
        }

        private void InitializeThumbnailProgressUiTimer()
        {
            _thumbnailProgressUiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ThumbnailProgressUiIntervalMs),
            };
            _thumbnailProgressUiTimer.Tick += ThumbnailProgressUiTimer_Tick;
        }

        // Dock 復元後の状態を見て、進捗タブの可視・選択キャッシュを初期化する。
        private void InitializeThumbnailProgressTabVisibilityMonitoring()
        {
            if (_thumbnailProgressTabMonitoringInitialized || ThumbnailProgressTab == null)
            {
                UpdateThumbnailProgressTabVisibilityState();
                return;
            }

            ThumbnailProgressTab.PropertyChanged += ThumbnailProgressTab_PropertyChanged;
            _thumbnailProgressTabMonitoringInitialized = true;
            UpdateThumbnailProgressTabVisibilityState();
        }

        private void ThumbnailProgressTab_PropertyChanged(
            object sender,
            PropertyChangedEventArgs e
        )
        {
            if (!ThumbnailProgressTabVisibilityGate.ShouldReactToProperty(e?.PropertyName ?? ""))
            {
                return;
            }

            UpdateThumbnailProgressTabVisibilityState();
            TryFlushThumbnailProgressUiIfVisible();
        }

        private void UpdateThumbnailProgressTabVisibilityState()
        {
            bool isVisible = ThumbnailProgressTabVisibilityGate.IsVisibleOrSelected(
                ThumbnailProgressTab
            );
            Interlocked.Exchange(ref _thumbnailProgressTabVisibleOrSelected, isVisible ? 1 : 0);
        }

        private bool IsThumbnailProgressTabVisibleOrSelectedCached()
        {
            return Volatile.Read(ref _thumbnailProgressTabVisibleOrSelected) == 1;
        }

        private bool IsThumbnailProgressUiEnabled()
        {
            return !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished;
        }

        private void MarkThumbnailProgressUiDirtyWhileHidden()
        {
            Interlocked.Exchange(ref _thumbnailProgressUiDirtyWhileHidden, 1);
        }

        private void ClearThumbnailProgressUiDirtyWhileHidden()
        {
            Interlocked.Exchange(ref _thumbnailProgressUiDirtyWhileHidden, 0);
        }

        private bool HasPendingThumbnailProgressUiWork()
        {
            return Volatile.Read(ref _thumbnailProgressUiDirtyWhileHidden) == 1
                || Interlocked.CompareExchange(ref _thumbnailProgressSnapshotRefreshRequested, 0, 0)
                    == 1;
        }

        private void TryFlushThumbnailProgressUiIfVisible()
        {
            if (!IsThumbnailProgressUiEnabled())
            {
                return;
            }

            if (!IsThumbnailProgressTabVisibleOrSelectedCached())
            {
                return;
            }

            if (!HasPendingThumbnailProgressUiWork())
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _thumbnailProgressSnapshotRefreshQueued, 0, 0) == 1)
            {
                return;
            }

            ForceThumbnailProgressSnapshotRefreshNow();
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

            int current = ClampThumbnailParallelismSetting(
                Properties.Settings.Default.ThumbnailParallelism
            );
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
            if (!IsThumbnailProgressUiEnabled())
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                try
                {
                    _ = Dispatcher.InvokeAsync(
                        ForceThumbnailProgressSnapshotRefreshNow,
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

                return;
            }

            UpdateThumbnailProgressTabVisibilityState();
            if (!IsThumbnailProgressTabVisibleOrSelectedCached())
            {
                MarkThumbnailProgressUiDirtyWhileHidden();
                return;
            }

            try
            {
                UpdateThumbnailProgressSnapshotUi();
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

        // GPU利用設定は進捗タブから即時反映する。
        private void ThumbnailProgressGpuDecodeEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (_isThumbnailProgressSettingsSyncing)
            {
                return;
            }

            bool next = ThumbnailProgressGpuDecodeEnabled.IsChecked == true;
            if (Properties.Settings.Default.ThumbnailGpuDecodeEnabled == next)
            {
                return;
            }

            Properties.Settings.Default.ThumbnailGpuDecodeEnabled = next;
            ApplyThumbnailGpuDecodeSetting();
        }

        // 進捗タブの並列スライダー変更を即時反映する。
        private void ThumbnailProgressParallelismSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            if (_isThumbnailProgressSettingsSyncing || !IsLoaded)
            {
                return;
            }

            int next = ClampThumbnailParallelismSetting((int)System.Math.Round(e.NewValue));
            ApplyThumbnailParallelismSetting(next, "progress-tab", prioritizeUi: true);
            SyncThumbnailProgressSettingControls();
        }

        // 巨大動画判定スライダーを設定へ反映する。
        private void ThumbnailProgressSlowLaneMinGbSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            if (_isThumbnailProgressSettingsSyncing || !IsLoaded)
            {
                return;
            }

            int next = ClampThumbnailSlowLaneMinGb((int)System.Math.Round(e.NewValue));
            if (Properties.Settings.Default.ThumbnailSlowLaneMinGb == next)
            {
                return;
            }

            Properties.Settings.Default.ThumbnailSlowLaneMinGb = next;
            SyncThumbnailProgressSettingControls();
        }

        // 通常動画寄りで軽快に回す。
        private void ThumbnailProgressPresetFastButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyThumbnailProgressPreset(slowLaneMinGb: 100, parallelDivisor: 2);
        }

        // 標準バランス設定へ戻す。
        private void ThumbnailProgressPresetNormalButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyThumbnailProgressPreset(slowLaneMinGb: 100, parallelDivisor: 3);
        }

        // 負荷を抑えて巨大動画寄りに寄せる。
        private void ThumbnailProgressPresetLowLoadButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyThumbnailProgressPreset(slowLaneMinGb: 50, parallelCount: 2);
        }

        // プリセットを一括反映し、進捗タブの表示も揃える。
        private void ApplyThumbnailProgressPreset(
            int slowLaneMinGb,
            int? parallelDivisor = null,
            int? parallelCount = null
        )
        {
            Properties.Settings.Default.ThumbnailSlowLaneMinGb = ClampThumbnailSlowLaneMinGb(
                slowLaneMinGb
            );
            ApplyThumbnailParallelismSetting(
                ResolveThumbnailProgressPresetParallelism(parallelDivisor, parallelCount),
                "progress-preset",
                prioritizeUi: true
            );
            SyncThumbnailProgressSettingControls();
        }

        // 進捗タブの設定UIを現在値へ寄せる。
        private void SyncThumbnailProgressSettingControls()
        {
            if (!IsLoaded)
            {
                return;
            }

            _isThumbnailProgressSettingsSyncing = true;
            try
            {
                if (ThumbnailProgressGpuDecodeEnabled != null)
                {
                    ThumbnailProgressGpuDecodeEnabled.IsChecked =
                        Properties.Settings.Default.ThumbnailGpuDecodeEnabled;
                }
                if (sliderThumbnailProgressParallelism != null)
                {
                    sliderThumbnailProgressParallelism.Value = ClampThumbnailParallelismSetting(
                        Properties.Settings.Default.ThumbnailParallelism
                    );
                }
                if (sliderThumbnailProgressSlowLaneMinGb != null)
                {
                    sliderThumbnailProgressSlowLaneMinGb.Value = ClampThumbnailSlowLaneMinGb(
                        Properties.Settings.Default.ThumbnailSlowLaneMinGb
                    );
                }
            }
            finally
            {
                _isThumbnailProgressSettingsSyncing = false;
            }
        }

        private void ThumbnailProgressUiTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // DB登録待ち/DB総数はイベント更新を主軸にしつつ、
                // キュー無変化時間帯の表示古さを防ぐため低頻度フォールバックを入れる。
                _thumbnailProgressUiTickAccumulatedMs += ThumbnailProgressUiIntervalMs;
                if (_thumbnailProgressUiTickAccumulatedMs >= ThumbnailProgressSnapshotFallbackIntervalMs)
                {
                    _thumbnailProgressUiTickAccumulatedMs = 0;
                    UpdateThumbnailProgressSnapshotUi();
                }

                TryQueuePeriodicThumbnailFailureSync();
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write("thumbnail-progress", $"ui update failed: {ex.Message}");
            }
        }

        // 進捗の構造情報（キュー数/スレッド/パネル）を反映する。
        private void UpdateThumbnailProgressSnapshotUi()
        {
            if (!IsThumbnailProgressUiEnabled())
            {
                return;
            }

            UpdateThumbnailProgressTabVisibilityState();
            if (!IsThumbnailProgressTabVisibleOrSelectedCached())
            {
                MarkThumbnailProgressUiDirtyWhileHidden();
                return;
            }

            ThumbnailProgressRuntimeSnapshot runtimeSnapshot =
                _thumbnailProgressRuntime.CreateSnapshot();
            ThumbnailProgressViewState thumbnailProgress = MainVM?.ThumbnailProgress;
            if (thumbnailProgress == null)
            {
                return;
            }

            _ = Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshRequested, 0);

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
                ClearThumbnailProgressUiDirtyWhileHidden();
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
            ClearThumbnailProgressUiDirtyWhileHidden();

            ThumbnailProgressUiMetricsLogger.RecordSnapshotApply(
                runtimeSnapshot.Version,
                dbPendingCount,
                dbTotalCount,
                runtimeSnapshot.ActiveWorkers.Count,
                applyStopwatch.Elapsed.TotalMilliseconds
            );
        }

        // 他スレッドからの進捗反映要求を1本に束ね、UIスレッドの連打を避ける。
        private void RequestThumbnailProgressSnapshotRefresh()
        {
            if (!IsThumbnailProgressUiEnabled())
            {
                return;
            }

            _ = Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshRequested, 1);
            if (!IsThumbnailProgressTabVisibleOrSelectedCached())
            {
                MarkThumbnailProgressUiDirtyWhileHidden();
                return;
            }

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
    }
}
