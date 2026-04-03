using System;
using System.ComponentModel;
using System.Threading;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Common;

namespace IndigoMovieManager.BottomTabs.ThumbnailProgress
{
    // 進捗タブの活性監視、dirty 管理、可視時 flush だけを担当する。
    internal sealed class ThumbnailProgressTabPresenter
    {
        private readonly LayoutAnchorable _tabHost;
        private readonly Func<bool> _isUiEnabled;
        private readonly Action _updateTimerState;
        private readonly Func<bool> _hasPendingUiWork;
        private readonly Func<bool> _isRefreshQueued;
        private readonly Action _forceRefreshNow;
        private int _tabVisibleOrSelected;
        private int _uiDirtyWhileHidden;
        private bool _monitoringInitialized;

        public ThumbnailProgressTabPresenter(
            LayoutAnchorable tabHost,
            Func<bool> isUiEnabled,
            Action updateTimerState,
            Func<bool> hasPendingUiWork,
            Func<bool> isRefreshQueued,
            Action forceRefreshNow
        )
        {
            _tabHost = tabHost;
            _isUiEnabled = isUiEnabled;
            _updateTimerState = updateTimerState;
            _hasPendingUiWork = hasPendingUiWork;
            _isRefreshQueued = isRefreshQueued;
            _forceRefreshNow = forceRefreshNow;
        }

        // host の active / selected 変化を拾い、前面へ戻った時だけ未反映分をまとめて流す。
        public void InitializeMonitoring()
        {
            if (_monitoringInitialized || _tabHost == null)
            {
                UpdateVisibilityState();
                _updateTimerState?.Invoke();
                return;
            }

            _tabHost.PropertyChanged += OnTabHostPropertyChanged;
            _monitoringInitialized = true;
            UpdateVisibilityState();
            _updateTimerState?.Invoke();
            TryFlushIfVisible();
        }

        public void UpdateVisibilityState()
        {
            bool isVisible = ThumbnailProgressTabVisibilityGate.IsVisibleOrSelected(_tabHost);
            Interlocked.Exchange(ref _tabVisibleOrSelected, isVisible ? 1 : 0);
        }

        public bool IsVisibleOrSelectedCached()
        {
            return Volatile.Read(ref _tabVisibleOrSelected) == 1;
        }

        public void MarkDirtyWhileHidden()
        {
            Interlocked.Exchange(ref _uiDirtyWhileHidden, 1);
        }

        public void ClearDirtyWhileHidden()
        {
            Interlocked.Exchange(ref _uiDirtyWhileHidden, 0);
        }

        public bool HasDirtyWhileHidden()
        {
            return Volatile.Read(ref _uiDirtyWhileHidden) == 1;
        }

        public void TryFlushIfVisible()
        {
            if (!_isUiEnabled())
            {
                return;
            }

            if (!IsVisibleOrSelectedCached())
            {
                return;
            }

            if (!(HasDirtyWhileHidden() || _hasPendingUiWork()))
            {
                return;
            }

            if (_isRefreshQueued())
            {
                return;
            }

            _forceRefreshNow?.Invoke();
        }

        private void OnTabHostPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!BottomTabActivationGate.ShouldReactToProperty(e?.PropertyName ?? ""))
            {
                return;
            }

            UpdateVisibilityState();
            _updateTimerState?.Invoke();
            TryFlushIfVisible();
        }
    }
}
