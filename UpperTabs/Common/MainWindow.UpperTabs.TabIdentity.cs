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

        // 選択中の TabItem から固定IDを逆引きし、表示順変更の影響をここで吸収する。
        private int ResolveUpperTabFixedIndexBySelectedItem(object selectedItem)
        {
            if (selectedItem == null)
            {
                return -1;
            }

            if (ReferenceEquals(selectedItem, TabSmall))
            {
                return UpperTabSmallFixedIndex;
            }

            if (ReferenceEquals(selectedItem, TabBig))
            {
                return UpperTabBigFixedIndex;
            }

            if (ReferenceEquals(selectedItem, TabGrid))
            {
                return UpperTabGridFixedIndex;
            }

            if (ReferenceEquals(selectedItem, TabList))
            {
                return UpperTabListFixedIndex;
            }

            if (ReferenceEquals(selectedItem, TabBig10))
            {
                return UpperTabBig10FixedIndex;
            }

            if (ReferenceEquals(selectedItem, TabThumbnailError))
            {
                return ThumbnailErrorTabIndex;
            }

            if (ReferenceEquals(selectedItem, TabDuplicateVideos))
            {
                return DuplicateVideoTabIndex;
            }

            return -1;
        }

        // 表示順と内部タブIDを切り離し、Queue/設定/ログの互換を守る。
        private int GetCurrentUpperTabFixedIndex()
        {
            return ResolveUpperTabFixedIndexBySelectedItem(Tabs?.SelectedItem);
        }

        // 現在の上側タブIDを安全に取り、未解決時だけ false を返す。
        private bool TryGetCurrentUpperTabFixedIndex(out int tabIndex)
        {
            tabIndex = GetCurrentUpperTabFixedIndex();
            return tabIndex >= 0;
        }

        // 現在タブの固定IDと「通常タブか」をまとめて返し、呼び出し側の分岐を薄くする。
        private bool TryGetCurrentUpperTabContext(out int tabIndex, out bool isStandardUpperTab)
        {
            isStandardUpperTab = false;
            if (!TryGetCurrentUpperTabFixedIndex(out tabIndex))
            {
                return false;
            }

            isStandardUpperTab = IsStandardUpperTabFixedIndex(tabIndex);
            return true;
        }

        // 通常のサムネイル一覧タブかどうかを、固定ID基準で 1 か所に寄せる。
        private static bool IsStandardUpperTabFixedIndex(int tabIndex)
        {
            return tabIndex is >= UpperTabSmallFixedIndex and <= UpperTabBig10FixedIndex;
        }

        // system.skin へ保存する互換名は、固定IDからだけ決める。
        private static string ResolveSkinNameByUpperTabFixedIndex(int tabIndex)
        {
            // 5x2 は既存互換のため保存せず、Grid に寄せる。
            return tabIndex switch
            {
                UpperTabSmallFixedIndex => "DefaultSmall",
                UpperTabBigFixedIndex => "DefaultBig",
                UpperTabGridFixedIndex => "DefaultGrid",
                UpperTabListFixedIndex => "DefaultList",
                _ => "DefaultGrid",
            };
        }

        // 外部スキン用 profile では 5x2 も含めた現在タブをそのまま退避する。
        private static string ResolveUpperTabStateNameByFixedIndex(int tabIndex)
        {
            return tabIndex switch
            {
                UpperTabSmallFixedIndex => "DefaultSmall",
                UpperTabBigFixedIndex => "DefaultBig",
                UpperTabGridFixedIndex => "DefaultGrid",
                UpperTabListFixedIndex => "DefaultList",
                UpperTabBig10FixedIndex => "DefaultBig10",
                _ => "DefaultGrid",
            };
        }

        // 保存済み skin 名から、表示すべき既定タブだけを選ぶ。
        private void SelectUpperTabDefaultViewBySkinName(string skin)
        {
            switch (NormalizeSkinName(skin))
            {
                case "DefaultSmall":
                    SelectUpperTabSmallAsDefaultView();
                    break;
                case "DefaultBig":
                    SelectUpperTabBigAsDefaultView();
                    break;
                case "DefaultGrid":
                    SelectUpperTabGridAsDefaultView();
                    break;
                case "DefaultList":
                    SelectUpperTabListAsDefaultView();
                    break;
                case "DefaultBig10":
                    SelectUpperTabBig10View();
                    break;
                default:
                    SelectUpperTabGridAsDefaultView();
                    break;
            }
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

        // 固定IDから実際の TabItem を引き、UI 側の参照をここへ閉じ込める。
        private TabItem GetUpperTabTabItemByFixedIndex(int tabIndex)
        {
            return tabIndex switch
            {
                UpperTabSmallFixedIndex => TabSmall,
                UpperTabBigFixedIndex => TabBig,
                UpperTabGridFixedIndex => TabGrid,
                UpperTabListFixedIndex => TabList,
                UpperTabBig10FixedIndex => TabBig10,
                ThumbnailErrorTabIndex => TabThumbnailError,
                DuplicateVideoTabIndex => TabDuplicateVideos,
                _ => null,
            };
        }

        // 固定IDでタブを選択し、存在しない時だけ false を返す。
        private bool TrySelectUpperTabByFixedIndex(int tabIndex)
        {
            TabItem tabItem = GetUpperTabTabItemByFixedIndex(tabIndex);
            if (tabItem == null)
            {
                return false;
            }

            tabItem.IsSelected = true;
            return true;
        }

        private void SelectUpperTabByFixedIndex(int tabIndex)
        {
            _ = TrySelectUpperTabByFixedIndex(tabIndex);
        }

        // 固定IDから表示 host を引き、通常/特殊タブの違いをここへ閉じ込める。
        private bool TryGetItemsControlByUpperTabFixedIndex(
            int tabIndex,
            out ItemsControl itemsControl
        )
        {
            itemsControl = tabIndex switch
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

            return itemsControl != null;
        }

        private ItemsControl GetItemsControlByUpperTabFixedIndex(int tabIndex)
        {
            return TryGetItemsControlByUpperTabFixedIndex(tabIndex, out ItemsControl itemsControl)
                ? itemsControl
                : null;
        }

        // 現在タブの固定IDと表示 host をまとめて取り、viewport 側の入口を揃える。
        private bool TryGetCurrentUpperTabItemsControl(
            out int tabIndex,
            out ItemsControl itemsControl
        )
        {
            tabIndex = -1;
            itemsControl = null;

            if (!TryGetCurrentUpperTabFixedIndex(out tabIndex))
            {
                return false;
            }

            return TryGetItemsControlByUpperTabFixedIndex(tabIndex, out itemsControl);
        }
    }
}
