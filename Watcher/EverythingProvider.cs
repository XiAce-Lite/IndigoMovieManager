using System.IO;
using EverythingSearchClient;

namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// Everything IPC を使って候補収集を行うProvider実装。
    /// ここでは mode 判定を持たず、純粋に問い合わせ結果だけを返す。
    /// </summary>
    internal sealed class EverythingProvider : IFileIndexProvider
    {
        private const uint SearchLimit = 1_000_000;
        private const uint ReceiveTimeoutMs = 1500;

        public AvailabilityResult CheckAvailability()
        {
            try
            {
                if (!SearchClient.IsEverythingAvailable())
                {
                    return new AvailabilityResult(false, EverythingReasonCodes.EverythingNotAvailable);
                }
            }
            catch (Exception ex)
            {
                return new AvailabilityResult(false, EverythingReasonCodes.BuildAvailabilityError(ex));
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
                return new FileIndexMovieResult(
                    false,
                    [],
                    null,
                    availability.Reason
                );
            }

            List<string> moviePaths = [];
            DateTime? maxObservedChangedUtc = null;

            try
            {
                // SearchClientは共有せず、問い合わせ単位で生成して競合を避ける。
                SearchClient searchClient = new();
                string normalizedRootWithSlash = NormalizeDirectoryPathWithTrailingSlash(
                    options.RootPath
                );
                string normalizedRootWithoutSlash = NormalizeDirectoryPathWithoutTrailingSlash(
                    options.RootPath
                );
                HashSet<string> targetExtensions = ParseTargetExtensions(options.CheckExt);
                List<string> queryList = BuildEverythingQueries(
                    normalizedRootWithSlash,
                    targetExtensions
                );
                HashSet<string> dedupe = new(StringComparer.OrdinalIgnoreCase);

                foreach (string query in queryList)
                {
                    Result result = searchClient.Search(
                        query,
                        SearchClient.SearchFlags.MatchPath,
                        SearchLimit,
                        0,
                        SearchClient.BehaviorWhenBusy.WaitOrContinue,
                        ReceiveTimeoutMs,
                        SearchClient.SortBy.Path,
                        SearchClient.SortDirection.Ascending
                    );

                    if (result.TotalItems > result.NumItems)
                    {
                        string truncatedReason = EverythingReasonCodes.BuildEverythingResultTruncated(
                            result.NumItems,
                            result.TotalItems
                        );
                        return new FileIndexMovieResult(false, [], null, truncatedReason);
                    }

                    foreach (Result.Item item in result.Items ?? [])
                    {
                        if (IsContainer(item.Flags))
                        {
                            continue;
                        }

                        string fullPath = BuildFullPath(item);
                        if (string.IsNullOrWhiteSpace(fullPath))
                        {
                            continue;
                        }

                        if (!IsUnderRoot(fullPath, normalizedRootWithSlash))
                        {
                            continue;
                        }

                        if (
                            !options.IncludeSubdirectories
                            && !IsDirectChild(fullPath, normalizedRootWithoutSlash)
                        )
                        {
                            continue;
                        }

                        if (!IsTargetExtension(fullPath, targetExtensions))
                        {
                            continue;
                        }

                        if (!IsChangedSince(item, options.ChangedSinceUtc))
                        {
                            continue;
                        }

                        if (TryGetItemChangedUtc(item, out DateTime itemChangedUtc))
                        {
                            if (
                                !maxObservedChangedUtc.HasValue
                                || itemChangedUtc > maxObservedChangedUtc.Value
                            )
                            {
                                maxObservedChangedUtc = itemChangedUtc;
                            }
                        }

                        if (dedupe.Add(fullPath))
                        {
                            moviePaths.Add(fullPath);
                        }
                    }
                }

                string reason = options.ChangedSinceUtc.HasValue
                    ? $"{EverythingReasonCodes.OkPrefix}query_count={queryList.Count} since={options.ChangedSinceUtc.Value:O}"
                    : $"{EverythingReasonCodes.OkPrefix}query_count={queryList.Count}";

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
                SearchClient searchClient = new();
                string normalizedRootWithSlash = NormalizeDirectoryPathWithTrailingSlash(thumbFolder);
                string quotedRoot = QuoteForEverything(normalizedRootWithSlash);
                string query = $"{quotedRoot} ext:jpg";

                Result result = searchClient.Search(
                    query,
                    SearchClient.SearchFlags.MatchPath,
                    SearchLimit,
                    0,
                    SearchClient.BehaviorWhenBusy.WaitOrContinue,
                    ReceiveTimeoutMs,
                    SearchClient.SortBy.Path,
                    SearchClient.SortDirection.Ascending
                );

                if (result.TotalItems > result.NumItems)
                {
                    string truncatedReason = EverythingReasonCodes.BuildEverythingResultTruncated(
                        result.NumItems,
                        result.TotalItems
                    );
                    return new FileIndexThumbnailBodyResult(
                        false,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        truncatedReason
                    );
                }

                foreach (Result.Item item in result.Items ?? [])
                {
                    if (IsContainer(item.Flags))
                    {
                        continue;
                    }

                    string fileName = item.Name ?? "";
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

                return new FileIndexThumbnailBodyResult(true, existingThumbBodies, EverythingReasonCodes.Ok);
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

        private static bool IsContainer(Result.ItemFlags flags)
        {
            return (flags & Result.ItemFlags.Folder) == Result.ItemFlags.Folder
                || (flags & Result.ItemFlags.Drive) == Result.ItemFlags.Drive;
        }

        private static string BuildFullPath(Result.Item item)
        {
            string name = item.Name ?? "";
            string path = item.Path ?? "";
            if (string.IsNullOrWhiteSpace(path))
            {
                return name;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                return path;
            }
            return Path.Combine(path, name);
        }

        private static bool IsUnderRoot(string candidatePath, string rootWithSlash)
        {
            try
            {
                string normalizedCandidate = Path.GetFullPath(candidatePath);
                return normalizedCandidate.StartsWith(
                    rootWithSlash,
                    StringComparison.OrdinalIgnoreCase
                );
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

        private static bool IsTargetExtension(string fullPath, HashSet<string> targetExtensions)
        {
            if (targetExtensions.Count < 1)
            {
                return true;
            }

            string ext = Path.GetExtension(fullPath);
            return targetExtensions.Contains(ext);
        }

        private static bool IsChangedSince(Result.Item item, DateTime? changedSinceUtc)
        {
            if (!changedSinceUtc.HasValue)
            {
                return true;
            }

            DateTime baselineUtc = NormalizeToUtc(changedSinceUtc.Value);
            if (item.LastWriteTime.HasValue)
            {
                return NormalizeToUtc(item.LastWriteTime.Value) >= baselineUtc;
            }

            if (item.CreationTime.HasValue)
            {
                return NormalizeToUtc(item.CreationTime.Value) >= baselineUtc;
            }

            // タイムスタンプ取得不能時は取りこぼし回避のため採用する。
            return true;
        }

        private static bool TryGetItemChangedUtc(Result.Item item, out DateTime changedUtc)
        {
            if (item.LastWriteTime.HasValue)
            {
                changedUtc = NormalizeToUtc(item.LastWriteTime.Value);
                return true;
            }

            if (item.CreationTime.HasValue)
            {
                changedUtc = NormalizeToUtc(item.CreationTime.Value);
                return true;
            }

            changedUtc = default;
            return false;
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

        private static List<string> BuildEverythingQueries(
            string normalizedRootWithSlash,
            HashSet<string> targetExtensions
        )
        {
            string quotedRoot = QuoteForEverything(normalizedRootWithSlash);
            if (targetExtensions.Count < 1)
            {
                return [quotedRoot];
            }

            List<string> queries = [];
            foreach (string ext in targetExtensions)
            {
                string extWithoutDot = ext.TrimStart('.');
                if (string.IsNullOrWhiteSpace(extWithoutDot))
                {
                    continue;
                }

                queries.Add($"{quotedRoot} ext:{extWithoutDot}");
            }

            if (queries.Count < 1)
            {
                queries.Add(quotedRoot);
            }

            return queries;
        }

        private static string QuoteForEverything(string value)
        {
            string escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
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
    }
}
