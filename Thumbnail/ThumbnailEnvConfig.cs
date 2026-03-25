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
        public const string FfmpegOnePassThreadCount = "IMM_THUMB_FFMPEG1PASS_THREADS";
        public const string FfmpegOnePassPriority = "IMM_THUMB_FFMPEG1PASS_PRIORITY";
        public const string UltraLargeFileThresholdGbEnvName = "IMM_THUMB_ULTRA_LARGE_FILE_GB";
        public const string ThumbDecoder = "IMM_THUMB_DECODER";
        public const string ThumbFileLog = "IMM_THUMB_FILE_LOG";
        private const string SlowLaneSettingName = "ThumbnailSlowLaneMinGb";
        private const int DefaultSlowLaneMinGb = 3;
        private const int MinSlowLaneMinGb = 1;
        private const int MaxSlowLaneMinGb = 200;
        private const double DefaultUltraLargeFileThresholdGb = 32.0d;
        private const long OneGbBytes = 1024L * 1024L * 1024L;
        private static readonly object GpuDetectSync = new();
        private static readonly object StartupGpuInitSync = new();
        private static readonly object SlowLaneSettingsSync = new();
        private static bool hasCachedGpuMode;
        private static string cachedGpuMode = "off";
        private static bool startupGpuModeInitialized;
        private static string startupGpuMode = "off";
        private static long lastSlowLaneSettingsReadUtcTicks;
        private static int cachedSlowLaneMinGb = DefaultSlowLaneMinGb;

        // --- 読み取りヘルパー ---
        public static string GetGpuDecodeMode() =>
            Environment.GetEnvironmentVariable(GpuDecodeMode)?.Trim() ?? "";

        public static string GetThumbEngine() =>
            Environment.GetEnvironmentVariable(ThumbEngine)?.Trim() ?? "";

        public static string GetFfmpegExePath() =>
            Environment.GetEnvironmentVariable(FfmpegExePath);

        public static string GetFfmpegOnePassThreadCount() =>
            Environment.GetEnvironmentVariable(FfmpegOnePassThreadCount)?.Trim() ?? "";

        public static string GetFfmpegOnePassPriority() =>
            Environment.GetEnvironmentVariable(FfmpegOnePassPriority)?.Trim() ?? "";

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

        public static string NormalizeFfmpegOnePassPriority(string priority)
        {
            if (string.IsNullOrWhiteSpace(priority))
            {
                return "";
            }

            return priority.Trim().ToLowerInvariant() switch
            {
                "idle" => "idle",
                "below_normal" => "below_normal",
                "belownormal" => "below_normal",
                "low" => "below_normal",
                "normal" => "normal",
                "above_normal" => "above_normal",
                "abovenormal" => "above_normal",
                "high" => "high",
                _ => "",
            };
        }

        // ffmpeg1pass のエコ運転ヒントは、空文字で解除 / 値ありで明示に寄せる。
        public static void ApplyFfmpegOnePassExecutionHints(int? threadCount, string priority)
        {
            string threadText =
                threadCount.HasValue && threadCount.Value >= 1 ? threadCount.Value.ToString() : null;
            string normalizedPriority = NormalizeFfmpegOnePassPriority(priority);
            if (string.IsNullOrWhiteSpace(normalizedPriority))
            {
                normalizedPriority = null;
            }

            Environment.SetEnvironmentVariable(FfmpegOnePassThreadCount, threadText);
            Environment.SetEnvironmentVariable(FfmpegOnePassPriority, normalizedPriority);
        }

        public static (int? ThreadCount, string Priority) ResolveFfmpegOnePassEcoHint(
            int configuredParallelism,
            int slowLaneMinGb,
            int logicalCoreCount = -1
        )
        {
            int currentParallelism = ClampThumbnailParallelism(configuredParallelism);
            int currentSlowLaneMinGb = ClampSlowLaneMinGb(slowLaneMinGb);
            int safeLogicalCoreCount = logicalCoreCount > 0
                ? logicalCoreCount
                : Environment.ProcessorCount;

            if (
                IsFfmpegOnePassEcoPresetMatch(
                    currentParallelism,
                    currentSlowLaneMinGb,
                    safeLogicalCoreCount,
                    50,
                    parallelCount: 2
                )
            )
            {
                return (1, "idle");
            }

            if (
                IsFfmpegOnePassEcoPresetMatch(
                    currentParallelism,
                    currentSlowLaneMinGb,
                    safeLogicalCoreCount,
                    100,
                    parallelDivisor: 3
                )
            )
            {
                return (2, "below_normal");
            }

            if (
                IsFfmpegOnePassEcoPresetMatch(
                    currentParallelism,
                    currentSlowLaneMinGb,
                    safeLogicalCoreCount,
                    100,
                    parallelDivisor: 2
                )
            )
            {
                return (null, "");
            }

            if (currentParallelism <= 2)
            {
                return (1, "idle");
            }

            if (currentParallelism <= 4)
            {
                return (2, "below_normal");
            }

            return (null, "");
        }

        public static void ApplyFfmpegOnePassExecutionHintsForCurrentSettings(
            Action<string> log = null
        )
        {
            int configuredParallelism = ReadUserSettingInt(
                "ThumbnailParallelism",
                8,
                1,
                GetThumbnailParallelismUpperBound()
            );
            int slowLaneMinGb = ReadUserSettingInt(
                SlowLaneSettingName,
                DefaultSlowLaneMinGb,
                MinSlowLaneMinGb,
                MaxSlowLaneMinGb
            );
            (int? threadCount, string priority) = ResolveFfmpegOnePassEcoHint(
                configuredParallelism,
                slowLaneMinGb
            );
            ApplyFfmpegOnePassExecutionHints(threadCount, priority);
            log?.Invoke(
                $"ffmpeg1pass eco applied: threads={(threadCount.HasValue ? threadCount.Value.ToString() : "auto")} priority={(string.IsNullOrWhiteSpace(priority) ? "default" : priority)} parallel={configuredParallelism} slow_gb={slowLaneMinGb}"
            );
        }

        public static bool IsSlowLaneMovie(long movieSizeBytes)
        {
            long normalizedSizeBytes = movieSizeBytes < 0 ? 0 : movieSizeBytes;
            return normalizedSizeBytes >= ResolveSlowLaneThresholdBytes();
        }

        // rescue handoff では設定変更直後の値をその場で使いたいため、キャッシュを通さず読む。
        public static bool IsSlowLaneMovieImmediate(long movieSizeBytes)
        {
            long normalizedSizeBytes = movieSizeBytes < 0 ? 0 : movieSizeBytes;
            int slowLaneMinGb = ReadUserSettingInt(
                SlowLaneSettingName,
                DefaultSlowLaneMinGb,
                MinSlowLaneMinGb,
                MaxSlowLaneMinGb
            );
            return normalizedSizeBytes >= (slowLaneMinGb * OneGbBytes);
        }

        public static bool IsUltraLargeMovie(long movieSizeBytes)
        {
            long normalizedSizeBytes = movieSizeBytes < 0 ? 0 : movieSizeBytes;
            if (normalizedSizeBytes <= 0)
            {
                return false;
            }

            double thresholdGb = ReadDoubleFromEnv(
                UltraLargeFileThresholdGbEnvName,
                DefaultUltraLargeFileThresholdGb
            );
            if (thresholdGb <= 0)
            {
                return false;
            }

            double movieSizeGb = normalizedSizeBytes / (1024d * 1024d * 1024d);
            return movieSizeGb >= thresholdGb;
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

        private static long ResolveSlowLaneThresholdBytes()
        {
            RefreshCachedSlowLaneSettingsIfNeeded();
            return cachedSlowLaneMinGb * OneGbBytes;
        }

        private static void RefreshCachedSlowLaneSettingsIfNeeded()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            if (nowTicks - lastSlowLaneSettingsReadUtcTicks < TimeSpan.FromSeconds(1).Ticks)
            {
                return;
            }

            lock (SlowLaneSettingsSync)
            {
                nowTicks = DateTime.UtcNow.Ticks;
                if (nowTicks - lastSlowLaneSettingsReadUtcTicks < TimeSpan.FromSeconds(1).Ticks)
                {
                    return;
                }

                cachedSlowLaneMinGb = ReadUserSettingInt(
                    SlowLaneSettingName,
                    DefaultSlowLaneMinGb,
                    MinSlowLaneMinGb,
                    MaxSlowLaneMinGb
                );
                lastSlowLaneSettingsReadUtcTicks = nowTicks;
            }
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

        private static double ReadDoubleFromEnv(string envName, double fallback)
        {
            string raw = Environment.GetEnvironmentVariable(envName)?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            if (
                double.TryParse(
                    raw,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double parsed
                )
            )
            {
                return parsed;
            }

            return fallback;
        }

        private static bool IsFfmpegOnePassEcoPresetMatch(
            int currentParallelism,
            int currentSlowLaneMinGb,
            int logicalCoreCount,
            int presetSlowLaneMinGb,
            int? parallelDivisor = null,
            int? parallelCount = null
        )
        {
            return currentSlowLaneMinGb == ClampSlowLaneMinGb(presetSlowLaneMinGb)
                && currentParallelism
                    == ResolvePresetParallelism(logicalCoreCount, parallelDivisor, parallelCount);
        }

        private static int ResolvePresetParallelism(
            int logicalCoreCount,
            int? parallelDivisor,
            int? parallelCount
        )
        {
            if (parallelCount.HasValue)
            {
                return ClampThumbnailParallelism(parallelCount.Value);
            }

            int safeDivisor = parallelDivisor.GetValueOrDefault();
            if (safeDivisor < 1)
            {
                safeDivisor = 1;
            }

            int safeLogicalCoreCount = logicalCoreCount > 0 ? logicalCoreCount : 1;
            int resolved = safeLogicalCoreCount / safeDivisor;
            if (resolved < 1)
            {
                resolved = 1;
            }

            return ClampThumbnailParallelism(resolved);
        }

        public static int ThumbnailParallelismUpperBound => GetThumbnailParallelismUpperBound();

        // 並列数の上限は論理コア数の2倍で統一する。
        public static int GetThumbnailParallelismUpperBound()
        {
            int logicalCoreCount = Environment.ProcessorCount;
            if (logicalCoreCount < 1)
            {
                logicalCoreCount = 1;
            }

            return logicalCoreCount * 2;
        }

        public static int ClampThumbnailParallelism(int parallelism)
        {
            if (parallelism < 1)
            {
                return 1;
            }

            int upperBound = GetThumbnailParallelismUpperBound();
            if (parallelism > upperBound)
            {
                return upperBound;
            }

            return parallelism;
        }

        private static int ClampSlowLaneMinGb(int value)
        {
            if (value < MinSlowLaneMinGb)
            {
                return MinSlowLaneMinGb;
            }

            if (value > MaxSlowLaneMinGb)
            {
                return MaxSlowLaneMinGb;
            }

            return value;
        }

        private static int ReadUserSettingInt(
            string settingName,
            int defaultValue,
            int minValue,
            int maxValue
        )
        {
            if (!TryReadUserSettingInt(settingName, out int configuredValue))
            {
                return defaultValue;
            }

            if (configuredValue < minValue || configuredValue > maxValue)
            {
                return defaultValue;
            }

            return configuredValue;
        }

        private static bool TryReadUserSettingInt(string settingName, out int value)
        {
            value = 0;
            object settings = GetSettingsDefaultInstance();
            if (settings == null)
            {
                return false;
            }

            try
            {
                var settingProperty = settings
                    .GetType()
                    .GetProperty(settingName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (settingProperty == null)
                {
                    return false;
                }

                object raw = settingProperty.GetValue(settings);
                if (raw is int intValue)
                {
                    value = intValue;
                    return true;
                }

                if (raw != null && int.TryParse(raw.ToString(), out int parsed))
                {
                    value = parsed;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static object GetSettingsDefaultInstance()
        {
            try
            {
                Type settingsType = ResolveSettingsType();
                if (settingsType == null)
                {
                    return null;
                }

                var defaultProperty = settingsType.GetProperty(
                    "Default",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
                );
                return defaultProperty?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveSettingsType()
        {
            const string settingsTypeName = "IndigoMovieManager.Properties.Settings";
            Type resolved = Type.GetType(
                $"{settingsTypeName}, IndigoMovieManager_fork_workthree",
                false
            );
            if (resolved != null)
            {
                return resolved;
            }

            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                Type found = loadedAssemblies[i].GetType(settingsTypeName, false);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
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
