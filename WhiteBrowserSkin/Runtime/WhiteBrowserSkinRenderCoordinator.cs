using System.IO;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// どの HTML をどの仮想ホスト前提で WebView2 へ渡すかを決める薄い coordinator。
    /// いまは初回描画だけに絞り、差分描画の責務は将来ここへ寄せる。
    /// </summary>
    public sealed class WhiteBrowserSkinRenderCoordinator
    {
        private readonly object cacheSync = new();
        private CachedRenderDocument _cachedDocument;

        public WhiteBrowserSkinRenderDocument BuildInitialDocument(
            string skinRootPath,
            string skinHtmlPath
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(skinRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(skinHtmlPath);

            string normalizedSkinRootPath = Path.GetFullPath(skinRootPath);
            string normalizedSkinHtmlPath = Path.GetFullPath(skinHtmlPath);
            string htmlDirectoryPath =
                Path.GetDirectoryName(normalizedSkinHtmlPath) ?? normalizedSkinRootPath;
            string relativeDirectoryPath = Path.GetRelativePath(
                normalizedSkinRootPath,
                htmlDirectoryPath
            );
            string skinBaseUri = WhiteBrowserSkinHostPaths.BuildSkinBaseUri(relativeDirectoryPath);
            FileInfo htmlFile = new(normalizedSkinHtmlPath);
            DateTime lastWriteTimeUtc = htmlFile.LastWriteTimeUtc;
            long fileLength = htmlFile.Exists ? htmlFile.Length : 0;

            lock (cacheSync)
            {
                if (
                    _cachedDocument?.Matches(
                        normalizedSkinRootPath,
                        normalizedSkinHtmlPath,
                        skinBaseUri,
                        lastWriteTimeUtc,
                        fileLength
                    ) == true
                )
                {
                    return _cachedDocument.Document;
                }
            }

            WhiteBrowserSkinEncodingNormalizationResult normalized =
                WhiteBrowserSkinEncodingNormalizer.NormalizeFromFile(
                    normalizedSkinHtmlPath,
                    skinBaseUri
                );

            WhiteBrowserSkinRenderDocument document = new(
                normalized.NormalizedHtml,
                normalized.SourceEncodingName,
                normalized.InjectedBaseUri,
                WhiteBrowserSkinHostPaths.BuildThumbnailBaseUri()
            );

            lock (cacheSync)
            {
                // 同じ skin HTML の再表示では正規化済み HTML を再利用し、reload の初動を軽くする。
                _cachedDocument = new CachedRenderDocument(
                    normalizedSkinRootPath,
                    normalizedSkinHtmlPath,
                    skinBaseUri,
                    lastWriteTimeUtc,
                    fileLength,
                    document
                );
            }

            return document;
        }

        private sealed record CachedRenderDocument(
            string SkinRootPath,
            string SkinHtmlPath,
            string SkinBaseUri,
            DateTime LastWriteTimeUtc,
            long FileLength,
            WhiteBrowserSkinRenderDocument Document
        )
        {
            public bool Matches(
                string skinRootPath,
                string skinHtmlPath,
                string skinBaseUri,
                DateTime lastWriteTimeUtc,
                long fileLength
            )
            {
                return string.Equals(SkinRootPath, skinRootPath, StringComparison.Ordinal)
                    && string.Equals(SkinHtmlPath, skinHtmlPath, StringComparison.Ordinal)
                    && string.Equals(SkinBaseUri, skinBaseUri, StringComparison.Ordinal)
                    && LastWriteTimeUtc == lastWriteTimeUtc
                    && FileLength == fileLength;
            }
        }
    }
}
