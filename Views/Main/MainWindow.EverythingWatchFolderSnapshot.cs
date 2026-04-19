using System.Collections.Generic;
using System.Data;
using System.IO;
using IndigoMovieManager.DB;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private string _everythingPollWatchFolderSnapshotDbPath = "";
        private string[] _everythingPollWatchFolderSnapshot = [];

        // DB切替や監視設定変更までは、watch一覧の再読込を避けて poll 判定を軽く保つ。
        private string[] GetEverythingPollWatchFoldersSnapshot(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath) || !Path.Exists(dbPath))
            {
                return [];
            }

            if (AreSameMainDbPath(_everythingPollWatchFolderSnapshotDbPath, dbPath))
            {
                return _everythingPollWatchFolderSnapshot;
            }

            DataTable watchTable = SQLite.GetData(dbPath, "select dir from watch where watch = 1");
            _everythingPollWatchFolderSnapshot = ExtractEverythingPollWatchFolders(watchTable);
            _everythingPollWatchFolderSnapshotDbPath = dbPath;
            return _everythingPollWatchFolderSnapshot;
        }

        // 監視設定やDBが変わった時だけ snapshot を捨て、次回 poll で組み直す。
        private void InvalidateEverythingWatchPollWatchFolderSnapshot()
        {
            _everythingPollWatchFolderSnapshotDbPath = "";
            _everythingPollWatchFolderSnapshot = [];
        }

        // watch テーブルから、Everything poll 判定に必要なフォルダだけを順序維持で抜き出す。
        internal static string[] ExtractEverythingPollWatchFolders(DataTable watchTable)
        {
            if (watchTable == null || watchTable.Rows.Count < 1)
            {
                return [];
            }

            List<string> watchFolders = [];
            foreach (DataRow row in watchTable.Rows)
            {
                string watchFolder = row["dir"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(watchFolder))
                {
                    watchFolders.Add(watchFolder);
                }
            }

            return [.. watchFolders];
        }
    }
}
