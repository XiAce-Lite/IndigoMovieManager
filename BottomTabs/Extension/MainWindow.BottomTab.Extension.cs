using System.Windows;
using IndigoMovieManager.BottomTabs.Extension;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private ExtensionTabPresenter _extensionTabPresenter;

        private void InitializeExtensionTabSupport()
        {
            _extensionTabPresenter ??= new ExtensionTabPresenter(
                exDetail,
                ApplyExtensionDetailCurrentState
            );
            _extensionTabPresenter.Initialize();
        }

        private bool IsExtensionTabVisibleOrSelected()
        {
            return _extensionTabPresenter?.IsVisibleOrSelected() == true;
        }

        // 以前のブランクタブ抑止は外し、救済タブでも通常の詳細表示を許可する。
        private bool ShouldSuppressExtensionDetailForCurrentTab()
        {
            return false;
        }

        private void MarkExtensionTabDirty()
        {
            _extensionTabPresenter?.MarkDirty();
        }

        private void TryFlushExtensionTabIfVisible()
        {
            _extensionTabPresenter?.TryFlushIfVisible();
        }

        private void ApplyExtensionDetailCurrentState()
        {
            _extensionTabPresenter?.ClearDirty();

            if ((MainVM?.DbInfo?.SearchCount ?? 0) == 0)
            {
                ExtensionTabViewHost?.HideRecord();
                return;
            }

            if (ShouldSuppressExtensionDetailForCurrentTab())
            {
                ExtensionTabViewHost?.HideRecord();
                return;
            }

            MovieRecords record = GetSelectedItemByTabIndex();
            if (record != null)
            {
                ShowExtensionDetail(record);
                return;
            }

            ExtensionTabViewHost?.ShowContainer();
        }

        private void RefreshActiveExtensionDetailTab(MovieRecords record)
        {
            if (record == null)
            {
                HideExtensionDetail();
                return;
            }

            ExtensionTabViewHost?.ShowRecord(record);
            ExtensionTabViewHost?.RefreshDetail();
        }

        // 詳細ペインを閉じる時は、表示とDataContextをまとめて落とす。
        private void HideExtensionDetail()
        {
            // 非アクティブ時でも選択解除の見た目は素直に落としておく。
            // ここは軽いUI更新だけなので、省エネ gate では止めない。
            _extensionTabPresenter?.ClearDirty();
            ExtensionTabViewHost?.HideRecord();
        }

        // 選択中レコードを詳細ペインへ流し込む。
        private void ShowExtensionDetail(MovieRecords record)
        {
            if (record == null)
            {
                HideExtensionDetail();
                return;
            }

            if (ShouldSuppressExtensionDetailForCurrentTab())
            {
                HideExtensionDetail();
                return;
            }

            bool canRunActiveWork = IsExtensionTabVisibleOrSelected();
            if (!canRunActiveWork)
            {
                // 選択切替の表示自体は止めず、重い処理だけ後でまとめて流す。
                MarkExtensionTabDirty();
                ExtensionTabViewHost?.ShowRecord(record);
                return;
            }

            EnsureActiveExtensionDetailThumbnail(record);
            RefreshActiveExtensionDetailTab(record);
        }

        // 検索結果件数に応じて、詳細ペインの見せ方だけ先に決める。
        private void UpdateExtensionDetailVisibilityBySearchCount()
        {
            UpdateTagEditorVisibilityBySearchCount();

            if (!IsExtensionTabVisibleOrSelected())
            {
                MarkExtensionTabDirty();
                return;
            }

            if ((MainVM?.DbInfo?.SearchCount ?? 0) == 0)
            {
                HideExtensionDetail();
                return;
            }

            ApplyExtensionDetailCurrentState();
        }

        // 詳細タブが見えている時だけ再描画する。
        internal void RefreshExtensionDetailView()
        {
            if (!IsExtensionTabVisibleOrSelected())
            {
                MarkExtensionTabDirty();
                return;
            }

            ExtensionTabViewHost?.RefreshDetail();
        }

        /// <summary>
        /// 画面の全リストを強制アップデート！詳細情報のDataContextもガッツリ再設定して最新の顔を見せるぜ！✨
        /// </summary>
        private void Refresh()
        {
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                HideTagEditor();
                return;
            }

            ShowExtensionDetail(mv);
            ShowTagEditor(mv);
        }
    }
}
