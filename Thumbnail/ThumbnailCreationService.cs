using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【サムネイル生成の絶対的オーケストレータ】✨
    /// 状況とルールを見極め、最適な生成エンジンを召喚してサムネイルを爆誕させるぜ！🔥
    /// </summary>
    public sealed class ThumbnailCreationService
    {
        private readonly ThumbnailBookmarkCoordinator bookmarkCoordinator;
        private readonly ThumbnailCreateEntryCoordinator createEntryCoordinator;

        [Obsolete("ThumbnailCreationServiceFactory.CreateDefault() を使用してください。service の生成入口は Factory に統一します。")]
        public ThumbnailCreationService()
            : this(ThumbnailCreationServiceFactory.CreateDefaultComposition()) { }

        [Obsolete("ThumbnailCreationServiceFactory.Create(hostRuntime, processLogWriter) を使用してください。service の生成入口は Factory に統一します。")]
        public ThumbnailCreationService(
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
            : this(
                ThumbnailCreationServiceFactory.CreateComposition(
                    hostRuntime: hostRuntime,
                    processLogWriter: processLogWriter
                )
            ) { }

        [Obsolete("ThumbnailCreationServiceFactory.Create(videoMetadataProvider, logger, hostRuntime, processLogWriter) を使用してください。service の生成入口は Factory に統一します。")]
        public ThumbnailCreationService(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
            : this(
                ThumbnailCreationServiceFactory.CreateComposition(
                    videoMetadataProvider: videoMetadataProvider,
                    logger: logger,
                    hostRuntime: hostRuntime,
                    processLogWriter: processLogWriter
                )
            ) { }

        internal static ThumbnailCreationService Create(
            ThumbnailCreationServiceComposition composition
        )
        {
            return new ThumbnailCreationService(composition);
        }

        private ThumbnailCreationService(ThumbnailCreationServiceComposition composition)
        {
            ArgumentNullException.ThrowIfNull(composition);
            bookmarkCoordinator = composition.BookmarkCoordinator;
            createEntryCoordinator = composition.CreateEntryCoordinator;
        }

        /// <summary>
        /// ブックマーク用のとっておきの一枚（単一フレーム）を生成する専用ルートだ！📸
        /// </summary>
        [Obsolete("CreateBookmarkThumbAsync(ThumbnailBookmarkArgs, CancellationToken) を使用してください。")]
        public Task<bool> CreateBookmarkThumbAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos
        )
        {
            // 既存呼び出し互換のため残す wrapper。新規呼び出しは ThumbnailBookmarkArgs を使う。
            return CreateBookmarkThumbAsync(
                new ThumbnailBookmarkArgs
                {
                    MovieFullPath = movieFullPath,
                    SaveThumbPath = saveThumbPath,
                    CapturePos = capturePos,
                }
            );
        }

        public Task<bool> CreateBookmarkThumbAsync(
            ThumbnailBookmarkArgs args,
            CancellationToken cts = default
        )
        {
            ArgumentNullException.ThrowIfNull(args);
            return bookmarkCoordinator.CreateAsync(
                args.MovieFullPath,
                args.SaveThumbPath,
                args.CapturePos,
                cts
            );
        }

        /// <summary>
        /// サムネイル生成の本丸！通常・手動を問わず、すべての生成処理はここから始まる激アツなメイン・エントリーポイントだぜ！🚀
        /// </summary>
        [Obsolete("CreateThumbAsync(ThumbnailCreateArgs, CancellationToken) を使用してください。")]
        public Task<ThumbnailCreateResult> CreateThumbAsync(
            QueueObj queueObj,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual = false,
            CancellationToken cts = default,
            string sourceMovieFullPathOverride = null,
            string initialEngineHint = null,
            ThumbInfo thumbInfoOverride = null
        )
        {
            // 既存呼び出し互換のため残す wrapper。新規呼び出しは ThumbnailCreateArgs を使う。
            return CreateThumbAsync(
                new ThumbnailCreateArgs
                {
                    QueueObj = queueObj,
                    DbName = dbName,
                    ThumbFolder = thumbFolder,
                    IsResizeThumb = isResizeThumb,
                    IsManual = isManual,
                    SourceMovieFullPathOverride = sourceMovieFullPathOverride,
                    InitialEngineHint = initialEngineHint,
                    ThumbInfoOverride = thumbInfoOverride,
                },
                cts
            );
        }

        [Obsolete("CreateThumbAsync(ThumbnailCreateArgs, CancellationToken) を使用してください。")]
        public Task<ThumbnailCreateResult> CreateThumbAsync(
            ThumbnailRequest request,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual = false,
            CancellationToken cts = default,
            string sourceMovieFullPathOverride = null,
            string initialEngineHint = null,
            ThumbInfo thumbInfoOverride = null
        )
        {
            // 既存呼び出し互換のため残す wrapper。新規呼び出しは ThumbnailCreateArgs を使う。
            return CreateThumbAsync(
                new ThumbnailCreateArgs
                {
                    Request = request,
                    DbName = dbName,
                    ThumbFolder = thumbFolder,
                    IsResizeThumb = isResizeThumb,
                    IsManual = isManual,
                    SourceMovieFullPathOverride = sourceMovieFullPathOverride,
                    InitialEngineHint = initialEngineHint,
                    ThumbInfoOverride = thumbInfoOverride,
                },
                cts
            );
        }

        public Task<ThumbnailCreateResult> CreateThumbAsync(
            ThumbnailCreateArgs args,
            CancellationToken cts = default
        )
        {
            ArgumentNullException.ThrowIfNull(args);
            return createEntryCoordinator.CreateAsync(args, cts);
        }
    }
}
