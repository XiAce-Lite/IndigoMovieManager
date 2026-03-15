using System.ComponentModel;
using IndigoMovieManager.BottomTabs.Common;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string SavedSearchPreparingMessage = "保存済み検索条件は準備中です。";
        private bool _savedSearchTabMonitoringInitialized;
        private bool _savedSearchTabDirty;
        private string _savedSearchPendingMessage = SavedSearchPreparingMessage;

        // 保存済み検索条件タブは、まず表示文言と遅延反映の責務だけをここへ寄せる。
        private void InitializeSavedSearchTabSupport()
        {
            if (!_savedSearchTabMonitoringInitialized && TagBar != null)
            {
                TagBar.PropertyChanged += SavedSearchTab_PropertyChanged;
                _savedSearchTabMonitoringInitialized = true;
            }

            ApplySavedSearchPlaceholderText();
            TryFlushSavedSearchTabIfVisible();
        }

        private void SavedSearchTab_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!BottomTabActivationGate.ShouldReactToProperty(e?.PropertyName ?? ""))
            {
                return;
            }

            TryFlushSavedSearchTabIfVisible();
        }

        private bool IsSavedSearchTabVisibleOrSelected()
        {
            return BottomTabActivationGate.IsVisibleOrSelected(TagBar);
        }

        private void MarkSavedSearchTabDirty()
        {
            _savedSearchTabDirty = true;
        }

        private void TryFlushSavedSearchTabIfVisible()
        {
            if (!_savedSearchTabDirty || !IsSavedSearchTabVisibleOrSelected())
            {
                return;
            }

            _savedSearchTabDirty = false;
            SavedSearchTabViewHost?.SetPlaceholderText(_savedSearchPendingMessage);
        }

        // 後で一覧や状態表示へ広げても、文言更新の入口はここで固定する。
        private void ApplySavedSearchPlaceholderText(string message = null)
        {
            _savedSearchPendingMessage = string.IsNullOrWhiteSpace(message)
                ? SavedSearchPreparingMessage
                : message;

            if (SavedSearchTabViewHost == null)
            {
                return;
            }

            if (!IsSavedSearchTabVisibleOrSelected())
            {
                MarkSavedSearchTabDirty();
                return;
            }

            _savedSearchTabDirty = false;
            SavedSearchTabViewHost.SetPlaceholderText(_savedSearchPendingMessage);
        }
    }
}
