using System;
using System.ComponentModel;
using System.Windows.Threading;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Common;

namespace IndigoMovieManager.BottomTabs.Debug
{
    // Debug タブの活性監視とタイマー制御だけを担当し、重い操作本体は MainWindow 側へ残す。
    internal sealed class DebugTabPresenter
    {
        private readonly LayoutAnchorable _tabHost;
        private readonly DispatcherTimer _refreshTimer;
        private readonly Func<bool> _shouldShowDebugTab;
        private readonly Action<bool> _updateRefreshState;
        private readonly Action<bool> _updateTimerState;
        private bool _monitoringInitialized;
        private bool _wasActive;

        public DebugTabPresenter(
            LayoutAnchorable tabHost,
            DispatcherTimer refreshTimer,
            Func<bool> shouldShowDebugTab,
            Action<bool> updateRefreshState,
            Action<bool> updateTimerState
        )
        {
            _tabHost = tabHost;
            _refreshTimer = refreshTimer;
            _shouldShowDebugTab = shouldShowDebugTab;
            _updateRefreshState = updateRefreshState;
            _updateTimerState = updateTimerState;
        }

        public bool WasActive => _wasActive;

        // host の active / selected 変化を拾い、Debug タブ前面時の更新契機をまとめる。
        public void Initialize()
        {
            if (!_shouldShowDebugTab() || _tabHost == null || _monitoringInitialized)
            {
                return;
            }

            _tabHost.PropertyChanged += OnTabHostPropertyChanged;
            _monitoringInitialized = true;
            _updateRefreshState(true);
        }

        public bool IsActive()
        {
            if (_tabHost == null || _tabHost.IsHidden)
            {
                return false;
            }

            return _tabHost.IsSelected || _tabHost.IsActive;
        }

        public void HandleTimerTick(Action refreshLogPreview)
        {
            if (!IsActive())
            {
                _updateTimerState(false);
                _wasActive = false;
                return;
            }

            refreshLogPreview?.Invoke();
        }

        public void RecordRefreshState(bool isActive)
        {
            _wasActive = isActive;
        }

        private void OnTabHostPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!BottomTabActivationGate.ShouldReactToProperty(e?.PropertyName ?? ""))
            {
                return;
            }

            _updateRefreshState(false);
        }
    }
}
