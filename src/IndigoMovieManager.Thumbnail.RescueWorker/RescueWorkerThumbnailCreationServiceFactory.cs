namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    /// <summary>
    /// rescue worker 用の host 依存をまとめて、サムネイル生成 service を組み立てる。
    /// </summary>
    internal static class RescueWorkerThumbnailCreationServiceFactory
    {
        internal static IThumbnailCreationService Create(string logDirectoryPath)
        {
            IThumbnailCreationHostRuntime hostRuntime = new RescueWorkerHostRuntime(
                logDirectoryPath
            );
            IThumbnailCreateProcessLogWriter processLogWriter =
                new RescueWorkerProcessLogWriter(hostRuntime);
            return ThumbnailCreationServiceFactory.Create(hostRuntime, processLogWriter);
        }
    }
}
