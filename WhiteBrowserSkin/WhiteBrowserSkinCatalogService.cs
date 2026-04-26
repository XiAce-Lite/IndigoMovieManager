using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace IndigoMovieManager.Skin
{
    /// <summary>
    /// WhiteBrowser 互換の skin フォルダを走査し、使えるスキン一覧へまとめる。
    /// HTML/JS の完全実行はまだ持たず、config の読込と既存タブへの安全マップを担当する。
    /// </summary>
    public static partial class WhiteBrowserSkinCatalogService
    {
        private const string DefaultGridSkinName = "DefaultGrid";
        private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly HashSet<string> BuiltInSkinNames = new(NameComparer)
        {
            "DefaultSmall",
            "DefaultBig",
            DefaultGridSkinName,
            "DefaultList",
            "DefaultBig10",
        };
        // built-in skin 定義は不変なので、毎回 new し直さず共有して allocation を減らす。
        private static readonly IReadOnlyList<WhiteBrowserSkinDefinition> SharedBuiltInDefinitions =
            Array.AsReadOnly(
                [
                    CreateBuiltIn("DefaultSmall"),
                    CreateBuiltIn("DefaultBig"),
                    CreateBuiltIn(DefaultGridSkinName),
                    CreateBuiltIn("DefaultList"),
                    CreateBuiltIn("DefaultBig10"),
                ]
            );
        private static readonly object CacheGate = new();
        private static readonly Dictionary<string, CatalogCacheEntry> CatalogCache =
            new(StringComparer.OrdinalIgnoreCase);
        private static int _catalogLoadHitCountForTesting;
        private static int _catalogLoadMissCountForTesting;
        private static int _catalogSignatureBuildCountForTesting;
        private static int _lastCatalogSignatureDirectoryCountForTesting;
        private static int _lastCatalogSignatureReusedItemCountForTesting;
        private static double _lastCatalogSignatureElapsedMillisecondsForTesting;
        private static int _catalogLoadCoreCountForTesting;
        private static int _lastCatalogLoadCoreExternalDefinitionCountForTesting;
        private static int _lastCatalogLoadCoreReusedDefinitionCountForTesting;
        private static int _lastCatalogLoadCoreSkippedDefinitionCountForTesting;
        private static double _lastCatalogLoadCoreElapsedMillisecondsForTesting;

        static WhiteBrowserSkinCatalogService()
        {
            // WhiteBrowser 由来の Shift_JIS skin を素直に読めるよう、コードページを先に有効化する。
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [GeneratedRegex(
            "<div\\s+id\\s*=\\s*[\"']config[\"'][^>]*>(?<body>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        )]
        private static partial Regex ConfigDivRegex();

        [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
        private static partial Regex HtmlCommentRegex();

        public static IReadOnlyList<WhiteBrowserSkinDefinition> Load(string skinRootPath)
        {
            string normalizedSkinRootPath = NormalizeSkinRootPath(skinRootPath);
            if (string.IsNullOrWhiteSpace(normalizedSkinRootPath))
            {
                return SharedBuiltInDefinitions;
            }

            CatalogCacheEntry previousCacheEntry = null;
            lock (CacheGate)
            {
                CatalogCache.TryGetValue(normalizedSkinRootPath, out previousCacheEntry);
            }

            CatalogSnapshot snapshot = BuildCatalogSnapshot(normalizedSkinRootPath, previousCacheEntry);
            string signature = snapshot.Signature;
            lock (CacheGate)
            {
                if (
                    CatalogCache.TryGetValue(normalizedSkinRootPath, out CatalogCacheEntry cachedEntry)
                    && string.Equals(cachedEntry.Signature, signature, StringComparison.Ordinal)
                )
                {
                    Interlocked.Increment(ref _catalogLoadHitCountForTesting);
                    DebugRuntimeLog.RecordCatalogCacheHit();
                    DebugRuntimeLog.Write(
                        "skin-catalog",
                        $"catalog cache hit: root='{normalizedSkinRootPath}' count={cachedEntry.Definitions.Count}"
                    );
                    return cachedEntry.Definitions;
                }

                previousCacheEntry = cachedEntry ?? previousCacheEntry;
            }

            IReadOnlyList<WhiteBrowserSkinDefinition> loadedDefinitions = LoadCore(
                snapshot,
                previousCacheEntry
            );
            lock (CacheGate)
            {
                CatalogCache[normalizedSkinRootPath] = new CatalogCacheEntry(
                    signature,
                    loadedDefinitions,
                    snapshot
                );
            }

            Interlocked.Increment(ref _catalogLoadMissCountForTesting);
            DebugRuntimeLog.RecordCatalogCacheMiss();
            DebugRuntimeLog.Write(
                "skin-catalog",
                $"catalog cache miss: root='{normalizedSkinRootPath}' count={loadedDefinitions.Count}"
            );
            return loadedDefinitions;
        }

        private static IReadOnlyList<WhiteBrowserSkinDefinition> LoadCore(
            CatalogSnapshot snapshot,
            CatalogCacheEntry previousCacheEntry
        )
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<WhiteBrowserSkinDefinition> result = [.. SharedBuiltInDefinitions];
            if (snapshot == null || snapshot.Items.Count < 1)
            {
                return CompleteCatalogLoadCore(
                    result,
                    snapshot?.RootPath,
                    snapshot,
                    externalDefinitionCount: 0,
                    reusedDefinitionCount: 0,
                    skippedDefinitionCount: 0,
                    stopwatch
                );
            }

            IReadOnlyDictionary<string, CatalogSnapshotItem> previousItemsByName =
                previousCacheEntry?.SnapshotItemsByName
                ?? new Dictionary<string, CatalogSnapshotItem>(NameComparer);
            IReadOnlyDictionary<string, WhiteBrowserSkinDefinition> previousDefinitionsByName =
                previousCacheEntry?.ExternalDefinitionsByName
                ?? new Dictionary<string, WhiteBrowserSkinDefinition>(NameComparer);
            HashSet<string> registeredDefinitionNames = result
                .Select(x => x.Name)
                .ToHashSet(NameComparer);

            List<WhiteBrowserSkinDefinition> externalDefinitions = [];
            int reusedDefinitionCount = 0;
            int skippedDefinitionCount = 0;
            foreach (CatalogSnapshotItem snapshotItem in snapshot.Items)
            {
                // built-in と同名の external skin は採用しないので、重い読込へ入る前に止める。
                if (!registeredDefinitionNames.Add(snapshotItem.DirectoryName ?? ""))
                {
                    skippedDefinitionCount++;
                    continue;
                }

                WhiteBrowserSkinDefinition definition = TryReuseCachedDefinition(
                    snapshotItem,
                    previousItemsByName,
                    previousDefinitionsByName
                );
                if (definition != null)
                {
                    reusedDefinitionCount++;
                }
                else
                {
                    definition = TryLoadExternal(snapshotItem);
                }

                if (definition == null)
                {
                    registeredDefinitionNames.Remove(snapshotItem.DirectoryName ?? "");
                    continue;
                }

                externalDefinitions.Add(definition);
            }

            result.AddRange(externalDefinitions.OrderBy(x => x.Name, NameComparer));
            return CompleteCatalogLoadCore(
                result,
                snapshot?.RootPath,
                snapshot,
                externalDefinitionCount: externalDefinitions.Count,
                reusedDefinitionCount: reusedDefinitionCount,
                skippedDefinitionCount: skippedDefinitionCount,
                stopwatch
            );
        }

        internal static void ResetCacheForTesting()
        {
            lock (CacheGate)
            {
                CatalogCache.Clear();
            }

            Interlocked.Exchange(ref _catalogLoadHitCountForTesting, 0);
            Interlocked.Exchange(ref _catalogLoadMissCountForTesting, 0);
            Interlocked.Exchange(ref _catalogSignatureBuildCountForTesting, 0);
            Interlocked.Exchange(ref _lastCatalogSignatureDirectoryCountForTesting, 0);
            Interlocked.Exchange(ref _lastCatalogSignatureReusedItemCountForTesting, 0);
            Volatile.Write(ref _lastCatalogSignatureElapsedMillisecondsForTesting, 0);
            Interlocked.Exchange(ref _catalogLoadCoreCountForTesting, 0);
            Interlocked.Exchange(ref _lastCatalogLoadCoreExternalDefinitionCountForTesting, 0);
            Interlocked.Exchange(ref _lastCatalogLoadCoreReusedDefinitionCountForTesting, 0);
            Interlocked.Exchange(ref _lastCatalogLoadCoreSkippedDefinitionCountForTesting, 0);
            Volatile.Write(ref _lastCatalogLoadCoreElapsedMillisecondsForTesting, 0);
        }

        internal static int GetCatalogLoadHitCountForTesting()
        {
            return Volatile.Read(ref _catalogLoadHitCountForTesting);
        }

        internal static int GetCatalogLoadMissCountForTesting()
        {
            return Volatile.Read(ref _catalogLoadMissCountForTesting);
        }

        internal static int GetCatalogSignatureBuildCountForTesting()
        {
            return Volatile.Read(ref _catalogSignatureBuildCountForTesting);
        }

        internal static int GetLastCatalogSignatureDirectoryCountForTesting()
        {
            return Volatile.Read(ref _lastCatalogSignatureDirectoryCountForTesting);
        }

        internal static int GetLastCatalogSignatureReusedItemCountForTesting()
        {
            return Volatile.Read(ref _lastCatalogSignatureReusedItemCountForTesting);
        }

        internal static double GetLastCatalogSignatureElapsedMillisecondsForTesting()
        {
            return Volatile.Read(ref _lastCatalogSignatureElapsedMillisecondsForTesting);
        }

        internal static int GetCatalogLoadCoreCountForTesting()
        {
            return Volatile.Read(ref _catalogLoadCoreCountForTesting);
        }

        internal static int GetLastCatalogLoadCoreExternalDefinitionCountForTesting()
        {
            return Volatile.Read(ref _lastCatalogLoadCoreExternalDefinitionCountForTesting);
        }

        internal static int GetLastCatalogLoadCoreReusedDefinitionCountForTesting()
        {
            return Volatile.Read(ref _lastCatalogLoadCoreReusedDefinitionCountForTesting);
        }

        internal static int GetLastCatalogLoadCoreSkippedDefinitionCountForTesting()
        {
            return Volatile.Read(ref _lastCatalogLoadCoreSkippedDefinitionCountForTesting);
        }

        internal static double GetLastCatalogLoadCoreElapsedMillisecondsForTesting()
        {
            return Volatile.Read(ref _lastCatalogLoadCoreElapsedMillisecondsForTesting);
        }

        public static string ResolveSkinRootPath(string appBaseDirectory)
        {
            string baseDirectory = string.IsNullOrWhiteSpace(appBaseDirectory)
                ? AppContext.BaseDirectory
                : appBaseDirectory;
            return Path.Combine(baseDirectory, "skin");
        }

        public static WhiteBrowserSkinDefinition ResolveByName(
            IReadOnlyList<WhiteBrowserSkinDefinition> definitions,
            string skinName
        )
        {
            if (definitions == null || definitions.Count < 1)
            {
                return null;
            }

            string normalizedSkinName = skinName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(normalizedSkinName))
            {
                normalizedSkinName = DefaultGridSkinName;
            }

            return definitions.FirstOrDefault(x => NameComparer.Equals(x.Name, normalizedSkinName))
                ?? definitions.FirstOrDefault(x => NameComparer.Equals(x.Name, DefaultGridSkinName))
                ?? definitions[0];
        }

        public static WhiteBrowserSkinDefinition TryResolveExactByName(
            IReadOnlyList<WhiteBrowserSkinDefinition> definitions,
            string skinName
        )
        {
            if (definitions == null || definitions.Count < 1)
            {
                return null;
            }

            string normalizedSkinName = skinName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(normalizedSkinName))
            {
                return null;
            }

            return definitions.FirstOrDefault(x => NameComparer.Equals(x.Name, normalizedSkinName));
        }

        private static WhiteBrowserSkinDefinition CreateBuiltIn(string skinName)
        {
            return new WhiteBrowserSkinDefinition(
                skinName,
                "",
                "",
                WhiteBrowserSkinConfig.Empty,
                skinName,
                isBuiltIn: true
            );
        }

        private static string NormalizeSkinRootPath(string skinRootPath)
        {
            string normalizedPath = skinRootPath?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(normalizedPath);
            }
            catch
            {
                return normalizedPath;
            }
        }

        private static CatalogSnapshot BuildCatalogSnapshot(
            string skinRootPath,
            CatalogCacheEntry previousCacheEntry
        )
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int directoryCount = 0;
            int reusedItemCount = 0;
            if (string.IsNullOrWhiteSpace(skinRootPath) || !Directory.Exists(skinRootPath))
            {
                return CompleteCatalogSignatureBuild(
                    skinRootPath,
                    directoryCount,
                    reusedItemCount,
                    stopwatch,
                    "missing",
                    []
                );
            }

            IReadOnlyDictionary<string, CatalogSnapshotItem> previousItemsByName =
                previousCacheEntry?.SnapshotItemsByName
                ?? new Dictionary<string, CatalogSnapshotItem>(NameComparer);
            StringBuilder signature = new();
            List<CatalogSnapshotItem> items = [];
            foreach (
                string directoryPath in Directory.EnumerateDirectories(skinRootPath).OrderBy(x => x, NameComparer)
            )
            {
                directoryCount++;
                string directoryName = Path.GetFileName(directoryPath) ?? "";
                // 結果に絶対採用しない built-in 同名 external は、
                // snapshot でも追わずに落として無駄な再走査と miss を減らす。
                if (BuiltInSkinNames.Contains(directoryName))
                {
                    continue;
                }

                long directoryLastWriteTicks = Directory.GetLastWriteTimeUtc(directoryPath).Ticks;
                signature.Append(directoryName);
                signature.Append('|');
                signature.Append(directoryLastWriteTicks);
                signature.Append('|');

                if (
                    previousItemsByName.TryGetValue(directoryName, out CatalogSnapshotItem previousItem)
                    && string.Equals(previousItem.DirectoryPath, directoryPath, StringComparison.OrdinalIgnoreCase)
                )
                {
                    if (
                        string.IsNullOrWhiteSpace(previousItem.HtmlPath)
                        && previousItem.DirectoryLastWriteTicks == directoryLastWriteTicks
                    )
                    {
                        reusedItemCount++;
                        AppendSnapshotItemSignature(signature, previousItem);
                        items.Add(previousItem);
                        continue;
                    }

                    // ディレクトリ時刻が不変なら、優先候補の並びは変わっていないので前回 HtmlPath だけ確認する。
                    if (
                        previousItem.DirectoryLastWriteTicks == directoryLastWriteTicks
                        && !string.IsNullOrWhiteSpace(previousItem.HtmlPath)
                        && File.Exists(previousItem.HtmlPath)
                    )
                    {
                        FileInfo cachedHtmlInfo = new(previousItem.HtmlPath);
                        if (
                            string.Equals(previousItem.HtmlPath, cachedHtmlInfo.FullName, StringComparison.OrdinalIgnoreCase)
                            && previousItem.HtmlLength == cachedHtmlInfo.Length
                            && previousItem.HtmlLastWriteTicks == cachedHtmlInfo.LastWriteTimeUtc.Ticks
                        )
                        {
                            CatalogSnapshotItem reusedItem = new(
                                directoryPath,
                                directoryName,
                                previousItem.HtmlPath,
                                directoryLastWriteTicks: directoryLastWriteTicks,
                                htmlLength: cachedHtmlInfo.Length,
                                htmlLastWriteTicks: cachedHtmlInfo.LastWriteTimeUtc.Ticks
                            );
                            reusedItemCount++;
                            AppendSnapshotItemSignature(signature, reusedItem);
                            items.Add(reusedItem);
                            continue;
                        }
                    }

                    string preferredHtmlPath = TryResolvePreferredCachedHtmlPath(
                        directoryPath,
                        previousItem
                    );
                    CatalogSnapshotItem currentItem = CreateCatalogSnapshotItem(
                        directoryPath,
                        directoryName,
                        directoryLastWriteTicks,
                        preferredHtmlPath
                    );
                    AppendSnapshotItemSignature(signature, currentItem);
                    items.Add(currentItem);
                    continue;
                }

                CatalogSnapshotItem resolvedCurrentItem = CreateCatalogSnapshotItem(
                    directoryPath,
                    directoryName,
                    directoryLastWriteTicks
                );
                AppendSnapshotItemSignature(signature, resolvedCurrentItem);
                items.Add(resolvedCurrentItem);
            }

            return CompleteCatalogSignatureBuild(
                skinRootPath,
                directoryCount,
                reusedItemCount,
                stopwatch,
                signature.ToString(),
                items
            );
        }

        private static void AppendSnapshotItemSignature(
            StringBuilder signature,
            CatalogSnapshotItem snapshotItem
        )
        {
            if (signature == null || snapshotItem == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(snapshotItem.HtmlPath))
            {
                signature.Append("no-html;");
                return;
            }

            signature.Append(Path.GetFileName(snapshotItem.HtmlPath));
            signature.Append('|');
            signature.Append(snapshotItem.HtmlLength);
            signature.Append('|');
            signature.Append(snapshotItem.HtmlLastWriteTicks);
            signature.Append(';');
        }

        private static CatalogSnapshot CompleteCatalogSignatureBuild(
            string skinRootPath,
            int directoryCount,
            int reusedItemCount,
            Stopwatch stopwatch,
            string signature,
            IReadOnlyList<CatalogSnapshotItem> items
        )
        {
            stopwatch?.Stop();
            Interlocked.Increment(ref _catalogSignatureBuildCountForTesting);
            Interlocked.Exchange(ref _lastCatalogSignatureDirectoryCountForTesting, directoryCount);
            Interlocked.Exchange(ref _lastCatalogSignatureReusedItemCountForTesting, reusedItemCount);
            Volatile.Write(
                ref _lastCatalogSignatureElapsedMillisecondsForTesting,
                stopwatch?.Elapsed.TotalMilliseconds ?? 0
            );
            DebugRuntimeLog.RecordCatalogSignatureElapsed(
                stopwatch?.Elapsed.TotalMilliseconds ?? 0
            );
            DebugRuntimeLog.Write(
                "skin-catalog",
                $"catalog signature built: root='{skinRootPath ?? ""}' directories={directoryCount} reused={reusedItemCount} elapsed_ms={(stopwatch?.Elapsed.TotalMilliseconds ?? 0):F3}"
            );
            return new CatalogSnapshot(
                skinRootPath,
                signature ?? "",
                items ?? Array.Empty<CatalogSnapshotItem>()
            );
        }

        private static IReadOnlyList<WhiteBrowserSkinDefinition> CompleteCatalogLoadCore(
            IReadOnlyList<WhiteBrowserSkinDefinition> definitions,
            string skinRootPath,
            CatalogSnapshot snapshot,
            int externalDefinitionCount,
            int reusedDefinitionCount,
            int skippedDefinitionCount,
            Stopwatch stopwatch
        )
        {
            stopwatch?.Stop();
            Interlocked.Increment(ref _catalogLoadCoreCountForTesting);
            Interlocked.Exchange(
                ref _lastCatalogLoadCoreExternalDefinitionCountForTesting,
                externalDefinitionCount
            );
            Interlocked.Exchange(
                ref _lastCatalogLoadCoreReusedDefinitionCountForTesting,
                reusedDefinitionCount
            );
            Interlocked.Exchange(
                ref _lastCatalogLoadCoreSkippedDefinitionCountForTesting,
                skippedDefinitionCount
            );
            Volatile.Write(
                ref _lastCatalogLoadCoreElapsedMillisecondsForTesting,
                stopwatch?.Elapsed.TotalMilliseconds ?? 0
            );
            DebugRuntimeLog.RecordCatalogLoadCore(reusedDefinitionCount, skippedDefinitionCount);
            DebugRuntimeLog.RecordCatalogLoadElapsed(
                stopwatch?.Elapsed.TotalMilliseconds ?? 0
            );
            DebugRuntimeLog.Write(
                "skin-catalog",
                $"catalog load core built: root='{skinRootPath ?? ""}' items={snapshot?.Items.Count ?? 0} external={externalDefinitionCount} reused={reusedDefinitionCount} skipped={skippedDefinitionCount} elapsed_ms={(stopwatch?.Elapsed.TotalMilliseconds ?? 0):F3}"
            );
            return definitions ?? Array.Empty<WhiteBrowserSkinDefinition>();
        }

        private static WhiteBrowserSkinDefinition TryReuseCachedDefinition(
            CatalogSnapshotItem snapshotItem,
            IReadOnlyDictionary<string, CatalogSnapshotItem> previousItemsByName,
            IReadOnlyDictionary<string, WhiteBrowserSkinDefinition> previousDefinitionsByName
        )
        {
            if (
                snapshotItem == null
                || previousItemsByName == null
                || previousDefinitionsByName == null
                || string.IsNullOrWhiteSpace(snapshotItem.DirectoryName)
                || !previousItemsByName.TryGetValue(snapshotItem.DirectoryName, out CatalogSnapshotItem previousItem)
                || !previousDefinitionsByName.TryGetValue(snapshotItem.DirectoryName, out WhiteBrowserSkinDefinition previousDefinition)
            )
            {
                return null;
            }

            if (
                !string.Equals(previousItem.DirectoryPath, snapshotItem.DirectoryPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(previousItem.HtmlPath, snapshotItem.HtmlPath, StringComparison.OrdinalIgnoreCase)
                || previousItem.HtmlLength != snapshotItem.HtmlLength
                || previousItem.HtmlLastWriteTicks != snapshotItem.HtmlLastWriteTicks
            )
            {
                return null;
            }

            return previousDefinition;
        }

        private static WhiteBrowserSkinDefinition TryLoadExternal(CatalogSnapshotItem snapshotItem)
        {
            if (snapshotItem == null || string.IsNullOrWhiteSpace(snapshotItem.DirectoryPath))
            {
                return null;
            }

            string directoryName = snapshotItem.DirectoryName ?? "";
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                return null;
            }

            string htmlPath = snapshotItem.HtmlPath ?? "";
            if (string.IsNullOrWhiteSpace(htmlPath) || !File.Exists(htmlPath))
            {
                return null;
            }

            string html = ReadSkinHtmlText(htmlPath);
            WhiteBrowserSkinConfig config = ParseConfig(html);
            string preferredTabStateName = ResolvePreferredTabStateName(directoryName, config);

            return new WhiteBrowserSkinDefinition(
                directoryName,
                snapshotItem.DirectoryPath,
                htmlPath,
                config,
                preferredTabStateName,
                isBuiltIn: false
            );
        }

        private static CatalogSnapshotItem CreateCatalogSnapshotItem(
            string directoryPath,
            string directoryName,
            long directoryLastWriteTicks,
            string preferredCachedHtmlPath = null
        )
        {
            (string htmlPath, long htmlLength, long htmlLastWriteTicks) = ResolveSkinHtmlCandidate(
                directoryPath,
                directoryName,
                preferredCachedHtmlPath
            );
            return new CatalogSnapshotItem(
                directoryPath,
                directoryName,
                htmlPath,
                directoryLastWriteTicks,
                htmlLength,
                htmlLastWriteTicks
            );
        }

        private static (string HtmlPath, long HtmlLength, long HtmlLastWriteTicks) ResolveSkinHtmlCandidate(
            string directoryPath,
            string directoryName,
            string preferredCachedHtmlPath = null
        )
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return (null, 0, 0);
            }

            string standardHtmFileName = string.IsNullOrWhiteSpace(directoryName)
                ? ""
                : $"{directoryName}.htm";
            string standardHtmlFileName = string.IsNullOrWhiteSpace(directoryName)
                ? ""
                : $"{directoryName}.html";
            string firstHtm = null;
            string firstHtml = null;
            string preferredResolved = null;
            string standardHtmlResolved = null;

            // 標準名優先・前回 custom HTML 維持・fallback .htm 優先を 1 回の列挙で決める。
            foreach (string filePath in Directory.EnumerateFiles(directoryPath))
            {
                string fileName = Path.GetFileName(filePath) ?? "";
                if (
                    !string.IsNullOrWhiteSpace(standardHtmFileName)
                    && string.Equals(fileName, standardHtmFileName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return CreateSkinHtmlCandidate(filePath);
                }

                if (
                    standardHtmlResolved == null
                    && !string.IsNullOrWhiteSpace(standardHtmlFileName)
                    && string.Equals(fileName, standardHtmlFileName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    standardHtmlResolved = filePath;
                }

                if (
                    preferredResolved == null
                    && !string.IsNullOrWhiteSpace(preferredCachedHtmlPath)
                    && string.Equals(
                        filePath,
                        preferredCachedHtmlPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    preferredResolved = filePath;
                }

                string extension = Path.GetExtension(filePath);
                if (firstHtm == null && string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase))
                {
                    firstHtm = filePath;
                    continue;
                }

                if (
                    firstHtml == null
                    && string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
                )
                {
                    firstHtml = filePath;
                }
            }

            return CreateSkinHtmlCandidate(
                standardHtmlResolved ?? preferredResolved ?? firstHtm ?? firstHtml
            );
        }

        private static (string HtmlPath, long HtmlLength, long HtmlLastWriteTicks) CreateSkinHtmlCandidate(
            string filePath
        )
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return (null, 0, 0);
            }

            FileInfo fileInfo = new(filePath);
            return (fileInfo.FullName, fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks);
        }

        private static string TryResolvePreferredCachedHtmlPath(
            string directoryPath,
            CatalogSnapshotItem previousItem
        )
        {
            if (
                string.IsNullOrWhiteSpace(directoryPath)
                || previousItem == null
                || string.IsNullOrWhiteSpace(previousItem.HtmlPath)
            )
            {
                return null;
            }

            string previousHtmlDirectoryPath = Path.GetDirectoryName(previousItem.HtmlPath) ?? "";
            if (!string.Equals(previousHtmlDirectoryPath, directoryPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!File.Exists(previousItem.HtmlPath))
            {
                return null;
            }

            return previousItem.HtmlPath;
        }

        private static string ReadSkinHtmlText(string htmlPath)
        {
            byte[] bytes = File.ReadAllBytes(htmlPath);
            if (bytes.Length < 1)
            {
                return "";
            }

            try
            {
                // まず UTF-8 として厳密に試し、壊れていれば Shift_JIS へ戻す。
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(932).GetString(bytes);
            }
        }

        private static WhiteBrowserSkinConfig ParseConfig(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return WhiteBrowserSkinConfig.Empty;
            }

            Match match = ConfigDivRegex().Match(html);
            if (!match.Success)
            {
                return WhiteBrowserSkinConfig.Empty;
            }

            string body = HtmlCommentRegex().Replace(match.Groups["body"].Value, " ");
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (string statement in body.Split(';'))
            {
                string trimmedStatement = statement.Trim();
                if (string.IsNullOrWhiteSpace(trimmedStatement))
                {
                    continue;
                }

                int separatorIndex = trimmedStatement.IndexOf(':');
                if (separatorIndex < 1)
                {
                    continue;
                }

                string key = trimmedStatement[..separatorIndex].Trim();
                string value = trimmedStatement[(separatorIndex + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    values[key] = value;
                }
            }

            return new WhiteBrowserSkinConfig
            {
                SkinVersion = GetString(values, "skin-version"),
                ThumbWidth = GetInt(values, "thum-width", 160),
                ThumbHeight = GetInt(values, "thum-height", 120),
                ThumbColumn = GetInt(values, "thum-column", 1),
                ThumbRow = GetInt(values, "thum-row", 1),
                SeamlessScroll = GetInt(values, "seamless-scroll", 0),
                ScrollId = GetString(values, "scroll-id", "view"),
                MultiSelect = GetInt(values, "multi-select", 0),
            };
        }

        private static string ResolvePreferredTabStateName(
            string directoryName,
            WhiteBrowserSkinConfig config
        )
        {
            string normalizedName = (directoryName ?? "").Replace(" ", "").Replace("　", "");
            if (NameComparer.Equals(normalizedName, "DefaultSmall"))
            {
                return "DefaultSmall";
            }

            if (NameComparer.Equals(normalizedName, "DefaultBig"))
            {
                return "DefaultBig";
            }

            if (NameComparer.Equals(normalizedName, DefaultGridSkinName))
            {
                return DefaultGridSkinName;
            }

            if (NameComparer.Equals(normalizedName, "DefaultList"))
            {
                return "DefaultList";
            }

            if (NameComparer.Equals(normalizedName, "DefaultBig10"))
            {
                return "DefaultBig10";
            }

            int column = config?.ThumbColumn ?? 1;
            int row = config?.ThumbRow ?? 1;
            int width = config?.ThumbWidth ?? 160;
            int height = config?.ThumbHeight ?? 120;

            if (column == 5 && row == 2)
            {
                return "DefaultBig10";
            }

            if (column == 5 && row == 1)
            {
                return "DefaultList";
            }

            if (column == 1 && row == 1)
            {
                return width <= 80 || height <= 60 ? "DefaultList" : DefaultGridSkinName;
            }

            if (width <= 140 && height <= 100)
            {
                return "DefaultSmall";
            }

            if (width >= 180 && height >= 130)
            {
                return "DefaultBig";
            }

            return DefaultGridSkinName;
        }

        private static int GetInt(
            IReadOnlyDictionary<string, string> values,
            string key,
            int defaultValue
        )
        {
            if (
                values != null
                && values.TryGetValue(key, out string value)
            )
            {
                if (int.TryParse(value, out int parsed))
                {
                    return parsed;
                }

                if (bool.TryParse(value, out bool boolValue))
                {
                    return boolValue ? 1 : 0;
                }
            }

            return defaultValue;
        }

        private static string GetString(
            IReadOnlyDictionary<string, string> values,
            string key,
            string defaultValue = ""
        )
        {
            if (values != null && values.TryGetValue(key, out string value))
            {
                return value ?? defaultValue;
            }

            return defaultValue;
        }

        private sealed class CatalogCacheEntry
        {
            internal CatalogCacheEntry(
                string signature,
                IReadOnlyList<WhiteBrowserSkinDefinition> definitions,
                CatalogSnapshot snapshot
            )
            {
                Signature = signature ?? "";
                Definitions = definitions ?? Array.Empty<WhiteBrowserSkinDefinition>();
                SnapshotItems = snapshot?.Items ?? Array.Empty<CatalogSnapshotItem>();
                // 直近 miss で組んだ辞書を持ち回し、次回 load ごとの再構築を避ける。
                SnapshotItemsByName = SnapshotItems.ToDictionary(x => x.DirectoryName, x => x, NameComparer);
                ExternalDefinitionsByName = Definitions
                    .Where(x => x != null && !x.IsBuiltIn)
                    .ToDictionary(x => x.Name, x => x, NameComparer);
            }

            internal string Signature { get; }
            internal IReadOnlyList<WhiteBrowserSkinDefinition> Definitions { get; }
            internal IReadOnlyList<CatalogSnapshotItem> SnapshotItems { get; }
            internal IReadOnlyDictionary<string, CatalogSnapshotItem> SnapshotItemsByName { get; }
            internal IReadOnlyDictionary<string, WhiteBrowserSkinDefinition> ExternalDefinitionsByName { get; }
        }

        private sealed class CatalogSnapshot
        {
            internal CatalogSnapshot(
                string rootPath,
                string signature,
                IReadOnlyList<CatalogSnapshotItem> items
            )
            {
                RootPath = rootPath ?? "";
                Signature = signature ?? "";
                Items = items ?? Array.Empty<CatalogSnapshotItem>();
            }

            internal string RootPath { get; }
            internal string Signature { get; }
            internal IReadOnlyList<CatalogSnapshotItem> Items { get; }
        }

        private sealed class CatalogSnapshotItem
        {
            internal CatalogSnapshotItem(
                string directoryPath,
                string directoryName,
                string htmlPath,
                long directoryLastWriteTicks,
                long htmlLength,
                long htmlLastWriteTicks
            )
            {
                DirectoryPath = directoryPath ?? "";
                DirectoryName = directoryName ?? "";
                HtmlPath = htmlPath ?? "";
                DirectoryLastWriteTicks = directoryLastWriteTicks;
                HtmlLength = htmlLength;
                HtmlLastWriteTicks = htmlLastWriteTicks;
            }

            internal string DirectoryPath { get; }
            internal string DirectoryName { get; }
            internal string HtmlPath { get; }
            internal long DirectoryLastWriteTicks { get; }
            internal long HtmlLength { get; }
            internal long HtmlLastWriteTicks { get; }
        }
    }
}
