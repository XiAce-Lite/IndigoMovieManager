using IndigoMovieManager.BottomTabs.SavedSearch;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private SavedSearchTabPresenter _savedSearchTabPresenter;

        // SavedSearch 固有の状態管理は presenter へ寄せ、MainWindow 側は接続だけに絞る。
        private void InitializeSavedSearchTabSupport()
        {
            if (_savedSearchTabPresenter == null && TagBar != null && SavedSearchTabViewHost != null)
            {
                _savedSearchTabPresenter = new SavedSearchTabPresenter(
                    TagBar,
                    SavedSearchTabViewHost,
                    getDbFullPath: () => MainVM?.DbInfo?.DBFullPath ?? "",
                    executeSearchAsync: ExecuteExternalSkinSearchAsync
                );
            }

            _savedSearchTabPresenter?.Initialize();
        }

        private void ReloadSavedSearchItems()
        {
            _savedSearchTabPresenter?.ReloadItems();
        }

        private void ApplySavedSearchPlaceholderText(string message = null)
        {
            _savedSearchTabPresenter?.ApplyPlaceholderText(message);
        }
    }
}
