using System.Text;
using System.Text.Json;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
        // direct / child の専用入口は本流 orchestration から切り離し、読める塊にする。
        private async Task<int> RunDirectIndexRepairAsync(DirectIndexRepairRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.MoviePath) || !File.Exists(request.MoviePath))
            {
                Console.Error.WriteLine($"direct index repair movie not found: {request.MoviePath}");
                return 2;
            }

            string moviePath = Path.GetFullPath(request.MoviePath);
            bool keepRepairedMovieFile = true;
            string repairedMoviePath = BuildRepairOutputPath(moviePath, keepRepairedMovieFile);
            VideoIndexRepairService repairService = new();
            TimeSpan repairTimeout = ResolveRepairTimeout();

            Console.WriteLine(
                $"direct index repair start: movie='{moviePath}' output='{repairedMoviePath}' timeout_sec={repairTimeout.TotalSeconds:0}"
            );

            try
            {
                VideoIndexRepairResult repairResult = await RunWithTimeoutAsync(
                        cts => repairService.RepairAsync(moviePath, repairedMoviePath, cts),
                        repairTimeout,
                        $"direct index repair timeout: movie='{moviePath}'"
                    )
                    .ConfigureAwait(false);

                if (repairResult.IsSuccess && File.Exists(repairResult.OutputPath))
                {
                    if (
                        !TryValidateDirectIndexRepairOutput(
                            moviePath,
                            repairResult.OutputPath,
                            out string validationFailureReason
                        )
                    )
                    {
                        Console.WriteLine(
                            $"direct index repair failed: movie='{moviePath}' output='{repairResult.OutputPath}' reason='{validationFailureReason}'"
                        );
                        return 1;
                    }

                    Console.WriteLine(
                        $"direct index repair succeeded: movie='{moviePath}' repaired='{repairResult.OutputPath}'"
                    );
                    return 0;
                }

                string failureReason = string.IsNullOrWhiteSpace(repairResult.ErrorMessage)
                    ? "direct index repair failed"
                    : repairResult.ErrorMessage;
                Console.WriteLine(
                    $"direct index repair failed: movie='{moviePath}' output='{repairedMoviePath}' reason='{failureReason}'"
                );
                return 1;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine(
                    $"direct index repair failed: movie='{moviePath}' output='{repairedMoviePath}' reason='timeout'"
                );
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"direct index repair failed: movie='{moviePath}' output='{repairedMoviePath}' reason='{ex.Message}'"
                );
                return 1;
            }
        }

        // repair 動画は残したまま、中身が極端に欠けている個体だけ成功扱いを止める。
        private static bool TryValidateDirectIndexRepairOutput(
            string inputMoviePath,
            string repairedMoviePath,
            out string failureReason
        )
        {
            failureReason = "";
            long inputBytes = TryGetMovieFileLength(inputMoviePath);
            long outputBytes = TryGetMovieFileLength(repairedMoviePath);
            long minAcceptedBytes = inputBytes > 0
                ? Math.Max(
                    DirectIndexRepairMinOutputBytes,
                    (long)Math.Ceiling(inputBytes * DirectIndexRepairMinOutputRatio)
                )
                : DirectIndexRepairMinOutputBytes;

            if (outputBytes < minAcceptedBytes)
            {
                failureReason =
                    $"repaired file too small: input_bytes={inputBytes} output_bytes={outputBytes} min_bytes={minAcceptedBytes}";
                return false;
            }

            double? repairedDurationSec = TryProbeDurationSecWithFfprobe(repairedMoviePath);
            if (!repairedDurationSec.HasValue || repairedDurationSec.Value <= 0d)
            {
                failureReason = "repaired duration probe failed";
                return false;
            }

            return true;
        }

        private static async Task<int> RunIsolatedAttemptChildAsync(
            IsolatedEngineAttemptRequest request
        )
        {
            ThumbnailCreateResult result;

            try
            {
                Environment.SetEnvironmentVariable(ThumbnailEnvConfig.ThumbEngine, request.EngineId);
                IThumbnailCreationService thumbnailCreationService =
                    RescueWorkerThumbnailCreationServiceFactory.Create(request.LogDirectoryPath);
                QueueObj queueObj = new()
                {
                    MovieFullPath = request.MoviePath ?? "",
                    MovieSizeBytes = Math.Max(0, request.MovieSizeBytes),
                    Tabindex = request.TabIndex,
                };
                ThumbnailCreateArgs createArgs =
                    ThumbnailCreateArgsCompatibility.FromLegacyQueueObj(
                        queueObj,
                        dbName: request.DbName,
                        thumbFolder: request.ThumbFolder,
                        isResizeThumb: false,
                        isManual: false,
                        sourceMovieFullPathOverride: string.Equals(
                            request.SourceMoviePath,
                            request.MoviePath,
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? null
                            : request.SourceMoviePath,
                        traceId: request.TraceId,
                        thumbInfoOverride: BuildThumbInfoFromCsv(
                            request.TabIndex,
                            request.DbName,
                            request.ThumbFolder,
                            request.ThumbSecCsv
                        )
                    );
                try
                {
                    result = await thumbnailCreationService
                        .CreateThumbAsync(createArgs, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                finally
                {
                    ThumbnailCreateArgsCompatibility.ApplyBackToLegacyQueueObj(
                        createArgs,
                        queueObj
                    );
                }
            }
            catch (Exception ex)
            {
                result = new ThumbnailCreateResult
                {
                    SaveThumbFileName = "",
                    DurationSec = null,
                    IsSuccess = false,
                    ErrorMessage = ex.Message ?? "isolated engine attempt failed",
                };
            }

            IsolatedEngineAttemptResultPayload payload = new()
            {
                SaveThumbFileName = result.SaveThumbFileName ?? "",
                DurationSec = result.DurationSec,
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage ?? "",
            };
            string json = JsonSerializer.Serialize(payload);
            Directory.CreateDirectory(
                Path.GetDirectoryName(request.ResultJsonPath) ?? Path.GetTempPath()
            );
            await File.WriteAllTextAsync(
                    request.ResultJsonPath,
                    json,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                )
                .ConfigureAwait(false);
            return payload.IsSuccess ? 0 : 1;
        }

        private static IsolatedEngineAttemptResultPayload TryReadIsolatedAttemptResult(
            string resultJsonPath
        )
        {
            try
            {
                if (!File.Exists(resultJsonPath))
                {
                    return null;
                }

                string json = File.ReadAllText(resultJsonPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<IsolatedEngineAttemptResultPayload>(json);
            }
            catch
            {
                return null;
            }
        }

        internal static string BuildIsolatedAttemptFailureMessage(
            int exitCode,
            string stdout,
            string stderr,
            string engineId
        )
        {
            string detail = FirstNonEmptyLine(stderr) ?? FirstNonEmptyLine(stdout) ?? "";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return $"isolated engine attempt failed: engine={engineId}, exit_code={exitCode}, detail={detail}";
            }

            return $"isolated engine attempt failed: engine={engineId}, exit_code={exitCode}";
        }

        private static string FirstNonEmptyLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            using StringReader reader = new(text);
            while (true)
            {
                string line = reader.ReadLine();
                if (line == null)
                {
                    return "";
                }

                string trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    return trimmed;
                }
            }
        }
    }
}
