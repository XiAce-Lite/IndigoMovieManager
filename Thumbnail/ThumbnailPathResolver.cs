using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイルのファイル名/フルパス生成を一本化する。
    /// </summary>
    internal static class ThumbnailPathResolver
    {
        // 生成規則は「動画名本体.#hash.jpg」で統一する。
        internal static string BuildThumbnailFileName(string movieNameOrPath, string hash)
        {
            string body = "";
            if (!string.IsNullOrWhiteSpace(movieNameOrPath))
            {
                body = Path.GetFileNameWithoutExtension(movieNameOrPath) ?? "";
            }

            return $"{body}.#{hash ?? ""}.jpg";
        }

        // 出力フォルダとファイル名を結合して最終パスを返す。
        internal static string BuildThumbnailPath(string outPath, string movieNameOrPath, string hash)
        {
            return Path.Combine(outPath ?? "", BuildThumbnailFileName(movieNameOrPath, hash));
        }

        // TabInfo を受け取るオーバーロード。生成側と表示側で同じ規則を使う。
        internal static string BuildThumbnailPath(
            TabInfo tabInfo,
            string movieNameOrPath,
            string hash
        )
        {
            return BuildThumbnailPath(tabInfo?.OutPath ?? "", movieNameOrPath, hash);
        }
    }
}