using System.Diagnostics;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 動画単位の詳細トレースを、Debug + 環境変数指定時だけ有効にする。
    /// </summary>
    public static class ThumbnailMovieTraceRuntime
    {
        public const string TraceEnvName = "IMM_THUMB_MOVIE_TRACE";
        public const string TraceFilterEnvName = "IMM_THUMB_MOVIE_TRACE_FILTER";
        public const string TraceLogDirectoryEnvName = "IMM_THUMB_MOVIE_TRACE_LOG_DIR";

        public static bool IsEnabled()
        {
#if DEBUG
            return IsEnabledValue(Environment.GetEnvironmentVariable(TraceEnvName));
#else
            return false;
#endif
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

        public static string TryCreateTraceId(
            string movieFullPath,
            string sourceMovieFullPath = "",
            string existingTraceId = ""
        )
        {
#if !DEBUG
            return "";
#else
            string normalizedExistingTraceId = NormalizeTraceId(existingTraceId);
            if (!string.IsNullOrWhiteSpace(normalizedExistingTraceId))
            {
                return normalizedExistingTraceId;
            }

            if (!IsEnabled())
            {
                return "";
            }

            string filterRaw =
                Environment.GetEnvironmentVariable(TraceFilterEnvName)?.Trim() ?? "";
            if (!ShouldTraceMovie(movieFullPath, sourceMovieFullPath, filterRaw))
            {
                return "";
            }

            return Guid.NewGuid().ToString("N");
#endif
        }

        [Conditional("DEBUG")]
        public static void ConfigureLogDirectoryFromHost(string defaultLogDirectoryPath)
        {
            string configuredLogDirectoryPath =
                Environment.GetEnvironmentVariable(TraceLogDirectoryEnvName)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(configuredLogDirectoryPath))
            {
                configuredLogDirectoryPath = defaultLogDirectoryPath?.Trim() ?? "";
            }

            if (!string.IsNullOrWhiteSpace(configuredLogDirectoryPath))
            {
                ThumbnailMovieTraceLog.ConfigureLogDirectory(configuredLogDirectoryPath);
            }
        }

        internal static bool ShouldTraceMovie(
            string movieFullPath,
            string sourceMovieFullPath,
            string filterRaw
        )
        {
            if (string.IsNullOrWhiteSpace(filterRaw))
            {
                return true;
            }

            string[] filters = (filterRaw ?? "")
                .Split([';', '\r', '\n', '|'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (filters.Length < 1)
            {
                return true;
            }

            return MatchesAnyFilter(movieFullPath, filters)
                || MatchesAnyFilter(sourceMovieFullPath, filters);
        }

        public static string NormalizeTraceId(string traceId)
        {
            if (string.IsNullOrWhiteSpace(traceId))
            {
                return "";
            }

            string normalized = traceId.Trim();
            return normalized.Length > 128 ? normalized[..128] : normalized;
        }

        private static bool MatchesAnyFilter(string candidatePath, IReadOnlyList<string> filters)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || filters == null || filters.Count < 1)
            {
                return false;
            }

            string normalizedPath = candidatePath.Trim();
            string fileName = Path.GetFileName(normalizedPath);

            for (int i = 0; i < filters.Count; i++)
            {
                string filter = filters[i];
                if (string.IsNullOrWhiteSpace(filter))
                {
                    continue;
                }

                if (string.Equals(normalizedPath, filter, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (
                    !string.IsNullOrWhiteSpace(fileName)
                    && string.Equals(fileName, filter, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
}
