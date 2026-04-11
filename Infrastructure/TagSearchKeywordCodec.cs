using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IndigoMovieManager.Infrastructure
{
    /// <summary>
    /// タグバー起点の検索語を、通常検索と混ざらない exact tag 構文へ寄せる。
    /// 複数タグや空白を含むタグでも、checked 状態と検索条件を往復できるようにする。
    /// </summary>
    internal static class TagSearchKeywordCodec
    {
        private const string TagPrefix = "!tag:";

        public static string BuildKeyword(IEnumerable<string> tagNames)
        {
            return BuildKeyword(tagNames, "");
        }

        public static string BuildKeyword(IEnumerable<string> tagNames, string remainingQuery)
        {
            string[] normalizedTags = TagTextParser.SplitDistinct(
                tagNames,
                StringComparer.CurrentCultureIgnoreCase
            );
            string normalizedRemainingQuery = NormalizeRemainingQuery(remainingQuery);
            if (normalizedTags.Length == 0)
            {
                return normalizedRemainingQuery;
            }

            string tagQuery = string.Join(" ", normalizedTags.Select(BuildToken));
            if (string.IsNullOrWhiteSpace(normalizedRemainingQuery))
            {
                return tagQuery;
            }

            return $"{normalizedRemainingQuery} {tagQuery}";
        }

        public static string[] ExtractActiveTags(string searchKeyword)
        {
            return ParseSearchKeyword(searchKeyword).ActiveTags;
        }

        public static bool TryResolveSingleTag(string searchKeyword, out string tagName)
        {
            tagName = "";
            string normalizedKeyword = (searchKeyword ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                return false;
            }

            ParsedSearchKeyword parsedKeyword = ParseSearchKeyword(normalizedKeyword);
            if (parsedKeyword.ActiveTags.Length > 0)
            {
                if (
                    parsedKeyword.ActiveTags.Length != 1
                    || !string.IsNullOrWhiteSpace(parsedKeyword.RemainingQuery)
                )
                {
                    return false;
                }

                tagName = parsedKeyword.ActiveTags[0];
                return true;
            }

            if (LooksLikeComplexSearch(normalizedKeyword))
            {
                return false;
            }

            tagName = normalizedKeyword;
            return true;
        }

        public static bool TryParsePureTagQuery(string searchKeyword, out string[] tagNames)
        {
            ParsedSearchKeyword parsedKeyword = ParseSearchKeyword(searchKeyword);
            tagNames = parsedKeyword.ActiveTags;
            return tagNames.Length > 0 && string.IsNullOrWhiteSpace(parsedKeyword.RemainingQuery);
        }

        public static string ReplaceTagFilters(string searchKeyword, IEnumerable<string> tagNames)
        {
            ParsedSearchKeyword parsedKeyword = ParseSearchKeyword(searchKeyword);
            return BuildKeyword(tagNames, parsedKeyword.RemainingQuery);
        }

        private static string BuildToken(string tagName)
        {
            string normalizedTagName = (tagName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalizedTagName))
            {
                return "";
            }

            if (!RequiresQuotedToken(normalizedTagName))
            {
                return $"{TagPrefix}{normalizedTagName}";
            }

            return $"{TagPrefix}\"{EscapeQuoted(normalizedTagName)}\"";
        }

        private static bool LooksLikeComplexSearch(string searchKeyword)
        {
            return searchKeyword.Contains(" | ", StringComparison.Ordinal)
                || searchKeyword.Contains('\r')
                || searchKeyword.Contains('\n')
                || searchKeyword.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)
                || searchKeyword.StartsWith("-", StringComparison.Ordinal)
                || searchKeyword.StartsWith("{", StringComparison.Ordinal)
                || searchKeyword.StartsWith("\"", StringComparison.Ordinal)
                || searchKeyword.StartsWith("'", StringComparison.Ordinal);
        }

        private static bool RequiresQuotedToken(string tagName)
        {
            foreach (char ch in tagName)
            {
                if (
                    char.IsWhiteSpace(ch)
                    || ch == '"'
                    || ch == '\''
                    || ch == '|'
                    || ch == '{'
                    || ch == '}'
                    || ch == '!'
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static string EscapeQuoted(string tagName)
        {
            return (tagName ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static bool StartsWithTagPrefix(string text, int index)
        {
            if (index < 0 || index + TagPrefix.Length > text.Length)
            {
                return false;
            }

            return text.AsSpan(index, TagPrefix.Length).Equals(
                TagPrefix.AsSpan(),
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static string ReadTagToken(string text, ref int index)
        {
            if (index >= text.Length)
            {
                return "";
            }

            char current = text[index];
            if (current == '"' || current == '\'')
            {
                return ReadQuotedToken(text, ref index);
            }

            int start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                if (text[index] == '|')
                {
                    return "";
                }

                index++;
            }

            return text[start..index].Trim();
        }

        private static string ReadRawToken(string text, ref int index)
        {
            if (index >= text.Length)
            {
                return "";
            }

            int start = index;
            char current = text[index];
            if (current == '"' || current == '\'')
            {
                char quote = current;
                index++;
                bool escaped = false;
                while (index < text.Length)
                {
                    char tokenChar = text[index];
                    index++;

                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (tokenChar == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (tokenChar == quote)
                    {
                        break;
                    }
                }

                return text[start..index].Trim();
            }

            if (
                current == '-'
                && index + 1 < text.Length
                && (text[index + 1] == '"' || text[index + 1] == '\'')
            )
            {
                char quote = text[index + 1];
                index += 2;
                bool escaped = false;
                while (index < text.Length)
                {
                    char tokenChar = text[index];
                    index++;

                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (tokenChar == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (tokenChar == quote)
                    {
                        break;
                    }
                }

                return text[start..index].Trim();
            }

            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            return text[start..index].Trim();
        }

        private static string ReadQuotedToken(string text, ref int index)
        {
            char quote = text[index];
            index++;
            StringBuilder builder = new();
            bool escaped = false;

            while (index < text.Length)
            {
                char current = text[index];
                index++;

                if (escaped)
                {
                    builder.Append(current);
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == quote)
                {
                    return builder.ToString();
                }

                builder.Append(current);
            }

            return "";
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        private static string NormalizeRemainingQuery(string remainingQuery)
        {
            return string.Join(" ", TokenizeRemainingQuery(remainingQuery));
        }

        internal static string[] TokenizeRemainingQuery(string remainingQuery)
        {
            string normalizedQuery = (remainingQuery ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return [];
            }

            List<string> tokens = [];
            int index = 0;
            while (index < normalizedQuery.Length)
            {
                SkipWhitespace(normalizedQuery, ref index);
                if (index >= normalizedQuery.Length)
                {
                    break;
                }

                string rawToken = ReadRawToken(normalizedQuery, ref index);
                if (!string.IsNullOrWhiteSpace(rawToken))
                {
                    tokens.Add(rawToken);
                }
            }

            return tokens.ToArray();
        }

        private static ParsedSearchKeyword ParseSearchKeyword(string searchKeyword)
        {
            string normalizedKeyword = (searchKeyword ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                return ParsedSearchKeyword.Empty;
            }

            List<string> parsedTags = [];
            List<string> remainingTokens = [];
            int index = 0;
            while (index < normalizedKeyword.Length)
            {
                SkipWhitespace(normalizedKeyword, ref index);
                if (index >= normalizedKeyword.Length)
                {
                    break;
                }

                if (StartsWithTagPrefix(normalizedKeyword, index))
                {
                    int tagTokenIndex = index + TagPrefix.Length;
                    string parsedTag = ReadTagToken(normalizedKeyword, ref tagTokenIndex);
                    if (!string.IsNullOrWhiteSpace(parsedTag))
                    {
                        parsedTags.Add(parsedTag);
                        index = tagTokenIndex;
                        continue;
                    }
                }

                string rawToken = ReadRawToken(normalizedKeyword, ref index);
                if (!string.IsNullOrWhiteSpace(rawToken))
                {
                    remainingTokens.Add(rawToken);
                }
            }

            return new ParsedSearchKeyword(
                TagTextParser.SplitDistinct(parsedTags, StringComparer.CurrentCultureIgnoreCase),
                string.Join(" ", remainingTokens)
            );
        }

        private sealed record ParsedSearchKeyword(string[] ActiveTags, string RemainingQuery)
        {
            public static ParsedSearchKeyword Empty { get; } = new([], "");
        }
    }
}
