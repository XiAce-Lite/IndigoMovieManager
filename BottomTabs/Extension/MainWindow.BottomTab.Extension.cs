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

        // サムネ失敗タブでは、下部の詳細ペインへレコードを流さない。
        private bool ShouldSuppressExtensionDetailForCurrentTab()
        {
            return Tabs?.SelectedIndex == ThumbnailErrorTabIndex;
        }

        private void MarkExtensionTabDirty()
        {
            _extensionTabDirty = true;
        }

        private void TryFlushExtensionTabIfVisible()
        {
            if (!_extensionTabDirty || !IsExtensionTabVisibleOrSelected())
            {
                return;
            }

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
            if (!IsExtensionTabVisibleOrSelected())
            {
                MarkExtensionTabDirty();
                return;
            }

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

            if (!IsExtensionTabVisibleOrSelected())
            {
                MarkExtensionTabDirty();
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
