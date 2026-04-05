using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Threading;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Common;

namespace IndigoMovieManager.BottomTabs.ThumbnailError
{
    // サムネ失敗タブの活性監視、active cache、タイマー制御だけを担当する。
    internal sealed class ThumbnailErrorTabPresenter
    {
        private readonly Func<bool> _hasHost;
        private readonly LayoutAnchorable _tabHost;
        private readonly DispatcherTimer _timer;
        private readonly Action _requestSnapshotRefresh;
        private readonly Action _updateTimerState;
        private readonly Action _clearInactiveState;
        private int _tabActive;
        private bool _monitoringInitialized;

        public ThumbnailErrorTabPresenter(
            Func<bool> hasHost,
            LayoutAnchorable tabHost,
            DispatcherTimer timer,
            Action requestSnapshotRefresh,
            Action updateTimerState,
            Action clearInactiveState
        )
        {
            _hasHost = hasHost;
            _tabHost = tabHost;
            _timer = timer;
            _requestSnapshotRefresh = requestSnapshotRefresh;
            _updateTimerState = updateTimerState;
            _clearInactiveState = clearInactiveState;
        }

        public void InitializeMonitoring()
        {
            if (!_hasHost() || _monitoringInitialized || _tabHost == null)
            {
                UpdateActiveState();
                _updateTimerState?.Invoke();
                return;
            }

            _tabHost.PropertyChanged += OnTabHostPropertyChanged;
            _monitoringInitialized = true;
            UpdateActiveState();
            _updateTimerState?.Invoke();
        }

        public void UpdateActiveState()
        {
            bool isActive =
                _hasHost()
                && _tabHost != null
                && !_tabHost.IsHidden
                && (_tabHost.IsSelected || _tabHost.IsActive);

            Interlocked.Exchange(ref _tabActive, isActive ? 1 : 0);
            if (!isActive)
            {
                _clearInactiveState?.Invoke();
            }
        }

        public bool IsActiveCached()
        {
            return Volatile.Read(ref _tabActive) == 1;
        }

        public void HandleTimerTick(Action onInactive, Action onPoll)
        {
            if (!_hasHost() || !IsActiveCached())
            {
                onInactive?.Invoke();
                return;
            }

            onPoll?.Invoke();
        }

        private void OnTabHostPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!BottomTabActivationGate.ShouldReactToProperty(e?.PropertyName ?? ""))
            {
                return;
            }

            UpdateActiveState();
            _updateTimerState?.Invoke();
            if (IsActiveCached())
            {
                _requestSnapshotRefresh?.Invoke();
            }
        }
    }
}
