using System;
using System.ComponentModel;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Common;

namespace IndigoMovieManager.BottomTabs.TagEditor
{
    // タグタブの活性監視と dirty 管理だけを担当し、描画本体は MainWindow と View へ残す。
    internal sealed class TagEditorTabPresenter
    {
        private readonly LayoutAnchorable _tabHost;
        private readonly Action _flushCurrentState;
        private bool _monitoringInitialized;
        private bool _isDirty;

        public TagEditorTabPresenter(LayoutAnchorable tabHost, Action flushCurrentState)
        {
            _tabHost = tabHost;
            _flushCurrentState = flushCurrentState;
        }

        public void Initialize()
        {
            if (_monitoringInitialized || _tabHost == null)
            {
                return;
            }

            _tabHost.PropertyChanged += OnTabHostPropertyChanged;
            _monitoringInitialized = true;
            TryFlushIfVisible();
        }

        public bool IsVisibleOrSelected()
        {
            if (_tabHost == null || _tabHost.IsHidden)
            {
                return false;
            }

            return _tabHost.IsSelected || _tabHost.IsActive;
        }

        public void MarkDirty()
        {
            _isDirty = true;
        }

        public void ClearDirty()
        {
            _isDirty = false;
        }

        public void TryFlushIfVisible()
        {
            if (!IsVisibleOrSelected())
            {
                return;
            }

            _flushCurrentState?.Invoke();
        }

        private void OnTabHostPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!BottomTabActivationGate.ShouldReactToProperty(e?.PropertyName ?? ""))
            {
                return;
            }

            TryFlushIfVisible();
        }
    }
}
