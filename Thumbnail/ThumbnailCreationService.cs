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
        private readonly ThumbnailCreateWorkflowCoordinator createWorkflowCoordinator;
        public ThumbnailCreationService()
            : this(CreateOptions()) { }

        public ThumbnailCreationService(
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
            : this(
                CreateOptions(
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
                CreateOptions(
                    videoMetadataProvider: videoMetadataProvider,
                    logger: logger,
                    hostRuntime: hostRuntime,
                    processLogWriter: processLogWriter
                )
            ) { }

        internal static ThumbnailCreationService CreateForTesting(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            ThumbnailCreationOptions options = null
        )
        {
            return new ThumbnailCreationService(
                CreateOptions(
                    engineSet: ThumbnailCreationServiceComponentFactory.CreateEngineSet(
                        ffMediaToolkitEngine,
                        ffmpegOnePassEngine,
                        openCvEngine,
                        autogenEngine
                    ),
                    videoMetadataProvider: options?.VideoMetadataProvider,
                    logger: options?.Logger,
                    hostRuntime: options?.HostRuntime,
                    processLogWriter: options?.ProcessLogWriter
                )
            );
        }

        private ThumbnailCreationService(ThumbnailCreationOptions options)
        {
            ThumbnailCreationServiceComposition composition =
                ThumbnailCreationServiceComponentFactory.Compose(options);
            bookmarkCoordinator = composition.BookmarkCoordinator;
            createWorkflowCoordinator = composition.CreateWorkflowCoordinator;
        }

        // 公開入口やテスト入口の差はここで吸収し、service 本体は options だけを見る。
        private static ThumbnailCreationOptions CreateOptions(
            ThumbnailCreationEngineSet engineSet = null,
            IVideoMetadataProvider videoMetadataProvider = null,
            IThumbnailLogger logger = null,
            IThumbnailCreationHostRuntime hostRuntime = null,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
        {
            return new ThumbnailCreationOptions
            {
                EngineSet =
                    engineSet ?? ThumbnailCreationServiceComponentFactory.CreateDefaultEngineSet(),
                VideoMetadataProvider = videoMetadataProvider ?? NoOpVideoMetadataProvider.Instance,
                Logger = logger ?? NoOpThumbnailLogger.Instance,
                HostRuntime = hostRuntime ?? FallbackThumbnailCreationHostRuntime.Instance,
                ProcessLogWriter = processLogWriter,
            };
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
            // 既存の QueueObj 呼び出しは残しつつ、中の本流だけ新契約へ寄せていく。
            ThumbnailRequest request = queueObj?.ToThumbnailRequest() ?? new ThumbnailRequest();
            try
            {
                return await CreateThumbAsync(
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
            finally
            {
                queueObj?.ApplyThumbnailRequest(request);
            }
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
            return await createWorkflowCoordinator.ExecuteAsync(
                new ThumbnailCreateWorkflowRequest
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
