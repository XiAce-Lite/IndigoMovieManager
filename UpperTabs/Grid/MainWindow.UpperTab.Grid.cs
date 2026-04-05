using System.Windows.Controls;
using System.Collections.Generic;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // Grid タブの host 取得はここへ寄せ、通常タブ分割の最初の入口にする。
        private ListView GetUpperTabGridList()
        {
            return GridList;
        }

        // Grid タブを前面化した直後は、先頭選択だけここから揃える。
        private void SelectFirstUpperTabGridItemIfAvailable()
        {
            if (GetUpperTabGridList()?.Items.Count > 0)
            {
                GetUpperTabGridList().SelectedIndex = 0;
            }
        }

        // Grid を既定表示へ戻す経路は 1 か所へ寄せ、後続の host 化に備える。
        private void SelectUpperTabGridAsDefaultView()
        {
            SelectUpperTabByFixedIndex(UpperTabGridFixedIndex);
            SelectFirstUpperTabGridItemIfAvailable();
        }

        // Grid タブで今選ばれている 1 件の取得を、この dir 側へ寄せる。
        private MovieRecords GetSelectedUpperTabGridMovieRecord()
        {
            return GetUpperTabGridList()?.SelectedItem as MovieRecords;
        }

        // Grid タブで複数選択されている一覧の取得も、この dir 側へ揃える。
        private List<MovieRecords> GetSelectedUpperTabGridMovieRecords()
        {
            List<MovieRecords> records = [];
            if (GetUpperTabGridList()?.SelectedItems == null)
            {
                return records;
            }

            foreach (MovieRecords item in GetUpperTabGridList().SelectedItems)
            {
                records.Add(item);
            }

            return records;
        }

        // ラベルクリック時に Grid タブの選択を同期する入口。
        private void SelectUpperTabGridMovieRecord(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            GetUpperTabGridList().SelectedItem = record;
        }
    }
}
