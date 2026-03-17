using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// service 本体を public facade のまま保つための生成入口。
    /// </summary>
    internal static class ThumbnailCreationServiceFactory
    {
        public static ThumbnailCreationServiceComposition CreateDefaultComposition()
        {
            return ThumbnailCreationServiceComponentFactory.Compose(
                ThumbnailCreationServiceComponentFactory.CreateDefaultOptions()
            );
        }

        public static ThumbnailCreationServiceComposition CreateComposition(
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
        {
            return ThumbnailCreationServiceComponentFactory.Compose(
                ThumbnailCreationServiceComponentFactory.CreateOptions(
                    hostRuntime: hostRuntime,
                    processLogWriter: processLogWriter
                )
            );
        }

        public static ThumbnailCreationServiceComposition CreateComposition(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
        {
            return ThumbnailCreationServiceComponentFactory.Compose(
                ThumbnailCreationServiceComponentFactory.CreateOptions(
                    videoMetadataProvider: videoMetadataProvider,
                    logger: logger,
                    hostRuntime: hostRuntime,
                    processLogWriter: processLogWriter
                )
            );
        }

        public static ThumbnailCreationService CreateDefault()
        {
            return ThumbnailCreationService.Create(CreateDefaultComposition());
        }

        public static ThumbnailCreationService Create(
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
        {
            return ThumbnailCreationService.Create(
                CreateComposition(hostRuntime, processLogWriter)
            );
        }

        public static ThumbnailCreationService Create(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
        {
            return ThumbnailCreationService.Create(
                CreateComposition(
                    videoMetadataProvider,
                    logger,
                    hostRuntime,
                    processLogWriter
                )
            );
        }

        public static ThumbnailCreationService CreateForTesting(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            ThumbnailCreationOptions options = null
        )
        {
            return ThumbnailCreationService.Create(
                ThumbnailCreationServiceComponentFactory.Compose(
                    ThumbnailCreationServiceComponentFactory.CreateTestingOptions(
                        ffMediaToolkitEngine,
                        ffmpegOnePassEngine,
                        openCvEngine,
                        autogenEngine,
                        options
                    )
                )
            );
        }
    }
}
