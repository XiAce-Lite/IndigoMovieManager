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
        private string _everythingPollEligibleWatchFolderSnapshotDbPath = "";
        private string[] _everythingPollEligibleWatchFolderSnapshot = [];

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

        // drive 種別や NTFS 判定は毎周やらず、watch 一覧が変わった時だけまとめて評価する。
        private string[] GetEverythingPollEligibleWatchFoldersSnapshot(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath) || !Path.Exists(dbPath))
            {
                return [];
            }

            if (AreSameMainDbPath(_everythingPollEligibleWatchFolderSnapshotDbPath, dbPath))
            {
                return _everythingPollEligibleWatchFolderSnapshot;
            }

            string[] watchFolders = GetEverythingPollWatchFoldersSnapshot(dbPath);
            _everythingPollEligibleWatchFolderSnapshot = ExtractEverythingEligibleWatchFolders(
                watchFolders,
                watchFolder => IsEverythingEligiblePath(watchFolder, out _)
            );
            _everythingPollEligibleWatchFolderSnapshotDbPath = dbPath;
            return _everythingPollEligibleWatchFolderSnapshot;
        }

        // 監視設定やDBが変わった時だけ snapshot を捨て、次回 poll で組み直す。
        private void InvalidateEverythingWatchPollWatchFolderSnapshot()
        {
            _everythingPollWatchFolderSnapshotDbPath = "";
            _everythingPollWatchFolderSnapshot = [];
            _everythingPollEligibleWatchFolderSnapshotDbPath = "";
            _everythingPollEligibleWatchFolderSnapshot = [];
            ResetEverythingWatchPollAdaptiveDelayState();
        }

        // watch テーブルから、Everything poll 判定に必要なフォルダだけを順序維持で抜き出す。
        internal static string[] ExtractEverythingPollWatchFolders(DataTable watchTable)
        {
            if (watchTable == null || watchTable.Rows.Count < 1)
            {
                return [];
            }

            List<string> watchFolders = [];
            HashSet<string> seen = new(System.StringComparer.OrdinalIgnoreCase);
            foreach (DataRow row in watchTable.Rows)
            {
                string watchFolder = row["dir"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(watchFolder))
                {
                    if (!seen.Add(watchFolder))
                    {
                        continue;
                    }

                    watchFolders.Add(watchFolder);
                }
            }

            return [.. watchFolders];
        }

        // Everything 高速経路に乗せられる監視フォルダだけを順序維持で残す。
        // 重複は先に潰してから eligibility を判定し、同じ候補に重い判定を何度も走らせない。
        internal static string[] ExtractEverythingEligibleWatchFolders(
            IEnumerable<string> watchFolders,
            Func<string, bool> isEverythingEligiblePath
        )
        {
            if (watchFolders == null)
            {
                return [];
            }

            List<string> eligibleWatchFolders = [];
            HashSet<string> seen = new(System.StringComparer.OrdinalIgnoreCase);
            foreach (string watchFolder in watchFolders)
            {
                if (string.IsNullOrWhiteSpace(watchFolder))
                {
                    continue;
                }

                if (!seen.Add(watchFolder))
                {
                    continue;
                }

                if (isEverythingEligiblePath?.Invoke(watchFolder) != true)
                {
                    continue;
                }

                eligibleWatchFolders.Add(watchFolder);
            }

            return [.. eligibleWatchFolders];
        }
    }
}
