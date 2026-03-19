using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IndigoMovieManager.BottomTabs.ThumbnailProgress;
using IndigoMovieManager.ViewModels;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 一時対応: サムネイル作成中ダイアログ表示を止める。
        private static readonly bool TemporaryPauseThumbnailProgressDialog = true;
        private const int ThumbnailProgressUiIntervalMs = 500;
        private const int ThumbnailProgressSnapshotFallbackIntervalMs = 3000;

        private bool _isThumbnailProgressSettingsSyncing;
        private readonly IThumbnailQueueProgressPresenter _thumbnailQueueProgressPresenter =
            TemporaryPauseThumbnailProgressDialog
                ? NoOpThumbnailQueueProgressPresenter.Instance
                : new AppThumbnailQueueProgressPresenter();
        private readonly ThumbnailProgressRuntime _thumbnailProgressRuntime = new();
        private readonly DateTime _thumbnailProgressSessionStartedUtc = DateTime.UtcNow;
        private DispatcherTimer _thumbnailProgressUiTimer;
        private int _thumbnailProgressUiTickAccumulatedMs;
        // 進捗スナップショット更新要求はここで集約し、UI反映の連打を抑える。
        private int _thumbnailProgressSnapshotRefreshQueued;
        private int _thumbnailProgressSnapshotRefreshRequested;
        private int _thumbnailProgressUiDirtyWhileHidden;
        private int _thumbnailProgressTabVisibleOrSelected;
        private long _thumbnailProgressLastAppliedSnapshotVersion = -1;
        private int _thumbnailProgressLastAppliedLogicalCoreCount = -1;
        private string _thumbnailProgressLastAppliedRescueWorkerSignature = "";
        private bool _thumbnailProgressTabMonitoringInitialized;
        private CheckBox ThumbnailProgressResizeThumbCheckBox =>
            ThumbnailProgressTabViewHost?.ResizeThumbCheckBox;
        private CheckBox ThumbnailProgressGpuDecodeEnabled =>
            ThumbnailProgressTabViewHost?.GpuDecodeEnabledCheckBox;
        private Slider sliderThumbnailProgressParallelism =>
            ThumbnailProgressTabViewHost?.ParallelismSlider;
        private Slider sliderThumbnailProgressSlowLaneMinGb =>
            ThumbnailProgressTabViewHost?.SlowLaneMinGbSlider;
        private RadioButton ThumbnailProgressPresetLowSpeedRadioButton =>
            ThumbnailProgressTabViewHost?.PresetLowSpeedRadioButton;
        private RadioButton ThumbnailProgressPresetNormalRadioButton =>
            ThumbnailProgressTabViewHost?.PresetNormalRadioButton;
        private RadioButton ThumbnailProgressPresetFastRadioButton =>
            ThumbnailProgressTabViewHost?.PresetFastRadioButton;

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
            if (value > 200)
            {
                return 200;
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

        internal static (int? ThreadCount, string Priority) ResolveThumbnailFfmpegOnePassEcoHint(
            int configuredParallelism,
            int slowLaneMinGb
        )
        {
            return ThumbnailEnvConfig.ResolveFfmpegOnePassEcoHint(
                configuredParallelism,
                slowLaneMinGb
            );
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
                UpdateThumbnailProgressUiTimerState();
                return;
            }

            ThumbnailProgressTab.PropertyChanged += ThumbnailProgressTab_PropertyChanged;
            _thumbnailProgressTabMonitoringInitialized = true;
            UpdateThumbnailProgressTabVisibilityState();
            UpdateThumbnailProgressUiTimerState();
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
            UpdateThumbnailProgressUiTimerState();
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

        private void UpdateThumbnailProgressUiTimerState()
        {
            if (_thumbnailProgressUiTimer == null)
            {
                return;
            }

            if (IsThumbnailProgressTabVisibleOrSelectedCached())
            {
                if (!_thumbnailProgressUiTimer.IsEnabled)
                {
                    _thumbnailProgressUiTimer.Start();
                }

                return;
            }

            if (_thumbnailProgressUiTimer.IsEnabled)
            {
                _thumbnailProgressUiTimer.Stop();
            }
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
            ApplyThumbnailFfmpegEcoSetting();

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

        // 起動直後から設定並列数ぶんのThread枠を見せ、待機中でも右側が空にならないようにする。
        private void PrimeThumbnailProgressWorkerPanels()
        {
            ThumbnailProgressViewState thumbnailProgress = MainVM?.ThumbnailProgress;
            if (thumbnailProgress == null)
            {
                return;
            }

            int configuredParallelism = ClampThumbnailParallelismSetting(
                Properties.Settings.Default.ThumbnailParallelism
            );
            UpdateThumbnailProgressConfiguredParallelism(configuredParallelism);
            thumbnailProgress.ApplySnapshot(
                _thumbnailProgressRuntime.CreateSnapshot(),
                Environment.ProcessorCount
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

        private static void ApplyThumbnailFfmpegEcoSetting()
        {
            ThumbnailEnvConfig.ApplyFfmpegOnePassExecutionHintsForCurrentSettings(
                message => DebugRuntimeLog.Write("thumbnail", message)
            );
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
            ApplyThumbnailFfmpegEcoSetting();
            SyncThumbnailProgressSettingControls();
        }

        // 通常動画寄りで軽快に回す。
        private void ThumbnailProgressPresetFastButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isThumbnailProgressSettingsSyncing)
            {
                return;
            }

            ApplyThumbnailProgressPreset(slowLaneMinGb: 100, parallelDivisor: 2);
        }

        // 標準バランス設定へ戻す。
        private void ThumbnailProgressPresetNormalButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isThumbnailProgressSettingsSyncing)
            {
                return;
            }

            ApplyThumbnailProgressPreset(slowLaneMinGb: 100, parallelDivisor: 3);
        }

        // 低速寄りにして巨大動画を優先する。
        private void ThumbnailProgressPresetLowLoadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isThumbnailProgressSettingsSyncing)
            {
                return;
            }

            ApplyThumbnailProgressPreset(slowLaneMinGb: 50, parallelCount: 2);
        }

        // 現在の設定値がどのプリセットに一致するかを返す。
        private ThumbnailProgressPresetKind ResolveThumbnailProgressPresetKind()
        {
            int currentParallelism = ClampThumbnailParallelismSetting(
                Properties.Settings.Default.ThumbnailParallelism
            );
            int currentSlowLaneMinGb = ClampThumbnailSlowLaneMinGb(
                Properties.Settings.Default.ThumbnailSlowLaneMinGb
            );

            if (IsThumbnailProgressPresetMatch(currentParallelism, currentSlowLaneMinGb, 50, parallelCount: 2))
            {
                return ThumbnailProgressPresetKind.LowSpeed;
            }

            if (IsThumbnailProgressPresetMatch(currentParallelism, currentSlowLaneMinGb, 100, parallelDivisor: 3))
            {
                return ThumbnailProgressPresetKind.Normal;
            }

            if (IsThumbnailProgressPresetMatch(currentParallelism, currentSlowLaneMinGb, 100, parallelDivisor: 2))
            {
                return ThumbnailProgressPresetKind.Fast;
            }

            return ThumbnailProgressPresetKind.Custom;
        }

        // 現在値とプリセット値が一致しているかだけを判定する。
        private static bool IsThumbnailProgressPresetMatch(
            int currentParallelism,
            int currentSlowLaneMinGb,
            int presetSlowLaneMinGb,
            int? parallelDivisor = null,
            int? parallelCount = null
        )
        {
            return currentSlowLaneMinGb == ClampThumbnailSlowLaneMinGb(presetSlowLaneMinGb)
                && currentParallelism
                    == ResolveThumbnailProgressPresetParallelism(parallelDivisor, parallelCount);
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
            ApplyThumbnailFfmpegEcoSetting();
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

                ThumbnailProgressPresetKind presetKind = ResolveThumbnailProgressPresetKind();
                if (ThumbnailProgressPresetLowSpeedRadioButton != null)
                {
                    ThumbnailProgressPresetLowSpeedRadioButton.IsChecked =
                        presetKind == ThumbnailProgressPresetKind.LowSpeed;
                }
                if (ThumbnailProgressPresetNormalRadioButton != null)
                {
                    ThumbnailProgressPresetNormalRadioButton.IsChecked =
                        presetKind == ThumbnailProgressPresetKind.Normal;
                }
                if (ThumbnailProgressPresetFastRadioButton != null)
                {
                    ThumbnailProgressPresetFastRadioButton.IsChecked =
                        presetKind == ThumbnailProgressPresetKind.Fast;
                }

                // 待機中でもThreadカード枠が出るよう、設定中の並列数をRuntimeへ戻す。
                UpdateThumbnailProgressConfiguredParallelism(
                    ClampThumbnailParallelismSetting(Properties.Settings.Default.ThumbnailParallelism)
                );
            }
            finally
            {
                _isThumbnailProgressSettingsSyncing = false;
            }
        }

        private enum ThumbnailProgressPresetKind
        {
            Custom,
            LowSpeed,
            Normal,
            Fast,
        }

        internal readonly record struct ThumbnailProgressUiTickBehavior(
            bool ShouldRefreshUi,
            bool ShouldQueueFailureSync
        );

        private void ThumbnailProgressUiTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                ThumbnailProgressUiTickBehavior tickBehavior =
                    ResolveThumbnailProgressUiTickBehavior(
                        IsThumbnailProgressTabVisibleOrSelectedCached()
                    );

                if (tickBehavior.ShouldQueueFailureSync)
                {
                    TryQueuePeriodicThumbnailFailureSync();
                }

                if (!tickBehavior.ShouldRefreshUi)
                {
                    UpdateThumbnailProgressUiTimerState();
                    MarkThumbnailProgressUiDirtyWhileHidden();
                    return;
                }

                // 進捗イベントが止む時間帯でも表示を置き去りにしないよう、
                // 低頻度フォールバックで最新スナップショットを取り直す。
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

        // rescued 同期は進捗タブが隠れていても回し、重い UI 更新だけ可視時に絞る。
        internal static ThumbnailProgressUiTickBehavior ResolveThumbnailProgressUiTickBehavior(
            bool isThumbnailProgressTabVisibleOrSelected
        )
        {
            return new ThumbnailProgressUiTickBehavior(
                ShouldRefreshUi: isThumbnailProgressTabVisibleOrSelected,
                ShouldQueueFailureSync: true
            );
        }

        // 進捗の構造情報（キュー数/スレッド/パネル）を反映する。
        private void UpdateThumbnailProgressSnapshotUi(bool requireVisibleSelection = true)
        {
            if (!IsThumbnailProgressUiEnabled())
            {
                return;
            }

            UpdateThumbnailProgressTabVisibilityState();
            if (requireVisibleSelection && !IsThumbnailProgressTabVisibleOrSelectedCached())
            {
                MarkThumbnailProgressUiDirtyWhileHidden();
                return;
            }

            ThumbnailProgressRuntimeSnapshot runtimeSnapshot =
                _thumbnailProgressRuntime.CreateSnapshot();
            ThumbnailProgressWorkerSnapshot rescueWorkerSnapshot =
                ResolveThumbnailProgressRescueWorkerSnapshot(out string rescueWorkerSignature);
            ThumbnailProgressViewState thumbnailProgress = MainVM?.ThumbnailProgress;
            if (thumbnailProgress == null)
            {
                return;
            }

            _ = Interlocked.Exchange(ref _thumbnailProgressSnapshotRefreshRequested, 0);
            int dbPendingCount = MainVM?.PendingMovieRecs?.Count ?? 0;
            int dbTotalCount = MainVM?.MovieRecs?.Count ?? 0;
            int logicalCoreCount = Environment.ProcessorCount;

            // 同一バージョンかつ表示値が同じならUI反映を省略し、負荷時の詰まりを避ける。
            if (
                runtimeSnapshot.Version == _thumbnailProgressLastAppliedSnapshotVersion
                && logicalCoreCount == _thumbnailProgressLastAppliedLogicalCoreCount
                && string.Equals(
                    rescueWorkerSignature,
                    _thumbnailProgressLastAppliedRescueWorkerSignature,
                    StringComparison.Ordinal
                )
            )
            {
                if (requireVisibleSelection)
                {
                    ClearThumbnailProgressUiDirtyWhileHidden();
                }
                return;
            }

            Stopwatch applyStopwatch = Stopwatch.StartNew();
            thumbnailProgress.ApplySnapshot(
                AttachThumbnailProgressRescueWorkerSnapshot(runtimeSnapshot, rescueWorkerSnapshot),
                logicalCoreCount
            );
            applyStopwatch.Stop();

            _thumbnailProgressLastAppliedSnapshotVersion = runtimeSnapshot.Version;
            _thumbnailProgressLastAppliedLogicalCoreCount = logicalCoreCount;
            _thumbnailProgressLastAppliedRescueWorkerSignature = rescueWorkerSignature;
            _thumbnailProgressUiTickAccumulatedMs = 0;
            if (requireVisibleSelection)
            {
                ClearThumbnailProgressUiDirtyWhileHidden();
            }

            ThumbnailProgressUiMetricsLogger.RecordSnapshotApply(
                runtimeSnapshot.Version,
                dbPendingCount,
                dbTotalCount,
                runtimeSnapshot.ActiveWorkers.Count,
                applyStopwatch.Elapsed.TotalMilliseconds
            );
        }

        // rescue exe が書いた FailureDb を読み、右パネル用の1枚へ整形する。
        private ThumbnailProgressWorkerSnapshot ResolveThumbnailProgressRescueWorkerSnapshot(
            out string signature
        )
        {
            signature = "";
            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            if (failureDbService == null)
            {
                return null;
            }

            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                ThumbnailFailureRecord record = failureDbService.GetLatestRescueDisplayRecord(nowUtc);
                if (record == null)
                {
                    return null;
                }

                ThumbnailProgressRescueWorkerExtra extra =
                    ParseThumbnailProgressRescueWorkerExtra(record.ExtraJson);
                ThumbnailProgressWorkerSnapshot snapshot =
                    BuildThumbnailProgressRescueWorkerSnapshot(record, extra, nowUtc);
                signature = BuildThumbnailProgressRescueWorkerSignature(record, snapshot);
                return snapshot;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-progress",
                    $"rescue snapshot read failed: {ex.Message}"
                );
                return null;
            }
        }

        private static ThumbnailProgressRuntimeSnapshot AttachThumbnailProgressRescueWorkerSnapshot(
            ThumbnailProgressRuntimeSnapshot runtimeSnapshot,
            ThumbnailProgressWorkerSnapshot rescueWorkerSnapshot
        )
        {
            if (runtimeSnapshot == null || rescueWorkerSnapshot == null)
            {
                return runtimeSnapshot;
            }

            return new ThumbnailProgressRuntimeSnapshot
            {
                Version = runtimeSnapshot.Version,
                SessionCompletedCount = runtimeSnapshot.SessionCompletedCount,
                SessionTotalCount = runtimeSnapshot.SessionTotalCount,
                TotalCreatedCount = runtimeSnapshot.TotalCreatedCount,
                CurrentParallelism = runtimeSnapshot.CurrentParallelism,
                ConfiguredParallelism = runtimeSnapshot.ConfiguredParallelism,
                EnqueueLogs = runtimeSnapshot.EnqueueLogs,
                ActiveWorkers = runtimeSnapshot.ActiveWorkers,
                RescueWorker = rescueWorkerSnapshot,
            };
        }

        private static ThumbnailProgressWorkerSnapshot BuildThumbnailProgressRescueWorkerSnapshot(
            ThumbnailFailureRecord record,
            ThumbnailProgressRescueWorkerExtra extra,
            DateTime nowUtc
        )
        {
            (string statusText, bool isActive) = ResolveThumbnailProgressRescueWorkerStatus(
                record,
                nowUtc
            );
            string moviePath = ResolveThumbnailProgressRescueMoviePath(record, extra);
            string outputThumbPath = string.Equals(record?.Status, "rescued", StringComparison.Ordinal)
                ? record?.OutputThumbPath ?? ""
                : "";
            string previewImagePath = !string.IsNullOrWhiteSpace(outputThumbPath)
                && File.Exists(outputThumbPath)
                    ? outputThumbPath
                    : "";

            return new ThumbnailProgressWorkerSnapshot
            {
                WorkerLabel = "救済Worker",
                MoviePath = moviePath,
                DisplayMovieName = ResolveThumbnailProgressDisplayMovieName(moviePath),
                PreviewImagePath = previewImagePath,
                PreviewCacheKey = string.IsNullOrWhiteSpace(previewImagePath)
                    ? ""
                    : $"rescue:{record?.FailureId ?? 0}",
                PreviewRevision = record?.UpdatedAtUtc.Ticks ?? 0,
                IsActive = isActive,
                StatusTextOverride = statusText,
                DetailText = BuildThumbnailProgressRescueWorkerDetailText(record, extra, nowUtc),
            };
        }

        private static (string StatusText, bool IsActive) ResolveThumbnailProgressRescueWorkerStatus(
            ThumbnailFailureRecord record,
            DateTime nowUtc
        )
        {
            if (record == null)
            {
                return ("待機", false);
            }

            return record.Status switch
            {
                "processing_rescue" when !IsThumbnailProgressRescueLeaseExpired(record, nowUtc) =>
                    ("救済中", true),
                "processing_rescue" => ("救済待ち", false),
                "pending_rescue" => ("救済待ち", false),
                "rescued" => ("完了", false),
                _ => ("待機", false),
            };
        }

        private static bool IsThumbnailProgressRescueLeaseExpired(
            ThumbnailFailureRecord record,
            DateTime nowUtc
        )
        {
            if (record == null || !string.Equals(record.Status, "processing_rescue", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(record.LeaseUntilUtc))
            {
                return false;
            }

            return DateTime.TryParse(
                    record.LeaseUntilUtc,
                    out DateTime leaseUntilUtc
                )
                && leaseUntilUtc.ToUniversalTime() < nowUtc;
        }

        private static string ResolveThumbnailProgressRescueMoviePath(
            ThumbnailFailureRecord record,
            ThumbnailProgressRescueWorkerExtra extra
        )
        {
            if (!string.IsNullOrWhiteSpace(extra?.SourcePath))
            {
                return extra.SourcePath;
            }

            if (!string.IsNullOrWhiteSpace(record?.SourcePath))
            {
                return record.SourcePath;
            }

            return record?.MoviePath ?? "";
        }

        private static string ResolveThumbnailProgressDisplayMovieName(string moviePath)
        {
            string fileName = Path.GetFileName(moviePath ?? "");
            return string.IsNullOrWhiteSpace(fileName) ? "(不明)" : fileName;
        }

        private static string BuildThumbnailProgressRescueWorkerDetailText(
            ThumbnailFailureRecord record,
            ThumbnailProgressRescueWorkerExtra extra,
            DateTime nowUtc
        )
        {
            List<string> parts = [];
            string launchObservation = BuildThumbnailProgressRescueLaunchObservationText(
                ResolveThumbnailProgressRescuePriority(record, extra),
                ResolveThumbnailProgressRescuePriorityUntilUtc(record, extra),
                extra?.LaunchWaitPolicy ?? "",
                extra?.RequiresIdle,
                nowUtc
            );
            if (!string.IsNullOrWhiteSpace(launchObservation))
            {
                parts.Add(launchObservation);
            }

            if (!string.IsNullOrWhiteSpace(extra?.Phase))
            {
                parts.Add($"段階:{extra.Phase}");
            }

            if (!string.IsNullOrWhiteSpace(extra?.Engine))
            {
                parts.Add($"エンジン:{extra.Engine}");
            }

            if (extra?.RepairApplied == true)
            {
                parts.Add("修復あり");
            }

            string detail = !string.IsNullOrWhiteSpace(extra?.Detail)
                ? extra.Detail
                : !string.IsNullOrWhiteSpace(extra?.FailureReason)
                    ? extra.FailureReason
                    : record?.FailureReason ?? "";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                parts.Add(detail);
            }

            if (parts.Count < 1 && extra?.RequiresIdle == true)
            {
                parts.Add("通常キューが空くまで待機");
            }

            return parts.Count < 1 ? "" : string.Join(" / ", parts);
        }

        // 進捗タブでは、救済要求がどの優先度でどう起動扱いになるかを短く見せる。
        internal static string BuildThumbnailProgressRescueLaunchObservationText(
            ThumbnailQueuePriority priority,
            string priorityUntilUtc,
            string launchWaitPolicy,
            bool? requiresIdle,
            DateTime nowUtc
        )
        {
            bool hasPriorityMetadata =
                ThumbnailQueuePriorityHelper.IsPreferred(priority)
                || !string.IsNullOrWhiteSpace(priorityUntilUtc);
            bool hasLaunchMetadata =
                !string.IsNullOrWhiteSpace(launchWaitPolicy) || requiresIdle.HasValue;
            if (!hasPriorityMetadata && !hasLaunchMetadata)
            {
                return "";
            }

            List<string> parts = [];
            if (hasPriorityMetadata)
            {
                parts.Add(
                    $"優先:{ResolveThumbnailProgressRescuePriorityLabel(priority, priorityUntilUtc, nowUtc)}"
                );
            }

            string launchLabel = ResolveThumbnailProgressRescueLaunchPolicyLabel(
                priority,
                priorityUntilUtc,
                launchWaitPolicy,
                requiresIdle,
                nowUtc
            );
            if (!string.IsNullOrWhiteSpace(launchLabel))
            {
                parts.Add($"開始:{launchLabel}");
            }

            return parts.Count < 1 ? "" : string.Join(" / ", parts);
        }

        private static ThumbnailQueuePriority ResolveThumbnailProgressRescuePriority(
            ThumbnailFailureRecord record,
            ThumbnailProgressRescueWorkerExtra extra
        )
        {
            if (
                string.Equals(extra?.PriorityRaw, "preferred", StringComparison.OrdinalIgnoreCase)
            )
            {
                return ThumbnailQueuePriority.Preferred;
            }

            if (
                string.Equals(extra?.PriorityRaw, "normal", StringComparison.OrdinalIgnoreCase)
            )
            {
                return ThumbnailQueuePriority.Normal;
            }

            return ThumbnailQueuePriorityHelper.Normalize(record?.Priority ?? ThumbnailQueuePriority.Normal);
        }

        private static string ResolveThumbnailProgressRescuePriorityUntilUtc(
            ThumbnailFailureRecord record,
            ThumbnailProgressRescueWorkerExtra extra
        )
        {
            return !string.IsNullOrWhiteSpace(extra?.PriorityUntilUtc)
                ? extra.PriorityUntilUtc
                : record?.PriorityUntilUtc ?? "";
        }

        private static string ResolveThumbnailProgressRescuePriorityLabel(
            ThumbnailQueuePriority priority,
            string priorityUntilUtc,
            DateTime nowUtc
        )
        {
            ThumbnailQueuePriority normalizedPriority = ThumbnailQueuePriorityHelper.Normalize(priority);
            if (!ThumbnailQueuePriorityHelper.IsPreferred(normalizedPriority))
            {
                return "通常";
            }

            if (string.IsNullOrWhiteSpace(priorityUntilUtc))
            {
                return "固定";
            }

            if (
                DateTime.TryParse(priorityUntilUtc, out DateTime parsedPriorityUntilUtc)
                && parsedPriorityUntilUtc.ToUniversalTime() <= nowUtc
            )
            {
                return "期限切れ";
            }

            return "一時";
        }

        private static string ResolveThumbnailProgressRescueLaunchPolicyLabel(
            ThumbnailQueuePriority priority,
            string priorityUntilUtc,
            string launchWaitPolicy,
            bool? requiresIdle,
            DateTime nowUtc
        )
        {
            if (!string.IsNullOrWhiteSpace(launchWaitPolicy))
            {
                return launchWaitPolicy switch
                {
                    "preferred-bypass" => "優先起動",
                    "wait-idle" => "アイドル待ち",
                    "immediate" => "即時",
                    _ => "",
                };
            }

            if (!requiresIdle.HasValue)
            {
                return "";
            }

            bool isPreferredActive =
                ThumbnailQueuePriorityHelper.IsPreferred(priority)
                && !string.Equals(
                    ResolveThumbnailProgressRescuePriorityLabel(priority, priorityUntilUtc, nowUtc),
                    "期限切れ",
                    StringComparison.Ordinal
                );
            if (requiresIdle.Value)
            {
                return isPreferredActive ? "優先起動" : "アイドル待ち";
            }

            return "即時";
        }

        private static string BuildThumbnailProgressRescueWorkerSignature(
            ThumbnailFailureRecord record,
            ThumbnailProgressWorkerSnapshot snapshot
        )
        {
            if (record == null)
            {
                return "";
            }

            return string.Join(
                "|",
                record.FailureId,
                record.Status ?? "",
                record.UpdatedAtUtc.Ticks,
                record.OutputThumbPath ?? "",
                snapshot?.StatusTextOverride ?? "",
                snapshot?.DetailText ?? ""
            );
        }

        private static ThumbnailProgressRescueWorkerExtra ParseThumbnailProgressRescueWorkerExtra(
            string extraJson
        )
        {
            if (string.IsNullOrWhiteSpace(extraJson))
            {
                return ThumbnailProgressRescueWorkerExtra.Empty;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(extraJson);
                JsonElement root = document.RootElement;
                return new ThumbnailProgressRescueWorkerExtra
                {
                    Phase = ReadThumbnailProgressJsonString(root, "CurrentPhase", "Phase", "phase"),
                    Engine = ReadThumbnailProgressJsonString(
                        root,
                        "CurrentEngine",
                        "EngineForced",
                        "Engine"
                    ),
                    SourcePath = ReadThumbnailProgressJsonString(root, "SourcePath"),
                    Detail = ReadThumbnailProgressJsonString(root, "Detail", "detail"),
                    FailureReason = ReadThumbnailProgressJsonString(
                        root,
                        "CurrentFailureReason",
                        "FailureReason",
                        "reason"
                    ),
                    PriorityRaw = ReadThumbnailProgressJsonString(root, "priority", "Priority"),
                    PriorityUntilUtc = ReadThumbnailProgressJsonString(
                        root,
                        "priority_until_utc",
                        "PriorityUntilUtc"
                    ),
                    LaunchWaitPolicy = ReadThumbnailProgressJsonString(
                        root,
                        "launch_wait_policy",
                        "LaunchWaitPolicy"
                    ),
                    RepairApplied = ReadThumbnailProgressJsonBoolean(root, "RepairApplied"),
                    RequiresIdle = ReadThumbnailProgressJsonBooleanNullable(
                        root,
                        "requires_idle",
                        "RequiresIdle"
                    ),
                };
            }
            catch
            {
                return ThumbnailProgressRescueWorkerExtra.Empty;
            }
        }

        private static string ReadThumbnailProgressJsonString(
            JsonElement root,
            params string[] propertyNames
        )
        {
            if (!TryReadThumbnailProgressJsonProperty(root, out JsonElement value, propertyNames))
            {
                return "";
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        }

        private static bool ReadThumbnailProgressJsonBoolean(
            JsonElement root,
            params string[] propertyNames
        )
        {
            if (!TryReadThumbnailProgressJsonProperty(root, out JsonElement value, propertyNames))
            {
                return false;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(value.GetString(), out bool parsed) && parsed,
                _ => false,
            };
        }

        private static bool? ReadThumbnailProgressJsonBooleanNullable(
            JsonElement root,
            params string[] propertyNames
        )
        {
            if (!TryReadThumbnailProgressJsonProperty(root, out JsonElement value, propertyNames))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
                _ => null,
            };
        }

        private static bool TryReadThumbnailProgressJsonProperty(
            JsonElement root,
            out JsonElement value,
            params string[] propertyNames
        )
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    for (int i = 0; i < propertyNames.Length; i++)
                    {
                        if (string.Equals(property.Name, propertyNames[i], StringComparison.OrdinalIgnoreCase))
                        {
                            value = property.Value;
                            return true;
                        }
                    }
                }
            }

            value = default;
            return false;
        }

        private sealed class ThumbnailProgressRescueWorkerExtra
        {
            public static ThumbnailProgressRescueWorkerExtra Empty { get; } =
                new();

            public string Phase { get; init; } = "";
            public string Engine { get; init; } = "";
            public string SourcePath { get; init; } = "";
            public string Detail { get; init; } = "";
            public string FailureReason { get; init; } = "";
            public string PriorityRaw { get; init; } = "";
            public string PriorityUntilUtc { get; init; } = "";
            public string LaunchWaitPolicy { get; init; } = "";
            public bool RepairApplied { get; init; }
            public bool? RequiresIdle { get; init; }
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
                    UpdateThumbnailProgressSnapshotUi(requireVisibleSelection: false);
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
            UpdateThumbnailProgressTabVisibilityState();
            UpdateThumbnailProgressUiTimerState();
        }
    }
}
