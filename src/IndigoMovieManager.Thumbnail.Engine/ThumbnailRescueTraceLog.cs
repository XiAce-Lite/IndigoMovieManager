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
        private static string configuredLogDirectoryPath = "";

        // app / worker が実運用のログ出力先を明示したい時だけ上書きする。
        public static void ConfigureLogDirectory(string logDirectoryPath)
        {
            Interlocked.Exchange(ref configuredLogDirectoryPath, logDirectoryPath?.Trim() ?? "");
        }

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

            // panel size はレイアウトだけ分かればよいので、TabInfo の保存先責務を経由しない。
            ThumbnailLayoutProfile layout = ThumbnailLayoutProfileResolver.Resolve(
                tabIndex,
                ThumbnailDetailModeRuntime.ReadRuntimeMode()
            );
            return layout.FolderName;
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
                string logDirectoryPath = ResolveLogDirectoryPath();
                Directory.CreateDirectory(logDirectoryPath);
                string logPath = LogFileTimeWindowSeparator.PrepareForWrite(
                    Path.Combine(logDirectoryPath, FileName)
                );
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

        internal static string ResolveLogDirectoryPath()
        {
            string configured = Interlocked.CompareExchange(ref configuredLogDirectoryPath, "", "");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            string baseDir = string.IsNullOrWhiteSpace(AppContext.BaseDirectory)
                ? Directory.GetCurrentDirectory()
                : AppContext.BaseDirectory;
            return Path.Combine(baseDir, "logs");
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
