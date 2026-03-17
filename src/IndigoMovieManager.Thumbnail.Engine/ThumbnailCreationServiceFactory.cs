using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// service 本体を public facade のまま保つための生成入口。
    /// </summary>
    internal static class ThumbnailCreationServiceFactory
    {
        public static ThumbnailCreationService CreateDefault()
        {
            return ThumbnailCreationService.Create(
                ThumbnailCreationServiceComponentFactory.CreateDefaultOptions()
            );
        }

        public static ThumbnailCreationService Create(
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
        {
            return ThumbnailCreationService.Create(
                ThumbnailCreationServiceComponentFactory.CreateOptions(
                    hostRuntime: hostRuntime,
                    processLogWriter: processLogWriter
                )
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
                ThumbnailCreationServiceComponentFactory.CreateOptions(
                    videoMetadataProvider: videoMetadataProvider,
                    logger: logger,
                    hostRuntime: hostRuntime,
                    processLogWriter: processLogWriter
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
                ThumbnailCreationServiceComponentFactory.CreateTestingOptions(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine,
                    options
                )
            );
        }
    }
}
