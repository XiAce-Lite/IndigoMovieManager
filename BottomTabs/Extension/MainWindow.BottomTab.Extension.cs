using System.ComponentModel;
using System.Windows;
using IndigoMovieManager.BottomTabs.Common;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private bool _extensionTabMonitoringInitialized;
        private bool _extensionTabDirty;

        private void InitializeExtensionTabSupport()
        {
            if (_extensionTabMonitoringInitialized || exDetail == null)
            {
                return;
            }

            exDetail.PropertyChanged += ExtensionTab_PropertyChanged;
            _extensionTabMonitoringInitialized = true;
            TryFlushExtensionTabIfVisible();
        }

        private void ExtensionTab_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!BottomTabActivationGate.ShouldReactToProperty(e?.PropertyName ?? ""))
            {
                return;
            }

            TryFlushExtensionTabIfVisible();
        }

        private bool IsExtensionTabVisibleOrSelected()
        {
            if (exDetail == null || exDetail.IsHidden)
            {
                return false;
            }

            // 詳細サムネ生成は前面で見ている時だけ許可し、表示されているだけでは動かさない。
            return exDetail.IsSelected || exDetail.IsActive;
        }

        // 以前のブランクタブ抑止は外し、救済タブでも通常の詳細表示を許可する。
        private bool ShouldSuppressExtensionDetailForCurrentTab()
        {
            return false;
        }

        private void MarkExtensionTabDirty()
        {
            _extensionTabDirty = true;
        }

        private void TryFlushExtensionTabIfVisible()
        {
            if (!IsExtensionTabVisibleOrSelected())
            {
                return;
            }

            if (_extensionTabDirty)
            {
                // 非表示中に溜めた変更は、前面へ戻った瞬間にまとめて反映する。
                ApplyExtensionDetailCurrentState();
                return;
            }

            // 詳細タブが前面に来た時は、dirty が無くても現在選択から表示状態を組み直す。
            ApplyExtensionDetailCurrentState();
        }

        private void ApplyExtensionDetailCurrentState()
        {
            _extensionTabDirty = false;

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

        // 詳細ペインを閉じる時は、表示とDataContextをまとめて落とす。
        private void HideExtensionDetail()
        {
            // 非アクティブ時でも選択解除の見た目は素直に落としておく。
            // ここは軽いUI更新だけなので、省エネ gate では止めない。
            _extensionTabDirty = false;
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

            PrepareExtensionDetailThumbnail(record, enqueueIfMissing: true);
            TryAutoRescueExtensionDetailThumbnail(record);
            ExtensionTabViewHost?.ShowRecord(record);
        }

        // 検索結果件数に応じて、詳細ペインの見せ方だけ先に決める。
        private void UpdateExtensionDetailVisibilityBySearchCount()
        {
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
                return;
            }

            ShowExtensionDetail(mv);
        }
    }
}
