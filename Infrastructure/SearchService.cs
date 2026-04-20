using System;
using System.Collections.Generic;
using System.Linq;

namespace IndigoMovieManager.Infrastructure
{
    /// <summary>
    /// 検索文字列の解釈と各フィールド比較を 1 か所へ寄せる。
    /// UI はここを呼ぶだけにして、検索仕様の正本を本体へ固定する。
    /// </summary>
    public static class SearchService
    {
        public static bool IsDuplicateSearchKeyword(string searchKeyword)
        {
            if (string.IsNullOrWhiteSpace(searchKeyword))
            {
                return false;
            }

            string searchText = searchKeyword.Trim();
            if (!(searchText.StartsWith('{') && searchText.EndsWith('}')))
            {
                return false;
            }

            string inner = searchText[1..^1].Trim();
            return inner.Equals("dup", StringComparison.CurrentCultureIgnoreCase);
        }

        public static bool IsTagOnlySearchKeyword(string searchKeyword)
        {
            if (string.IsNullOrWhiteSpace(searchKeyword))
            {
                return false;
            }

            string searchText = searchKeyword.Trim();
            if (searchText.Equals("!notag", StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }

            if (searchText.StartsWith('{') && searchText.EndsWith('}'))
            {
                string inner = searchText[1..^1].Trim();
                if (inner.Equals("notag", StringComparison.CurrentCultureIgnoreCase))
                {
                    return true;
                }
            }

            return TagSearchKeywordCodec.TryParsePureTagQuery(searchText, out _);
        }

        public static IEnumerable<MovieRecords> FilterMovies(
            IEnumerable<MovieRecords> source,
            string searchKeyword
        )
        {
            var query = source ?? Enumerable.Empty<MovieRecords>();
            if (string.IsNullOrWhiteSpace(searchKeyword))
            {
                return query;
            }

            var searchText = searchKeyword.Trim();

            // exact tag 構文は通常検索と共存できるよう、先にタグ条件だけ抜き出す。
            query = ApplyExactTagFilters(query, searchText, out string remainingSearchText);
            if (!ReferenceEquals(query, source) && string.IsNullOrWhiteSpace(remainingSearchText))
            {
                return query;
            }

            searchText = remainingSearchText;

            // 全体をクォートした時は、既存どおりフレーズ一致で扱う。
            if (TryGetQuotedPhrase(searchText, out string exact))
            {
                StringComparison comparison = ResolveSearchComparison(exact);
                return query.Where(item =>
                    ContainsInAnyField(item.GetSearchFieldsForFilter(), exact, comparison)
                );
            }

            // 既存の特殊コマンドはそのまま service 側へ寄せる。
            if (searchText.StartsWith('{') && searchText.EndsWith('}'))
            {
                var inner = searchText[1..^1].Trim();

                if (inner.Equals("notag", StringComparison.CurrentCultureIgnoreCase))
                {
                    return query.Where(item => item.GetNormalizedTagsForFilter().Length == 0);
                }

                if (inner.Equals("dup", StringComparison.CurrentCultureIgnoreCase))
                {
                    return FilterDuplicateMovies(query);
                }
            }

            // 通常検索は OR -> AND -> NOT の順で既存仕様を保つ。
            SearchTerm[][] orGroups = CompileOrGroups(searchText);
            return query.Where(item =>
            {
                string[] fields = item.GetSearchFieldsForFilter();

                return MatchesAnyOrGroup(fields, orGroups);
            });
        }

        private static IEnumerable<MovieRecords> ApplyExactTagFilters(
            IEnumerable<MovieRecords> query,
            string searchText,
            out string remainingSearchText
        )
        {
            remainingSearchText = searchText ?? "";

            if (searchText.Equals("!notag", StringComparison.CurrentCultureIgnoreCase))
            {
                remainingSearchText = "";
                return query.Where(item => item.GetNormalizedTagsForFilter().Length == 0);
            }

            string[] tagKeywords = TagSearchKeywordCodec.ExtractActiveTags(searchText);
            if (tagKeywords.Length == 0)
            {
                return query;
            }

            remainingSearchText = TagSearchKeywordCodec.ReplaceTagFilters(searchText, Array.Empty<string>());
            return query.Where(item =>
                HasAllExactTags(item.GetNormalizedTagsForFilter(), tagKeywords)
            );
        }

        private static bool TryGetQuotedPhrase(string searchText, out string exact)
        {
            exact = string.Empty;
            if (searchText.Length < 2)
            {
                return false;
            }

            bool isDoubleQuoted = searchText.StartsWith('"') && searchText.EndsWith('"');
            bool isSingleQuoted = searchText.StartsWith('\'') && searchText.EndsWith('\'');
            if (!isDoubleQuoted && !isSingleQuoted)
            {
                return false;
            }

            exact = searchText[1..^1];
            return true;
        }

        private static SearchTerm[][] CompileOrGroups(string searchText)
        {
            string[] tokens = TagSearchKeywordCodec.TokenizeRemainingQuery(searchText);
            if (tokens.Length == 0)
            {
                return [];
            }

            List<SearchTerm[]> groups = [];
            List<SearchTerm> currentGroup = [];
            foreach (string token in tokens)
            {
                if (token == "|")
                {
                    if (currentGroup.Count > 0)
                    {
                        groups.Add(currentGroup.ToArray());
                        currentGroup.Clear();
                    }

                    continue;
                }

                currentGroup.Add(CompileTerm(token));
            }

            if (currentGroup.Count > 0)
            {
                groups.Add(currentGroup.ToArray());
            }

            return groups.Count == 0 ? [] : groups.ToArray();
        }

        private static SearchTerm CompileTerm(string token)
        {
            bool isNegative = token.StartsWith('-');
            string normalizedToken = isNegative ? token[1..] : token;
            bool isQuoted = TryGetQuotedPhrase(normalizedToken, out string exactTerm);

            return new SearchTerm(
                isQuoted ? exactTerm : normalizedToken,
                isNegative,
                isQuoted,
                ResolveSearchComparison(isQuoted ? exactTerm : normalizedToken)
            );
        }

        private static bool MatchesAnyOrGroup(string[] fields, SearchTerm[][] orGroups)
        {
            foreach (SearchTerm[] group in orGroups)
            {
                if (MatchesAllTerms(fields, group))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAllTerms(string[] fields, SearchTerm[] group)
        {
            foreach (SearchTerm term in group)
            {
                if (string.IsNullOrWhiteSpace(term.Text))
                {
                    continue;
                }

                bool isMatched = term.IsNegative
                    ? ContainsInNoField(fields, term.Text, term.Comparison)
                    : ContainsInAnyField(fields, term.Text, term.Comparison);
                if (!isMatched)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsInAnyField(
            string[] fields,
            string text,
            StringComparison comparison
        )
        {
            foreach (string field in fields)
            {
                if (field.Contains(text, comparison))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsInNoField(
            string[] fields,
            string text,
            StringComparison comparison
        )
        {
            foreach (string field in fields)
            {
                if (field.Contains(text, comparison))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasAllExactTags(string[] movieTags, string[] requiredTags)
        {
            foreach (string requiredTag in requiredTags)
            {
                StringComparison comparison = ResolveSearchComparison(requiredTag);
                bool isMatched = false;
                foreach (string movieTag in movieTags)
                {
                    if (movieTag.Equals(requiredTag, comparison))
                    {
                        isMatched = true;
                        break;
                    }
                }

                if (!isMatched)
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<MovieRecords> FilterDuplicateMovies(IEnumerable<MovieRecords> query)
        {
            List<MovieRecords> materialized = query as List<MovieRecords> ?? query.ToList();
            Dictionary<string, int> hashCounts = [];

            foreach (MovieRecords item in materialized)
            {
                if (string.IsNullOrEmpty(item?.Hash))
                {
                    continue;
                }

                hashCounts.TryGetValue(item.Hash, out int currentCount);
                hashCounts[item.Hash] = currentCount + 1;
            }

            return materialized.Where(item =>
                !string.IsNullOrEmpty(item?.Hash)
                && hashCounts.TryGetValue(item.Hash, out int count)
                && count > 1
            );
        }

        // ASCII だけの検索語は culture 比較より ordinal ignore case の方が軽い。
        // 日本語などを含む語は従来どおり culture 比較を維持して互換性を守る。
        private static StringComparison ResolveSearchComparison(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return StringComparison.CurrentCultureIgnoreCase;
            }

            foreach (char c in text)
            {
                if (c > sbyte.MaxValue)
                {
                    return StringComparison.CurrentCultureIgnoreCase;
                }
            }

            return StringComparison.OrdinalIgnoreCase;
        }

        private readonly record struct SearchTerm(
            string Text,
            bool IsNegative,
            bool IsQuoted,
            StringComparison Comparison
        );
    }
}
