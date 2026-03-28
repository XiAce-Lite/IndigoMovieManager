namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// ThumbnailCreationService を生成する「唯一の正規入口」。
    ///
    /// 【全体の流れでの位置づけ】
    ///   アプリ起動 → MainWindow 初期化
    ///     → AppThumbnailCreationServiceFactory（UI層のアダプタ）
    ///       → ★ここ★ ThumbnailCreationServiceFactory.Create()
    ///         → Composition（依存部品の組み立て）
    ///           → ThumbnailCreationService（実体）を返却
    ///
    /// UI層は Create() で IThumbnailCreationService を受け取るだけ。
    /// Engine 内部の部品構成（VideoMetadataProvider, Logger 等）はこの Factory が隠蔽する。
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
