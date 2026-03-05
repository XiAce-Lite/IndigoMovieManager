using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// EverythingLite のインデックス機能を IFileIndexProvider 契約へ接続する。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class EverythingLiteProvider : IFileIndexProvider
    {
        private const int SearchLimit = 1_000_000;
        private static readonly TimeSpan RebuildCooldown = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan CacheEntryIdleTtl = TimeSpan.FromMinutes(20);
        private const int MaxCacheEntries = 32;
        private static readonly object CacheMaintenanceLock = new();
        private static readonly ConcurrentDictionary<string, LiteIndexCacheEntry> IndexCache = new(
            StringComparer.OrdinalIgnoreCase
        );

        public AvailabilityResult CheckAvailability()
        {
            if (!OperatingSystem.IsWindows())
            {
                return new AvailabilityResult(false, EverythingReasonCodes.EverythingNotAvailable);
            }

            return new AvailabilityResult(true, EverythingReasonCodes.Ok);
        }

        public FileIndexMovieResult CollectMoviePaths(FileIndexQueryOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.RootPath))
            {
                throw new ArgumentException("RootPath is required.", nameof(options));
            }

            AvailabilityResult availability = CheckAvailability();
            if (!availability.CanUse)
            {
                return new FileIndexMovieResult(false, [], null, availability.Reason);
            }

            try
            {
                string normalizedRootWithSlash = NormalizeDirectoryPathWithTrailingSlash(options.RootPath);
                string normalizedRootWithoutSlash = NormalizeDirectoryPathWithoutTrailingSlash(
                    options.RootPath
                );
                HashSet<string> targetExtensions = ParseTargetExtensions(options.CheckExt);
                DateTime? changedSinceUtc = options.ChangedSinceUtc.HasValue
                    ? NormalizeToUtc(options.ChangedSinceUtc.Value)
                    : null;

                // ルート単位でサービスを再利用し、短時間の連続呼び出しでは再構築を間引く。
                LiteIndexCacheEntry cacheEntry = GetOrCreateCacheEntry(normalizedRootWithoutSlash);
                IReadOnlyList<Lite.SearchResultItem> indexedItems = QueryIndexedItems(
                    cacheEntry,
                    changedSinceUtc,
                    out bool rebuilt,
                    out DateTime indexedAtUtc
                );

                List<string> moviePaths = [];
                DateTime? maxObservedChangedUtc = null;
                foreach (Lite.SearchResultItem item in indexedItems)
                {
                    if (item.IsDirectory)
                    {
                        continue;
                    }

                    if (!IsUnderRoot(item.FullPath, normalizedRootWithSlash))
                    {
                        continue;
                    }

                    if (
                        !options.IncludeSubdirectories
                        && !IsDirectChild(item.FullPath, normalizedRootWithoutSlash)
                    )
                    {
                        continue;
                    }

                    if (!IsTargetExtension(item.FullPath, targetExtensions))
                    {
                        continue;
                    }

                    DateTime itemChangedUtc = NormalizeToUtc(item.LastWriteTimeUtc);
                    if (changedSinceUtc.HasValue && itemChangedUtc < changedSinceUtc.Value)
                    {
                        continue;
                    }

                    moviePaths.Add(item.FullPath);
                    if (
                        !maxObservedChangedUtc.HasValue
                        || itemChangedUtc > maxObservedChangedUtc.Value
                    )
                    {
                        maxObservedChangedUtc = itemChangedUtc;
                    }
                }

                string reason = changedSinceUtc.HasValue
                    ? $"{EverythingReasonCodes.OkPrefix}provider=everythinglite index={(rebuilt ? "rebuilt" : "cached")} indexed_at={indexedAtUtc:O} count={moviePaths.Count} since={changedSinceUtc.Value:O}"
                    : $"{EverythingReasonCodes.OkPrefix}provider=everythinglite index={(rebuilt ? "rebuilt" : "cached")} indexed_at={indexedAtUtc:O} count={moviePaths.Count}";

                return new FileIndexMovieResult(true, moviePaths, maxObservedChangedUtc, reason);
            }
            catch (Exception ex)
            {
                return new FileIndexMovieResult(
                    false,
                    [],
                    null,
                    EverythingReasonCodes.BuildEverythingQueryError(ex)
                );
            }
        }

        public FileIndexThumbnailBodyResult CollectThumbnailBodies(string thumbFolder)
        {
            if (string.IsNullOrWhiteSpace(thumbFolder))
            {
                throw new ArgumentException("thumbFolder is required.", nameof(thumbFolder));
            }

            AvailabilityResult availability = CheckAvailability();
            if (!availability.CanUse)
            {
                return new FileIndexThumbnailBodyResult(
                    false,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    availability.Reason
                );
            }

            HashSet<string> existingThumbBodies = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(thumbFolder))
                {
                    return new FileIndexThumbnailBodyResult(
                        true,
                        existingThumbBodies,
                        EverythingReasonCodes.Ok
                    );
                }

                foreach (
                    string filePath in Directory.EnumerateFiles(
                        thumbFolder,
                        "*.jpg",
                        SearchOption.TopDirectoryOnly
                    )
                )
                {
                    string fileName = Path.GetFileName(filePath) ?? "";
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        continue;
                    }

                    string body = ExtractThumbnailBody(fileName);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        existingThumbBodies.Add(body);
                    }
                }

                return new FileIndexThumbnailBodyResult(
                    true,
                    existingThumbBodies,
                    EverythingReasonCodes.Ok
                );
            }
            catch (Exception ex)
            {
                return new FileIndexThumbnailBodyResult(
                    false,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    EverythingReasonCodes.BuildEverythingThumbQueryError(ex)
                );
            }
        }

        private static Lite.IFileIndexService CreateService(string rootPath)
        {
            Lite.FileIndexServiceOptions options = new()
            {
                BackendMode = Lite.FileIndexBackendMode.StandardFileSystem,
                StandardUserRoots = [rootPath],
                StandardUserExcludePaths = [],
            };

            return new Lite.FileIndexService(options);
        }

        private static LiteIndexCacheEntry CreateCacheEntry(string rootPath)
        {
            return new LiteIndexCacheEntry(CreateService(rootPath));
        }

        private static LiteIndexCacheEntry GetOrCreateCacheEntry(string rootPath)
        {
            DateTime nowUtc = DateTime.UtcNow;
            CleanupExpiredEntries(nowUtc);

            LiteIndexCacheEntry entry = IndexCache.GetOrAdd(rootPath, CreateCacheEntry);
            TouchCacheEntry(entry, nowUtc);
            EnforceCacheSizeLimit(rootPath);
            return entry;
        }

        private static IReadOnlyList<Lite.SearchResultItem> QueryIndexedItems(
            LiteIndexCacheEntry cacheEntry,
            DateTime? changedSinceUtc,
            out bool rebuilt,
            out DateTime indexedAtUtc
        )
        {
            lock (cacheEntry.SyncRoot)
            {
                DateTime nowUtc = DateTime.UtcNow;
                cacheEntry.LastAccessUtc = nowUtc;
                bool isStaleByCooldown =
                    !cacheEntry.HasIndexed
                    || nowUtc - cacheEntry.LastIndexedUtc >= RebuildCooldown;
                bool requiresCatchUp =
                    changedSinceUtc.HasValue && cacheEntry.LastIndexedUtc < changedSinceUtc.Value;
                if (isStaleByCooldown || requiresCatchUp)
                {
                    _ = cacheEntry
                        .Service.RebuildIndexAsync(null, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    cacheEntry.LastIndexedUtc = DateTime.UtcNow;
                    cacheEntry.HasIndexed = true;
                    rebuilt = true;
                }
                else
                {
                    rebuilt = false;
                }

                indexedAtUtc = cacheEntry.LastIndexedUtc;
                return cacheEntry.Service.Search("", SearchLimit);
            }
        }

        private static void CleanupExpiredEntries(DateTime nowUtc)
        {
            lock (CacheMaintenanceLock)
            {
                foreach (KeyValuePair<string, LiteIndexCacheEntry> pair in IndexCache.ToArray())
                {
                    DateTime lastAccessUtc = GetLastAccessUtc(pair.Value);
                    if (nowUtc - lastAccessUtc < CacheEntryIdleTtl)
                    {
                        continue;
                    }

                    if (IndexCache.TryRemove(pair.Key, out LiteIndexCacheEntry removed))
                    {
                        DisposeCacheEntry(removed);
                    }
                }
            }
        }

        private static void EnforceCacheSizeLimit(string protectedRootPath)
        {
            if (IndexCache.Count <= MaxCacheEntries)
            {
                return;
            }

            lock (CacheMaintenanceLock)
            {
                int overflow = IndexCache.Count - MaxCacheEntries;
                if (overflow <= 0)
                {
                    return;
                }

                // 直近利用が古い順に削除し、現在処理中ルートは保護する。
                List<KeyValuePair<string, DateTime>> removalCandidates = [];
                foreach (KeyValuePair<string, LiteIndexCacheEntry> pair in IndexCache)
                {
                    if (
                        string.Equals(
                            pair.Key,
                            protectedRootPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        continue;
                    }

                    removalCandidates.Add(new KeyValuePair<string, DateTime>(pair.Key, GetLastAccessUtc(pair.Value)));
                }

                foreach (
                    KeyValuePair<string, DateTime> candidate in removalCandidates
                        .OrderBy(x => x.Value)
                        .Take(overflow)
                )
                {
                    if (IndexCache.TryRemove(candidate.Key, out LiteIndexCacheEntry removed))
                    {
                        DisposeCacheEntry(removed);
                    }
                }
            }
        }

        private static void TouchCacheEntry(LiteIndexCacheEntry cacheEntry, DateTime nowUtc)
        {
            lock (cacheEntry.SyncRoot)
            {
                cacheEntry.LastAccessUtc = nowUtc;
            }
        }

        private static DateTime GetLastAccessUtc(LiteIndexCacheEntry cacheEntry)
        {
            lock (cacheEntry.SyncRoot)
            {
                return cacheEntry.LastAccessUtc;
            }
        }

        private static void DisposeCacheEntry(LiteIndexCacheEntry cacheEntry)
        {
            lock (cacheEntry.SyncRoot)
            {
                cacheEntry.Service.Dispose();
                cacheEntry.HasIndexed = false;
                cacheEntry.LastIndexedUtc = default;
            }
        }

        // テストからキャッシュ状態を検証するための補助API。
        internal static int GetCacheEntryCountForTesting()
        {
            return IndexCache.Count;
        }

        // テストから上限値を参照し、将来の定数変更に追従できるようにする。
        internal static int GetCacheCapacityForTesting()
        {
            return MaxCacheEntries;
        }

        // テスト終了時にキャッシュを明示クリアして、ケース間の干渉を防ぐ。
        internal static void ClearCacheForTesting()
        {
            foreach (KeyValuePair<string, LiteIndexCacheEntry> pair in IndexCache.ToArray())
            {
                if (IndexCache.TryRemove(pair.Key, out LiteIndexCacheEntry removed))
                {
                    DisposeCacheEntry(removed);
                }
            }
        }

        private sealed class LiteIndexCacheEntry
        {
            public LiteIndexCacheEntry(Lite.IFileIndexService service)
            {
                Service = service;
                LastAccessUtc = DateTime.UtcNow;
            }

            public object SyncRoot { get; } = new();
            public Lite.IFileIndexService Service { get; }
            public DateTime LastAccessUtc { get; set; }
            public DateTime LastIndexedUtc { get; set; }
            public bool HasIndexed { get; set; }
        }

        // "{body}.#{hash}.jpg" 形式から "{body}" 部分を抽出する。
        private static string ExtractThumbnailBody(string fileName)
        {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(nameWithoutExt))
            {
                return "";
            }

            int hashMarkerIndex = nameWithoutExt.LastIndexOf(
                ".#",
                StringComparison.OrdinalIgnoreCase
            );
            if (hashMarkerIndex >= 0)
            {
                return nameWithoutExt[..hashMarkerIndex];
            }

            return nameWithoutExt;
        }

        private static bool IsUnderRoot(string candidatePath, string rootWithSlash)
        {
            try
            {
                string normalizedCandidate = Path.GetFullPath(candidatePath);
                return normalizedCandidate.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDirectChild(string candidatePath, string rootWithoutSlash)
        {
            try
            {
                string normalizedCandidate = Path.GetFullPath(candidatePath);
                string parent = Path.GetDirectoryName(normalizedCandidate) ?? "";
                string normalizedParent = NormalizeDirectoryPathWithoutTrailingSlash(parent);
                return string.Equals(
                    normalizedParent,
                    rootWithoutSlash,
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch
            {
                return false;
            }
        }

        private static HashSet<string> ParseTargetExtensions(string checkExt)
        {
            HashSet<string> targetExtensions = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(checkExt))
            {
                return targetExtensions;
            }

            string[] parts = checkExt.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in parts)
            {
                string ext = raw.Trim().Replace("*", "");
                if (string.IsNullOrWhiteSpace(ext))
                {
                    continue;
                }

                if (!ext.StartsWith('.'))
                {
                    ext = "." + ext;
                }

                targetExtensions.Add(ext);
            }

            return targetExtensions;
        }

        private static bool IsTargetExtension(string fullPath, HashSet<string> targetExtensions)
        {
            if (targetExtensions.Count < 1)
            {
                return true;
            }

            string ext = Path.GetExtension(fullPath);
            return targetExtensions.Contains(ext);
        }

        private static string NormalizeDirectoryPathWithTrailingSlash(string path)
        {
            string normalized = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalized + Path.DirectorySeparatorChar;
        }

        private static string NormalizeDirectoryPathWithoutTrailingSlash(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static DateTime NormalizeToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
            };
        }
    }
}
