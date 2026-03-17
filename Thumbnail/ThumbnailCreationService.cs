using System.IO;
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

        private readonly ThumbnailEngineRouter engineRouter;
        private readonly ThumbnailCreateWorkflowCoordinator createWorkflowCoordinator;
        public ThumbnailCreationService()
            : this(
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                FallbackThumbnailCreationHostRuntime.Instance
            ) { }

        public ThumbnailCreationService(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger
        )
            : this(
                videoMetadataProvider,
                logger,
                FallbackThumbnailCreationHostRuntime.Instance
            ) { }

        public ThumbnailCreationService(IThumbnailCreationHostRuntime hostRuntime)
            : this(
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                hostRuntime
            ) { }

        public ThumbnailCreationService(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime
        )
            : this(videoMetadataProvider, logger, hostRuntime, null) { }

        public ThumbnailCreationService(
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter
        )
            : this(
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                hostRuntime,
                processLogWriter
            ) { }

        public ThumbnailCreationService(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter
        )
            : this(
                ThumbnailCreationServiceComponentFactory.CreateDefaultEngineSet(),
                videoMetadataProvider,
                logger,
                hostRuntime,
                processLogWriter
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine
        )
            : this(
                ThumbnailCreationServiceComponentFactory.CreateEngineSet(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine
                ),
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                FallbackThumbnailCreationHostRuntime.Instance,
                null
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IThumbnailCreationHostRuntime hostRuntime
        )
            : this(
                ThumbnailCreationServiceComponentFactory.CreateEngineSet(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine
                ),
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                hostRuntime,
                null
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter
        )
            : this(
                ThumbnailCreationServiceComponentFactory.CreateEngineSet(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine
                ),
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                hostRuntime,
                processLogWriter
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger
        )
            : this(
                ThumbnailCreationServiceComponentFactory.CreateEngineSet(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine
                ),
                videoMetadataProvider,
                logger,
                FallbackThumbnailCreationHostRuntime.Instance,
                null
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime
        )
            : this(
                ThumbnailCreationServiceComponentFactory.CreateEngineSet(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine
                ),
                videoMetadataProvider,
                logger,
                hostRuntime,
                null
            ) { }

        private ThumbnailCreationService(
            ThumbnailCreationEngineSet engineSet,
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter
        )
        {
            ThumbnailCreationServiceComposition composition =
                ThumbnailCreationServiceComponentFactory.Compose(
                    new ThumbnailCreationServiceComponentRequest
                    {
                        EngineSet = engineSet,
                        VideoMetadataProvider = videoMetadataProvider,
                        Logger = logger,
                        HostRuntime = hostRuntime,
                        ProcessLogWriter = processLogWriter,
                    }
                );
            engineRouter = composition.EngineRouter;
            createWorkflowCoordinator = composition.CreateWorkflowCoordinator;
        }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter
        )
            : this(
                ThumbnailCreationServiceComponentFactory.CreateEngineSet(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine
                ),
                videoMetadataProvider,
                logger,
                hostRuntime,
                processLogWriter
            ) { }

        /// <summary>
        /// ブックマーク用のとっておきの一枚（単一フレーム）を生成する専用ルートだ！📸
        /// </summary>
        public async Task<bool> CreateBookmarkThumbAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos
        )
        {
            if (!Path.Exists(movieFullPath))
            {
                return false;
            }

            IThumbnailGenerationEngine engine = engineRouter.ResolveForBookmark();
            try
            {
                return await engine.CreateBookmarkAsync(
                    movieFullPath,
                    saveThumbPath,
                    capturePos,
                    CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"bookmark create failed: engine={engine.EngineId}, movie='{movieFullPath}', err='{ex.Message}'"
                );
                return false;
            }
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
