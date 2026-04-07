using System;
using System.Linq;
using System.Windows.Controls;
using AvalonDock.Layout;

namespace IndigoMovieManager.BottomTabs.ThumbnailError
{
    // サムネ失敗タブの host 出し入れとレイアウト木調整だけを担当する。
    internal sealed class ThumbnailErrorBottomTabHostController
    {
        private readonly LayoutAnchorable _tabHost;
        private readonly LayoutAnchorablePane _targetPane;
        private readonly Func<ILayoutContainer> _getLayoutRoot;
        private readonly Func<bool> _shouldShow;
        private readonly Func<bool> _hasDispatcherAccess;
        private readonly Func<DataGrid> _getDataGrid;
        private readonly Action<string> _writeLog;
        private readonly string _contentId;

        public ThumbnailErrorBottomTabHostController(
            LayoutAnchorable tabHost,
            LayoutAnchorablePane targetPane,
            Func<ILayoutContainer> getLayoutRoot,
            Func<bool> shouldShow,
            Func<bool> hasDispatcherAccess,
            Func<DataGrid> getDataGrid,
            Action<string> writeLog,
            string contentId
        )
        {
            _tabHost = tabHost;
            _targetPane = targetPane;
            _getLayoutRoot = getLayoutRoot;
            _shouldShow = shouldShow;
            _hasDispatcherAccess = hasDispatcherAccess;
            _getDataGrid = getDataGrid;
            _writeLog = writeLog;
            _contentId = contentId;
        }

        public void ApplyVisibility()
        {
            if (_tabHost == null || _targetPane == null)
            {
                return;
            }

            if (!_shouldShow())
            {
                int removedLayoutCount = 0;
                if (_getLayoutRoot() is ILayoutContainer layoutRoot)
                {
                    removedLayoutCount = RemoveLayoutAnchorablesByContentId(
                        layoutRoot,
                        _contentId
                    );
                }

                _tabHost.IsSelected = false;
                _tabHost.IsActive = false;
                if (_tabHost.Parent is ILayoutContainer hiddenParent)
                {
                    hiddenParent.RemoveChild(_tabHost);
                }
                else
                {
                    _tabHost.Hide();
                }

                if (removedLayoutCount > 0)
                {
                    _writeLog?.Invoke(
                        $"removed thumbnail error tab from layout on hidden: count={removedLayoutCount}"
                    );
                }

                return;
            }

            if (_tabHost.Parent is ILayoutContainer currentParent && !ReferenceEquals(currentParent, _targetPane))
            {
                currentParent.RemoveChild(_tabHost);
            }

            if (!_targetPane.Children.Contains(_tabHost))
            {
                _targetPane.Children.Add(_tabHost);
            }

            _tabHost.Show();
        }

        public bool HasHost()
        {
            if (!_shouldShow())
            {
                return false;
            }

            // UI スレッド外では可視判定だけ先に返し、WPF 要素への直接アクセスを避ける。
            if (!_hasDispatcherAccess())
            {
                return true;
            }

            return _tabHost != null && _tabHost.Parent != null;
        }

        public DataGrid GetDataGrid()
        {
            if (!HasHost())
            {
                return null;
            }

            return _getDataGrid?.Invoke();
        }

        private static int RemoveLayoutAnchorablesByContentId(
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
    }
}
