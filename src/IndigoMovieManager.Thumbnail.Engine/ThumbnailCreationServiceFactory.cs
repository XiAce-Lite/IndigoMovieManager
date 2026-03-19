namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// ThumbnailCreationService を生成する唯一の正規入口。
    /// </summary>
    public static class ThumbnailCreationServiceFactory
    {
        internal static ThumbnailCreationServiceComposition CreateDefaultComposition()
        {
            return ThumbnailCreationServiceComponentFactory.Compose(
                ThumbnailCreationServiceComponentFactory.CreateDefaultOptions()
            );
        }

        internal static ThumbnailCreationServiceComposition CreateComposition(
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

        internal static ThumbnailCreationServiceComposition CreateComposition(
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

        internal static IThumbnailCreationService CreateDefault()
        {
            return new ThumbnailCreationService(CreateDefaultComposition());
        }

        public static IThumbnailCreationService Create(
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
        {
            return new ThumbnailCreationService(
                CreateComposition(hostRuntime, processLogWriter)
            );
        }

        public static IThumbnailCreationService Create(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
        {
            return new ThumbnailCreationService(
                CreateComposition(
                    videoMetadataProvider,
                    logger,
                    hostRuntime,
                    processLogWriter
                )
            );
        }
    }
}
