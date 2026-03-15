using System.Text;
using System.Threading;

namespace IndigoMovieManager.Thumbnail
{
    // 改善処理の流れだけを追う debug 専用ログ。通常運用では完全に無効にする。
    public static class ThumbnailRescueTraceLog
    {
        public const string TraceEnvName = "IMM_THUMB_RESCUE_TRACE";

        private const string FileName = "thumbnail-rescue-trace.csv";
        private const string Header =
            "ts_utc,source,failure_id,movie_path,tab_index,panel_size,route_id,symptom_class,phase,engine,action,result,elapsed_ms,failure_kind,reason,output_path";
        private const string MutexName =
            "Local\\IndigoMovieManager_fork_workthree_thumbnail_rescue_trace";

        public static bool IsEnabled()
        {
            return IsEnabledValue(Environment.GetEnvironmentVariable(TraceEnvName));
        }

        internal static bool IsEnabledValue(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            return rawValue.Trim().ToLowerInvariant() switch
            {
                "1" => true,
                "true" => true,
                "on" => true,
                "yes" => true,
                _ => false,
            };
        }

        public static string BuildPanelSizeLabel(int tabIndex, string dbName, string thumbFolder = "")
        {
            if (tabIndex < 0)
            {
                return "";
            }

            TabInfo tabInfo = new(tabIndex, dbName ?? "", thumbFolder ?? "");
            return $"{tabInfo.Width}x{tabInfo.Height}x{tabInfo.Columns}x{tabInfo.Rows}";
        }

        public static void Write(
            string source,
            string action,
            string result,
            long failureId = 0,
            string moviePath = "",
            int tabIndex = -1,
            string panelSize = "",
            string routeId = "",
            string symptomClass = "",
            string phase = "",
            string engine = "",
            long elapsedMs = -1,
            string failureKind = "",
            string reason = "",
            string outputPath = ""
        )
        {
            if (!IsEnabled())
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(AppLocalDataPaths.LogsPath);
                string logPath = Path.Combine(AppLocalDataPaths.LogsPath, FileName);
                using Mutex mutex = new(false, MutexName);
                bool hasLock = false;

                try
                {
                    try
                    {
                        hasLock = mutex.WaitOne(TimeSpan.FromSeconds(1));
                    }
                    catch (AbandonedMutexException)
                    {
                        hasLock = true;
                    }

                    if (!hasLock)
                    {
                        return;
                    }

                    bool needsHeader = !File.Exists(logPath) || new FileInfo(logPath).Length <= 0;
                    using FileStream stream = new(
                        logPath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite
                    );
                    using StreamWriter writer = new(stream, new UTF8Encoding(false));
                    if (needsHeader)
                    {
                        writer.WriteLine(Header);
                    }

                    writer.WriteLine(
                        BuildCsvLine(
                            DateTime.UtcNow,
                            source,
                            failureId,
                            moviePath,
                            tabIndex,
                            panelSize,
                            routeId,
                            symptomClass,
                            phase,
                            engine,
                            action,
                            result,
                            elapsedMs,
                            failureKind,
                            reason,
                            outputPath
                        )
                    );
                }
                finally
                {
                    if (hasLock)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
            catch
            {
                // debug 専用なので、本体処理を止めない。
            }
        }

        internal static string BuildCsvLine(
            DateTime timestampUtc,
            string source,
            long failureId,
            string moviePath,
            int tabIndex,
            string panelSize,
            string routeId,
            string symptomClass,
            string phase,
            string engine,
            string action,
            string result,
            long elapsedMs,
            string failureKind,
            string reason,
            string outputPath
        )
        {
            string[] values =
            [
                timestampUtc.ToString("O"),
                source ?? "",
                failureId > 0 ? failureId.ToString() : "",
                moviePath ?? "",
                tabIndex >= 0 ? tabIndex.ToString() : "",
                panelSize ?? "",
                routeId ?? "",
                symptomClass ?? "",
                phase ?? "",
                engine ?? "",
                action ?? "",
                result ?? "",
                elapsedMs >= 0 ? elapsedMs.ToString() : "",
                failureKind ?? "",
                reason ?? "",
                outputPath ?? "",
            ];

            return string.Join(",", values.Select(EscapeCsv));
        }

        private static string EscapeCsv(string value)
        {
            string normalized = (value ?? "").Replace("\r", " ").Replace("\n", " ");
            if (
                normalized.Contains(',')
                || normalized.Contains('"')
                || normalized.Contains(' ')
            )
            {
                return $"\"{normalized.Replace("\"", "\"\"")}\"";
            }

            return normalized;
        }
    }
}
