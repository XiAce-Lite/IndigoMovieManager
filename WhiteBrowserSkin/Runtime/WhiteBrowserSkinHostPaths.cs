using System;
using System.Linq;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// WebView2 から参照する仮想ホスト名を一箇所へ集める。
    /// 受け側はこの定数だけを使えば、URL 文字列のぶれを防げる。
    /// </summary>
    public static class WhiteBrowserSkinHostPaths
    {
        public const string SkinVirtualHostName = "skin.local";
        public const string ThumbnailVirtualHostName = "thum.local";

        public static string BuildSkinBaseUri(string skinRelativeDirectoryPath)
        {
            string normalizedPath = NormalizeRelativePath(skinRelativeDirectoryPath);
            return string.IsNullOrWhiteSpace(normalizedPath)
                ? $"https://{SkinVirtualHostName}/"
                : $"https://{SkinVirtualHostName}/{normalizedPath}/";
        }

        public static string BuildThumbnailBaseUri()
        {
            return $"https://{ThumbnailVirtualHostName}/";
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            string normalized = (relativePath ?? "")
                .Replace('\\', '/')
                .Trim('/')
                .Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "";
            }

            // `#TagInputRelation` のような WB 由来フォルダ名でも、fragment 扱いされない base href にする。
            return string.Join(
                "/",
                normalized
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString)
            );
        }
    }
}
