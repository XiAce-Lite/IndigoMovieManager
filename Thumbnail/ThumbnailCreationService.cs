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
        public ThumbnailCreationService()
            : this(ThumbnailCreationServiceFactory.CreateDefaultComposition()) { }

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
        public async Task<bool> CreateBookmarkThumbAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos
        )
        {
            return await bookmarkCoordinator.CreateAsync(
                movieFullPath,
                saveThumbPath,
                capturePos,
                CancellationToken.None
            );
        }

        /// <summary>
        /// サムネイル生成の本丸！通常・手動を問わず、すべての生成処理はここから始まる激アツなメイン・エントリーポイントだぜ！🚀
        /// </summary>
        public async Task<ThumbnailCreateResult> CreateThumbAsync(
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
            return await createEntryCoordinator.CreateAsync(
                queueObj,
                dbName,
                thumbFolder,
                isResizeThumb,
                isManual,
                cts,
                sourceMovieFullPathOverride,
                initialEngineHint,
                thumbInfoOverride
            );
        }

        public async Task<ThumbnailCreateResult> CreateThumbAsync(
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
            return await createEntryCoordinator.CreateAsync(
                request,
                dbName,
                thumbFolder,
                isResizeThumb,
                isManual,
                cts,
                sourceMovieFullPathOverride,
                initialEngineHint,
                thumbInfoOverride
            );
        }
    }
}
