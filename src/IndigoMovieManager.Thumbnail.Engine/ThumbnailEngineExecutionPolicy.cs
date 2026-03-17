using System.Globalization;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 生成エンジンの順序、skip、autogen retry の判断をまとめる。
    /// </summary>
    internal sealed class ThumbnailEngineExecutionPolicy
    {
        private const string EngineEnvName = "IMM_THUMB_ENGINE";
        private const string AutogenRetryEnvName = "IMM_THUMB_AUTOGEN_RETRY";
        private const string AutogenRetryDelayMsEnvName = "IMM_THUMB_AUTOGEN_RETRY_DELAY_MS";
        private const int DefaultAutogenRetryCount = 0;
        private const int DefaultAutogenRetryDelayMs = 300;
        private static readonly string[] AutogenTransientRetryKeywords =
        [
            "a generic error occurred in gdi+",
            "no frames decoded",
            "resource temporarily unavailable",
            "cannot allocate memory",
            "timeout",
        ];
        private static readonly string[] FfmpegOnePassSkipKeywords =
        [
            "invalid data found when processing input",
            "moov atom not found",
            "video stream is missing",
        ];

        private readonly IThumbnailGenerationEngine ffMediaToolkitEngine;
        private readonly IThumbnailGenerationEngine ffmpegOnePassEngine;
        private readonly IThumbnailGenerationEngine openCvEngine;
        private readonly IThumbnailGenerationEngine autogenEngine;

        public ThumbnailEngineExecutionPolicy(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine
        )
        {
            this.ffMediaToolkitEngine =
                ffMediaToolkitEngine
                ?? throw new ArgumentNullException(nameof(ffMediaToolkitEngine));
            this.ffmpegOnePassEngine =
                ffmpegOnePassEngine ?? throw new ArgumentNullException(nameof(ffmpegOnePassEngine));
            this.openCvEngine =
                openCvEngine ?? throw new ArgumentNullException(nameof(openCvEngine));
            this.autogenEngine =
                autogenEngine ?? throw new ArgumentNullException(nameof(autogenEngine));
        }

        public List<IThumbnailGenerationEngine> BuildThumbnailEngineOrder(
            IThumbnailGenerationEngine selectedEngine,
            ThumbnailJobContext context
        )
        {
            List<IThumbnailGenerationEngine> order = [];
            AddEngine(order, selectedEngine);

            if (IsForcedEngineMode())
            {
                return order;
            }

            if (context?.IsManual != true && string.IsNullOrWhiteSpace(context?.InitialEngineHint))
            {
                // 通常自動レーンは autogen 1 本だけで見切り、本線内フォールバックを持たせない。
                return order;
            }

            // OpenCV は ANSI 制約があるため、絵文字パスでは候補から外す。
            bool skipOpenCv = context?.HasEmojiPath == true;

            if (context?.IsManual == true)
            {
                if (
                    string.Equals(
                        selectedEngine?.EngineId,
                        "ffmediatoolkit",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    if (!skipOpenCv)
                    {
                        AddEngine(order, openCvEngine);
                    }
                }
                else
                {
                    AddEngine(order, ffMediaToolkitEngine);
                }
                return order;
            }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "autogen",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                AddEngine(order, ffMediaToolkitEngine);
                AddEngine(order, ffmpegOnePassEngine);
                if (!skipOpenCv)
                {
                    AddEngine(order, openCvEngine);
                }
                return order;
            }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "ffmediatoolkit",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                AddEngine(order, autogenEngine);
                AddEngine(order, ffmpegOnePassEngine);
                if (!skipOpenCv)
                {
                    AddEngine(order, openCvEngine);
                }
                return order;
            }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "ffmpeg1pass",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                AddEngine(order, autogenEngine);
                AddEngine(order, ffMediaToolkitEngine);
                if (!skipOpenCv)
                {
                    AddEngine(order, openCvEngine);
                }
                return order;
            }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "opencv",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                AddEngine(order, autogenEngine);
                AddEngine(order, ffMediaToolkitEngine);
                AddEngine(order, ffmpegOnePassEngine);
                return order;
            }

            AddEngine(order, autogenEngine);
            AddEngine(order, ffMediaToolkitEngine);
            AddEngine(order, ffmpegOnePassEngine);
            if (!skipOpenCv)
            {
                AddEngine(order, openCvEngine);
            }
            return order;
        }

        public bool ShouldRecordFallbackToFfmpegOnePass(
            IThumbnailGenerationEngine selectedEngine,
            IThumbnailGenerationEngine candidate,
            int orderIndex
        )
        {
            return orderIndex > 0
                && string.Equals(
                    selectedEngine?.EngineId,
                    "autogen",
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(
                    candidate?.EngineId,
                    "ffmpeg1pass",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        public bool ShouldSkipFfmpegOnePassByKnownInvalidInput(
            IReadOnlyList<string> engineErrorMessages
        )
        {
            if (engineErrorMessages == null || engineErrorMessages.Count < 1)
            {
                return false;
            }

            for (int i = 0; i < engineErrorMessages.Count; i++)
            {
                string message = engineErrorMessages[i];
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                if (ContainsAnyKeyword(message, FfmpegOnePassSkipKeywords))
                {
                    return true;
                }
            }
            return false;
        }

        public ThumbnailAutogenRetryDecision EvaluateAutogenRetry(
            IThumbnailGenerationEngine candidate,
            ThumbnailCreateResult result,
            int currentRetryCount
        )
        {
            if (
                !string.Equals(
                    candidate?.EngineId,
                    "autogen",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return ThumbnailAutogenRetryDecision.None;
            }

            bool isTransientFailure =
                result != null && !result.IsSuccess && IsAutogenTransientRetryError(result.ErrorMessage);
            int maxRetryCount = ResolveAutogenRetryCount();
            bool canRetry =
                isTransientFailure
                && currentRetryCount < maxRetryCount
                && IsAutogenRetryEnabled();
            int retryDelayMs = canRetry ? ResolveAutogenRetryDelayMs() : 0;
            return new ThumbnailAutogenRetryDecision(
                isTransientFailure,
                canRetry,
                retryDelayMs,
                maxRetryCount
            );
        }

        private static bool IsForcedEngineMode()
        {
            string mode = Environment.GetEnvironmentVariable(EngineEnvName)?.Trim() ?? "";
            return !string.IsNullOrWhiteSpace(mode)
                && !string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAutogenRetryEnabled()
        {
            string mode = Environment.GetEnvironmentVariable(AutogenRetryEnvName)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(mode))
            {
                return true;
            }

            string normalized = mode.ToLowerInvariant();
            return normalized is "1" or "true" or "on" or "yes" or "auto";
        }

        private static int ResolveAutogenRetryCount()
        {
            return DefaultAutogenRetryCount;
        }

        private static int ResolveAutogenRetryDelayMs()
        {
            string raw = Environment.GetEnvironmentVariable(AutogenRetryDelayMsEnvName)?.Trim() ?? "";
            if (
                !string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            )
            {
                if (parsed < 0)
                {
                    return 0;
                }
                if (parsed > 5000)
                {
                    return 5000;
                }
                return parsed;
            }

            return DefaultAutogenRetryDelayMs;
        }

        private static bool IsAutogenTransientRetryError(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return false;
            }

            string normalized = errorMessage.ToLowerInvariant();
            for (int i = 0; i < AutogenTransientRetryKeywords.Length; i++)
            {
                if (normalized.Contains(AutogenTransientRetryKeywords[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsAnyKeyword(string text, IReadOnlyList<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null)
            {
                return false;
            }

            for (int i = 0; i < keywords.Count; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddEngine(
            List<IThumbnailGenerationEngine> order,
            IThumbnailGenerationEngine engine
        )
        {
            if (engine == null)
            {
                return;
            }

            for (int i = 0; i < order.Count; i++)
            {
                if (
                    string.Equals(
                        order[i].EngineId,
                        engine.EngineId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return;
                }
            }

            order.Add(engine);
        }
    }

    /// <summary>
    /// autogen 再試行の判定結果だけを service 側へ返す。
    /// </summary>
    internal readonly struct ThumbnailAutogenRetryDecision
    {
        public static ThumbnailAutogenRetryDecision None { get; } = new(false, false, 0, 0);

        public ThumbnailAutogenRetryDecision(
            bool isTransientFailure,
            bool canRetry,
            int retryDelayMs,
            int maxRetryCount
        )
        {
            IsTransientFailure = isTransientFailure;
            CanRetry = canRetry;
            RetryDelayMs = retryDelayMs;
            MaxRetryCount = maxRetryCount;
        }

        public bool IsTransientFailure { get; }
        public bool CanRetry { get; }
        public int RetryDelayMs { get; }
        public int MaxRetryCount { get; }
    }
}
