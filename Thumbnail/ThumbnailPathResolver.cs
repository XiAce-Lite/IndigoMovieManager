using System.Collections.Generic;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイルのファイル名/フルパス生成を一本化する。
    /// </summary>
    public static class ThumbnailPathResolver
    {
        private static readonly object SuccessThumbnailDirectoryCacheLock = new();
        private static readonly Dictionary<string, SuccessThumbnailDirectoryCacheEntry> SuccessThumbnailDirectoryCache =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan SuccessThumbnailDirectoryCacheTtl = TimeSpan.FromSeconds(1);

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
                    RemoveSuccessThumbnailDirectoryCache(outPath);
                    return false;
                }

                string body = Path.GetFileNameWithoutExtension(movieNameOrPath) ?? "";
                if (string.IsNullOrWhiteSpace(body))
                {
                    return false;
                }

                IReadOnlyDictionary<string, string> successThumbnailPathIndex =
                    GetOrBuildSuccessThumbnailPathIndex(outPath);
                if (successThumbnailPathIndex.TryGetValue(body, out string cachedSuccessThumbnailPath))
                {
                    successThumbnailPath = cachedSuccessThumbnailPath;
                    return true;
                }
            }
            catch
            {
                // 探索失敗時は既存successなし扱いで後段に委ねる。
            }

            return false;
        }

        // 同じ出力フォルダを何百回も走査しないよう、jpg一覧は短時間だけ使い回す。
        private static IReadOnlyDictionary<string, string> GetOrBuildSuccessThumbnailPathIndex(
            string outPath
        )
        {
            DateTime directoryLastWriteTimeUtc = Directory.GetLastWriteTimeUtc(outPath);
            DateTime capturedAtUtc = DateTime.UtcNow;

            lock (SuccessThumbnailDirectoryCacheLock)
            {
                if (
                    SuccessThumbnailDirectoryCache.TryGetValue(outPath, out SuccessThumbnailDirectoryCacheEntry entry)
                    && entry.DirectoryLastWriteTimeUtc == directoryLastWriteTimeUtc
                    && capturedAtUtc - entry.CapturedAtUtc <= SuccessThumbnailDirectoryCacheTtl
                )
                {
                    return entry.SuccessThumbnailPathsByBody;
                }
            }

            Dictionary<string, string> successThumbnailPathsByBody =
                BuildSuccessThumbnailPathIndex(outPath);

            lock (SuccessThumbnailDirectoryCacheLock)
            {
                SuccessThumbnailDirectoryCache[outPath] = new SuccessThumbnailDirectoryCacheEntry
                {
                    DirectoryLastWriteTimeUtc = directoryLastWriteTimeUtc,
                    CapturedAtUtc = capturedAtUtc,
                    SuccessThumbnailPathsByBody = successThumbnailPathsByBody,
                };
            }

            return successThumbnailPathsByBody;
        }

        private static Dictionary<string, string> BuildSuccessThumbnailPathIndex(string outPath)
        {
            Dictionary<string, string> successThumbnailPathsByBody =
                new(StringComparer.OrdinalIgnoreCase);

            foreach (
                string thumbnailPath in Directory.EnumerateFiles(
                    outPath,
                    "*.jpg",
                    SearchOption.TopDirectoryOnly
                )
            )
            {
                string fileName = Path.GetFileName(thumbnailPath) ?? "";
                if (string.IsNullOrWhiteSpace(fileName) || IsErrorMarker(thumbnailPath))
                {
                    continue;
                }

                if (!TryExtractThumbnailBody(fileName, out string body))
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

                successThumbnailPathsByBody.TryAdd(body, thumbnailPath);
            }

            return successThumbnailPathsByBody;
        }

        private static bool TryExtractThumbnailBody(string fileName, out string body)
        {
            body = "";
            if (!fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int delimiterIndex = fileName.LastIndexOf(".#", StringComparison.Ordinal);
            if (delimiterIndex <= 0)
            {
                return false;
            }

            body = fileName[..delimiterIndex];
            return !string.IsNullOrWhiteSpace(body);
        }

        private static void RemoveSuccessThumbnailDirectoryCache(string outPath)
        {
            if (string.IsNullOrWhiteSpace(outPath))
            {
                return;
            }

            lock (SuccessThumbnailDirectoryCacheLock)
            {
                SuccessThumbnailDirectoryCache.Remove(outPath);
            }
        }

        private sealed class SuccessThumbnailDirectoryCacheEntry
        {
            public DateTime DirectoryLastWriteTimeUtc { get; init; }

            public DateTime CapturedAtUtc { get; init; }

            public IReadOnlyDictionary<string, string> SuccessThumbnailPathsByBody { get; init; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
