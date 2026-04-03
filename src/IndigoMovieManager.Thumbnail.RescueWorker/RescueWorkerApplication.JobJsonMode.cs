using System.Reflection;
using System.Text;
using System.Text.Json;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
        private const string JobJsonModeCommand = "rescue";
        private const string JobJsonModeValue = "rescue-main";
        private const string JobJsonContractVersion = "1";
        private const string ArtifactManifestFileName = "rescue-worker-artifact.json";
        private const string ProcessLogFileName = "thumbnail-create-process.csv";
        private const string RescueTraceFileName = "thumbnail-rescue-trace.csv";
        private const string JobJsonModeUsageText =
            "usage: IndigoMovieManager.Thumbnail.RescueWorker rescue --job-json <path> --result-json <path>";
        private static readonly JsonSerializerOptions JobJsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        internal static bool IsJobJsonModeCommand(string[] args)
        {
            return args != null
                && args.Length > 0
                && string.Equals(args[0], JobJsonModeCommand, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryParseJobJsonArguments(
            string[] args,
            out string jobJsonPath,
            out string resultJsonPath
        )
        {
            jobJsonPath = "";
            resultJsonPath = "";
            if (!IsJobJsonModeCommand(args))
            {
                return false;
            }

            for (int i = 1; i < args.Length; i++)
            {
                if (
                    string.Equals(args[i], "--job-json", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    jobJsonPath = args[++i] ?? "";
                    continue;
                }

                if (
                    string.Equals(args[i], "--result-json", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    resultJsonPath = args[++i] ?? "";
                }
            }

            return !string.IsNullOrWhiteSpace(jobJsonPath)
                && !string.IsNullOrWhiteSpace(resultJsonPath);
        }

        private async Task<int> RunJobJsonModeAsync(string jobJsonPath, string resultJsonPath)
        {
            DateTimeOffset startedAt = DateTimeOffset.Now;
            string normalizedResultJsonPath = NormalizePathOrEmpty(resultJsonPath);
            RescueWorkerMainJobContract request = new();

            try
            {
                if (
                    !TryReadMainJobContract(
                        jobJsonPath,
                        out request,
                        out string contractErrorCode,
                        out string contractErrorMessage
                    )
                )
                {
                    Console.Error.WriteLine(contractErrorMessage);
                    await WriteJobJsonResultAsync(
                            normalizedResultJsonPath,
                            BuildContractFailureJobResult(
                                request.RequestId,
                                contractErrorCode,
                                contractErrorMessage,
                                startedAt,
                                DateTimeOffset.Now,
                                request.LogDirectoryPath
                            )
                        )
                        .ConfigureAwait(false);
                    return 2;
                }

                int exitCode = await RunMainRescueAsync(
                        request.MainDbFullPath,
                        request.ThumbFolderOverride,
                        request.LogDirectoryPath,
                        request.FailureDbDirectoryPath,
                        request.RequestedFailureId
                    )
                    .ConfigureAwait(false);

                await WriteJobJsonResultAsync(
                        normalizedResultJsonPath,
                        BuildMainJobResult(request, exitCode, startedAt, DateTimeOffset.Now)
                    )
                    .ConfigureAwait(false);
                return exitCode;
            }
            catch (Exception ex)
            {
                string message = $"RescueWorker crashed: {ex.Message}";
                Console.Error.WriteLine(message);
                await WriteJobJsonResultAsync(
                        normalizedResultJsonPath,
                        BuildContractFailureJobResult(
                            request.RequestId,
                            "WORKER_EXCEPTION",
                            message,
                            startedAt,
                            DateTimeOffset.Now,
                            request.LogDirectoryPath
                        )
                    )
                    .ConfigureAwait(false);
                return 1;
            }
        }

        internal static bool TryReadMainJobContract(
            string jobJsonPath,
            out RescueWorkerMainJobContract request,
            out string errorCode,
            out string errorMessage
        )
        {
            request = new();
            errorCode = "";
            errorMessage = "";
            string normalizedJobJsonPath = NormalizePathOrEmpty(jobJsonPath);
            if (string.IsNullOrWhiteSpace(normalizedJobJsonPath))
            {
                errorCode = "JOB_JSON_PATH_MISSING";
                errorMessage = "job json path is missing.";
                return false;
            }

            if (!File.Exists(normalizedJobJsonPath))
            {
                errorCode = "JOB_JSON_NOT_FOUND";
                errorMessage = $"job json not found: {normalizedJobJsonPath}";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(normalizedJobJsonPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                errorCode = "JOB_JSON_READ_FAILED";
                errorMessage = $"job json read failed: {ex.Message}";
                return false;
            }

            try
            {
                request =
                    JsonSerializer.Deserialize<RescueWorkerMainJobContract>(
                        json,
                        JobJsonSerializerOptions
                    ) ?? new();
            }
            catch (Exception ex)
            {
                errorCode = "JOB_JSON_INVALID";
                errorMessage = $"job json deserialize failed: {ex.Message}";
                return false;
            }

            string jobJsonDirectoryPath =
                Path.GetDirectoryName(normalizedJobJsonPath) ?? Directory.GetCurrentDirectory();
            request.ContractVersion = (request.ContractVersion ?? "").Trim();
            request.Mode = (request.Mode ?? "").Trim();
            request.RequestId = (request.RequestId ?? "").Trim();
            request.MainDbFullPath = NormalizePathFromBaseOrEmpty(
                request.MainDbFullPath,
                jobJsonDirectoryPath
            );
            request.ThumbFolderOverride = NormalizePathFromBaseOrEmpty(
                request.ThumbFolderOverride,
                jobJsonDirectoryPath
            );
            request.LogDirectoryPath = NormalizePathFromBaseOrEmpty(
                request.LogDirectoryPath,
                jobJsonDirectoryPath
            );
            request.FailureDbDirectoryPath = NormalizePathFromBaseOrEmpty(
                request.FailureDbDirectoryPath,
                jobJsonDirectoryPath
            );

            if (!string.Equals(request.ContractVersion, JobJsonContractVersion, StringComparison.Ordinal))
            {
                errorCode = "JOB_CONTRACT_VERSION_UNSUPPORTED";
                errorMessage =
                    $"job contractVersion is unsupported: '{request.ContractVersion}'";
                return false;
            }

            if (!string.Equals(request.Mode, JobJsonModeValue, StringComparison.Ordinal))
            {
                errorCode = "JOB_MODE_UNSUPPORTED";
                errorMessage = $"job mode is unsupported: '{request.Mode}'";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                errorCode = "JOB_REQUEST_ID_MISSING";
                errorMessage = "job requestId is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.MainDbFullPath))
            {
                errorCode = "JOB_MAIN_DB_MISSING";
                errorMessage = "job mainDbFullPath is missing.";
                return false;
            }

            if (request.RequestedFailureId < 0)
            {
                errorCode = "JOB_FAILURE_ID_INVALID";
                errorMessage = $"job requestedFailureId is invalid: {request.RequestedFailureId}";
                return false;
            }

            return true;
        }

        internal static RescueWorkerMainJobResult BuildMainJobResult(
            RescueWorkerMainJobContract request,
            int exitCode,
            DateTimeOffset startedAt,
            DateTimeOffset finishedAt
        )
        {
            string status = exitCode == 0 ? "success" : "failed";
            string resultCode = exitCode switch
            {
                0 => "OK",
                2 => "INVALID_INPUT",
                _ => "RESCUE_FAILED",
            };
            string message = exitCode switch
            {
                0 => "RescueWorker completed.",
                2 => "RescueWorker rejected the request.",
                _ => "RescueWorker failed.",
            };

            List<RescueWorkerMainJobError> errors = [];
            if (exitCode != 0)
            {
                errors.Add(
                    new RescueWorkerMainJobError
                    {
                        Code = resultCode,
                        Message = message,
                    }
                );
            }

            return CreateMainJobResult(
                request?.RequestId ?? "",
                status,
                resultCode,
                message,
                request?.LogDirectoryPath ?? "",
                startedAt,
                finishedAt,
                errors
            );
        }

        internal static IReadOnlyList<RescueWorkerMainJobArtifact> BuildMainJobArtifacts(
            string logDirectoryPath
        )
        {
            List<RescueWorkerMainJobArtifact> artifacts = [];
            string normalizedLogDirectoryPath = NormalizePathOrEmpty(logDirectoryPath);
            if (!string.IsNullOrWhiteSpace(normalizedLogDirectoryPath))
            {
                artifacts.Add(
                    new RescueWorkerMainJobArtifact
                    {
                        Type = "process-log",
                        Path = Path.Combine(normalizedLogDirectoryPath, ProcessLogFileName),
                    }
                );
                artifacts.Add(
                    new RescueWorkerMainJobArtifact
                    {
                        Type = "rescue-trace",
                        Path = Path.Combine(normalizedLogDirectoryPath, RescueTraceFileName),
                    }
                );
            }

            string manifestPath = Path.Combine(AppContext.BaseDirectory, ArtifactManifestFileName);
            if (File.Exists(manifestPath))
            {
                artifacts.Add(
                    new RescueWorkerMainJobArtifact
                    {
                        Type = "worker-artifact-manifest",
                        Path = manifestPath,
                    }
                );
            }

            return artifacts;
        }

        private static RescueWorkerMainJobResult BuildContractFailureJobResult(
            string requestId,
            string resultCode,
            string message,
            DateTimeOffset startedAt,
            DateTimeOffset finishedAt,
            string logDirectoryPath
        )
        {
            return CreateMainJobResult(
                requestId,
                status: "failed",
                resultCode,
                message,
                logDirectoryPath,
                startedAt,
                finishedAt,
                [
                    new RescueWorkerMainJobError
                    {
                        Code = resultCode,
                        Message = message,
                    },
                ]
            );
        }

        private static RescueWorkerMainJobResult CreateMainJobResult(
            string requestId,
            string status,
            string resultCode,
            string message,
            string logDirectoryPath,
            DateTimeOffset startedAt,
            DateTimeOffset finishedAt,
            IReadOnlyList<RescueWorkerMainJobError> errors
        )
        {
            return new RescueWorkerMainJobResult
            {
                ContractVersion = JobJsonContractVersion,
                Mode = JobJsonModeValue,
                RequestId = requestId ?? "",
                Status = status ?? "",
                ResultCode = resultCode ?? "",
                Message = message ?? "",
                EngineVersion = ResolveWorkerEngineVersion(),
                CompatibilityVersion = RescueWorkerArtifactContract.CompatibilityVersion,
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                Artifacts = BuildMainJobArtifacts(logDirectoryPath).ToList(),
                Errors = errors?.ToList() ?? [],
            };
        }

        private static async Task WriteJobJsonResultAsync(
            string resultJsonPath,
            RescueWorkerMainJobResult result
        )
        {
            if (string.IsNullOrWhiteSpace(resultJsonPath))
            {
                return;
            }

            string normalizedResultJsonPath = NormalizePathOrEmpty(resultJsonPath);
            if (string.IsNullOrWhiteSpace(normalizedResultJsonPath))
            {
                return;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(normalizedResultJsonPath) ?? Path.GetTempPath()
            );
            string json = JsonSerializer.Serialize(
                result ?? new RescueWorkerMainJobResult(),
                JobJsonSerializerOptions
            );
            await File.WriteAllTextAsync(
                    normalizedResultJsonPath,
                    json,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                )
                .ConfigureAwait(false);
        }

        private static string NormalizePathOrEmpty(string path)
        {
            string trimmed = path?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return "";
            }

            return Path.GetFullPath(trimmed);
        }

        private static string NormalizePathFromBaseOrEmpty(string path, string baseDirectoryPath)
        {
            string trimmed = path?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return "";
            }

            if (Path.IsPathRooted(trimmed))
            {
                return Path.GetFullPath(trimmed);
            }

            string normalizedBaseDirectoryPath = string.IsNullOrWhiteSpace(baseDirectoryPath)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(baseDirectoryPath);
            return Path.GetFullPath(Path.Combine(normalizedBaseDirectoryPath, trimmed));
        }

        private static string ResolveWorkerEngineVersion()
        {
            Assembly assembly = typeof(RescueWorkerApplication).Assembly;
            string informationalVersion =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion ?? "";
            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion.Split('+', 2)[0];
            }

            Version version = assembly.GetName().Version;
            if (version == null)
            {
                return "0.0.0";
            }

            return version.Build >= 0
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : $"{version.Major}.{version.Minor}";
        }

        internal sealed class RescueWorkerMainJobContract
        {
            public string ContractVersion { get; set; } = "";

            public string Mode { get; set; } = "";

            public string RequestId { get; set; } = "";

            public string MainDbFullPath { get; set; } = "";

            public string ThumbFolderOverride { get; set; } = "";

            public string LogDirectoryPath { get; set; } = "";

            public string FailureDbDirectoryPath { get; set; } = "";

            public long RequestedFailureId { get; set; }

            public Dictionary<string, string> Metadata { get; set; } = [];
        }

        internal sealed class RescueWorkerMainJobResult
        {
            public string ContractVersion { get; set; } = "";

            public string Mode { get; set; } = "";

            public string RequestId { get; set; } = "";

            public string Status { get; set; } = "";

            public string ResultCode { get; set; } = "";

            public string Message { get; set; } = "";

            public string EngineVersion { get; set; } = "";

            public string CompatibilityVersion { get; set; } = "";

            public DateTimeOffset StartedAt { get; set; }

            public DateTimeOffset FinishedAt { get; set; }

            public List<RescueWorkerMainJobArtifact> Artifacts { get; set; } = [];

            public List<RescueWorkerMainJobError> Errors { get; set; } = [];
        }

        internal sealed class RescueWorkerMainJobArtifact
        {
            public string Type { get; set; } = "";

            public string Path { get; set; } = "";
        }

        internal sealed class RescueWorkerMainJobError
        {
            public string Code { get; set; } = "";

            public string Message { get; set; } = "";

            public string Target { get; set; } = "";
        }
    }
}
