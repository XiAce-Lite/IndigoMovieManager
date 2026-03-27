using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイルのファイル名・フルパス生成と、成功 jpg キャッシュを一本化する。
    ///
    /// 【全体の流れでの位置づけ】
    ///   Engine（生成時） / Queue（スキップ判定） / FailureSync（掃除判定）
    ///     → ★ここ★ BuildThumbnailPath() / TryFindExistingSuccessThumbnailPath() を呼ぶ
    ///
    /// サムネファイル名は「動画名本体.#hash.jpg」で統一。ERROR マーカーは「.#ERROR.jpg」。
    /// 同一フォルダの走査結果は短時間キャッシュし、大量動画時の I/O を抑制する。
    /// </summary>
    public static class ThumbnailPathResolver
    {
        private static readonly object SuccessThumbnailDirectoryCacheLock = new();
        private static readonly Dictionary<string, SuccessThumbnailDirectoryCacheEntry> SuccessThumbnailDirectoryCache =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SuccessThumbnailDirectoryPrewarmInFlight =
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

        // 本exeで保存成功したjpgは、その場でキャッシュへ反映して再走査回数を減らす。
        public static void RememberSuccessThumbnailPath(string thumbnailPath)
        {
            if (string.IsNullOrWhiteSpace(thumbnailPath) || IsErrorMarker(thumbnailPath))
            {
                return;
            }

            string outPath = Path.GetDirectoryName(thumbnailPath) ?? "";
            if (string.IsNullOrWhiteSpace(outPath))
            {
                return;
            }

            string fileName = Path.GetFileName(thumbnailPath) ?? "";
            if (!TryExtractThumbnailBody(fileName, out string body))
            {
                return;
            }

            try
            {
                if (new FileInfo(thumbnailPath).Length <= 0)
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            lock (SuccessThumbnailDirectoryCacheLock)
            {
                if (
                    !SuccessThumbnailDirectoryCache.TryGetValue(
                        outPath,
                        out SuccessThumbnailDirectoryCacheEntry entry
                    )
                )
                {
                    return;
                }

                Dictionary<string, string> updatedSuccessThumbnailPathsByBody =
                    new(entry.SuccessThumbnailPathsByBody, StringComparer.OrdinalIgnoreCase)
                    {
                        [body] = thumbnailPath,
                    };
                HashSet<string> updatedThumbnailFileNames =
                    new(entry.ThumbnailFileNames, StringComparer.OrdinalIgnoreCase)
                    {
                        fileName,
                    };
                HashSet<string> updatedThumbnailBodies =
                    new(entry.ThumbnailBodies, StringComparer.OrdinalIgnoreCase)
                    {
                        ExtractThumbnailBodyForLookup(fileName),
                    };

                SuccessThumbnailDirectoryCache[outPath] = new SuccessThumbnailDirectoryCacheEntry
                {
                    DirectoryLastWriteTimeUtc = Directory.GetLastWriteTimeUtc(outPath),
                    CapturedAtUtc = DateTime.UtcNow,
                    SuccessThumbnailPathsByBody = updatedSuccessThumbnailPathsByBody,
                    ThumbnailFileNames = updatedThumbnailFileNames,
                    ThumbnailBodies = updatedThumbnailBodies,
                };
            }
        }

        // 初回参照より前に裏でインデックスを作り、最初の同期走査発生を減らす。
        public static void PrewarmSuccessThumbnailPathIndex(string outPath)
        {
            if (string.IsNullOrWhiteSpace(outPath))
            {
                return;
            }

            try
            {
                if (!Directory.Exists(outPath))
                {
                    RemoveSuccessThumbnailDirectoryCache(outPath);
                    return;
                }

                DateTime directoryLastWriteTimeUtc = Directory.GetLastWriteTimeUtc(outPath);
                DateTime capturedAtUtc = DateTime.UtcNow;
                bool shouldQueueRefresh = false;

                lock (SuccessThumbnailDirectoryCacheLock)
                {
                    if (
                        SuccessThumbnailDirectoryCache.TryGetValue(
                            outPath,
                            out SuccessThumbnailDirectoryCacheEntry entry
                        )
                    )
                    {
                        if (
                            entry.DirectoryLastWriteTimeUtc == directoryLastWriteTimeUtc
                            && capturedAtUtc - entry.CapturedAtUtc <= SuccessThumbnailDirectoryCacheTtl
                        )
                        {
                            return;
                        }

                        if (entry.RefreshQueued)
                        {
                            return;
                        }

                        entry.RefreshQueued = true;
                        shouldQueueRefresh = true;
                    }
                    else
                    {
                        if (!SuccessThumbnailDirectoryPrewarmInFlight.Add(outPath))
                        {
                            return;
                        }

                        shouldQueueRefresh = true;
                    }
                }

                if (shouldQueueRefresh)
                {
                    QueueSuccessThumbnailPathIndexRefresh(outPath);
                }
            }
            catch
            {
                RemoveSuccessThumbnailDirectoryCache(outPath);
            }
        }

        // 一覧用のファイル名集合は、共有スナップショットから返して追加走査を避ける。
        public static HashSet<string> BuildThumbnailFileNameLookup(string outPath)
        {
            HashSet<string> fileNames = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(outPath) || !Directory.Exists(outPath))
            {
                return fileNames;
            }

            try
            {
                SuccessThumbnailDirectoryCacheEntry entry =
                    GetOrBuildSuccessThumbnailDirectoryCacheEntry(outPath);
                return new HashSet<string>(entry.ThumbnailFileNames, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return fileNames;
            }
        }

        // body集合も同じスナップショットから返し、EverythingLite 側の同期列挙を避ける。
        public static HashSet<string> BuildThumbnailBodyLookup(string outPath)
        {
            HashSet<string> bodies = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(outPath) || !Directory.Exists(outPath))
            {
                return bodies;
            }

            try
            {
                SuccessThumbnailDirectoryCacheEntry entry =
                    GetOrBuildSuccessThumbnailDirectoryCacheEntry(outPath);
                return new HashSet<string>(entry.ThumbnailBodies, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return bodies;
            }
        }

        // 同じ出力フォルダを何百回も走査しないよう、jpg一覧は短時間だけ使い回す。
        // キャッシュが古くても即返しし、更新は裏で回してUI主経路の停止を避ける。
        private static IReadOnlyDictionary<string, string> GetOrBuildSuccessThumbnailPathIndex(
            string outPath
        )
        {
            return GetOrBuildSuccessThumbnailDirectoryCacheEntry(outPath).SuccessThumbnailPathsByBody;
        }

        // 1回のフォルダ列挙結果を success辞書・file name集合・body集合へ共用する。
        private static SuccessThumbnailDirectoryCacheEntry GetOrBuildSuccessThumbnailDirectoryCacheEntry(
            string outPath
        )
        {
            DateTime directoryLastWriteTimeUtc = Directory.GetLastWriteTimeUtc(outPath);
            DateTime capturedAtUtc = DateTime.UtcNow;
            SuccessThumbnailDirectoryCacheEntry staleEntry = null;
            bool shouldQueueRefresh = false;

            lock (SuccessThumbnailDirectoryCacheLock)
            {
                if (SuccessThumbnailDirectoryCache.TryGetValue(outPath, out SuccessThumbnailDirectoryCacheEntry entry))
                {
                    if (
                        entry.DirectoryLastWriteTimeUtc == directoryLastWriteTimeUtc
                        && capturedAtUtc - entry.CapturedAtUtc <= SuccessThumbnailDirectoryCacheTtl
                    )
                    {
                        return entry;
                    }

                    staleEntry = entry;
                    if (!entry.RefreshQueued)
                    {
                        entry.RefreshQueued = true;
                        shouldQueueRefresh = true;
                    }
                }
            }

            if (staleEntry != null)
            {
                if (shouldQueueRefresh)
                {
                    QueueSuccessThumbnailPathIndexRefresh(outPath);
                }

                return staleEntry;
            }

            SuccessThumbnailDirectoryCacheEntry builtEntry = BuildSuccessThumbnailDirectoryCacheEntry(
                outPath,
                directoryLastWriteTimeUtc,
                capturedAtUtc
            );

            lock (SuccessThumbnailDirectoryCacheLock)
            {
                SuccessThumbnailDirectoryCache[outPath] = builtEntry;
            }

            return builtEntry;
        }

        // 古いスナップショットを返した後でだけ裏更新し、次回参照から新状態へ追いつく。
        private static void QueueSuccessThumbnailPathIndexRefresh(string outPath)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(outPath))
                    {
                        RemoveSuccessThumbnailDirectoryCache(outPath);
                        return;
                    }

                    SuccessThumbnailDirectoryCacheEntry refreshedEntry =
                        BuildSuccessThumbnailDirectoryCacheEntry(
                            outPath,
                            Directory.GetLastWriteTimeUtc(outPath),
                            DateTime.UtcNow
                        );

                    lock (SuccessThumbnailDirectoryCacheLock)
                    {
                        SuccessThumbnailDirectoryPrewarmInFlight.Remove(outPath);
                        SuccessThumbnailDirectoryCache[outPath] = refreshedEntry;
                    }
                }
                catch
                {
                    lock (SuccessThumbnailDirectoryCacheLock)
                    {
                        SuccessThumbnailDirectoryPrewarmInFlight.Remove(outPath);
                        if (
                            SuccessThumbnailDirectoryCache.TryGetValue(
                                outPath,
                                out SuccessThumbnailDirectoryCacheEntry entry
                            )
                        )
                        {
                            entry.RefreshQueued = false;
                        }
                    }
                }
            });
        }

        private static SuccessThumbnailDirectoryCacheEntry BuildSuccessThumbnailDirectoryCacheEntry(
            string outPath,
            DateTime directoryLastWriteTimeUtc,
            DateTime capturedAtUtc
        )
        {
            Dictionary<string, string> successThumbnailPathsByBody =
                new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> thumbnailFileNames = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> thumbnailBodies = new(StringComparer.OrdinalIgnoreCase);

            foreach (
                string thumbnailPath in Directory.EnumerateFiles(
                    outPath,
                    "*.jpg",
                    SearchOption.TopDirectoryOnly
                )
            )
            {
                string fileName = Path.GetFileName(thumbnailPath) ?? "";
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                thumbnailFileNames.Add(fileName);

                string lookupBody = ExtractThumbnailBodyForLookup(fileName);
                if (!string.IsNullOrWhiteSpace(lookupBody))
                {
                    thumbnailBodies.Add(lookupBody);
                }

                if (IsErrorMarker(thumbnailPath))
                {
                    continue;
                }

                if (!TryExtractThumbnailBody(fileName, out string successBody))
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

                successThumbnailPathsByBody.TryAdd(successBody, thumbnailPath);
            }

            return new SuccessThumbnailDirectoryCacheEntry
            {
                DirectoryLastWriteTimeUtc = directoryLastWriteTimeUtc,
                CapturedAtUtc = capturedAtUtc,
                SuccessThumbnailPathsByBody = successThumbnailPathsByBody,
                ThumbnailFileNames = thumbnailFileNames,
                ThumbnailBodies = thumbnailBodies,
            };
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

        // file name/body照会では plain jpg も扱うため、拡張子なし名を基準に ".#" を削る。
        private static string ExtractThumbnailBodyForLookup(string fileName)
        {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName) ?? "";
            if (string.IsNullOrWhiteSpace(nameWithoutExt))
            {
                return "";
            }

            int delimiterIndex = nameWithoutExt.LastIndexOf(".#", StringComparison.OrdinalIgnoreCase);
            if (delimiterIndex >= 0)
            {
                return nameWithoutExt[..delimiterIndex];
            }

            return nameWithoutExt;
        }

        private static void RemoveSuccessThumbnailDirectoryCache(string outPath)
        {
            if (string.IsNullOrWhiteSpace(outPath))
            {
                return;
            }

            lock (SuccessThumbnailDirectoryCacheLock)
            {
                SuccessThumbnailDirectoryPrewarmInFlight.Remove(outPath);
                SuccessThumbnailDirectoryCache.Remove(outPath);
            }
        }

        internal static bool HasCachedSuccessThumbnailPathIndex(string outPath)
        {
            if (string.IsNullOrWhiteSpace(outPath))
            {
                return false;
            }

            lock (SuccessThumbnailDirectoryCacheLock)
            {
                return SuccessThumbnailDirectoryCache.ContainsKey(outPath);
            }
        }

        private sealed class SuccessThumbnailDirectoryCacheEntry
        {
            public DateTime DirectoryLastWriteTimeUtc { get; init; }

            public DateTime CapturedAtUtc { get; init; }

            public bool RefreshQueued { get; set; }

            public IReadOnlyDictionary<string, string> SuccessThumbnailPathsByBody { get; init; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlySet<string> ThumbnailFileNames { get; init; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlySet<string> ThumbnailBodies { get; init; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
