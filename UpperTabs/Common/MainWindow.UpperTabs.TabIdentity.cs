using System.Windows.Controls;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int UpperTabSmallFixedIndex = 0;
        private const int UpperTabBigFixedIndex = 1;
        private const int UpperTabGridFixedIndex = 2;
        private const int UpperTabListFixedIndex = 3;
        private const int UpperTabBig10FixedIndex = 4;

        // 表示順と内部タブIDを切り離し、Queue/設定/ログの互換を守る。
        private int GetCurrentUpperTabFixedIndex()
        {
            if (Tabs?.SelectedItem == null)
            {
                return -1;
            }

            if (ReferenceEquals(Tabs.SelectedItem, TabSmall))
            {
                return UpperTabSmallFixedIndex;
            }

            if (ReferenceEquals(Tabs.SelectedItem, TabBig))
            {
                return UpperTabBigFixedIndex;
            }

            if (ReferenceEquals(Tabs.SelectedItem, TabGrid))
            {
                return UpperTabGridFixedIndex;
            }

            if (ReferenceEquals(Tabs.SelectedItem, TabList))
            {
                return UpperTabListFixedIndex;
            }

            if (ReferenceEquals(Tabs.SelectedItem, TabBig10))
            {
                return UpperTabBig10FixedIndex;
            }

            if (ReferenceEquals(Tabs.SelectedItem, TabThumbnailError))
            {
                return ThumbnailErrorTabIndex;
            }

            if (ReferenceEquals(Tabs.SelectedItem, TabDuplicateVideos))
            {
                return DuplicateVideoTabIndex;
            }

            return -1;
        }

        // Grid を先頭へ寄せるが、x:Name と固定タブIDは変えずに表示順だけ差し替える。
        private void InitializeUpperTabDisplayOrder()
        {
            if (Tabs == null || TabGrid == null)
            {
                return;
            }

            if (Tabs.Items.Count > 0 && ReferenceEquals(Tabs.Items[0], TabGrid))
            {
                return;
            }

            Tabs.Items.Remove(TabGrid);
            Tabs.Items.Insert(0, TabGrid);
        }

        private void SelectUpperTabByFixedIndex(int tabIndex)
        {
            switch (tabIndex)
            {
                case UpperTabSmallFixedIndex:
                    TabSmall.IsSelected = true;
                    break;
                case UpperTabBigFixedIndex:
                    TabBig.IsSelected = true;
                    break;
                case UpperTabGridFixedIndex:
                    TabGrid.IsSelected = true;
                    break;
                case UpperTabListFixedIndex:
                    TabList.IsSelected = true;
                    break;
                case UpperTabBig10FixedIndex:
                    TabBig10.IsSelected = true;
                    break;
                case ThumbnailErrorTabIndex:
                    TabThumbnailError.IsSelected = true;
                    break;
                case DuplicateVideoTabIndex:
                    TabDuplicateVideos.IsSelected = true;
                    break;
            }
        }

        private ItemsControl GetItemsControlByUpperTabFixedIndex(int tabIndex)
        {
            return tabIndex switch
            {
                UpperTabSmallFixedIndex => SmallList,
                UpperTabBigFixedIndex => BigList,
                UpperTabGridFixedIndex => GridList,
                UpperTabListFixedIndex => ListDataGrid,
                UpperTabBig10FixedIndex => BigList10,
                ThumbnailErrorTabIndex => GetUpperTabRescueDataGrid(),
                DuplicateVideoTabIndex => GetUpperTabDuplicateDetailDataGrid(),
                _ => null,
            };
        }
    }
}
