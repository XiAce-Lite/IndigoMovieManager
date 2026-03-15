using System.Windows;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 詳細ペインを閉じる時は、表示とDataContextをまとめて落とす。
        private void HideExtensionDetail()
        {
            if (viewExtDetail == null)
            {
                return;
            }

            viewExtDetail.DataContext = null;
            viewExtDetail.Visibility = Visibility.Collapsed;
        }

        // 選択中レコードを詳細ペインへ流し込む。
        private void ShowExtensionDetail(MovieRecords record)
        {
            if (record == null)
            {
                HideExtensionDetail();
                return;
            }

            if (viewExtDetail == null)
            {
                return;
            }

            viewExtDetail.DataContext = record;
            viewExtDetail.Visibility = Visibility.Visible;
        }

        // 検索結果件数に応じて、詳細ペインの見せ方だけ先に決める。
        private void UpdateExtensionDetailVisibilityBySearchCount()
        {
            if ((MainVM?.DbInfo?.SearchCount ?? 0) == 0)
            {
                HideExtensionDetail();
                return;
            }

            if (viewExtDetail != null)
            {
                viewExtDetail.Visibility = Visibility.Visible;
            }
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
