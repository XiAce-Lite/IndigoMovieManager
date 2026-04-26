using System.Data;
using System.Globalization;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 監視フォルダごとの増分同期基準時刻をsystemテーブルから読む。
        private DateTime? LoadEverythingLastSyncUtc(
            string dbFullPath,
            long requestScopeStamp,
            string watchFolder,
            bool sub
        )
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return null;
            }

            if (!IsCurrentWatchScanScope(dbFullPath, requestScopeStamp))
            {
                return null;
            }

            try
            {
                string attr = BuildEverythingLastSyncAttr(watchFolder, sub);
                string escapedAttr = attr.Replace("'", "''");
                DataTable dt = GetData(
                    dbFullPath,
                    $"select value from system where attr = '{escapedAttr}' limit 1"
                );
                if (dt?.Rows.Count < 1)
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"load last_sync missing: db='{dbFullPath}' folder='{watchFolder}' sub={sub} attr='{attr}'"
                    );
                    return null;
                }

                string raw = dt.Rows[0]["value"]?.ToString() ?? "";
                if (
                    DateTime.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out DateTime parsedUtc
                    )
                )
                {
                    return parsedUtc;
                }

                DebugRuntimeLog.Write(
                    "watch-check",
                    $"load last_sync invalid: db='{dbFullPath}' folder='{watchFolder}' sub={sub} attr='{attr}' raw='{raw}'"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"load last_sync failed: db='{dbFullPath}' folder='{watchFolder}' sub={sub} reason={ex.GetType().Name}"
                );
            }

            return null;
        }

        // 増分同期基準時刻をsystemテーブルへ保存する。
        private void SaveEverythingLastSyncUtc(
            string dbFullPath,
            long requestScopeStamp,
            string watchFolder,
            bool sub,
            DateTime lastSyncUtc
        )
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            if (!IsCurrentWatchScanScope(dbFullPath, requestScopeStamp))
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"save last_sync skipped stale: db='{dbFullPath}' folder='{watchFolder}'"
                );
                return;
            }

            try
            {
                string attr = BuildEverythingLastSyncAttr(watchFolder, sub);
                string normalizedUtc = lastSyncUtc
                    .ToUniversalTime()
                    .ToString("O", CultureInfo.InvariantCulture);
                TryPersistSystemValue(dbFullPath, attr, normalizedUtc);
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"persist last_sync: db='{dbFullPath}' folder='{watchFolder}' sub={sub} attr='{attr}' value='{normalizedUtc}'"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"save last_sync failed: db='{dbFullPath}' folder='{watchFolder}' sub={sub} reason={ex.GetType().Name}"
                );
            }
        }
    }
}
