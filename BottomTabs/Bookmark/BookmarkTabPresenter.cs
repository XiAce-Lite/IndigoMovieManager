using System;
using System.ComponentModel;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Common;

namespace IndigoMovieManager.BottomTabs.Bookmark
{
    // Bookmark タブの活性監視と遅延 flush だけを担当し、読込本体は MainWindow 側へ残す。
    internal sealed class BookmarkTabPresenter
    {
        private readonly LayoutAnchorable _tabHost;
        private readonly BookmarkTabView _view;
        private readonly Action _reloadCore;
        private bool _monitoringInitialized;
        private bool _isDirty;

        public BookmarkTabPresenter(LayoutAnchorable tabHost, BookmarkTabView view, Action reloadCore)
        {
            _tabHost = tabHost;
            _view = view;
            _reloadCore = reloadCore;
        }

        // host の表示変化を拾い、前面へ戻った時だけ未反映分をまとめて流す。
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
            return BottomTabActivationGate.IsVisibleOrSelected(_tabHost);
        }

        public void MarkDirty()
        {
            _isDirty = true;
        }

        public void TryFlushIfVisible()
        {
            if (!_isDirty || !IsVisibleOrSelected())
            {
                return;
            }

            _reloadCore?.Invoke();
        }

        public void ReloadOrMarkDirty()
        {
            if (!IsVisibleOrSelected())
            {
                MarkDirty();
                return;
            }

            _reloadCore?.Invoke();
        }

        public void RefreshView()
        {
            _view?.RefreshItems();
        }

        public void OnReloadCompleted()
        {
            _isDirty = false;
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
