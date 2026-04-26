using System;
using System.ComponentModel;
using System.Windows.Threading;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Common;

namespace IndigoMovieManager.BottomTabs.Log
{
    // Log タブの活性監視とタイマー制御だけを担当し、重い読込本体は MainWindow 側へ残す。
    internal sealed class LogTabPresenter
    {
        private readonly LayoutAnchorable _tabHost;
        private readonly DispatcherTimer _refreshTimer;
        private readonly Func<bool> _shouldShowLogTab;
        private readonly Action<bool> _updateRefreshState;
        private readonly Action<bool> _updateTimerState;
        private bool _monitoringInitialized;
        private bool _wasActive;

        public LogTabPresenter(
            LayoutAnchorable tabHost,
            DispatcherTimer refreshTimer,
            Func<bool> shouldShowLogTab,
            Action<bool> updateRefreshState,
            Action<bool> updateTimerState
        )
        {
            _tabHost = tabHost;
            _refreshTimer = refreshTimer;
            _shouldShowLogTab = shouldShowLogTab;
            _updateRefreshState = updateRefreshState;
            _updateTimerState = updateTimerState;
        }

        public bool WasActive => _wasActive;

        // host の active / selected 変化を拾い、Log タブ前面時の更新契機をまとめる。
        public void Initialize()
        {
            if (!_shouldShowLogTab() || _tabHost == null || _monitoringInitialized)
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

        public void HandleTimerTick(Action refreshPreview)
        {
            if (!IsActive())
            {
                _updateTimerState(false);
                _wasActive = false;
                return;
            }

            refreshPreview?.Invoke();
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
