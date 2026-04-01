using System.Windows.Controls;
using System.Collections.Generic;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // Big タブの host 取得を 1 か所へ寄せ、通常タブ分割の入口を揃える。
        private ListView GetUpperTabBigList()
        {
            return BigList;
        }

        // Big タブを前面化した直後は、先頭選択だけここから揃える。
        private void SelectFirstUpperTabBigItemIfAvailable()
        {
            if (GetUpperTabBigList()?.Items.Count > 0)
            {
                GetUpperTabBigList().SelectedIndex = 0;
            }
        }

        // Big を既定表示へ戻す経路を 1 か所へ寄せ、後続の host 化に備える。
        private void SelectUpperTabBigAsDefaultView()
        {
            SelectUpperTabByFixedIndex(UpperTabBigFixedIndex);
            SelectFirstUpperTabBigItemIfAvailable();
        }

        // Big タブで今選ばれている 1 件の取得を、この dir 側へ寄せる。
        private MovieRecords GetSelectedUpperTabBigMovieRecord()
        {
            return GetUpperTabBigList()?.SelectedItem as MovieRecords;
        }

        // Big タブで複数選択されている一覧の取得も、この dir 側へ揃える。
        private List<MovieRecords> GetSelectedUpperTabBigMovieRecords()
        {
            List<MovieRecords> records = [];
            if (GetUpperTabBigList()?.SelectedItems == null)
            {
                return records;
            }

            foreach (MovieRecords item in GetUpperTabBigList().SelectedItems)
            {
                records.Add(item);
            }

            return records;
        }

        // Big タブのクリック選択同期は、この dir 側で面倒を見る。
        private void TrySyncUpperTabBigSelectionFromItem(ListViewItem item)
        {
            if (item == null || item.IsSelected)
            {
                return;
            }

            item.IsSelected = true;
            GetUpperTabBigList().SelectedItem = item.DataContext;
        }

        // ラベルクリック時に Big タブの選択を同期する入口。
        private void SelectUpperTabBigMovieRecord(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            GetUpperTabBigList().SelectedItem = record;
        }
    }
}
