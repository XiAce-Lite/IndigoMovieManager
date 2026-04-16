using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private static readonly AsyncLocal<string> AmbientScopeText = new();
        private static readonly AsyncLocal<DebugRuntimeLogScopeMetrics> AmbientScopeMetrics = new();
        private static long _logSequence;

        [Conditional("DEBUG")]
        internal static void Write(string category, string message)
        {
            if (!ShouldWrite(category, message, DateTime.UtcNow))
            {
                return;
            }

            string line = BuildLineForTesting(
                DateTime.Now,
                category,
                message,
                Interlocked.Increment(ref _logSequence),
                AmbientScopeText.Value
            );
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

            Interlocked.Exchange(ref _logSequence, 0);
            AmbientScopeText.Value = "";
            AmbientScopeMetrics.Value = null;
        }

        internal static IDisposable BeginScopeForCurrentAsyncFlow(string scopeText)
        {
            string normalizedScopeText = NormalizeInlineText(scopeText);
            if (string.IsNullOrWhiteSpace(normalizedScopeText))
            {
                return DebugRuntimeLogScope.Empty;
            }

            string previousScopeText = AmbientScopeText.Value ?? "";
            DebugRuntimeLogScopeMetrics previousScopeMetrics = AmbientScopeMetrics.Value;
            AmbientScopeText.Value = string.IsNullOrWhiteSpace(previousScopeText)
                ? normalizedScopeText
                : $"{previousScopeText} {normalizedScopeText}";
            AmbientScopeMetrics.Value ??= new DebugRuntimeLogScopeMetrics();
            return new DebugRuntimeLogScope(previousScopeText, previousScopeMetrics);
        }

        internal static string GetAmbientScopeTextForTesting()
        {
            return AmbientScopeText.Value ?? "";
        }

        internal static string GetCurrentScopeText()
        {
            return AmbientScopeText.Value ?? "";
        }

        internal static void RecordCatalogCacheHit()
        {
            AmbientScopeMetrics.Value?.RecordCatalogCacheHit();
        }

        internal static void RecordCatalogCacheMiss()
        {
            AmbientScopeMetrics.Value?.RecordCatalogCacheMiss();
        }

        internal static void RecordCatalogLoadCore(int reusedCount, int skippedCount)
        {
            AmbientScopeMetrics.Value?.RecordCatalogLoadCore(reusedCount, skippedCount);
        }

        internal static void RecordCatalogSignatureElapsed(double elapsedMilliseconds)
        {
            AmbientScopeMetrics.Value?.RecordCatalogSignatureElapsed(elapsedMilliseconds);
        }

        internal static void RecordCatalogLoadElapsed(double elapsedMilliseconds)
        {
            AmbientScopeMetrics.Value?.RecordCatalogLoadElapsed(elapsedMilliseconds);
        }

        internal static void RecordSkinDbPersistQueued()
        {
            AmbientScopeMetrics.Value?.RecordSkinDbPersistQueued();
        }

        internal static void RecordSkinDbPersistFallbackApplied()
        {
            AmbientScopeMetrics.Value?.RecordSkinDbPersistFallbackApplied();
        }

        internal static string BuildCurrentScopeMetricSummary()
        {
            return AmbientScopeMetrics.Value?.BuildSummaryText() ?? "";
        }

        internal static string BuildLineForTesting(
            DateTime localNow,
            string category,
            string message,
            long sequence,
            string scopeText = ""
        )
        {
            // ログは1行で追える方が見返しやすいので、改行やタブはここで潰しておく。
            string normalizedCategory = NormalizeInlineText(category);
            string normalizedMessage = ComposeScopedMessage(message, scopeText);
            return $"{localNow:yyyy-MM-dd HH:mm:ss.fff} #{sequence:D6} [{normalizedCategory}] {normalizedMessage}";
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

        // request 単位の trace を async フローへぶら下げ、別カテゴリでも同じ流れを追えるようにする。
        private static string ComposeScopedMessage(string message, string scopeText)
        {
            string normalizedMessage = NormalizeInlineText(message);
            string normalizedScopeText = NormalizeInlineText(scopeText);
            if (string.IsNullOrWhiteSpace(normalizedScopeText))
            {
                return normalizedMessage;
            }

            if (string.IsNullOrWhiteSpace(normalizedMessage))
            {
                return normalizedScopeText;
            }

            return $"{normalizedScopeText} {normalizedMessage}";
        }

        private sealed class DebugRuntimeLogScope : IDisposable
        {
            internal static readonly DebugRuntimeLogScope Empty = new("");

            private readonly string _previousScopeText;
            private readonly DebugRuntimeLogScopeMetrics _previousScopeMetrics;
            private int _disposed;

            internal DebugRuntimeLogScope(string previousScopeText, DebugRuntimeLogScopeMetrics previousScopeMetrics = null)
            {
                _previousScopeText = previousScopeText ?? "";
                _previousScopeMetrics = previousScopeMetrics;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                AmbientScopeText.Value = _previousScopeText;
                AmbientScopeMetrics.Value = _previousScopeMetrics;
            }
        }

        private sealed class DebugRuntimeLogScopeMetrics
        {
            private int _catalogCacheHitCount;
            private int _catalogCacheMissCount;
            private int _catalogReusedCount;
            private int _catalogSkippedCount;
            private double _catalogSignatureElapsedMilliseconds;
            private double _catalogLoadElapsedMilliseconds;
            private int _skinDbPersistQueuedCount;
            private int _skinDbPersistFallbackAppliedCount;

            internal void RecordCatalogCacheHit()
            {
                _catalogCacheHitCount++;
            }

            internal void RecordCatalogCacheMiss()
            {
                _catalogCacheMissCount++;
            }

            internal void RecordCatalogLoadCore(int reusedCount, int skippedCount)
            {
                _catalogReusedCount += Math.Max(0, reusedCount);
                _catalogSkippedCount += Math.Max(0, skippedCount);
            }

            internal void RecordCatalogSignatureElapsed(double elapsedMilliseconds)
            {
                _catalogSignatureElapsedMilliseconds += Math.Max(0, elapsedMilliseconds);
            }

            internal void RecordCatalogLoadElapsed(double elapsedMilliseconds)
            {
                _catalogLoadElapsedMilliseconds += Math.Max(0, elapsedMilliseconds);
            }

            internal void RecordSkinDbPersistQueued()
            {
                _skinDbPersistQueuedCount++;
            }

            internal void RecordSkinDbPersistFallbackApplied()
            {
                _skinDbPersistFallbackAppliedCount++;
            }

            internal string BuildSummaryText()
            {
                List<string> parts = [];
                if (_catalogCacheHitCount > 0)
                {
                    parts.Add($"catalog_hit={_catalogCacheHitCount}");
                }

                if (_catalogCacheMissCount > 0)
                {
                    parts.Add($"catalog_miss={_catalogCacheMissCount}");
                }

                if (_skinDbPersistQueuedCount > 0)
                {
                    parts.Add($"persist_enqueued={_skinDbPersistQueuedCount}");
                }

                if (_skinDbPersistFallbackAppliedCount > 0)
                {
                    parts.Add($"persist_fallback_applied={_skinDbPersistFallbackAppliedCount}");
                }

                if (_catalogReusedCount > 0)
                {
                    parts.Add($"catalog_reused={_catalogReusedCount}");
                }

                if (_catalogSkippedCount > 0)
                {
                    parts.Add($"catalog_skipped={_catalogSkippedCount}");
                }

                if (_catalogSignatureElapsedMilliseconds > 0)
                {
                    parts.Add($"catalog_signature_ms={_catalogSignatureElapsedMilliseconds:F1}");
                }

                if (_catalogLoadElapsedMilliseconds > 0)
                {
                    parts.Add($"catalog_load_ms={_catalogLoadElapsedMilliseconds:F1}");
                }

                return string.Join(" ", parts);
            }
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

        private static string NormalizeInlineText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return value
                .Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();
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
