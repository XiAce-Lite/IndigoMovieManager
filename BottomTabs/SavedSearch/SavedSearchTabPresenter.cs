using System.ComponentModel;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Common;

namespace IndigoMovieManager.BottomTabs.SavedSearch
{
    // 保存済み検索条件タブの表示文言と遅延反映だけを担当する薄い presenter。
    internal sealed class SavedSearchTabPresenter
    {
        private const string PreparingMessage = "保存済み検索条件は準備中です。";

        private readonly LayoutAnchorable _tabHost;
        private readonly SavedSearchTabView _view;
        private bool _monitoringInitialized;
        private bool _isDirty;
        private string _pendingMessage = PreparingMessage;

        public SavedSearchTabPresenter(LayoutAnchorable tabHost, SavedSearchTabView view)
        {
            _tabHost = tabHost;
            _view = view;
        }

        // host の可視状態変化だけを拾い、表示可能時に未反映文言を流し込む。
        public void Initialize()
        {
            if (!_monitoringInitialized && _tabHost != null)
            {
                _tabHost.PropertyChanged += OnTabHostPropertyChanged;
                _monitoringInitialized = true;
            }

            ApplyPlaceholderText();
            TryFlushIfVisible();
        }

        // 後で一覧や状態表示へ広げても、文言更新の入口は presenter で固定する。
        public void ApplyPlaceholderText(string message = null)
        {
            _pendingMessage = string.IsNullOrWhiteSpace(message) ? PreparingMessage : message;

            if (_view == null)
            {
                return;
            }

            if (!IsVisibleOrSelected())
            {
                MarkDirty();
                return;
            }

            _isDirty = false;
            _view.SetPlaceholderText(_pendingMessage);
        }

        private void OnTabHostPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!BottomTabActivationGate.ShouldReactToProperty(e?.PropertyName ?? ""))
            {
                return;
            }

            TryFlushIfVisible();
        }

        private bool IsVisibleOrSelected()
        {
            return BottomTabActivationGate.IsVisibleOrSelected(_tabHost);
        }

        private void MarkDirty()
        {
            _isDirty = true;
        }

        private void TryFlushIfVisible()
        {
            if (!_isDirty || !IsVisibleOrSelected() || _view == null)
            {
                return;
            }

            _isDirty = false;
            _view.SetPlaceholderText(_pendingMessage);
        }
    }
}
