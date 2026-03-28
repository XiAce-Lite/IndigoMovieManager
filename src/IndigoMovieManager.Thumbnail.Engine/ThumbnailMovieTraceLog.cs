using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 動画単位の詳細トレースは専用ファイルへ分離し、既存 runtime log を膨らませない。
    /// </summary>
    public static class ThumbnailMovieTraceLog
    {
        public const string FileName = "thumbnail-movie-trace.ndjson";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        private static string configuredLogDirectoryPath = "";

        [Conditional("DEBUG")]
        public static void ConfigureLogDirectory(string logDirectoryPath)
        {
            Interlocked.Exchange(ref configuredLogDirectoryPath, logDirectoryPath?.Trim() ?? "");
        }

        [Conditional("DEBUG")]
        public static void Write(
            string traceId,
            string source,
            string phase,
            string moviePath = "",
            string sourceMoviePath = "",
            int tabIndex = -1,
            string engine = "",
            string result = "",
            string detail = "",
            string outputPath = "",
            string routeId = "",
            string symptomClass = "",
            long failureId = 0,
            int attemptNo = 0,
            double? durationSec = null,
            long fileSizeBytes = 0,
            string processEngineId = ""
        )
        {
            if (!ThumbnailMovieTraceRuntime.IsEnabled())
            {
                return;
            }

            string normalizedTraceId = ThumbnailMovieTraceRuntime.NormalizeTraceId(traceId);
            if (string.IsNullOrWhiteSpace(normalizedTraceId))
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
                using Mutex mutex = new(
                    false,
                    AppIdentityRuntime.BuildLocalMutexName("thumbnail_movie_trace")
                );
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

                    ThumbnailMovieTraceEntry entry = new()
                    {
                        TimestampUtc = DateTime.UtcNow,
                        ProcessId = Environment.ProcessId,
                        TraceId = normalizedTraceId,
                        Source = source ?? "",
                        Phase = phase ?? "",
                        MoviePath = moviePath ?? "",
                        SourceMoviePath = sourceMoviePath ?? "",
                        TabIndex = tabIndex >= 0 ? tabIndex : null,
                        Engine = engine ?? "",
                        Result = result ?? "",
                        Detail = detail ?? "",
                        OutputPath = outputPath ?? "",
                        RouteId = routeId ?? "",
                        SymptomClass = symptomClass ?? "",
                        FailureId = failureId > 0 ? failureId : null,
                        AttemptNo = attemptNo > 0 ? attemptNo : null,
                        DurationSec = durationSec.HasValue && durationSec.Value > 0
                            ? durationSec
                            : null,
                        FileSizeBytes = fileSizeBytes > 0 ? fileSizeBytes : null,
                        ProcessEngineId = processEngineId ?? "",
                    };

                    using FileStream stream = new(
                        logPath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite
                    );
                    using StreamWriter writer = new(stream, new UTF8Encoding(false));
                    writer.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));
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
                // Debug 専用なので、本体処理は止めない。
            }
        }

        internal static string ResolveLogDirectoryPath()
        {
            string configured = Interlocked.CompareExchange(ref configuredLogDirectoryPath, "", "");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            string envPath =
                Environment.GetEnvironmentVariable(
                    ThumbnailMovieTraceRuntime.TraceLogDirectoryEnvName
                )?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                return envPath;
            }

            string baseDir = string.IsNullOrWhiteSpace(AppContext.BaseDirectory)
                ? Directory.GetCurrentDirectory()
                : AppContext.BaseDirectory;
            return Path.Combine(baseDir, "logs");
        }

        private sealed class ThumbnailMovieTraceEntry
        {
            [JsonPropertyName("ts_utc")]
            public DateTime TimestampUtc { get; init; }

            [JsonPropertyName("pid")]
            public int ProcessId { get; init; }

            [JsonPropertyName("trace_id")]
            public string TraceId { get; init; } = "";

            [JsonPropertyName("source")]
            public string Source { get; init; } = "";

            [JsonPropertyName("phase")]
            public string Phase { get; init; } = "";

            [JsonPropertyName("movie_path")]
            public string MoviePath { get; init; } = "";

            [JsonPropertyName("source_movie_path")]
            public string SourceMoviePath { get; init; } = "";

            [JsonPropertyName("tab_index")]
            public int? TabIndex { get; init; }

            [JsonPropertyName("engine")]
            public string Engine { get; init; } = "";

            [JsonPropertyName("result")]
            public string Result { get; init; } = "";

            [JsonPropertyName("detail")]
            public string Detail { get; init; } = "";

            [JsonPropertyName("output_path")]
            public string OutputPath { get; init; } = "";

            [JsonPropertyName("route_id")]
            public string RouteId { get; init; } = "";

            [JsonPropertyName("symptom_class")]
            public string SymptomClass { get; init; } = "";

            [JsonPropertyName("failure_id")]
            public long? FailureId { get; init; }

            [JsonPropertyName("attempt_no")]
            public int? AttemptNo { get; init; }

            [JsonPropertyName("duration_sec")]
            public double? DurationSec { get; init; }

            [JsonPropertyName("file_size_bytes")]
            public long? FileSizeBytes { get; init; }

            [JsonPropertyName("process_engine_id")]
            public string ProcessEngineId { get; init; } = "";
        }
    }
}
