using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using IndigoMovieManager.BottomTabs.Common;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int ThumbnailErrorUiIntervalMs = 1000;

        private DispatcherTimer _thumbnailErrorUiTimer;
        private int _thumbnailErrorRefreshQueued;
        private int _thumbnailErrorRefreshRequested;
        private int _thumbnailErrorUiDirtyWhileHidden;
        private int _thumbnailErrorTabActive;
        private bool _thumbnailErrorTabMonitoringInitialized;
        private IReadOnlyList<string> _thumbnailErrorPreferredViewportKeysSnapshot =
            Array.Empty<string>();
        private DateTime _thumbnailErrorViewportPriorityLastUtc = DateTime.MinValue;

        // サムネ失敗タブは、前面の間だけ軽いポーリングで進行状況を追う。
        private void InitializeThumbnailErrorUiSupport()
        {
            InitializeThumbnailErrorTabVisibilityMonitoring();
            InitializeThumbnailErrorUiTimer();
        }

        private void InitializeThumbnailErrorUiTimer()
        {
            if (_thumbnailErrorUiTimer != null)
            {
                return;
            }

            _thumbnailErrorUiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ThumbnailErrorUiIntervalMs),
            };
            _thumbnailErrorUiTimer.Tick += ThumbnailErrorUiTimer_Tick;
            UpdateThumbnailErrorUiTimerState();
        }

        private void InitializeThumbnailErrorTabVisibilityMonitoring()
        {
            if (_thumbnailErrorTabMonitoringInitialized || ThumbnailErrorBottomTab == null)
            {
                UpdateThumbnailErrorTabVisibilityState();
                UpdateThumbnailErrorUiTimerState();
                return;
            }

            ThumbnailErrorBottomTab.PropertyChanged += ThumbnailErrorBottomTab_PropertyChanged;
            _thumbnailErrorTabMonitoringInitialized = true;
            UpdateThumbnailErrorTabVisibilityState();
            UpdateThumbnailErrorUiTimerState();
        }

        private void ThumbnailErrorBottomTab_PropertyChanged(
            object sender,
            PropertyChangedEventArgs e
        )
        {
            if (!BottomTabActivationGate.ShouldReactToProperty(e?.PropertyName ?? ""))
            {
                return;
            }

            UpdateThumbnailErrorTabVisibilityState();
            UpdateThumbnailErrorUiTimerState();
            if (IsThumbnailErrorTabActiveCached())
            {
                RequestThumbnailErrorSnapshotRefresh();
            }
        }

        private void UpdateThumbnailErrorTabVisibilityState()
        {
            bool isActive =
                ThumbnailErrorBottomTab != null
                && !ThumbnailErrorBottomTab.IsHidden
                && (ThumbnailErrorBottomTab.IsSelected || ThumbnailErrorBottomTab.IsActive);
            Interlocked.Exchange(ref _thumbnailErrorTabActive, isActive ? 1 : 0);
            if (!isActive)
            {
                _thumbnailErrorPreferredViewportKeysSnapshot = Array.Empty<string>();
                _thumbnailErrorViewportPriorityLastUtc = DateTime.MinValue;
            }
        }

        private bool IsThumbnailErrorTabActiveCached()
        {
            return Volatile.Read(ref _thumbnailErrorTabActive) == 1;
        }

        // 既存 partial からの呼び出し名は残し、意味だけ「アクティブ時」に寄せる。
        private bool IsThumbnailErrorTabVisibleOrSelectedCached()
        {
            return IsThumbnailErrorTabActiveCached();
        }

        private void UpdateThumbnailErrorUiTimerState()
        {
            if (_thumbnailErrorUiTimer == null)
            {
                return;
            }

            if (IsThumbnailErrorTabActiveCached())
            {
                if (!_thumbnailErrorUiTimer.IsEnabled)
                {
                    _thumbnailErrorUiTimer.Start();
                }

                return;
            }

            if (_thumbnailErrorUiTimer.IsEnabled)
            {
                _thumbnailErrorUiTimer.Stop();
            }
        }

        // 他スレッドからの更新要求はここへ束ね、UI再構築の連打を避ける。
        private void RequestThumbnailErrorSnapshotRefresh()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            Interlocked.Exchange(ref _thumbnailErrorRefreshRequested, 1);
            Interlocked.Exchange(ref _thumbnailErrorRecordsDirty, 1);
            if (!IsThumbnailErrorTabActiveCached())
            {
                Interlocked.Exchange(ref _thumbnailErrorUiDirtyWhileHidden, 1);
                return;
            }

            QueueThumbnailErrorSnapshotRefresh();
        }

        private void QueueThumbnailErrorSnapshotRefresh()
        {
            if (Interlocked.Exchange(ref _thumbnailErrorRefreshQueued, 1) == 1)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(ProcessThumbnailErrorSnapshotRefreshQueue)
            );
        }

        private void ProcessThumbnailErrorSnapshotRefreshQueue()
        {
            try
            {
                if (Interlocked.Exchange(ref _thumbnailErrorRefreshRequested, 0) == 1)
                {
                    RefreshThumbnailErrorRecords();
                    Interlocked.Exchange(ref _thumbnailErrorUiDirtyWhileHidden, 0);
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-error-tab",
                    $"error tab refresh failed: {ex.Message}"
                );
            }
            finally
            {
                Interlocked.Exchange(ref _thumbnailErrorRefreshQueued, 0);
                if (Interlocked.CompareExchange(ref _thumbnailErrorRefreshRequested, 0, 0) == 1)
                {
                    QueueThumbnailErrorSnapshotRefresh();
                }
            }
        }

        // 見えている間だけ 1 秒周期で再読込し、待機中→救済中→反映待ちを追えるようにする。
        private void ThumbnailErrorUiTimer_Tick(object sender, EventArgs e)
        {
            if (!IsThumbnailErrorTabActiveCached())
            {
                UpdateThumbnailErrorUiTimerState();
                Interlocked.Exchange(ref _thumbnailErrorUiDirtyWhileHidden, 1);
                return;
            }

            TryPromoteVisibleThumbnailErrorRecords();

            if (!ShouldPollThumbnailErrorProgress())
            {
                return;
            }

            RequestThumbnailErrorSnapshotRefresh();
        }

        private bool ShouldPollThumbnailErrorProgress()
        {
            if (
                MainVM?.ThumbnailErrorRecs?.Any(x => x != null && x.ProgressSummaryKey != "unqueued")
                == true
            )
            {
                return true;
            }

            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            return failureDbService != null && failureDbService.HasPendingRescueWork(DateTime.UtcNow);
        }
    }
}
