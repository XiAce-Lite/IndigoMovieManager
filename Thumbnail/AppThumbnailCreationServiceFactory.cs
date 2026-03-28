using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    /// <summary>
    /// UI層（MainWindow）と Engine 層（ThumbnailCreationServiceFactory）を繋ぐアダプタ。
    ///
    /// 【全体の流れでの位置づけ】
    ///   MainWindow 初期化
    ///     → ★ここ★ AppThumbnailCreationServiceFactory.Create()
    ///       → アプリ固有の依存（AppVideoMetadataProvider / AppThumbnailLogger）を組み立て
    ///       → Engine 層の ThumbnailCreationServiceFactory.Create() へ委譲
    ///       → IThumbnailCreationService を返却
    ///
    /// Engine は UI やアプリ固有の事情を知らない。
    /// このクラスが「アプリのログ先」「Sinku.dll のメタ取得」等を Engine に注入する橋渡し役。
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
