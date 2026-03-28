using System.Globalization;
using System.IO;
using System.Text;
using IndigoMovieManager;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 進捗タブのUI反映時間（ApplySnapshot）をCSVへ記録する。
    /// </summary>
    internal static class ThumbnailProgressUiMetricsLogger
    {
        private const string LogFileName = "thumbnail-progress-ui.csv";
        private static readonly object LogLock = new();

        public static void RecordSnapshotApply(
            long snapshotVersion,
            int dbPendingCount,
            int dbTotalCount,
            int workerCount,
            double applyDurationMs
        )
        {
            try
            {
                string logDir = AppLocalDataPaths.LogsPath;
                Directory.CreateDirectory(logDir);

                string logPath = global::IndigoMovieManager.Thumbnail.LogFileTimeWindowSeparator.PrepareForWrite(
                    Path.Combine(logDir, LogFileName)
                );
                string line = string.Join(
                    ",",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    snapshotVersion.ToString(CultureInfo.InvariantCulture),
                    dbPendingCount.ToString(CultureInfo.InvariantCulture),
                    dbTotalCount.ToString(CultureInfo.InvariantCulture),
                    workerCount.ToString(CultureInfo.InvariantCulture),
                    applyDurationMs.ToString("0.###", CultureInfo.InvariantCulture)
                );

                lock (LogLock)
                {
                    bool needsHeader = !File.Exists(logPath) || new FileInfo(logPath).Length < 1;
                    using StreamWriter writer = new(logPath, append: true, new UTF8Encoding(false));
                    if (needsHeader)
                    {
                        writer.WriteLine(
                            "datetime,snapshot_version,db_pending_count,db_total_count,worker_count,apply_duration_ms"
                        );
                    }

                    writer.WriteLine(line);
                }
            }
            catch
            {
                // 計測ログ失敗で本体処理を止めない。
            }
        }
    }
}
