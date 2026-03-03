using System;
using System.Diagnostics;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル関連の環境変数を一元管理する静的クラス。
    /// </summary>
    public static class ThumbnailEnvConfig
    {
        // --- 環境変数名 ---
        public const string GpuDecodeMode = "IMM_THUMB_GPU_DECODE";
        public const string ThumbEngine = "IMM_THUMB_ENGINE";
        public const string FfmpegExePath = "IMM_FFMPEG_EXE_PATH";
        public const string ThumbDecoder = "IMM_THUMB_DECODER";
        public const string ThumbFileLog = "IMM_THUMB_FILE_LOG";
        private static readonly object GpuDetectSync = new();
        private static readonly object StartupGpuInitSync = new();
        private static bool hasCachedGpuMode;
        private static string cachedGpuMode = "off";
        private static bool startupGpuModeInitialized;
        private static string startupGpuMode = "off";

        // --- 読み取りヘルパー ---
        public static string GetGpuDecodeMode() =>
            Environment.GetEnvironmentVariable(GpuDecodeMode)?.Trim() ?? "";

        public static string GetThumbEngine() =>
            Environment.GetEnvironmentVariable(ThumbEngine)?.Trim() ?? "";

        public static string GetFfmpegExePath() =>
            Environment.GetEnvironmentVariable(FfmpegExePath);

        public static string GetThumbDecoder() =>
            Environment.GetEnvironmentVariable(ThumbDecoder)?.Trim() ?? "";

        public static bool IsThumbFileLogEnabled()
        {
            string mode = Environment.GetEnvironmentVariable(ThumbFileLog);
            if (string.IsNullOrWhiteSpace(mode))
                return false;
            string n = mode.Trim().ToLowerInvariant();
            return n is "1" or "true" or "on" or "yes";
        }

        /// <summary>
        /// GPUデコード設定値を正規化する。`qvc` はタイプミス互換で `qsv` として扱う。
        /// </summary>
        public static string NormalizeGpuDecodeMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return "";
            }

            return mode.Trim().ToLowerInvariant() switch
            {
                "cuda" => "cuda",
                "qsv" => "qsv",
                "qvc" => "qsv",
                "amd" => "amd",
                "amf" => "amd",
                "off" => "off",
                "auto" => "auto",
                _ => "",
            };
        }

        /// <summary>
        /// 設定と実行環境から、最終的に使うGPUデコードモードを解決する。
        /// 優先度は `cuda > qsv > amd`。
        /// </summary>
        public static string ResolveGpuDecodeMode(bool enableGpuDecode, Action<string> log = null)
        {
            if (!enableGpuDecode)
            {
                return "off";
            }

            string currentMode = NormalizeGpuDecodeMode(GetGpuDecodeMode());
            if (currentMode is "cuda" or "qsv" or "amd")
            {
                return currentMode;
            }

            lock (GpuDetectSync)
            {
                if (!hasCachedGpuMode)
                {
                    cachedGpuMode = DetectBestGpuDecodeMode(log);
                    hasCachedGpuMode = true;
                }

                return cachedGpuMode;
            }
        }

        /// <summary>
        /// 起動時にGPU利用可能モードを一度だけ判定して保持し、
        /// 設定画面のON/OFFに応じた適用値を環境変数へ反映する。
        /// </summary>
        public static string InitializeGpuDecodeModeAtStartup(
            bool enableGpuDecode,
            Action<string> log = null
        )
        {
            lock (StartupGpuInitSync)
            {
                if (!startupGpuModeInitialized)
                {
                    // 判定は起動中に一度だけ行い、以後は結果を使い回す。
                    string detectedMode = ResolveGpuDecodeMode(enableGpuDecode: true, log);
                    if (string.IsNullOrWhiteSpace(detectedMode))
                    {
                        detectedMode = "off";
                    }

                    startupGpuMode = detectedMode;
                    startupGpuModeInitialized = true;
                    log?.Invoke($"gpu decode startup mode fixed: {startupGpuMode}");
                }

                // 設定画面のON/OFFは毎回反映する（再判定はしない）。
                string appliedMode = enableGpuDecode ? startupGpuMode : "off";
                Environment.SetEnvironmentVariable(GpuDecodeMode, appliedMode);
                return appliedMode;
            }
        }

        /// <summary>
        /// ffmpegの機能情報から利用可能GPUモードを判定する。
        /// </summary>
        private static string DetectBestGpuDecodeMode(Action<string> log)
        {
            string ffmpegExePath = ResolveFfmpegExecutablePath();
            (int exitCode, string stdout, string stderr) = RunProcess(
                ffmpegExePath,
                "-hide_banner -encoders"
            );
            string scanText = $"{stdout}\n{stderr}".ToLowerInvariant();

            bool hasCuda = ContainsAny(scanText, "h264_nvenc", "hevc_nvenc", "av1_nvenc", "cuda");
            bool hasQsv = ContainsAny(scanText, "h264_qsv", "hevc_qsv", "av1_qsv", "_qsv");
            bool hasAmd = ContainsAny(scanText, "h264_amf", "hevc_amf", "av1_amf", "_amf");

            string selectedMode = hasCuda ? "cuda" : hasQsv ? "qsv" : hasAmd ? "amd" : "off";
            log?.Invoke(
                $"gpu detect: ffmpeg='{ffmpegExePath}' exit={exitCode} cuda={hasCuda} qsv={hasQsv} amd={hasAmd} selected={selectedMode}"
            );
            return selectedMode;
        }

        private static bool ContainsAny(string text, params string[] tokens)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (text.Contains(token, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static (int exitCode, string stdout, string stderr) RunProcess(
            string fileName,
            string arguments
        )
        {
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                using Process process = new() { StartInfo = psi };
                if (!process.Start())
                {
                    return (-1, "", "process start returned false");
                }

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(5000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // タイムアウト時のKill失敗は無視して続行する。
                    }

                    return (-1, "", "process timeout");
                }

                Task.WaitAll([stdoutTask, stderrTask], 5000);
                string stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
                string stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";
                return (process.ExitCode, stdout, stderr);
            }
            catch (Exception ex)
            {
                return (-1, "", ex.Message);
            }
        }

        private static string ResolveFfmpegExecutablePath()
        {
            string configuredPath = GetFfmpegExePath();
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

            for (int i = 0; i < bundledCandidates.Length; i++)
            {
                string candidate = bundledCandidates[i];
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "ffmpeg";
        }
    }
}
