using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイルのファイル名/フルパス生成を一本化する。
    /// </summary>
    public static class ThumbnailPathResolver
    {
        // 生成規則は「動画名本体.#hash.jpg」で統一する。
        public static string BuildThumbnailFileName(string movieNameOrPath, string hash)
        {
            string body = "";
            if (!string.IsNullOrWhiteSpace(movieNameOrPath))
            {
                body = Path.GetFileNameWithoutExtension(movieNameOrPath) ?? "";
            }

            return $"{body}.#{hash ?? ""}.jpg";
        }

        // 出力フォルダとファイル名を結合して最終パスを返す。
        public static string BuildThumbnailPath(
            string outPath,
            string movieNameOrPath,
            string hash
        )
        {
            return Path.Combine(outPath ?? "", BuildThumbnailFileName(movieNameOrPath, hash));
        }

        // TabInfo を受け取るオーバーロード。生成側と表示側で同じ規則を使う。
        public static string BuildThumbnailPath(
            TabInfo tabInfo,
            string movieNameOrPath,
            string hash
        )
        {
            return BuildThumbnailPath(tabInfo?.OutPath ?? "", movieNameOrPath, hash);
        }

        // エラーマーカーの固定ハッシュ値。正常サムネイルのハッシュと衝突しない値を使う。
        public const string ErrorMarkerHash = "ERROR";

        // エラーマーカーファイル名を生成する。規則: 「動画名本体.#ERROR.jpg」
        public static string BuildErrorMarkerFileName(string movieNameOrPath)
        {
            return BuildThumbnailFileName(movieNameOrPath, ErrorMarkerHash);
        }

        // エラーマーカーのフルパスを生成する。
        public static string BuildErrorMarkerPath(string outPath, string movieNameOrPath)
        {
            return Path.Combine(outPath ?? "", BuildErrorMarkerFileName(movieNameOrPath));
        }

        // 指定パスがエラーマーカーファイルかを判定する。
        public static bool IsErrorMarker(string thumbnailPath)
        {
            if (string.IsNullOrWhiteSpace(thumbnailPath))
            {
                return false;
            }

            string fileName = Path.GetFileName(thumbnailPath);
            return fileName.Contains($".#{ErrorMarkerHash}.", StringComparison.OrdinalIgnoreCase);
        }

        // 同じ動画名本体で既に正常jpgがある時は、ERRORマーカーを新設しない判断に使う。
        public static bool TryFindExistingSuccessThumbnailPath(
            string outPath,
            string movieNameOrPath,
            out string successThumbnailPath
        )
        {
            successThumbnailPath = "";
            if (string.IsNullOrWhiteSpace(outPath) || string.IsNullOrWhiteSpace(movieNameOrPath))
            {
                return false;
            }

            try
            {
                if (!Directory.Exists(outPath))
                {
                    return false;
                }

                string body = Path.GetFileNameWithoutExtension(movieNameOrPath) ?? "";
                if (string.IsNullOrWhiteSpace(body))
                {
                    return false;
                }

                string prefix = $"{body}.#";
                foreach (
                    string thumbnailPath in Directory.EnumerateFiles(
                        outPath,
                        "*.jpg",
                        SearchOption.TopDirectoryOnly
                    )
                )
                {
                    string fileName = Path.GetFileName(thumbnailPath) ?? "";
                    if (
                        !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || !fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        continue;
                    }

                    if (IsErrorMarker(thumbnailPath))
                    {
                        continue;
                    }

                    try
                    {
                        if (new FileInfo(thumbnailPath).Length <= 0)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    successThumbnailPath = thumbnailPath;
                    return true;
                }
            }
            catch
            {
                // 探索失敗時は既存successなし扱いで後段に委ねる。
            }

            return false;
        }
    }
}
