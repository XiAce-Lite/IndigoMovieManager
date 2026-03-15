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
        private int _thumbnailErrorTabVisibleOrSelected;
        private bool _thumbnailErrorTabMonitoringInitialized;

        // サムネ失敗タブは、見えている間だけ軽いポーリングで進行状況を追う。
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
            _thumbnailErrorUiTimer.Start();
        }

        private void InitializeThumbnailErrorTabVisibilityMonitoring()
        {
            if (_thumbnailErrorTabMonitoringInitialized || ThumbnailErrorBottomTab == null)
            {
                UpdateThumbnailErrorTabVisibilityState();
                return;
            }

            ThumbnailErrorBottomTab.PropertyChanged += ThumbnailErrorBottomTab_PropertyChanged;
            _thumbnailErrorTabMonitoringInitialized = true;
            UpdateThumbnailErrorTabVisibilityState();
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
            if (IsThumbnailErrorTabVisibleOrSelectedCached())
            {
                RequestThumbnailErrorSnapshotRefresh();
            }
        }

        private void UpdateThumbnailErrorTabVisibilityState()
        {
            bool isVisible = BottomTabActivationGate.IsVisibleOrSelected(ThumbnailErrorBottomTab);
            Interlocked.Exchange(ref _thumbnailErrorTabVisibleOrSelected, isVisible ? 1 : 0);
        }

        private bool IsThumbnailErrorTabVisibleOrSelectedCached()
        {
            return Volatile.Read(ref _thumbnailErrorTabVisibleOrSelected) == 1;
        }

        // 他スレッドからの更新要求はここへ束ね、UI再構築の連打を避ける。
        private void RequestThumbnailErrorSnapshotRefresh()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            Interlocked.Exchange(ref _thumbnailErrorRefreshRequested, 1);
            if (!IsThumbnailErrorTabVisibleOrSelectedCached())
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
            if (!IsThumbnailErrorTabVisibleOrSelectedCached())
            {
                return;
            }

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
