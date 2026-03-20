using System;
using System.Windows.Controls;
using AvalonDock.Layout;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string ThumbnailErrorBottomTabContentId = "ToolThumbnailError";
#if DEBUG
        private static readonly bool ShouldShowThumbnailErrorBottomTab = true;
#else
        private static readonly bool ShouldShowThumbnailErrorBottomTab = false;
#endif

        // layout 復元時にこのタブを必須扱いへするかを、構成フラグ基点で判定する。
        internal static bool ShouldRequireThumbnailErrorBottomTabInLayoutRestore(
            string layoutText,
            bool shouldShowThumbnailErrorBottomTab
        )
        {
            if (!shouldShowThumbnailErrorBottomTab)
            {
                return false;
            }

            return !layoutText.Contains(
                $"ContentId=\"{ThumbnailErrorBottomTabContentId}\"",
                StringComparison.OrdinalIgnoreCase
            );
        }

        // release ではレイアウト木から外し、古い layout 復元でも露出が戻らないようにする。
        private void ApplyThumbnailErrorBottomTabVisibility()
        {
            if (ThumbnailErrorBottomTab == null || uxAnchorablePane2 == null)
            {
                return;
            }

            if (!ShouldShowThumbnailErrorBottomTab)
            {
                if (ThumbnailErrorBottomTab.Parent is ILayoutContainer hiddenParent)
                {
                    hiddenParent.RemoveChild(ThumbnailErrorBottomTab);
                }

                return;
            }

            if (
                ThumbnailErrorBottomTab.Parent is ILayoutContainer currentParent
                && !ReferenceEquals(currentParent, uxAnchorablePane2)
            )
            {
                currentParent.RemoveChild(ThumbnailErrorBottomTab);
            }

            if (!uxAnchorablePane2.Children.Contains(ThumbnailErrorBottomTab))
            {
                uxAnchorablePane2.Children.Add(ThumbnailErrorBottomTab);
            }

            ThumbnailErrorBottomTab.Show();
        }

        private bool HasThumbnailErrorBottomTabHost()
        {
            if (!ShouldShowThumbnailErrorBottomTab)
            {
                return false;
            }

            // UI スレッド外では可視判定だけ先に返し、WPF 要素への直接アクセスを避ける。
            if (!Dispatcher.CheckAccess())
            {
                return true;
            }

            return ThumbnailErrorBottomTab != null && ThumbnailErrorBottomTab.Parent != null;
        }

        private DataGrid GetThumbnailErrorDataGrid()
        {
            if (!HasThumbnailErrorBottomTabHost())
            {
                return null;
            }

            return ThumbnailErrorBottomTabViewHost?.ErrorListDataGridControl;
        }
    }
}
