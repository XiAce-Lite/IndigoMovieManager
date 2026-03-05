using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// ミニパネル表示遅延（保存->初回表示）を軽量に記録する。
    /// </summary>
    internal static class ThumbnailPreviewLatencyTracker
    {
        private const string LogFileName = "thumbnail-progress-latency.csv";
        private const int MaxPendingCount = 4096;
        private const int MaxCompletedCount = 16384;
        private static readonly ConcurrentDictionary<string, SavedEvent> Pending = new(
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly ConcurrentDictionary<string, byte> Completed = new(
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly object LogLock = new();

        public static void RecordSaved(string previewCacheKey, long previewRevision, string savePath)
        {
            if (string.IsNullOrWhiteSpace(previewCacheKey) || previewRevision < 1)
            {
                return;
            }

            string eventKey = BuildEventKey(previewCacheKey, previewRevision);
            Pending[eventKey] = new SavedEvent
            {
                SavedAtUtc = DateTime.UtcNow,
                SavePath = savePath ?? "",
            };
            _ = Completed.TryRemove(eventKey, out _);
            PruneMapsIfNeeded();
        }

        public static void RecordDisplayed(
            string previewCacheKey,
            long previewRevision,
            string sourceType
        )
        {
            if (string.IsNullOrWhiteSpace(previewCacheKey) || previewRevision < 1)
            {
                return;
            }

            string eventKey = BuildEventKey(previewCacheKey, previewRevision);
            if (!Completed.TryAdd(eventKey, 0))
            {
                return;
            }

            if (!Pending.TryRemove(eventKey, out SavedEvent savedEvent))
            {
                return;
            }

            double latencyMs = (DateTime.UtcNow - savedEvent.SavedAtUtc).TotalMilliseconds;
            if (latencyMs < 0)
            {
                latencyMs = 0;
            }

            WriteLatencyLog(
                previewCacheKey,
                previewRevision,
                sourceType,
                latencyMs,
                savedEvent.SavePath
            );
        }

        public static void Reset()
        {
            Pending.Clear();
            Completed.Clear();
        }

        private static string BuildEventKey(string previewCacheKey, long previewRevision)
        {
            return $"{previewCacheKey}|{previewRevision.ToString(CultureInfo.InvariantCulture)}";
        }

        private static void PruneMapsIfNeeded()
        {
            if (Pending.Count > MaxPendingCount)
            {
                DateTime cutoffUtc = DateTime.UtcNow.AddMinutes(-10);
                foreach (var pair in Pending)
                {
                    if (Pending.Count <= MaxPendingCount)
                    {
                        break;
                    }

                    if (pair.Value.SavedAtUtc < cutoffUtc)
                    {
                        _ = Pending.TryRemove(pair.Key, out _);
                    }
                }
            }

            if (Completed.Count > MaxCompletedCount)
            {
                Completed.Clear();
            }
        }

        private static void WriteLatencyLog(
            string previewCacheKey,
            long previewRevision,
            string sourceType,
            double latencyMs,
            string savePath
        )
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IndigoMovieManager_fork",
                    "logs"
                );
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, LogFileName);
                bool needsHeader = !File.Exists(logPath) || new FileInfo(logPath).Length < 1;

                string line = string.Join(
                    ",",
                    EscapeCsv(
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    ),
                    EscapeCsv(previewCacheKey),
                    previewRevision.ToString(CultureInfo.InvariantCulture),
                    EscapeCsv(sourceType ?? ""),
                    latencyMs.ToString("0.###", CultureInfo.InvariantCulture),
                    EscapeCsv(savePath ?? "")
                );

                lock (LogLock)
                {
                    using StreamWriter writer = new(logPath, append: true, new UTF8Encoding(false));
                    if (needsHeader)
                    {
                        writer.WriteLine(
                            "datetime,preview_cache_key,preview_revision,source_type,latency_ms,save_path"
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

        private static string EscapeCsv(string value)
        {
            value ??= "";
            if (
                !value.Contains(',')
                && !value.Contains('"')
                && !value.Contains('\n')
                && !value.Contains('\r')
            )
            {
                return value;
            }

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        private sealed class SavedEvent
        {
            public DateTime SavedAtUtc { get; init; }
            public string SavePath { get; init; } = "";
        }
    }
}
