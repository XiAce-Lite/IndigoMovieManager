using System;
using System.ComponentModel;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Common;

namespace IndigoMovieManager.BottomTabs.Extension
{
    // 詳細タブの活性監視と dirty 管理だけを担当し、詳細描画本体は MainWindow 側へ残す。
    internal sealed class ExtensionTabPresenter
    {
        private readonly LayoutAnchorable _tabHost;
        private readonly Action _flushCurrentState;
        private bool _monitoringInitialized;
        private bool _isDirty;

        public ExtensionTabPresenter(LayoutAnchorable tabHost, Action flushCurrentState)
        {
            _tabHost = tabHost;
            _flushCurrentState = flushCurrentState;
        }

        // host の active / selected 変化を拾い、前面へ戻った瞬間だけ未反映分を流す。
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

            // 詳細サムネ生成は前面で見ている時だけ許可し、表示されているだけでは動かさない。
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

        public bool IsDirty()
        {
            return _isDirty;
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
