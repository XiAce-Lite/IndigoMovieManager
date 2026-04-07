using System;
using System.Windows.Controls;
using IndigoMovieManager.BottomTabs.ThumbnailError;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string ThumbnailErrorBottomTabContentId = "ToolThumbnailError";
        private static bool ShouldShowThumbnailErrorBottomTab => EvaluateShowDebugTab();
        private ThumbnailErrorBottomTabHostController _thumbnailErrorBottomTabHostController;

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
            _thumbnailErrorBottomTabHostController ??= new ThumbnailErrorBottomTabHostController(
                ThumbnailErrorBottomTab,
                uxAnchorablePane2,
                () => uxDockingManager?.Layout,
                () => ShouldShowThumbnailErrorBottomTab,
                () => Dispatcher.CheckAccess(),
                () => ThumbnailErrorBottomTabViewHost?.ErrorListDataGridControl,
                message => DebugRuntimeLog.Write("thumbnail-error-tab-visibility", message),
                ThumbnailErrorBottomTabContentId
            );
            _thumbnailErrorBottomTabHostController.ApplyVisibility();
        }

        private bool HasThumbnailErrorBottomTabHost()
        {
            return _thumbnailErrorBottomTabHostController?.HasHost() == true;
        }

        private DataGrid GetThumbnailErrorDataGrid()
        {
            return _thumbnailErrorBottomTabHostController?.GetDataGrid();
        }
    }
}
