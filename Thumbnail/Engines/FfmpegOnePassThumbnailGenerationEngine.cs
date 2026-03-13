using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// ffmpeg 1パス（抽出+tile）でサムネを作るエンジン。
    /// </summary>
    internal sealed class FfmpegOnePassThumbnailGenerationEngine : IThumbnailGenerationEngine
    {
        private const string FfmpegExePathEnvName = "IMM_FFMPEG_EXE_PATH";
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string FfmpegJpegQualityEnvName = "IMM_THUMB_JPEG_Q";
        private const string FfmpegScaleFlagsEnvName = "IMM_THUMB_SCALE_FLAGS";
        private const int DefaultJpegQuality = 5;

        public string EngineId => "ffmpeg1pass";
        public string EngineName => "ffmpeg1pass";

        public bool CanHandle(ThumbnailJobContext context)
        {
            return true;
        }

        public async Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken cts = default
        )
        {
            if (context == null)
            {
                return ThumbnailCreationService.CreateFailedResult("", null, "context is null");
            }

            if (context.IsManual)
            {
                return ThumbnailCreationService.CreateFailedResult(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    "ffmpeg1pass does not support manual mode"
                );
            }

            if (context.ThumbInfo == null || context.ThumbInfo.ThumbSec == null || context.ThumbInfo.ThumbSec.Count < 1)
            {
                return ThumbnailCreationService.CreateFailedResult(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    "thumb info is empty"
                );
            }

            double? durationSec = context.DurationSec;
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                durationSec = ThumbnailCreationService.TryGetDurationSecFromShell(context.MovieFullPath);
            }

            int panelCount = context.ThumbInfo.ThumbSec.Count;
            int cols = context.TabInfo.Columns;
            int rows = context.TabInfo.Rows;
            if (panelCount < 1 || cols < 1 || rows < 1)
            {
                return ThumbnailCreationService.CreateFailedResult(
                    context.SaveThumbFileName,
                    durationSec,
                    "invalid panel configuration"
                );
            }

            Size targetSize = ResolveTargetSize(context);
            string saveDir = Path.GetDirectoryName(context.SaveThumbFileName) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            string ffmpegExePath = ResolveFfmpegExecutablePath();
            double startSec = Math.Max(0, context.ThumbInfo.ThumbSec[0]);
            double intervalSec = ResolveFrameIntervalSec(context.ThumbInfo.ThumbSec, durationSec, panelCount);
            int jpegQuality = ResolveJpegQuality();
            string scaleFlags = ResolveScaleFlags();

            string startText = startSec.ToString("0.###", CultureInfo.InvariantCulture);
            string vf = BuildTileFilter(
                intervalSec,
                targetSize.Width,
                targetSize.Height,
                cols,
                rows,
                durationSec,
                panelCount,
                scaleFlags
            );

            ProcessStartInfo psi = new()
            {
                FileName = ffmpegExePath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            AddHwAccelArguments(psi);
            psi.ArgumentList.Add("-an");
            psi.ArgumentList.Add("-sn");
            psi.ArgumentList.Add("-dn");
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(startText);
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(context.MovieFullPath);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            // 失敗率低減を優先し、厳格判定で弾かれやすい非標準YUV系も許容して処理継続しやすくする。
            psi.ArgumentList.Add("-strict");
            psi.ArgumentList.Add("unofficial");
            // 失敗率低減を優先し、出力ピクセル形式は互換性の高い yuv420p に固定する。
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("yuv420p");
            psi.ArgumentList.Add("-q:v");
            psi.ArgumentList.Add(jpegQuality.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add(vf);
            psi.ArgumentList.Add(context.SaveThumbFileName);

            (bool ok, string err) = await RunProcessAsync(psi, cts);
            if (!ok || !Path.Exists(context.SaveThumbFileName))
            {
                return ThumbnailCreationService.CreateFailedResult(
                    context.SaveThumbFileName,
                    durationSec,
                    string.IsNullOrWhiteSpace(err) ? "ffmpeg one-pass failed" : err
                );
            }

            using FileStream dest = new(context.SaveThumbFileName, FileMode.Append, FileAccess.Write);
            dest.Write(context.ThumbInfo.SecBuffer);
            dest.Write(context.ThumbInfo.InfoBuffer);
            return ThumbnailCreationService.CreateSuccessResult(context.SaveThumbFileName, durationSec);
        }

        public async Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            if (!Path.Exists(movieFullPath))
            {
                return false;
            }

            string saveDir = Path.GetDirectoryName(saveThumbPath) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            string ffmpegExePath = ResolveFfmpegExecutablePath();
            string posSec = Math.Max(0, capturePos).ToString("0.###", CultureInfo.InvariantCulture);
            int jpegQuality = ResolveJpegQuality();
            string scaleFlags = ResolveScaleFlags();
            string vf =
                $"scale=640:480:force_original_aspect_ratio=decrease:flags={scaleFlags},pad=640:480:(ow-iw)/2:(oh-ih)/2:black";

            ProcessStartInfo psi = new()
            {
                FileName = ffmpegExePath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            AddHwAccelArguments(psi);
            psi.ArgumentList.Add("-an");
            psi.ArgumentList.Add("-sn");
            psi.ArgumentList.Add("-dn");
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(posSec);
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(movieFullPath);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            // 失敗率低減を優先し、厳格判定で弾かれやすい非標準YUV系も許容して処理継続しやすくする。
            psi.ArgumentList.Add("-strict");
            psi.ArgumentList.Add("unofficial");
            // 失敗率低減を優先し、出力ピクセル形式は互換性の高い yuv420p に固定する。
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("yuv420p");
            psi.ArgumentList.Add("-q:v");
            psi.ArgumentList.Add(jpegQuality.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add(vf);
            psi.ArgumentList.Add(saveThumbPath);

            (bool ok, _) = await RunProcessAsync(psi, cts);
            return ok && Path.Exists(saveThumbPath);
        }

        private static Size ResolveTargetSize(ThumbnailJobContext context)
        {
            if (context.IsResizeThumb && context.TabInfo.Width > 0 && context.TabInfo.Height > 0)
            {
                return new Size(context.TabInfo.Width, context.TabInfo.Height);
            }

            // 非リサイズ時は既存既定値に近い固定値を使う。
            return new Size(320, 240);
        }

        private static double ResolveFrameIntervalSec(
            IReadOnlyList<int> secList,
            double? durationSec,
            int panelCount
        )
        {
            if (secList != null && secList.Count >= 2)
            {
                int interval = secList[1] - secList[0];
                if (interval > 0)
                {
                    return interval;
                }
            }

            if (durationSec.HasValue && durationSec.Value > 0 && panelCount > 0)
            {
                double divide = durationSec.Value / (panelCount + 1);
                if (divide > 0.1)
                {
                    return divide;
                }
            }

            return 1d;
        }

        private static string BuildTileFilter(
            double intervalSec,
            int width,
            int height,
            int cols,
            int rows,
            double? durationSec,
            int panelCount,
            string scaleFlags
        )
        {
            double safeInterval = intervalSec > 0 ? intervalSec : 1d;
            string intervalText = safeInterval.ToString("0.###", CultureInfo.InvariantCulture);
            StringBuilder vf = new();

            // 短尺で必要フレーム数が不足する場合は、末尾フレーム複製で tile 完成を保証する。
            if (
                durationSec.HasValue
                && durationSec.Value > 0
                && panelCount > 0
                && durationSec.Value < safeInterval * panelCount
            )
            {
                double padSec = (safeInterval * panelCount) - durationSec.Value + 0.05;
                string padText = padSec.ToString("0.###", CultureInfo.InvariantCulture);
                vf.Append($"tpad=stop_mode=clone:stop_duration={padText},");
            }

            vf.Append($"fps=1/{intervalText},");
            vf.Append(
                $"scale={width}:{height}:force_original_aspect_ratio=decrease:flags={scaleFlags},"
            );
            vf.Append($"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black,");
            vf.Append($"tile={cols}x{rows}");
            return vf.ToString();
        }

        // JPEG品質は 2〜31 の範囲のみ受け入れ、範囲外は既定値へフォールバックする。
        private static int ResolveJpegQuality()
        {
            string raw = Environment.GetEnvironmentVariable(FfmpegJpegQualityEnvName)?.Trim() ?? "";
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                if (parsed >= 2 && parsed <= 31)
                {
                    return parsed;
                }
            }
            return DefaultJpegQuality;
        }

        // スケーラは速度優先の bilinear を既定にし、必要時のみ環境変数で変更できるようにする。
        private static string ResolveScaleFlags()
        {
            string raw = Environment.GetEnvironmentVariable(FfmpegScaleFlagsEnvName)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "bilinear";
            }

            return raw.ToLowerInvariant() switch
            {
                "nearest" => "nearest",
                "bilinear" => "bilinear",
                "bicubic" => "bicubic",
                "lanczos" => "lanczos",
                _ => "bilinear",
            };
        }

        /// <summary>
        /// GPUモード設定に応じて ffmpeg の -hwaccel を付与する。
        /// </summary>
        private static void AddHwAccelArguments(ProcessStartInfo psi)
        {
            string mode = ThumbnailEnvConfig.NormalizeGpuDecodeMode(
                Environment.GetEnvironmentVariable(GpuDecodeModeEnvName)?.Trim()
            );
            string hwAccel = mode switch
            {
                "cuda" => "cuda",
                "qsv" => "qsv",
                // AMD系は d3d11va を優先。
                "amd" => "d3d11va",
                // 明示OFFはCPUデコード固定。
                "off" => "",
                // 未指定時は従来どおりautoで実行。
                _ => "auto",
            };

            if (string.IsNullOrWhiteSpace(hwAccel))
            {
                return;
            }

            psi.ArgumentList.Add("-hwaccel");
            psi.ArgumentList.Add(hwAccel);
        }

        private static async Task<(bool ok, string err)> RunProcessAsync(
            ProcessStartInfo psi,
            CancellationToken cts
        )
        {
            Process process = null;
            try
            {
                process = new Process { StartInfo = psi };
                if (!process.Start())
                {
                    return (false, "process start returned false");
                }

                string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync(cts).ConfigureAwait(false);
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
                    // timeout時の後始末失敗は本体のキャンセルを優先する。
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
            string configuredPath = Environment.GetEnvironmentVariable(FfmpegExePathEnvName);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string normalizedConfiguredPath = configuredPath.Trim().Trim('"');
                if (File.Exists(normalizedConfiguredPath))
                {
                    return normalizedConfiguredPath;
                }
                if (Directory.Exists(normalizedConfiguredPath))
                {
                    string candidate = Path.Combine(normalizedConfiguredPath, "ffmpeg.exe");
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

            foreach (string candidate in bundledCandidates)
            {
                if (Path.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "ffmpeg";
        }
    }
}
