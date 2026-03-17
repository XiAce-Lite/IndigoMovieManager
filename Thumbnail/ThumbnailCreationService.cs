using System.Text;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【サムネイル生成の絶対的オーケストレータ】✨
    /// 状況とルールを見極め、最適な生成エンジンを召喚してサムネイルを爆誕させるぜ！🔥
    /// </summary>
    public sealed class ThumbnailCreationService
    {
        // .NET では既定で一部コードページ（例: 932）が無効なため、
        // 既存処理互換としてCodePagesプロバイダを有効化しておく。
        static ThumbnailCreationService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private readonly ThumbnailBookmarkCoordinator bookmarkCoordinator;
        private readonly ThumbnailCreateEntryCoordinator createEntryCoordinator;
        public ThumbnailCreationService()
            : this(ThumbnailCreationServiceComponentFactory.CreateDefaultOptions()) { }

        public ThumbnailCreationService(
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
            : this(
                ThumbnailCreationServiceComponentFactory.CreateOptions(
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
                ThumbnailCreationServiceComponentFactory.CreateOptions(
                    videoMetadataProvider: videoMetadataProvider,
                    logger: logger,
                    hostRuntime: hostRuntime,
                    processLogWriter: processLogWriter
                )
            ) { }

        internal static ThumbnailCreationService Create(ThumbnailCreationOptions options)
        {
            return new ThumbnailCreationService(options);
        }

        private ThumbnailCreationService(ThumbnailCreationOptions options)
        {
            ThumbnailCreationServiceComposition composition =
                ThumbnailCreationServiceComponentFactory.Compose(options);
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
                new ThumbnailCreateInvocation
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
                new ThumbnailCreateInvocation
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
    }
}
