namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 生成前の path / cache / hash / duration hint 準備をまとめる。
    /// </summary>
    internal sealed class ThumbnailCreatePreparationResolver
    {
        private readonly ThumbnailMovieMetaResolver movieMetaResolver;

        public ThumbnailCreatePreparationResolver(ThumbnailMovieMetaResolver movieMetaResolver)
        {
            this.movieMetaResolver =
                movieMetaResolver ?? throw new ArgumentNullException(nameof(movieMetaResolver));
        }

        public ThumbnailCreatePreparation Prepare(ThumbnailCreatePreparationRequest request)
        {
            request ??= new ThumbnailCreatePreparationRequest();
            request.Request ??= new ThumbnailRequest();

            ThumbnailLayoutProfile layoutProfile = ThumbnailLayoutProfileResolver.Resolve(
                request.Request.TabIndex,
                ThumbnailDetailModeRuntime.ReadRuntimeMode()
            );
            string outPath = ThumbnailMovieMetaResolver.ResolveThumbnailOutPath(
                layoutProfile,
                request.DbName,
                request.ThumbFolder
            );
            string movieFullPath = request.Request.MovieFullPath;
            string sourceMovieFullPath = string.IsNullOrWhiteSpace(request.SourceMovieFullPathOverride)
                ? movieFullPath
                : request.SourceMovieFullPathOverride.Trim();
            string normalizedInitialEngineHint = request.InitialEngineHint?.Trim() ?? "";
            string normalizedTraceId = ThumbnailMovieTraceRuntime.NormalizeTraceId(request.TraceId);

            CachedMovieMeta cacheMeta = movieMetaResolver.GetCachedMovieMeta(
                movieFullPath,
                request.Request.Hash,
                out string cacheKey
            );
            string hash = cacheMeta.Hash;
            if (string.IsNullOrWhiteSpace(request.Request.Hash))
            {
                // 以降の経路でも再利用できるよう、確定済みハッシュを入力契約へ戻す。
                request.Request.Hash = hash;
            }

            string saveThumbFileName = ThumbnailPathResolver.BuildThumbnailPath(
                outPath,
                movieFullPath,
                hash
            );

            return new ThumbnailCreatePreparation
            {
                Request = request.Request,
                LayoutProfile = layoutProfile,
                ThumbnailOutPath = outPath,
                MovieFullPath = movieFullPath,
                SourceMovieFullPath = sourceMovieFullPath,
                InitialEngineHint = normalizedInitialEngineHint,
                TraceId = normalizedTraceId,
                CacheKey = cacheKey,
                CacheMeta = cacheMeta,
                DurationSec = cacheMeta.DurationSec,
                SaveThumbFileName = saveThumbFileName,
            };
        }

        public double? ResolveDurationIfMissing(ThumbnailCreatePreparation preparation)
        {
            if (preparation == null)
            {
                return null;
            }

            if (preparation.DurationSec.HasValue && preparation.DurationSec.Value > 0)
            {
                return preparation.DurationSec;
            }

            preparation.DurationSec = movieMetaResolver.ResolveDurationSec(
                preparation.SourceMovieFullPath,
                preparation.CacheKey,
                preparation.CacheMeta
            );
            return preparation.DurationSec;
        }
    }

    internal sealed class ThumbnailCreatePreparationRequest
    {
        public ThumbnailRequest Request { get; set; } = new();
        public string DbName { get; init; } = "";
        public string ThumbFolder { get; init; } = "";
        public string SourceMovieFullPathOverride { get; init; } = "";
        public string InitialEngineHint { get; init; } = "";
        public string TraceId { get; init; } = "";
    }

    internal sealed class ThumbnailCreatePreparation
    {
        public ThumbnailRequest Request { get; init; } = new();
        public ThumbnailLayoutProfile LayoutProfile { get; init; }
        public string ThumbnailOutPath { get; init; } = "";
        public string MovieFullPath { get; init; } = "";
        public string SourceMovieFullPath { get; init; } = "";
        public string InitialEngineHint { get; init; } = "";
        public string TraceId { get; init; } = "";
        public string CacheKey { get; init; } = "";
        public CachedMovieMeta CacheMeta { get; init; }
        public double? DurationSec { get; set; }
        public string SaveThumbFileName { get; init; } = "";
    }
}
