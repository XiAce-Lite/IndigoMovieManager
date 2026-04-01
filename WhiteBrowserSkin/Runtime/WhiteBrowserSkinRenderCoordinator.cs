using System.IO;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// どの HTML をどの仮想ホスト前提で WebView2 へ渡すかを決める薄い coordinator。
    /// いまは初回描画だけに絞り、差分描画の責務は将来ここへ寄せる。
    /// </summary>
    public sealed class WhiteBrowserSkinRenderCoordinator
    {
        public WhiteBrowserSkinRenderDocument BuildInitialDocument(
            string skinRootPath,
            string skinHtmlPath
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(skinRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(skinHtmlPath);

            string htmlDirectoryPath = Path.GetDirectoryName(skinHtmlPath) ?? skinRootPath;
            string relativeDirectoryPath = Path.GetRelativePath(skinRootPath, htmlDirectoryPath);
            string skinBaseUri = WhiteBrowserSkinHostPaths.BuildSkinBaseUri(relativeDirectoryPath);
            WhiteBrowserSkinEncodingNormalizationResult normalized =
                WhiteBrowserSkinEncodingNormalizer.NormalizeFromFile(skinHtmlPath, skinBaseUri);

            return new WhiteBrowserSkinRenderDocument(
                normalized.NormalizedHtml,
                normalized.SourceEncodingName,
                normalized.InjectedBaseUri,
                WhiteBrowserSkinHostPaths.BuildThumbnailBaseUri()
            );
        }
    }
}
