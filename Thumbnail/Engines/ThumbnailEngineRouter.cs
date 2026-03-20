using System.Globalization;
using System.Text;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// 仕様に沿ってエンジンを選択するルーター。
    /// </summary>
    internal sealed class ThumbnailEngineRouter
    {
        private const string EngineEnvName = "IMM_THUMB_ENGINE";
        private const string LargeFileThresholdGbEnvName = "IMM_THUMB_LARGE_FILE_GB";
        private const string HighAvgBitrateMbpsEnvName = "IMM_THUMB_HIGH_AVG_BITRATE_MBPS";
        private const double DefaultLargeFileThresholdGb = 4.0d;
        private const double DefaultHighAvgBitrateMbps = 20.0d;
        private static readonly Encoding AnsiEncoding = CreateAnsiEncoding();
        private readonly IReadOnlyDictionary<string, IThumbnailGenerationEngine> engines;

        public ThumbnailEngineRouter(IEnumerable<IThumbnailGenerationEngine> engines)
        {
            Dictionary<string, IThumbnailGenerationEngine> map = new(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (IThumbnailGenerationEngine engine in engines ?? [])
            {
                if (engine == null || string.IsNullOrWhiteSpace(engine.EngineId))
                {
                    continue;
                }
                map[engine.EngineId] = engine;
            }
            this.engines = map;
        }

        // ブックマークは位置指定優先で FFMediaToolkit を選ぶ。
        public IThumbnailGenerationEngine ResolveForBookmark()
        {
            return TryGetEngine("ffmediatoolkit", out IThumbnailGenerationEngine engine)
                ? engine
                : engines.Values.FirstOrDefault();
        }

        public IThumbnailGenerationEngine ResolveForThumbnail(ThumbnailJobContext context)
        {
            if (TryResolveForcedEngine(out IThumbnailGenerationEngine forcedEngine))
            {
                return forcedEngine;
            }

            string initialEngineHint = context?.InitialEngineHint?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(initialEngineHint))
            {
                // 明示救済だけは呼び出し側から先頭エンジンを渡し、QueueObjへ役割外の状態を持たせない。
                return ResolveOrFallback(initialEngineHint);
            }

            // 既定運用では自動・手動ともに autogen を固定採用する。
            if (context?.IsManual == true)
            {
                return ResolveOrFallback("autogen");
            }

            if (context != null && IsUltraLargeMovie(context))
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"engine route override: id=autogen, reason='ultra-large-file-first-300sec', size_gb={context.FileSizeBytes / (1024d * 1024d * 1024d):0.###}, panel={context.PanelCount}"
                );
                return ResolveOrFallback("autogen");
            }

            if (context?.HasEmojiPath == true)
            {
                return ResolveOrFallback("autogen");
            }

            if (context != null && context.PanelCount >= 10 && IsLargeFile(context))
            {
                return ResolveOrFallback("ffmpeg1pass");
            }

            if (context != null && context.PanelCount >= 10 && IsHighAvgBitrate(context))
            {
                return ResolveOrFallback("autogen");
            }

            if (
                context?.PanelCount >= 10
                && context.DurationSec.HasValue
                && context.DurationSec.Value >= TimeSpan.FromMinutes(120).TotalSeconds
            )
            {
                return ResolveOrFallback("ffmpeg1pass");
            }

            return ResolveOrFallback("autogen");
        }

        public static bool HasUnmappableAnsiChar(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                _ = AnsiEncoding.GetBytes(text);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static Encoding CreateAnsiEncoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(
                932,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback
            );
        }

        private bool TryResolveForcedEngine(out IThumbnailGenerationEngine forcedEngine)
        {
            forcedEngine = null;
            string mode = Environment.GetEnvironmentVariable(EngineEnvName)?.Trim() ?? "";
            if (
                string.IsNullOrWhiteSpace(mode)
                || string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            if (!TryGetEngine(mode, out forcedEngine))
            {
                ThumbnailRuntimeLog.Write("thumbnail", $"unknown thumb engine '{mode}'. fallback=auto");
                forcedEngine = null;
                return false;
            }

            return true;
        }

        private IThumbnailGenerationEngine ResolveOrFallback(string engineId)
        {
            if (TryGetEngine(engineId, out IThumbnailGenerationEngine engine))
            {
                return engine;
            }

            if (engines.Count > 0)
            {
                return engines.Values.First();
            }

            throw new InvalidOperationException("thumbnail engine is not configured.");
        }

        private bool TryGetEngine(string engineId, out IThumbnailGenerationEngine engine)
        {
            engine = null;
            if (string.IsNullOrWhiteSpace(engineId))
            {
                return false;
            }
            if (!engines.TryGetValue(engineId, out engine))
            {
                return false;
            }
            if (!engine.CanHandle(null))
            {
                return false;
            }
            return true;
        }

        private static bool IsLargeFile(ThumbnailJobContext context)
        {
            if (context == null || context.FileSizeBytes <= 0)
            {
                return false;
            }

            double thresholdGb = ReadDoubleFromEnv(
                LargeFileThresholdGbEnvName,
                DefaultLargeFileThresholdGb
            );
            if (thresholdGb <= 0)
            {
                return false;
            }

            double fileGb = context.FileSizeBytes / (1024d * 1024d * 1024d);
            return fileGb >= thresholdGb;
        }

        private static bool IsUltraLargeMovie(ThumbnailJobContext context)
        {
            if (context == null)
            {
                return false;
            }

            return context.IsUltraLargeMovie || ThumbnailEnvConfig.IsUltraLargeMovie(context.FileSizeBytes);
        }

        private static bool IsHighAvgBitrate(ThumbnailJobContext context)
        {
            if (context?.AverageBitrateMbps == null || context.AverageBitrateMbps.Value <= 0)
            {
                return false;
            }

            double thresholdMbps = ReadDoubleFromEnv(
                HighAvgBitrateMbpsEnvName,
                DefaultHighAvgBitrateMbps
            );
            if (thresholdMbps <= 0)
            {
                return false;
            }

            return context.AverageBitrateMbps.Value >= thresholdMbps;
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
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsed
                )
            )
            {
                return parsed;
            }
            return fallback;
        }
    }
}
