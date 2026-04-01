using System.Windows.Controls;
using System.Collections.Generic;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // Small タブの host 取得を 1 か所へ寄せ、通常タブ分割の入口を揃える。
        private ListView GetUpperTabSmallList()
        {
            return SmallList;
        }

        // Small タブを前面化した直後は、先頭選択だけここから揃える。
        private void SelectFirstUpperTabSmallItemIfAvailable()
        {
            if (GetUpperTabSmallList()?.Items.Count > 0)
            {
                GetUpperTabSmallList().SelectedIndex = 0;
            }
        }

        // Small を既定表示へ戻す経路を 1 か所へ寄せ、後続の host 化に備える。
        private void SelectUpperTabSmallAsDefaultView()
        {
            SelectUpperTabByFixedIndex(UpperTabSmallFixedIndex);
            SelectFirstUpperTabSmallItemIfAvailable();
        }

        // Small タブで今選ばれている 1 件の取得を、この dir 側へ寄せる。
        private MovieRecords GetSelectedUpperTabSmallMovieRecord()
        {
            return GetUpperTabSmallList()?.SelectedItem as MovieRecords;
        }

        // Small タブで複数選択されている一覧の取得も、この dir 側へ揃える。
        private List<MovieRecords> GetSelectedUpperTabSmallMovieRecords()
        {
            List<MovieRecords> records = [];
            if (GetUpperTabSmallList()?.SelectedItems == null)
            {
                return records;
            }

            foreach (MovieRecords item in GetUpperTabSmallList().SelectedItems)
            {
                records.Add(item);
            }

            return records;
        }

        // Small タブのクリック選択同期は、この dir 側で面倒を見る。
        private void TrySyncUpperTabSmallSelectionFromItem(ListViewItem item)
        {
            if (item == null || item.IsSelected)
            {
                return;
            }

            item.IsSelected = true;
            GetUpperTabSmallList().SelectedItem = item.DataContext;
        }

        // ラベルクリック時に Small タブの選択を同期する入口。
        private void SelectUpperTabSmallMovieRecord(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            GetUpperTabSmallList().SelectedItem = record;
        }
    }
}
