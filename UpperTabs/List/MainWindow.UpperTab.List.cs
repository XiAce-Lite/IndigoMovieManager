using System.Windows.Controls;
using System.Collections.Generic;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // List タブの host 取得を 1 か所へ寄せ、通常タブ分割の入口を揃える。
        private DataGrid GetUpperTabListDataGrid()
        {
            return ListDataGrid;
        }

        // List タブを前面化した直後は、先頭選択だけここから揃える。
        private void SelectFirstUpperTabListItemIfAvailable()
        {
            if (GetUpperTabListDataGrid()?.Items.Count > 0)
            {
                GetUpperTabListDataGrid().SelectedIndex = 0;
            }
        }

        // List を既定表示へ戻す経路を 1 か所へ寄せ、後続の host 化に備える。
        private void SelectUpperTabListAsDefaultView()
        {
            SelectUpperTabByFixedIndex(UpperTabListFixedIndex);
            SelectFirstUpperTabListItemIfAvailable();
        }

        // List タブで今選ばれている 1 件の取得を、この dir 側へ寄せる。
        private MovieRecords GetSelectedUpperTabListMovieRecord()
        {
            return GetUpperTabListDataGrid()?.SelectedItem as MovieRecords;
        }

        // List タブで複数選択されている一覧の取得も、この dir 側へ揃える。
        private List<MovieRecords> GetSelectedUpperTabListMovieRecords()
        {
            List<MovieRecords> records = [];
            if (GetUpperTabListDataGrid()?.SelectedItems == null)
            {
                return records;
            }

            foreach (MovieRecords item in GetUpperTabListDataGrid().SelectedItems)
            {
                records.Add(item);
            }

            return records;
        }

        // 画像クリック時の選択同期は、List タブ側の host 操作としてここへ寄せる。
        private void SelectUpperTabListMovieRecord(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            GetUpperTabListDataGrid().SelectedItem = record;
        }
    }
}
