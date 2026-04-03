using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using IndigoMovieManager;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
        // worker 本線の engine 実行と child process 呼び出しだけを、この partial へ残す。
        private static async Task<ThumbnailCreateResult> RunCreateThumbAttemptAsync(
            IThumbnailCreationService thumbnailCreationService,
            QueueObj queueObj,
            MainDbContext mainDbContext,
            string engineId,
            string sourceMovieFullPathOverride,
            TimeSpan timeout,
            string timeoutMessage,
            ThumbInfo thumbInfoOverride,
            string logDirectoryPath,
            string traceId = ""
        )
        {
            if (ShouldUseIsolatedChildProcess(engineId))
            {
                return await RunIsolatedEngineAttemptInChildProcessAsync(
                        queueObj,
                        mainDbContext,
                        engineId,
                        sourceMovieFullPathOverride,
                        timeout,
                        timeoutMessage,
                        thumbInfoOverride,
                        logDirectoryPath,
                        traceId
                    )
                    .ConfigureAwait(false);
            }

            // エンジン切替はプロセス環境変数を使うため、このworkerは1プロセス1動画前提で動かす。
            Environment.SetEnvironmentVariable(ThumbnailEnvConfig.ThumbEngine, engineId);
            return await RunWithTimeoutAsync(
                    cts =>
                        thumbnailCreationService.CreateThumbAsync(
                            new ThumbnailCreateArgs
                            {
                                QueueObj = queueObj,
                                DbName = mainDbContext.DbName,
                                ThumbFolder = mainDbContext.ThumbFolder,
                                IsResizeThumb = false,
                                IsManual = false,
                                SourceMovieFullPathOverride = sourceMovieFullPathOverride,
                                TraceId = traceId,
                                ThumbInfoOverride = thumbInfoOverride,
                            },
                            cts
                        ),
                    timeout,
                    timeoutMessage
                )
                .ConfigureAwait(false);
        }

        // OpenCV のように token 非対応で掴みっぱなしになる engine は子プロセスへ隔離する。
        internal static bool ShouldUseIsolatedChildProcess(string engineId)
        {
            return string.Equals(engineId, "opencv", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<ThumbnailCreateResult> RunIsolatedEngineAttemptInChildProcessAsync(
            QueueObj queueObj,
            MainDbContext mainDbContext,
            string engineId,
            string sourceMovieFullPathOverride,
            TimeSpan timeout,
            string timeoutMessage,
            ThumbInfo thumbInfoOverride,
            string logDirectoryPath,
            string traceId
        )
        {
            string currentExePath = ResolveCurrentExecutablePath();
            string resultJsonPath = Path.Combine(
                Path.GetTempPath(),
                AppIdentityRuntime.ResolveStorageRootName(),
                "thumbnail-rescue-attempt",
                $"{Guid.NewGuid():N}.json"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(resultJsonPath) ?? Path.GetTempPath());

            IsolatedEngineAttemptRequest request = new(
                engineId,
                queueObj?.MovieFullPath ?? "",
                string.IsNullOrWhiteSpace(sourceMovieFullPathOverride)
                    ? queueObj?.MovieFullPath ?? ""
                    : sourceMovieFullPathOverride,
                mainDbContext.DbName,
                mainDbContext.ThumbFolder,
                queueObj?.Tabindex ?? 0,
                Math.Max(0, queueObj?.MovieSizeBytes ?? 0),
                BuildThumbSecCsv(thumbInfoOverride),
                resultJsonPath,
                logDirectoryPath,
                traceId
            );

            ProcessStartInfo startInfo = new()
            {
                FileName = currentExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };

            foreach (string argument in BuildIsolatedAttemptArguments(request))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            using CancellationTokenSource waitCts = new();
            waitCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (waitCts.IsCancellationRequested)
            {
                TryKillProcessTree(process);
                await process.WaitForExitAsync().ConfigureAwait(false);
                _ = await stdoutTask.ConfigureAwait(false);
                _ = await stderrTask.ConfigureAwait(false);
                TryDeleteFileQuietly(resultJsonPath);
                throw new TimeoutException($"{timeoutMessage}, timeout_sec={timeout.TotalSeconds:0}");
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            IsolatedEngineAttemptResultPayload payload = TryReadIsolatedAttemptResult(resultJsonPath);
            TryDeleteFileQuietly(resultJsonPath);

            if (payload != null)
            {
                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = payload.SaveThumbFileName ?? "",
                    DurationSec = payload.DurationSec,
                    IsSuccess = payload.IsSuccess,
                    ErrorMessage = payload.ErrorMessage ?? "",
                };
            }

            return new ThumbnailCreateResult
            {
                SaveThumbFileName = "",
                DurationSec = null,
                IsSuccess = false,
                ErrorMessage = BuildIsolatedAttemptFailureMessage(
                    process.ExitCode,
                    stdout,
                    stderr,
                    engineId
                ),
            };
        }

        private static string ResolveCurrentExecutablePath()
        {
            string processPath = Environment.ProcessPath ?? "";
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                return processPath;
            }

            using Process currentProcess = Process.GetCurrentProcess();
            string fallbackPath = currentProcess.MainModule?.FileName ?? "";
            if (!string.IsNullOrWhiteSpace(fallbackPath))
            {
                return fallbackPath;
            }

            throw new InvalidOperationException("rescue worker executable path could not be resolved");
        }

        internal static IReadOnlyList<string> BuildIsolatedAttemptArguments(
            IsolatedEngineAttemptRequest request
        )
        {
            List<string> args =
            [
                AttemptChildModeArg,
                "--engine",
                request.EngineId ?? "",
                "--movie",
                request.MoviePath ?? "",
                "--source-movie",
                request.SourceMoviePath ?? "",
                "--db-name",
                request.DbName ?? "",
                "--thumb-folder",
                request.ThumbFolder ?? "",
                "--tab-index",
                request.TabIndex.ToString(),
                "--movie-size-bytes",
                Math.Max(0, request.MovieSizeBytes).ToString(),
                "--result-json",
                request.ResultJsonPath ?? "",
            ];

            if (!string.IsNullOrWhiteSpace(request.ThumbSecCsv))
            {
                args.Insert(args.Count - 2, "--thumb-sec-csv");
                args.Insert(args.Count - 2, request.ThumbSecCsv ?? "");
            }

            if (!string.IsNullOrWhiteSpace(request.LogDirectoryPath))
            {
                args.Insert(args.Count - 2, "--log-dir");
                args.Insert(args.Count - 2, request.LogDirectoryPath ?? "");
            }

            if (!string.IsNullOrWhiteSpace(request.TraceId))
            {
                args.Insert(args.Count - 2, "--trace-id");
                args.Insert(args.Count - 2, request.TraceId ?? "");
            }

            return args;
        }

        internal static IReadOnlyList<ThumbInfo> BuildNearBlackRetryThumbInfos(
            int tabIndex,
            string dbName,
            string thumbFolder,
            double? durationSec,
            string rescueMode = ""
        )
        {
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                return [];
            }

            int safeMaxCaptureSec = ResolveSafeMaxCaptureSec(durationSec.Value);
            if (safeMaxCaptureSec < 1)
            {
                return [];
            }

            ThumbnailLayoutProfile layoutProfile = ResolveLayoutProfile(tabIndex);
            HashSet<int> uniqueSeconds = new();
            List<ThumbInfo> retryThumbInfos = new();
            IReadOnlyList<double> retryRatios = ResolveNearBlackRetryRatios(rescueMode);
            foreach (double ratio in retryRatios)
            {
                int captureSec = (int)Math.Floor(durationSec.Value * ratio);
                captureSec = Math.Max(1, Math.Min(safeMaxCaptureSec, captureSec));
                if (!uniqueSeconds.Add(captureSec))
                {
                    continue;
                }

                retryThumbInfos.Add(BuildUniformThumbInfo(layoutProfile, captureSec));
            }

            return retryThumbInfos;
        }

        internal static IReadOnlyList<double> BuildUltraShortNearBlackRetryCaptureSeconds(
            double? durationSec,
            string rescueMode = ""
        )
        {
            if (
                !durationSec.HasValue
                || durationSec.Value <= 0
                || durationSec.Value >= UltraShortDecimalRetryDurationThresholdSec
            )
            {
                return [];
            }

            double safeEnd = Math.Max(0.001d, durationSec.Value - 0.001d);
            HashSet<string> uniqueSeconds = new(StringComparer.Ordinal);
            List<double> captureSecs = [];
            IReadOnlyList<double> retryRatios = ResolveUltraShortNearBlackRetryRatios(rescueMode);
            foreach (double ratio in retryRatios)
            {
                double captureSec = Math.Clamp(durationSec.Value * ratio, 0.001d, safeEnd);
                captureSec = Math.Round(captureSec, 3, MidpointRounding.AwayFromZero);
                string key = captureSec.ToString("0.000", CultureInfo.InvariantCulture);
                if (!uniqueSeconds.Add(key))
                {
                    continue;
                }

                captureSecs.Add(captureSec);
            }

            return captureSecs;
        }

        internal static bool ShouldForceDarkHeavyBackgroundRetry(string rescueMode, string engineId)
        {
            if (!IsDarkHeavyBackgroundRescueMode(rescueMode))
            {
                return false;
            }

            return string.Equals(engineId, "autogen", StringComparison.OrdinalIgnoreCase)
                || string.Equals(engineId, "ffmpeg1pass", StringComparison.OrdinalIgnoreCase)
                || string.Equals(engineId, "ffmediatoolkit", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ShouldAllowDarkHeavyBackgroundLiteSuccess(
            string rescueMode,
            string engineId
        )
        {
            if (!IsDarkHeavyBackgroundLiteRescueMode(rescueMode))
            {
                return false;
            }

            return string.Equals(engineId, "autogen", StringComparison.OrdinalIgnoreCase)
                || string.Equals(engineId, "ffmpeg1pass", StringComparison.OrdinalIgnoreCase)
                || string.Equals(engineId, "ffmediatoolkit", StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizeRescueMode(string rescueMode)
        {
            if (IsForceIndexRepairRescueMode(rescueMode))
            {
                return ForceIndexRepairRescueMode;
            }

            if (IsDarkHeavyBackgroundRescueMode(rescueMode))
            {
                return DarkHeavyBackgroundRescueMode;
            }

            return IsDarkHeavyBackgroundLiteRescueMode(rescueMode)
                ? DarkHeavyBackgroundLiteRescueMode
                : "";
        }

        internal static bool IsForceIndexRepairRescueMode(string rescueMode)
        {
            return string.Equals(
                rescueMode ?? "",
                ForceIndexRepairRescueMode,
                StringComparison.OrdinalIgnoreCase
            );
        }

        internal static bool IsDarkHeavyBackgroundRescueMode(string rescueMode)
        {
            return string.Equals(
                rescueMode ?? "",
                DarkHeavyBackgroundRescueMode,
                StringComparison.OrdinalIgnoreCase
            );
        }

        internal static bool IsDarkHeavyBackgroundLiteRescueMode(string rescueMode)
        {
            return string.Equals(
                rescueMode ?? "",
                DarkHeavyBackgroundLiteRescueMode,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static IReadOnlyList<double> ResolveNearBlackRetryRatios(string rescueMode)
        {
            return IsDarkHeavyBackgroundRescueMode(rescueMode)
                    || IsDarkHeavyBackgroundLiteRescueMode(rescueMode)
                ? DarkHeavyBackgroundRetryRatios
                : NearBlackRetryRatios;
        }

        private static IReadOnlyList<double> ResolveUltraShortNearBlackRetryRatios(string rescueMode)
        {
            return IsDarkHeavyBackgroundRescueMode(rescueMode)
                    || IsDarkHeavyBackgroundLiteRescueMode(rescueMode)
                ? DarkHeavyBackgroundUltraShortRetryRatios
                : UltraShortNearBlackRetryRatios;
        }

        internal static bool ShouldRunAutogenVirtualDurationRetry(
            RescueExecutionPlan rescuePlan,
            string engineId,
            double? durationSec
        )
        {
            return string.Equals(engineId, "autogen", StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    rescuePlan.RouteId,
                    NearBlackOrOldFrameRouteId,
                    StringComparison.Ordinal
                )
                && durationSec.HasValue
                && durationSec.Value >= LongDurationNearBlackVirtualRetryThresholdSec;
        }

        internal static IReadOnlyList<AutogenVirtualDurationRetryPlan> BuildAutogenVirtualDurationRetryPlans(
            int tabIndex,
            double? durationSec
        )
        {
            if (
                !durationSec.HasValue
                || durationSec.Value < LongDurationNearBlackVirtualRetryThresholdSec
            )
            {
                return [];
            }

            ThumbnailLayoutProfile layoutProfile = ResolveLayoutProfile(tabIndex);
            List<AutogenVirtualDurationRetryPlan> result = [];
            HashSet<string> uniqueThumbSecCsv = new(StringComparer.Ordinal);

            foreach (double divisor in LongDurationVirtualDurationDivisors)
            {
                if (divisor <= 0d)
                {
                    continue;
                }

                double virtualDurationSec = durationSec.Value / divisor;
                ThumbInfo thumbInfo = BuildAutoThumbInfoForVirtualDuration(
                    layoutProfile,
                    virtualDurationSec
                );
                string thumbSecCsv = BuildThumbSecCsv(thumbInfo);
                if (string.IsNullOrWhiteSpace(thumbSecCsv) || !uniqueThumbSecCsv.Add(thumbSecCsv))
                {
                    continue;
                }

                result.Add(
                    new AutogenVirtualDurationRetryPlan(divisor, virtualDurationSec, thumbInfo)
                );
            }

            return result;
        }

        internal static IReadOnlyList<UltraShortFrameCandidate> SelectUltraShortRetryCandidates(
            IReadOnlyList<UltraShortFrameCandidate> candidates,
            int panelCount
        )
        {
            if (candidates == null || candidates.Count < 1)
            {
                return [];
            }

            int safePanelCount = Math.Max(1, panelCount);
            return candidates
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.AverageSaturation)
                .ThenByDescending(x => x.LumaStdDev)
                .ThenBy(x => x.CaptureSec)
                .Take(safePanelCount)
                .OrderBy(x => x.CaptureSec)
                .ToArray();
        }

        internal static double? ResolveNearBlackRetryDurationSec(
            double? durationSec,
            long movieSizeBytes,
            string moviePath,
            Func<string, double?> durationProbe = null
        )
        {
            if (durationSec.HasValue && durationSec.Value > 0)
            {
                return durationSec;
            }

            Func<string, double?> effectiveProbe = durationProbe ?? TryProbeDurationSecWithFfprobe;
            double? probedDurationSec = effectiveProbe(moviePath);
            if (probedDurationSec.HasValue && probedDurationSec.Value > 0)
            {
                return probedDurationSec;
            }

            if (IsLikelyUltraShort(movieSizeBytes, moviePath))
            {
                return UltraShortDecimalRetryFallbackDurationSec;
            }

            return durationSec;
        }

        internal static double CalculateFrameVisualScore(
            Bitmap source,
            out double averageLuma,
            out double averageSaturation,
            out double lumaStdDev
        )
        {
            averageLuma = 0d;
            averageSaturation = 0d;
            lumaStdDev = 0d;
            if (source == null || source.Width < 1 || source.Height < 1)
            {
                return 0d;
            }

            long count = 0;
            double lumaSum = 0d;
            double lumaSqSum = 0d;
            double saturationSum = 0d;
            for (int y = 0; y < source.Height; y += NearBlackThumbnailSampleStep)
            {
                for (int x = 0; x < source.Width; x += NearBlackThumbnailSampleStep)
                {
                    Color pixel = source.GetPixel(x, y);
                    double red = pixel.R;
                    double green = pixel.G;
                    double blue = pixel.B;
                    double luma = (0.2126d * red) + (0.7152d * green) + (0.0722d * blue);
                    double max = Math.Max(red, Math.Max(green, blue));
                    double min = Math.Min(red, Math.Min(green, blue));
                    double saturation = max <= 0d ? 0d : ((max - min) / max) * 255d;

                    count++;
                    lumaSum += luma;
                    lumaSqSum += luma * luma;
                    saturationSum += saturation;
                }
            }

            if (count < 1)
            {
                return 0d;
            }

            averageLuma = lumaSum / count;
            averageSaturation = saturationSum / count;
            double variance = Math.Max(0d, (lumaSqSum / count) - (averageLuma * averageLuma));
            lumaStdDev = Math.Sqrt(variance);

            // 明るさだけでなく、彩度とコントラストも足して「映えるコマ」を優先する。
            return (averageLuma * 0.35d) + (averageSaturation * 1.50d) + (lumaStdDev * 0.75d);
        }

        internal static string BuildThumbSecCsv(ThumbInfo thumbInfo)
        {
            if (thumbInfo?.ThumbSec == null || thumbInfo.ThumbSec.Count < 1)
            {
                return "";
            }

            return string.Join(",", thumbInfo.ThumbSec.Select(x => x.ToString()));
        }

        internal static ThumbInfo BuildThumbInfoFromCsv(
            int tabIndex,
            string dbName,
            string thumbFolder,
            string thumbSecCsv
        )
        {
            if (string.IsNullOrWhiteSpace(thumbSecCsv))
            {
                return null;
            }

            List<int> captureSecs = new();
            foreach (
                string part in thumbSecCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            )
            {
                if (int.TryParse(part.Trim(), out int captureSec))
                {
                    captureSecs.Add(Math.Max(0, captureSec));
                }
            }

            if (captureSecs.Count < 1)
            {
                return null;
            }

            return BuildExplicitThumbInfo(ResolveLayoutProfile(tabIndex), captureSecs);
        }

        private static ThumbInfo BuildUniformThumbInfo(
            ThumbnailLayoutProfile layoutProfile,
            int captureSec
        )
        {
            int thumbCount = Math.Max(1, layoutProfile?.DivCount ?? 1);
            int[] captureSecs = Enumerable.Repeat(Math.Max(0, captureSec), thumbCount).ToArray();
            return BuildExplicitThumbInfo(layoutProfile, captureSecs);
        }

        // autogen の通常候補計算と同じ割り方で、救済worker だけ仮想時間の panel 秒を組み立てる。
        private static ThumbInfo BuildAutoThumbInfoForVirtualDuration(
            ThumbnailLayoutProfile layoutProfile,
            double virtualDurationSec
        )
        {
            int thumbCount = Math.Max(1, layoutProfile?.DivCount ?? 1);
            int divideSec = 1;
            int maxCaptureSec = ResolveSafeMaxCaptureSec(virtualDurationSec);
            if (virtualDurationSec > 0d)
            {
                divideSec = (int)(virtualDurationSec / (thumbCount + 1));
                if (divideSec < 1)
                {
                    divideSec = 1;
                }
            }

            List<int> captureSecs = [];
            for (int i = 1; i < thumbCount + 1; i++)
            {
                int captureSec = i * divideSec;
                if (captureSec > maxCaptureSec)
                {
                    captureSec = maxCaptureSec;
                }

                captureSecs.Add(Math.Max(0, captureSec));
            }

            return BuildExplicitThumbInfo(layoutProfile, captureSecs);
        }

        private static ThumbInfo BuildExplicitThumbInfo(
            ThumbnailLayoutProfile layoutProfile,
            IReadOnlyList<int> captureSecs
        )
        {
            int thumbCount = Math.Max(1, layoutProfile?.DivCount ?? 1);
            ThumbnailSheetSpec spec = new()
            {
                ThumbWidth = layoutProfile?.Width ?? 160,
                ThumbHeight = layoutProfile?.Height ?? 120,
                ThumbRows = layoutProfile?.Rows ?? 1,
                ThumbColumns = layoutProfile?.Columns ?? 1,
                ThumbCount = thumbCount,
            };

            int fallbackSec = 0;
            if (captureSecs != null && captureSecs.Count > 0)
            {
                fallbackSec = Math.Max(0, captureSecs[captureSecs.Count - 1]);
            }

            for (int i = 0; i < thumbCount; i++)
            {
                int captureSec =
                    captureSecs != null && i < captureSecs.Count
                        ? Math.Max(0, captureSecs[i])
                        : fallbackSec;
                spec.CaptureSeconds.Add(captureSec);
            }
            return ThumbInfo.FromSheetSpec(spec);
        }

        private static int ResolveSafeMaxCaptureSec(double durationSec)
        {
            if (durationSec <= 0 || double.IsNaN(durationSec) || double.IsInfinity(durationSec))
            {
                return 0;
            }

            double safeEnd = Math.Max(0, durationSec - 0.001);
            return Math.Max(0, (int)Math.Floor(safeEnd));
        }

        private static string BuildThumbSecLabel(ThumbInfo thumbInfo)
        {
            return BuildThumbSecCsv(thumbInfo);
        }

        internal static string ResolveSucceededEngineId(
            string fallbackEngineId,
            ThumbnailCreateResult createResult
        )
        {
            string processEngineId = createResult?.ProcessEngineId?.Trim() ?? "";
            return string.IsNullOrWhiteSpace(processEngineId) ? (fallbackEngineId ?? "") : processEngineId;
        }

        private static string ResolveThumbnailOutputPath(
            QueueObj queueObj,
            MainDbContext mainDbContext,
            string movieNameOrPathOverride = ""
        )
        {
            if (queueObj == null || string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return "";
            }

            string hash = queueObj.Hash ?? "";
            if (string.IsNullOrWhiteSpace(hash))
            {
                hash = MovieHashCalculator.GetHashCrc32(queueObj.MovieFullPath);
                queueObj.Hash = hash;
            }

            if (string.IsNullOrWhiteSpace(hash))
            {
                return "";
            }

            string movieNameOrPath = string.IsNullOrWhiteSpace(movieNameOrPathOverride)
                ? queueObj.MovieFullPath
                : movieNameOrPathOverride;
            return ThumbnailPathResolver.BuildThumbnailPath(
                ResolveOutPath(queueObj.Tabindex, mainDbContext.DbName, mainDbContext.ThumbFolder),
                movieNameOrPath,
                hash
            );
        }

        private static async Task<(bool ok, string errorMessage)> ExtractSingleFrameJpegWithFfmpegAsync(
            string moviePath,
            double captureSec,
            string outputPath,
            TimeSpan timeout
        )
        {
            if (string.IsNullOrWhiteSpace(moviePath) || !File.Exists(moviePath))
            {
                return (false, "movie file not found");
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = ResolveFfmpegExecutablePath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-an");
            startInfo.ArgumentList.Add("-sn");
            startInfo.ArgumentList.Add("-dn");
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(captureSec.ToString("0.###", CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(moviePath);
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-strict");
            startInfo.ArgumentList.Add("unofficial");
            startInfo.ArgumentList.Add("-pix_fmt");
            startInfo.ArgumentList.Add("yuv420p");
            startInfo.ArgumentList.Add("-q:v");
            startInfo.ArgumentList.Add("5");
            startInfo.ArgumentList.Add(outputPath);

            try
            {
                using CancellationTokenSource timeoutCts = new();
                timeoutCts.CancelAfter(timeout);
                return await RunProcessAsync(startInfo, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (false, $"decimal near-black retry timeout: timeout_sec={timeout.TotalSeconds:0}");
            }
        }

        private static Bitmap BuildRepeatedFrameBitmap(
            Bitmap sourceBitmap,
            ThumbnailLayoutProfile layoutProfile
        )
        {
            int panelWidth = Math.Max(1, layoutProfile?.Width ?? 160);
            int panelHeight = Math.Max(1, layoutProfile?.Height ?? 120);
            int columns = Math.Max(1, layoutProfile?.Columns ?? 1);
            int rows = Math.Max(1, layoutProfile?.Rows ?? 1);
            int totalWidth = panelWidth * columns;
            int totalHeight = panelHeight * rows;

            Bitmap canvas = new(totalWidth, totalHeight);
            using Graphics g = Graphics.FromImage(canvas);
            using Bitmap panelBitmap = BuildSinglePanelBitmap(sourceBitmap, panelWidth, panelHeight);
            g.Clear(Color.Black);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    g.DrawImage(panelBitmap, x * panelWidth, y * panelHeight, panelWidth, panelHeight);
                }
            }

            return canvas;
        }

        private static Bitmap BuildMultiFrameBitmap(
            IReadOnlyList<UltraShortFrameCandidate> selectedCandidates,
            ThumbnailLayoutProfile layoutProfile
        )
        {
            int panelWidth = Math.Max(1, layoutProfile?.Width ?? 160);
            int panelHeight = Math.Max(1, layoutProfile?.Height ?? 120);
            int columns = Math.Max(1, layoutProfile?.Columns ?? 1);
            int rows = Math.Max(1, layoutProfile?.Rows ?? 1);
            int panelCount = Math.Max(1, columns * rows);
            int totalWidth = panelWidth * columns;
            int totalHeight = panelHeight * rows;

            List<UltraShortFrameCandidate> arrangedCandidates = ExpandUltraShortRetryCandidates(
                selectedCandidates,
                panelCount
            );
            Bitmap canvas = new(totalWidth, totalHeight);
            using Graphics g = Graphics.FromImage(canvas);
            g.Clear(Color.Black);
            for (int i = 0; i < arrangedCandidates.Count; i++)
            {
                UltraShortFrameCandidate candidate = arrangedCandidates[i];
                using Bitmap sourceBitmap = new(candidate.ImagePath);
                using Bitmap panelBitmap = BuildSinglePanelBitmap(sourceBitmap, panelWidth, panelHeight);
                int x = (i % columns) * panelWidth;
                int y = (i / columns) * panelHeight;
                g.DrawImage(panelBitmap, x, y, panelWidth, panelHeight);
            }

            return canvas;
        }

        private static Bitmap BuildSinglePanelBitmap(Bitmap sourceBitmap, int panelWidth, int panelHeight)
        {
            Bitmap panelBitmap = new(panelWidth, panelHeight);
            using Graphics g = Graphics.FromImage(panelBitmap);
            g.Clear(Color.Black);

            double scale = Math.Min(
                (double)panelWidth / Math.Max(1, sourceBitmap.Width),
                (double)panelHeight / Math.Max(1, sourceBitmap.Height)
            );
            int drawWidth = Math.Max(1, (int)Math.Round(sourceBitmap.Width * scale));
            int drawHeight = Math.Max(1, (int)Math.Round(sourceBitmap.Height * scale));
            int drawX = Math.Max(0, (panelWidth - drawWidth) / 2);
            int drawY = Math.Max(0, (panelHeight - drawHeight) / 2);
            g.DrawImage(sourceBitmap, drawX, drawY, drawWidth, drawHeight);
            return panelBitmap;
        }

        private static void SaveBitmapWithThumbInfo(Bitmap bitmap, ThumbInfo thumbInfo, string savePath)
        {
            if (
                !ThumbnailJpegMetadataWriter.TrySaveJpegWithThumbInfo(
                    bitmap,
                    savePath,
                    thumbInfo,
                    out string errorMessage
                )
            )
            {
                throw new InvalidOperationException(errorMessage);
            }
        }

        private static async Task<(bool ok, string errorMessage)> RunProcessAsync(
            ProcessStartInfo startInfo,
            CancellationToken cts
        )
        {
            Process process = null;
            try
            {
                process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    return (false, "process start returned false");
                }

                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync(cts).ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);
                _ = await stdoutTask.ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    return (false, $"exit={process.ExitCode}, err={stderr}");
                }

                return (true, "");
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (process != null && !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(2000);
                    }
                }
                catch
                {
                    // timeout時の後始末失敗よりも、救済本体が戻ることを優先する。
                }

                throw;
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            finally
            {
                process?.Dispose();
            }
        }

        private static string ResolveFfmpegExecutablePath()
        {
            string configuredPath =
                Environment.GetEnvironmentVariable("IMM_FFMPEG_EXE_PATH")?.Trim().Trim('"') ?? "";
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                if (File.Exists(configuredPath))
                {
                    return configuredPath;
                }

                if (Directory.Exists(configuredPath))
                {
                    string candidate = Path.Combine(configuredPath, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            string baseDir = AppContext.BaseDirectory;
            string[] bundledCandidates =
            [
                Path.Combine(baseDir, "ffmpeg.exe"),
                Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDir, "tools", "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDir, "runtimes", "win-x64", "native", "ffmpeg.exe"),
                Path.Combine(baseDir, "runtimes", "win-x86", "native", "ffmpeg.exe"),
            ];

            for (int i = 0; i < bundledCandidates.Length; i++)
            {
                if (File.Exists(bundledCandidates[i]))
                {
                    return bundledCandidates[i];
                }
            }

            return "ffmpeg";
        }

        private static string ResolveFfprobeExecutablePath()
        {
            string ffmpegPath = ResolveFfmpegExecutablePath();
            if (!string.IsNullOrWhiteSpace(ffmpegPath))
            {
                try
                {
                    string ffprobeCandidate = Path.Combine(
                        Path.GetDirectoryName(ffmpegPath) ?? "",
                        "ffprobe.exe"
                    );
                    if (File.Exists(ffprobeCandidate))
                    {
                        return ffprobeCandidate;
                    }
                }
                catch
                {
                    // ffprobe 解決失敗時は最後に PATH 解決へ落とす。
                }
            }

            return "ffprobe";
        }

        private static List<UltraShortFrameCandidate> ExpandUltraShortRetryCandidates(
            IReadOnlyList<UltraShortFrameCandidate> selectedCandidates,
            int panelCount
        )
        {
            List<UltraShortFrameCandidate> arranged = [];
            if (selectedCandidates == null || selectedCandidates.Count < 1)
            {
                return arranged;
            }

            UltraShortFrameCandidate bestCandidate = selectedCandidates
                .OrderByDescending(x => x.Score)
                .First();
            for (int i = 0; i < panelCount; i++)
            {
                if (i < selectedCandidates.Count)
                {
                    arranged.Add(selectedCandidates[i]);
                }
                else
                {
                    arranged.Add(bestCandidate);
                }
            }

            return arranged;
        }

        private static IReadOnlyList<int> BuildUltraShortCompositeCaptureSecs(
            IReadOnlyList<UltraShortFrameCandidate> selectedCandidates,
            int panelCount
        )
        {
            List<int> captureSecs = [];
            foreach (
                UltraShortFrameCandidate candidate in ExpandUltraShortRetryCandidates(
                    selectedCandidates,
                    Math.Max(1, panelCount)
                )
            )
            {
                captureSecs.Add(Math.Max(0, (int)Math.Floor(candidate.CaptureSec)));
            }

            return captureSecs;
        }

        internal static IReadOnlyList<double> BuildExperimentalFinalSeekCaptureSeconds(
            double durationSec,
            int sampleCount
        )
        {
            if (
                durationSec <= 0d
                || double.IsNaN(durationSec)
                || double.IsInfinity(durationSec)
                || sampleCount < 1
            )
            {
                return [];
            }

            double safeDurationSec = Math.Max(0.001d, durationSec - 0.001d);
            double intervalSec = safeDurationSec / (sampleCount + 1);
            if (intervalSec <= 0d)
            {
                return [];
            }

            List<double> captureSecs = [];
            for (int i = 1; i <= sampleCount; i++)
            {
                double captureSec = Math.Clamp(intervalSec * i, 0.001d, safeDurationSec);
                captureSecs.Add(
                    Math.Round(captureSec, 3, MidpointRounding.AwayFromZero)
                );
            }

            return captureSecs;
        }

        // seek が重い個体では random seek を連打せず、先頭からの低fps decode で候補だけ拾う。
        private static async Task<(
            bool IsSuccess,
            List<UltraShortFrameCandidate> Candidates,
            string ErrorMessage
        )> ExtractExperimentalFinalSeekCandidatesAsync(
            string moviePath,
            IReadOnlyList<double> captureSecs,
            TimeSpan timeout,
            string outputDirectoryPath
        )
        {
            if (string.IsNullOrWhiteSpace(moviePath) || !File.Exists(moviePath))
            {
                return (false, [], "experimental final seek movie file not found");
            }

            if (captureSecs == null || captureSecs.Count < 1)
            {
                return (false, [], "experimental final seek produced no capture points");
            }

            Directory.CreateDirectory(outputDirectoryPath);
            foreach (string existingPath in Directory.GetFiles(outputDirectoryPath, "*.jpg"))
            {
                TryDeleteFileQuietly(existingPath);
            }

            double intervalSec = Math.Max(0.001d, captureSecs[0]);
            string outputPattern = Path.Combine(outputDirectoryPath, "frame-%03d.jpg");
            ProcessStartInfo startInfo = new()
            {
                FileName = ResolveFfmpegExecutablePath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-an");
            startInfo.ArgumentList.Add("-sn");
            startInfo.ArgumentList.Add("-dn");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(moviePath);
            startInfo.ArgumentList.Add("-vf");
            startInfo.ArgumentList.Add(
                FormattableString.Invariant(
                    $"fps=1/{intervalSec:0.###},scale={ExperimentalFinalSeekScaleWidth}:-1"
                )
            );
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add(captureSecs.Count.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-pix_fmt");
            startInfo.ArgumentList.Add("yuv420p");
            startInfo.ArgumentList.Add("-q:v");
            startInfo.ArgumentList.Add("5");
            startInfo.ArgumentList.Add(outputPattern);

            (bool ok, string errorMessage) processResult;
            try
            {
                using CancellationTokenSource timeoutCts = new();
                timeoutCts.CancelAfter(timeout);
                processResult = await RunProcessAsync(startInfo, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (
                    false,
                    [],
                    $"experimental final seek timeout: timeout_sec={timeout.TotalSeconds:0}"
                );
            }

            if (!processResult.ok)
            {
                return (false, [], processResult.errorMessage);
            }

            string[] imagePaths = Directory
                .GetFiles(outputDirectoryPath, "frame-*.jpg")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (imagePaths.Length < 1)
            {
                return (false, [], "experimental final seek produced no frames");
            }

            List<UltraShortFrameCandidate> candidates = [];
            bool nearBlackOnly = false;
            for (int i = 0; i < imagePaths.Length; i++)
            {
                string imagePath = imagePaths[i];
                if (TryRejectNearBlackOutput(imagePath, out _))
                {
                    nearBlackOnly = true;
                    continue;
                }

                using Bitmap sourceBitmap = new(imagePath);
                double score = CalculateFrameVisualScore(
                    sourceBitmap,
                    out double averageLuma,
                    out double averageSaturation,
                    out double lumaStdDev
                );
                double captureSec = captureSecs[Math.Min(i, captureSecs.Count - 1)];
                candidates.Add(
                    new UltraShortFrameCandidate(
                        imagePath,
                        captureSec,
                        score,
                        averageLuma,
                        averageSaturation,
                        lumaStdDev
                    )
                );
            }

            if (candidates.Count < 1)
            {
                return (
                    false,
                    [],
                    nearBlackOnly
                        ? "experimental final seek produced only near-black frames"
                        : "experimental final seek produced no usable frames"
                );
            }

            return (true, candidates, "");
        }

        private static double? TryProbeDurationSecWithFfprobe(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath) || !File.Exists(moviePath))
            {
                return null;
            }

            Process process = null;
            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = ResolveFfprobeExecutablePath(),
                    Arguments =
                        "-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 "
                        + $"\"{moviePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    return null;
                }

                string stdout = process.StandardOutput.ReadToEnd();
                _ = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // probe 失敗は救済本体を止めない。
                    }

                    return null;
                }

                if (process.ExitCode != 0)
                {
                    return null;
                }

                if (
                    double.TryParse(
                        (stdout ?? "").Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double parsedDurationSec
                    )
                    && parsedDurationSec > 0
                )
                {
                    return parsedDurationSec;
                }

                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                process?.Dispose();
            }
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // timeout 終了時は best effort で殺せれば十分。
            }
        }

        internal static TimeSpan ResolveEngineAttemptTimeout()
        {
            return TimeSpan.FromSeconds(
                ResolveTimeoutSeconds(
                    EngineAttemptTimeoutSecEnvName,
                    DefaultEngineAttemptTimeoutSec,
                    minSeconds: 15,
                    maxSeconds: 3600
                )
            );
        }

        // OpenCV は token 非対応で戻りが遅い個体があるため、既定 budget を長めに分けて持つ。
        internal static TimeSpan ResolveEngineAttemptTimeout(string engineId)
        {
            if (string.Equals(engineId, "opencv", StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.FromSeconds(
                    ResolveTimeoutSeconds(
                        OpenCvAttemptTimeoutSecEnvName,
                        DefaultOpenCvAttemptTimeoutSec,
                        minSeconds: 30,
                        maxSeconds: 3600
                    )
                );
            }

            return ResolveEngineAttemptTimeout();
        }

        internal static TimeSpan ResolveRepairProbeTimeout()
        {
            return TimeSpan.FromSeconds(
                ResolveTimeoutSeconds(
                    RepairProbeTimeoutSecEnvName,
                    DefaultRepairProbeTimeoutSec,
                    minSeconds: 15,
                    maxSeconds: 1800
                )
            );
        }

        internal static TimeSpan ResolveRepairTimeout()
        {
            return TimeSpan.FromSeconds(
                ResolveTimeoutSeconds(
                    RepairTimeoutSecEnvName,
                    DefaultRepairTimeoutSec,
                    minSeconds: 30,
                    maxSeconds: 7200
                )
            );
        }

        internal static int ResolveTimeoutSeconds(
            string envName,
            int defaultSeconds,
            int minSeconds,
            int maxSeconds
        )
        {
            string raw = Environment.GetEnvironmentVariable(envName)?.Trim() ?? "";
            if (int.TryParse(raw, out int parsed))
            {
                return Math.Clamp(parsed, minSeconds, maxSeconds);
            }

            return Math.Clamp(defaultSeconds, minSeconds, maxSeconds);
        }

        private static void TryDeleteFileQuietly(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 一時修復ファイルの掃除失敗は観測だけ残して続行する。
            }
        }

    }
}
