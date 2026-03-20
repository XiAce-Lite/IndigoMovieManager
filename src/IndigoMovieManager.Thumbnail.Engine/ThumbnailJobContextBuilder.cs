using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// service 本体から、生成前の context 組み立てを切り離す。
    /// </summary>
    internal sealed class ThumbnailJobContextBuilder
    {
        private readonly ThumbnailMovieMetaResolver movieMetaResolver;

        public ThumbnailJobContextBuilder(ThumbnailMovieMetaResolver movieMetaResolver)
        {
            this.movieMetaResolver =
                movieMetaResolver ?? throw new ArgumentNullException(nameof(movieMetaResolver));
        }

        public ThumbnailJobContextBuildOutcome Build(ThumbnailJobContextBuildRequest request)
        {
            if (request == null)
            {
                return ThumbnailJobContextBuildOutcome.Fail("context build request is null");
            }

            ThumbInfo thumbInfo = ResolveThumbInfo(request, out string errorMessage);
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                return ThumbnailJobContextBuildOutcome.Fail(errorMessage);
            }

            double? avgBitrateMbps = ResolveAverageBitrateMbps(
                request.FileSizeBytes,
                request.DurationSec
            );
            string sourceMovieFullPath = string.IsNullOrWhiteSpace(request.SourceMovieFullPath)
                ? request.MovieFullPath
                : request.SourceMovieFullPath;
            string videoCodec = movieMetaResolver.ResolveVideoCodec(sourceMovieFullPath);

            ThumbnailJobContext context = new()
            {
                Request = request.Request,
                LayoutProfile = request.LayoutProfile,
                ThumbnailOutPath = request.ThumbnailOutPath,
                ThumbInfo = thumbInfo,
                MovieFullPath = sourceMovieFullPath,
                SaveThumbFileName = request.SaveThumbFileName,
                IsResizeThumb = request.IsResizeThumb,
                IsManual = request.IsManual,
                DurationSec = request.DurationSec,
                FileSizeBytes = request.FileSizeBytes,
                IsSlowLane = ThumbnailEnvConfig.IsSlowLaneMovie(request.FileSizeBytes),
                IsUltraLargeMovie = ThumbnailEnvConfig.IsUltraLargeMovie(request.FileSizeBytes),
                AverageBitrateMbps = avgBitrateMbps,
                HasEmojiPath = ThumbnailEngineRouter.HasUnmappableAnsiChar(request.MovieFullPath),
                VideoCodec = videoCodec,
                InitialEngineHint = request.InitialEngineHint?.Trim() ?? "",
                TraceId = ThumbnailMovieTraceRuntime.NormalizeTraceId(request.TraceId),
            };
            return ThumbnailJobContextBuildOutcome.Success(context);
        }

        private static ThumbInfo ResolveThumbInfo(
            ThumbnailJobContextBuildRequest request,
            out string errorMessage
        )
        {
            errorMessage = "";
            if (!request.IsManual)
            {
                // 救済worker から再取得秒を差し込まれた時だけ、その指示を優先する。
                return request.ThumbInfoOverride
                    ?? ThumbnailAutoThumbInfoBuilder.Build(request.LayoutProfile, request.DurationSec);
            }

            ThumbInfo thumbInfo = new();
            thumbInfo.GetThumbInfo(request.SaveThumbFileName);
            if (!thumbInfo.IsThumbnail)
            {
                errorMessage = "manual source thumbnail metadata is missing";
                return null;
            }

            if (
                request.Request?.ThumbPanelPosition != null
                && request.Request.ThumbTimePosition != null
            )
            {
                int panelPos = request.Request.ThumbPanelPosition.Value;
                if (panelPos >= 0 && panelPos < thumbInfo.ThumbSec.Count)
                {
                    thumbInfo.ThumbSec[panelPos] = request.Request.ThumbTimePosition.Value;
                }
            }

            thumbInfo.NewThumbInfo();
            return thumbInfo;
        }

        private static double? ResolveAverageBitrateMbps(long fileSizeBytes, double? durationSec)
        {
            if (fileSizeBytes <= 0 || !durationSec.HasValue || durationSec.Value <= 0)
            {
                return null;
            }

            return (fileSizeBytes * 8d) / (durationSec.Value * 1_000_000d);
        }
    }

    internal sealed class ThumbnailJobContextBuildRequest
    {
        public ThumbnailRequest Request { get; init; } = new();
        public ThumbnailLayoutProfile LayoutProfile { get; init; }
        public string ThumbnailOutPath { get; init; } = "";
        public string MovieFullPath { get; init; } = "";
        public string SourceMovieFullPath { get; init; } = "";
        public string SaveThumbFileName { get; init; } = "";
        public bool IsResizeThumb { get; init; }
        public bool IsManual { get; init; }
        public double? DurationSec { get; init; }
        public long FileSizeBytes { get; init; }
        public string InitialEngineHint { get; init; } = "";
        public string TraceId { get; init; } = "";
        public ThumbInfo ThumbInfoOverride { get; init; }
    }

    internal sealed class ThumbnailJobContextBuildOutcome
    {
        private ThumbnailJobContextBuildOutcome(
            ThumbnailJobContext context,
            string errorMessage
        )
        {
            Context = context;
            ErrorMessage = errorMessage ?? "";
        }

        public ThumbnailJobContext Context { get; }
        public string ErrorMessage { get; }
        public bool IsSuccess => Context != null;

        public static ThumbnailJobContextBuildOutcome Success(ThumbnailJobContext context)
        {
            return new ThumbnailJobContextBuildOutcome(context, "");
        }

        public static ThumbnailJobContextBuildOutcome Fail(string errorMessage)
        {
            return new ThumbnailJobContextBuildOutcome(null, errorMessage);
        }
    }
}
