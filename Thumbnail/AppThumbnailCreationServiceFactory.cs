using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    /// <summary>
    /// アプリ本体用の host 依存をまとめて、サムネイル生成 service を組み立てる。
    /// </summary>
    internal static class AppThumbnailCreationServiceFactory
    {
        internal static IThumbnailCreationService Create(string logDirectoryPath)
        {
            IThumbnailCreationHostRuntime hostRuntime = new DefaultThumbnailCreationHostRuntime(
                logDirectoryPath
            );
            IThumbnailCreateProcessLogWriter processLogWriter =
                new DefaultThumbnailCreateProcessLogWriter(hostRuntime);

            return ThumbnailCreationServiceFactory.Create(
                new AppVideoMetadataProvider(),
                new AppThumbnailLogger(),
                hostRuntime,
                processLogWriter
            );
        }
    }
}
