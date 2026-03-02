namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// インデックス提供元（Everythingや将来のスクラッチ実装）を共通化する契約。
    /// </summary>
    internal interface IFileIndexProvider
    {
        AvailabilityResult CheckAvailability();

        FileIndexMovieResult CollectMoviePaths(FileIndexQueryOptions options);

        FileIndexThumbnailBodyResult CollectThumbnailBodies(string thumbFolder);
    }

    /// <summary>
    /// ホスト側が使う統一窓口。
    /// モード判定とfallback方針はここで統一する。
    /// </summary>
    internal interface IIndexProviderFacade
    {
        bool IsIntegrationConfigured(IntegrationMode mode);

        AvailabilityResult CheckAvailability(IntegrationMode mode);

        ScanByProviderResult CollectMoviePathsWithFallback(
            FileIndexQueryOptions options,
            IntegrationMode mode
        );

        FileIndexThumbnailBodyResult CollectThumbnailBodiesWithFallback(
            string thumbFolder,
            IntegrationMode mode
        );
    }
}
