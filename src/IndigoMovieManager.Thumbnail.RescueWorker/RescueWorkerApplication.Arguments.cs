using System.Globalization;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
        // worker 起動直後の分岐を薄く保つため、CLI 復元だけを別 partial へ寄せる。
        internal static bool TryParseArguments(
            string[] args,
            out string mainDbFullPath,
            out string thumbFolderOverride,
            out string logDirectoryPath,
            out string failureDbDirectoryPath,
            out long requestedFailureId
        )
        {
            mainDbFullPath = "";
            thumbFolderOverride = "";
            logDirectoryPath = "";
            failureDbDirectoryPath = "";
            requestedFailureId = 0;
            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                if (
                    string.Equals(args[i], "--main-db", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    mainDbFullPath = args[i + 1] ?? "";
                    i++;
                    continue;
                }

                if (
                    string.Equals(args[i], "--thumb-folder", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    thumbFolderOverride = args[i + 1] ?? "";
                    i++;
                    continue;
                }

                if (
                    string.Equals(args[i], "--log-dir", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    logDirectoryPath = args[i + 1] ?? "";
                    i++;
                    continue;
                }

                if (
                    string.Equals(args[i], "--failure-db-dir", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    failureDbDirectoryPath = args[i + 1] ?? "";
                    i++;
                    continue;
                }

                if (
                    string.Equals(args[i], "--failure-id", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    _ = long.TryParse(
                        args[i + 1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out requestedFailureId
                    );
                    i++;
                }
            }

            return !string.IsNullOrWhiteSpace(mainDbFullPath);
        }

        internal static bool TryParseDirectIndexRepairArguments(
            string[] args,
            out DirectIndexRepairRequest request
        )
        {
            string moviePath = "";
            string logDirectoryPath = "";

            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                if (
                    string.Equals(args[i], "--movie", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    moviePath = args[i + 1] ?? "";
                    i++;
                    continue;
                }

                if (
                    string.Equals(args[i], "--log-dir", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    logDirectoryPath = args[i + 1] ?? "";
                    i++;
                }
            }

            request = new DirectIndexRepairRequest(moviePath, logDirectoryPath);
            return !string.IsNullOrWhiteSpace(moviePath);
        }

        internal static bool TryParseIsolatedAttemptArguments(
            string[] args,
            out IsolatedEngineAttemptRequest request
        )
        {
            string engineId = "";
            string moviePath = "";
            string sourceMoviePath = "";
            string dbName = "";
            string thumbFolder = "";
            string thumbSecCsv = "";
            string resultJsonPath = "";
            string logDirectoryPath = "";
            string traceId = "";
            int tabIndex = 0;
            long movieSizeBytes = 0;

            request = default;
            if (!HasArgument(args, AttemptChildModeArg))
            {
                return false;
            }

            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                switch (args[i]?.Trim())
                {
                    case AttemptChildModeArg:
                        break;
                    case "--engine" when i + 1 < args.Length:
                        engineId = args[++i] ?? "";
                        break;
                    case "--movie" when i + 1 < args.Length:
                        moviePath = args[++i] ?? "";
                        break;
                    case "--source-movie" when i + 1 < args.Length:
                        sourceMoviePath = args[++i] ?? "";
                        break;
                    case "--db-name" when i + 1 < args.Length:
                        dbName = args[++i] ?? "";
                        break;
                    case "--thumb-folder" when i + 1 < args.Length:
                        thumbFolder = args[++i] ?? "";
                        break;
                    case "--tab-index" when i + 1 < args.Length:
                        _ = int.TryParse(args[++i], out tabIndex);
                        break;
                    case "--movie-size-bytes" when i + 1 < args.Length:
                        _ = long.TryParse(args[++i], out movieSizeBytes);
                        break;
                    case "--thumb-sec-csv" when i + 1 < args.Length:
                        thumbSecCsv = args[++i] ?? "";
                        break;
                    case "--log-dir" when i + 1 < args.Length:
                        logDirectoryPath = args[++i] ?? "";
                        break;
                    case "--result-json" when i + 1 < args.Length:
                        resultJsonPath = args[++i] ?? "";
                        break;
                    case "--trace-id" when i + 1 < args.Length:
                        traceId = args[++i] ?? "";
                        break;
                }
            }

            if (
                string.IsNullOrWhiteSpace(engineId)
                || string.IsNullOrWhiteSpace(moviePath)
                || string.IsNullOrWhiteSpace(dbName)
                || string.IsNullOrWhiteSpace(thumbFolder)
                || string.IsNullOrWhiteSpace(resultJsonPath)
            )
            {
                return false;
            }

            request = new IsolatedEngineAttemptRequest(
                engineId,
                moviePath,
                string.IsNullOrWhiteSpace(sourceMoviePath) ? moviePath : sourceMoviePath,
                dbName,
                thumbFolder,
                tabIndex,
                Math.Max(0, movieSizeBytes),
                thumbSecCsv,
                resultJsonPath,
                logDirectoryPath,
                traceId
            );
            return true;
        }

        private static string ResolveLogDirectoryPathFromArgs(string[] args)
        {
            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                if (
                    string.Equals(args[i], "--log-dir", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    return args[i + 1] ?? "";
                }
            }

            return "";
        }

        private static string ResolveFailureDbDirectoryPathFromArgs(string[] args)
        {
            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                if (
                    string.Equals(args[i], "--failure-db-dir", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    return args[i + 1] ?? "";
                }
            }

            return "";
        }

        private static bool HasArgument(string[] args, string target)
        {
            if (args == null || args.Length < 1 || string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
