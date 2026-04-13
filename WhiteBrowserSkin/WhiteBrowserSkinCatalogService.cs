using System;
using System.Collections.Generic;
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
        private static readonly object CacheGate = new();
        private static readonly Dictionary<string, CatalogCacheEntry> CatalogCache =
            new(StringComparer.OrdinalIgnoreCase);
        private static int _catalogLoadMissCountForTesting;

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
                return CreateBuiltInDefinitions();
            }

            string signature = BuildCatalogSignature(normalizedSkinRootPath);
            lock (CacheGate)
            {
                if (
                    CatalogCache.TryGetValue(normalizedSkinRootPath, out CatalogCacheEntry cached)
                    && string.Equals(cached.Signature, signature, StringComparison.Ordinal)
                )
                {
                    return cached.Definitions;
                }
            }

            IReadOnlyList<WhiteBrowserSkinDefinition> loadedDefinitions = LoadCore(normalizedSkinRootPath);
            lock (CacheGate)
            {
                CatalogCache[normalizedSkinRootPath] = new CatalogCacheEntry(
                    signature,
                    loadedDefinitions
                );
            }

            Interlocked.Increment(ref _catalogLoadMissCountForTesting);
            return loadedDefinitions;
        }

        private static IReadOnlyList<WhiteBrowserSkinDefinition> LoadCore(string skinRootPath)
        {
            List<WhiteBrowserSkinDefinition> result = CreateBuiltInDefinitions();
            if (!Directory.Exists(skinRootPath))
            {
                return result;
            }

            List<WhiteBrowserSkinDefinition> externalDefinitions = [];
            foreach (string directoryPath in Directory.EnumerateDirectories(skinRootPath))
            {
                WhiteBrowserSkinDefinition definition = TryLoadExternal(directoryPath);
                if (definition == null)
                {
                    continue;
                }

                // built-in 名は予約済みとして扱い、外部 skin が同名でも上書きしない。
                if (result.Any(x => NameComparer.Equals(x.Name, definition.Name)))
                {
                    continue;
                }

                externalDefinitions.Add(definition);
            }

            result.AddRange(externalDefinitions.OrderBy(x => x.Name, NameComparer));
            return result;
        }

        internal static void ResetCacheForTesting()
        {
            lock (CacheGate)
            {
                CatalogCache.Clear();
            }

            Interlocked.Exchange(ref _catalogLoadMissCountForTesting, 0);
        }

        internal static int GetCatalogLoadMissCountForTesting()
        {
            return Volatile.Read(ref _catalogLoadMissCountForTesting);
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

        private static List<WhiteBrowserSkinDefinition> CreateBuiltInDefinitions()
        {
            return
            [
                CreateBuiltIn("DefaultSmall"),
                CreateBuiltIn("DefaultBig"),
                CreateBuiltIn(DefaultGridSkinName),
                CreateBuiltIn("DefaultList"),
                CreateBuiltIn("DefaultBig10"),
            ];
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

        private static string BuildCatalogSignature(string skinRootPath)
        {
            if (string.IsNullOrWhiteSpace(skinRootPath) || !Directory.Exists(skinRootPath))
            {
                return "missing";
            }

            StringBuilder signature = new();
            foreach (
                string directoryPath in Directory.EnumerateDirectories(skinRootPath).OrderBy(x => x, NameComparer)
            )
            {
                string directoryName = Path.GetFileName(directoryPath) ?? "";
                signature.Append(directoryName);
                signature.Append('|');
                signature.Append(Directory.GetLastWriteTimeUtc(directoryPath).Ticks);
                signature.Append('|');

                string htmlPath = ResolveSkinHtmlPath(directoryPath, directoryName);
                if (string.IsNullOrWhiteSpace(htmlPath) || !File.Exists(htmlPath))
                {
                    signature.Append("no-html;");
                    continue;
                }

                FileInfo htmlInfo = new(htmlPath);
                signature.Append(Path.GetFileName(htmlPath));
                signature.Append('|');
                signature.Append(htmlInfo.Length);
                signature.Append('|');
                signature.Append(htmlInfo.LastWriteTimeUtc.Ticks);
                signature.Append(';');
            }

            return signature.ToString();
        }

        private static WhiteBrowserSkinDefinition TryLoadExternal(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return null;
            }

            string directoryName = Path.GetFileName(directoryPath) ?? "";
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                return null;
            }

            string htmlPath = ResolveSkinHtmlPath(directoryPath, directoryName);
            if (string.IsNullOrWhiteSpace(htmlPath) || !File.Exists(htmlPath))
            {
                return null;
            }

            string html = ReadSkinHtmlText(htmlPath);
            WhiteBrowserSkinConfig config = ParseConfig(html);
            string preferredTabStateName = ResolvePreferredTabStateName(directoryName, config);

            return new WhiteBrowserSkinDefinition(
                directoryName,
                directoryPath,
                htmlPath,
                config,
                preferredTabStateName,
                isBuiltIn: false
            );
        }

        private static string ResolveSkinHtmlPath(string directoryPath, string directoryName)
        {
            string[] candidates =
            [
                Path.Combine(directoryPath, $"{directoryName}.htm"),
                Path.Combine(directoryPath, $"{directoryName}.html"),
            ];

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Directory
                .EnumerateFiles(directoryPath, "*.htm")
                .Concat(Directory.EnumerateFiles(directoryPath, "*.html"))
                .FirstOrDefault();
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
            internal CatalogCacheEntry(string signature, IReadOnlyList<WhiteBrowserSkinDefinition> definitions)
            {
                Signature = signature ?? "";
                Definitions = definitions ?? Array.Empty<WhiteBrowserSkinDefinition>();
            }

            internal string Signature { get; }
            internal IReadOnlyList<WhiteBrowserSkinDefinition> Definitions { get; }
        }
    }
}
