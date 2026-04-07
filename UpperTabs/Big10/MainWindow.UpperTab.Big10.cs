using System.Windows.Controls;
using System.Collections.Generic;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // Big10 タブの host 取得を 1 か所へ寄せ、通常タブ分割の入口を揃える。
        private ListView GetUpperTabBig10List()
        {
            return BigList10;
        }

        // Big10 タブを前面化した直後は、先頭選択だけここから揃える。
        private void SelectFirstUpperTabBig10ItemIfAvailable()
        {
            if (GetUpperTabBig10List()?.Items.Count > 0)
            {
                GetUpperTabBig10List().SelectedIndex = 0;
            }
        }

        // 5x2 は既定スキンへは戻さないが、明示選択時の入口だけ先に用意しておく。
        private void SelectUpperTabBig10View()
        {
            SelectUpperTabByFixedIndex(UpperTabBig10FixedIndex);
            SelectFirstUpperTabBig10ItemIfAvailable();
        }

        // 5x2 タブで今選ばれている 1 件の取得を、この dir 側へ寄せる。
        private MovieRecords GetSelectedUpperTabBig10MovieRecord()
        {
            return GetUpperTabBig10List()?.SelectedItem as MovieRecords;
        }

        // 5x2 タブで複数選択されている一覧の取得も、この dir 側へ揃える。
        private List<MovieRecords> GetSelectedUpperTabBig10MovieRecords()
        {
            List<MovieRecords> records = [];
            if (GetUpperTabBig10List()?.SelectedItems == null)
            {
                return records;
            }

            foreach (MovieRecords item in GetUpperTabBig10List().SelectedItems)
            {
                records.Add(item);
            }

            return records;
        }

        // 5x2 タブのクリック選択同期は、この dir 側で面倒を見る。
        private void TrySyncUpperTabBig10SelectionFromItem(ListViewItem item)
        {
            if (item == null || item.IsSelected)
            {
                return;
            }

            item.IsSelected = true;
            GetUpperTabBig10List().SelectedItem = item.DataContext;
        }

        // ラベルクリック時に 5x2 タブの選択を同期する入口。
        private void SelectUpperTabBig10MovieRecord(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            GetUpperTabBig10List().SelectedItem = record;
        }
    }
}
