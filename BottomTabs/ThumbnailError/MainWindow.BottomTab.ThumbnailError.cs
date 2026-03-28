using System;
using System.Linq;
using System.Windows.Controls;
using AvalonDock.Layout;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string ThumbnailErrorBottomTabContentId = "ToolThumbnailError";
        private static bool ShouldShowThumbnailErrorBottomTab => EvaluateShowDebugTab();

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
                int removedLayoutCount = 0;
                if (uxDockingManager?.Layout is ILayoutContainer layoutRoot)
                {
                    removedLayoutCount = RemoveLayoutAnchorablesByContentId(
                        layoutRoot,
                        ThumbnailErrorBottomTabContentId
                    );
                }

                ThumbnailErrorBottomTab.IsSelected = false;
                ThumbnailErrorBottomTab.IsActive = false;
                if (ThumbnailErrorBottomTab.Parent is ILayoutContainer hiddenParent)
                {
                    hiddenParent.RemoveChild(ThumbnailErrorBottomTab);
                }
                else
                {
                    ThumbnailErrorBottomTab.Hide();
                }

                if (removedLayoutCount > 0)
                {
                    DebugRuntimeLog.Write(
                        "thumbnail-error-tab-visibility",
                        $"removed thumbnail error tab from layout on hidden: count={removedLayoutCount}"
                    );
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

        private int RemoveLayoutAnchorablesByContentId(
            ILayoutContainer container,
            string targetContentId
        )
        {
            if (container == null || string.IsNullOrWhiteSpace(targetContentId))
            {
                return 0;
            }

            int removedCount = 0;
            foreach (ILayoutElement child in container.Children.ToArray())
            {
                if (child is ILayoutContainer childContainer)
                {
                    removedCount += RemoveLayoutAnchorablesByContentId(
                        childContainer,
                        targetContentId
                    );
                    continue;
                }

                if (
                    child is LayoutAnchorable layoutAnchorable
                    && string.Equals(
                        layoutAnchorable.ContentId,
                        targetContentId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    container.RemoveChild(child);
                    removedCount++;
                }
            }

            return removedCount;
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
