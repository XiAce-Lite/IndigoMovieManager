using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // MainWindow 配下の partial で同じ保存先規則を共有し、layout / path 解決を一箇所へ寄せる。
        private static ThumbnailLayoutProfile ResolveThumbnailLayoutProfile(int tabIndex)
        {
            return ThumbnailLayoutProfileResolver.Resolve(
                tabIndex,
                ThumbnailDetailModeRuntime.ReadRuntimeMode()
            );
        }

        // 現在の app 規約どおり、thumbFolder 未指定時は既定Thumb配下へ寄せる。
        private static string ResolveThumbnailOutPath(int tabIndex, string dbName, string thumbFolder)
        {
            ThumbnailLayoutProfile layoutProfile = ResolveThumbnailLayoutProfile(tabIndex);
            string thumbRoot = string.IsNullOrWhiteSpace(thumbFolder)
                ? ThumbRootResolver.GetDefaultThumbRoot(dbName)
                : thumbFolder;
            return layoutProfile.BuildOutPath(thumbRoot);
        }

        // DB配置と個別設定から、実運用で使うサムネ根を一箇所で解決する。
        private static string ResolveRuntimeThumbnailRoot(
            string dbFullPath,
            string dbName,
            string thumbFolder,
            string defaultBaseDirectory = ""
        )
        {
            return ThumbRootResolver.ResolveRuntimeThumbRoot(
                dbFullPath,
                dbName,
                thumbFolder,
                defaultBaseDirectory
            );
        }

        private string ResolveCurrentThumbnailRoot()
        {
            return ResolveRuntimeThumbnailRoot(
                MainVM?.DbInfo?.DBFullPath ?? "",
                MainVM?.DbInfo?.DBName ?? "",
                MainVM?.DbInfo?.ThumbFolder ?? ""
            );
        }

        private string ResolveCurrentThumbnailOutPath(int tabIndex)
        {
            return ResolveThumbnailOutPath(
                tabIndex,
                MainVM?.DbInfo?.DBName ?? "",
                MainVM?.DbInfo?.ThumbFolder ?? ""
            );
        }

        private string BuildCurrentThumbnailPath(int tabIndex, string movieNameOrPath, string hash)
        {
            return ThumbnailPathResolver.BuildThumbnailPath(
                ResolveCurrentThumbnailOutPath(tabIndex),
                movieNameOrPath,
                hash
            );
        }
    }
}
