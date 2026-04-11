using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using IndigoMovieManager.Properties;

namespace IndigoMovieManager
{
    // Debug実行時だけ、処理の開始/終了をローカルログへ残す。
    internal static class DebugRuntimeLog
    {
        private static readonly object LogLock = new();
        private static readonly object QuietLogLock = new();
        private const long MaxLogFileBytes = 20 * 1024 * 1024;
        private const int ReleaseWatchLogThrottleMilliseconds = 1200;
        private const int NoisyWatchRepairLogThrottleMilliseconds = 1500;
        private static readonly HashSet<string> ReleaseMinimalCategories = new(
            new[] { "watch-check", "ui-tempo" },
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly string[] AlwaysThrottledWatchMessagePrefixes =
        [
            "repair view by existing-db-movie:",
            "refresh filtered-view by existing-db-movie:",
        ];
        private static readonly HashSet<string> ReleaseMinimalWatchKeywords = new(
            new[] { "fail", "error", "exception", "shutdown", "critical", "recovery" },
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly bool IsReleaseLikeLoggingMode =
            !Debugger.IsAttached
            || string.Equals(
                Environment.GetEnvironmentVariable("INDIGO_RELEASE_LOG_MODE"),
                "1",
                StringComparison.OrdinalIgnoreCase
            );
        private static readonly Dictionary<string, DateTime> ReleaseLastWriteUtcByEvent = new(
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly Dictionary<string, DateTime> AlwaysThrottleLastWriteUtcByEvent =
            new(StringComparer.OrdinalIgnoreCase);

        [Conditional("DEBUG")]
        internal static void Write(string category, string message)
        {
            if (!ShouldWrite(category, message, DateTime.UtcNow))
            {
                return;
            }

            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}";
            Debug.WriteLine(line);

            try
            {
                // VS出力だけで追いにくいケースに備え、同じ内容をファイルにも追記する。
                string logDir = AppLocalDataPaths.LogsPath;
                Directory.CreateDirectory(logDir);
                string logPath = IndigoMovieManager.Thumbnail.LogFileTimeWindowSeparator.PrepareForWrite(
                    Path.Combine(logDir, "debug-runtime.log"),
                    MaxLogFileBytes
                );

                lock (LogLock)
                {
                    // 上限超過時は同日でも退避して、次の追記を継続できるようにする。
                    File.AppendAllText(logPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // ログ失敗で本体処理を止めない。
            }
        }

        internal static bool ShouldWriteForCurrentProcess(
            string category,
            string message,
            DateTime utcNow
        )
        {
            return ShouldWrite(category, message, utcNow);
        }

        internal static void ResetThrottleStateForTests()
        {
            lock (QuietLogLock)
            {
                ReleaseLastWriteUtcByEvent.Clear();
                AlwaysThrottleLastWriteUtcByEvent.Clear();
            }
        }

        private static bool ShouldWrite(string category, string message, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            // まずカテゴリ単位のON/OFFを見て、不要なログは入口で落とす。
            if (!IsCategoryEnabled(category))
            {
                return false;
            }

            if (TryBuildAlwaysThrottledWatchBucket(category, message, out string alwaysBucket))
            {
                return !IsLogThrottled(
                    alwaysBucket,
                    utcNow,
                    NoisyWatchRepairLogThrottleMilliseconds,
                    AlwaysThrottleLastWriteUtcByEvent
                );
            }

            if (!IsReleaseLikeLoggingMode || !IsReleaseMinimalCategory(category))
            {
                return true;
            }

            if (ReleaseMinimalWatchKeywords.Any(keyword => message.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            string bucket = BuildLogBucket(category, message);
            return !IsLogThrottled(
                bucket,
                utcNow,
                ReleaseWatchLogThrottleMilliseconds,
                ReleaseLastWriteUtcByEvent
            );
        }

        private static bool IsReleaseMinimalCategory(string category)
        {
            return ReleaseMinimalCategories.Contains(category);
        }

        private static bool IsCategoryEnabled(string category)
        {
            return ResolveToggleGroup(category) switch
            {
                DebugRuntimeLogToggleGroup.Watch => Settings.Default.DebugLogWatchEnabled,
                DebugRuntimeLogToggleGroup.Queue => Settings.Default.DebugLogQueueEnabled,
                DebugRuntimeLogToggleGroup.Thumbnail => Settings.Default.DebugLogThumbnailEnabled,
                DebugRuntimeLogToggleGroup.Ui => Settings.Default.DebugLogUiEnabled,
                DebugRuntimeLogToggleGroup.Skin => Settings.Default.DebugLogSkinEnabled,
                DebugRuntimeLogToggleGroup.DebugTool => Settings.Default.DebugLogDebugToolEnabled,
                DebugRuntimeLogToggleGroup.Database => Settings.Default.DebugLogDatabaseEnabled,
                _ => Settings.Default.DebugLogOtherEnabled,
            };
        }

        private static DebugRuntimeLogToggleGroup ResolveToggleGroup(string category)
        {
            string normalized = (category ?? "").Trim().ToLowerInvariant();
            if (normalized.Length == 0)
            {
                return DebugRuntimeLogToggleGroup.Other;
            }

            if (normalized.StartsWith("watch", StringComparison.Ordinal))
            {
                return DebugRuntimeLogToggleGroup.Watch;
            }

            if (normalized.StartsWith("queue", StringComparison.Ordinal))
            {
                return DebugRuntimeLogToggleGroup.Queue;
            }

            if (normalized.StartsWith("thumbnail", StringComparison.Ordinal))
            {
                return DebugRuntimeLogToggleGroup.Thumbnail;
            }

            if (
                normalized.StartsWith("ui-", StringComparison.Ordinal)
                || normalized is "lifecycle"
                || normalized is "layout"
                || normalized is "player"
                || normalized is "kana"
                || normalized is "overlay"
                || normalized is "task"
                || normalized is "task-start"
                || normalized is "task-end"
            )
            {
                return DebugRuntimeLogToggleGroup.Ui;
            }

            if (normalized.StartsWith("skin", StringComparison.Ordinal))
            {
                return DebugRuntimeLogToggleGroup.Skin;
            }

            if (
                normalized.StartsWith("debug", StringComparison.Ordinal)
                || normalized is "log-tab"
            )
            {
                return DebugRuntimeLogToggleGroup.DebugTool;
            }

            if (
                normalized.StartsWith("db", StringComparison.Ordinal)
                || normalized is "sinku"
            )
            {
                return DebugRuntimeLogToggleGroup.Database;
            }

            return DebugRuntimeLogToggleGroup.Other;
        }

        private static bool TryBuildAlwaysThrottledWatchBucket(
            string category,
            string message,
            out string bucket
        )
        {
            bucket = "";
            if (!string.Equals(category, "watch-check", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string trimmed = message.Trim();
            foreach (string prefix in AlwaysThrottledWatchMessagePrefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    bucket = BuildLogBucket(category, trimmed);
                    return true;
                }
            }

            return false;
        }

        private static bool IsLogThrottled(
            string bucket,
            DateTime now,
            int throttleMilliseconds,
            Dictionary<string, DateTime> lastWriteUtcByEvent
        )
        {
            lock (QuietLogLock)
            {
                if (
                    lastWriteUtcByEvent.TryGetValue(bucket, out DateTime lastWrite)
                    && (now - lastWrite).TotalMilliseconds < throttleMilliseconds
                )
                {
                    return true;
                }

                lastWriteUtcByEvent[bucket] = now;
                return false;
            }
        }

        private static string BuildLogBucket(string category, string message)
        {
            string trimmed = message.Trim();
            int colonIndex = trimmed.IndexOf(':');
            int cutIndex = colonIndex > 0 ? Math.Min(colonIndex, trimmed.Length) : trimmed.Length;

            if (trimmed.Length > 70)
            {
                cutIndex = Math.Min(70, trimmed.Length);
            }

            string eventKey = trimmed[..cutIndex].Trim();
            if (eventKey.Length == 0)
            {
                eventKey = "event";
            }

            return $"{category}|{eventKey}";
        }

        [Conditional("DEBUG")]
        internal static void TaskStart(string taskName, string detail = "")
        {
            Write("task-start", $"{taskName} {detail}".Trim());
        }

        [Conditional("DEBUG")]
        internal static void TaskEnd(string taskName, string detail = "")
        {
            Write("task-end", $"{taskName} {detail}".Trim());
        }

        private enum DebugRuntimeLogToggleGroup
        {
            Other,
            Watch,
            Queue,
            Thumbnail,
            Ui,
            Skin,
            DebugTool,
            Database,
        }
    }
}
