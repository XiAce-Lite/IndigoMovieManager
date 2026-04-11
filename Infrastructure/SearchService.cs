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
                return query.Where(item =>
                    BuildSearchFields(item).Any(field =>
                        field.Contains(exact, StringComparison.CurrentCultureIgnoreCase)
                    )
                );
            }

            // 既存の特殊コマンドはそのまま service 側へ寄せる。
            if (searchText.StartsWith('{') && searchText.EndsWith('}'))
            {
                var inner = searchText[1..^1].Trim();

                if (inner.Equals("notag", StringComparison.CurrentCultureIgnoreCase))
                {
                    return query.Where(item => !BuildNormalizedTags(item).Any());
                }

                if (inner.Equals("dup", StringComparison.CurrentCultureIgnoreCase))
                {
                    var duplicateHashes = query
                        .GroupBy(item => item.Hash)
                        .Where(group => !string.IsNullOrEmpty(group.Key) && group.Count() > 1)
                        .Select(group => group.Key)
                        .ToHashSet();
                    return query.Where(item => duplicateHashes.Contains(item.Hash));
                }
            }

            // 通常検索は OR -> AND -> NOT の順で既存仕様を保つ。
            string[][] orGroups = SplitOrGroups(searchText);
            return query.Where(item =>
            {
                string[] fields = BuildSearchFields(item);

                return orGroups.Any(group =>
                {
                    return group.All(term =>
                    {
                        bool isNegativeTerm = term.StartsWith('-');
                        string normalizedTerm = isNegativeTerm ? term[1..] : term;
                        if (string.IsNullOrWhiteSpace(normalizedTerm))
                        {
                            return true;
                        }

                        if (TryGetQuotedPhrase(normalizedTerm, out string exactTerm))
                        {
                            return isNegativeTerm
                                ? fields.All(field =>
                                    !field.Contains(exactTerm, StringComparison.CurrentCultureIgnoreCase)
                                )
                                : fields.Any(field =>
                                    field.Contains(exactTerm, StringComparison.CurrentCultureIgnoreCase)
                                );
                        }

                        if (isNegativeTerm)
                        {
                            return fields.All(field =>
                                !field.Contains(
                                    normalizedTerm,
                                    StringComparison.CurrentCultureIgnoreCase
                                )
                            );
                        }

                        return fields.Any(field =>
                            field.Contains(normalizedTerm, StringComparison.CurrentCultureIgnoreCase)
                        );
                    });
                });
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
                return query.Where(item => !BuildNormalizedTags(item).Any());
            }

            string[] tagKeywords = TagSearchKeywordCodec.ExtractActiveTags(searchText);
            if (tagKeywords.Length == 0)
            {
                return query;
            }

            remainingSearchText = TagSearchKeywordCodec.ReplaceTagFilters(searchText, Array.Empty<string>());
            return query.Where(item =>
                tagKeywords.All(tagKeyword =>
                    BuildNormalizedTags(item).Any(tag =>
                        tag.Equals(tagKeyword, StringComparison.CurrentCultureIgnoreCase)
                    )
                )
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

        private static string[][] SplitOrGroups(string searchText)
        {
            string[] tokens = TagSearchKeywordCodec.TokenizeRemainingQuery(searchText);
            if (tokens.Length == 0)
            {
                return [];
            }

            List<string[]> groups = [];
            List<string> currentGroup = [];
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

                currentGroup.Add(token);
            }

            if (currentGroup.Count > 0)
            {
                groups.Add(currentGroup.ToArray());
            }

            return groups.Count == 0 ? [] : groups.ToArray();
        }

        private static string[] BuildSearchFields(MovieRecords item)
        {
            string kana = ResolveSearchKana(item);
            string katakanaKana = JapaneseKanaProvider.ConvertToKatakana(kana);
            string roma = ResolveSearchRoma(item, kana);

            return
            [
                item?.Movie_Name ?? "",
                item?.Movie_Path ?? "",
                item?.Tags ?? "",
                item?.Comment1 ?? "",
                item?.Comment2 ?? "",
                item?.Comment3 ?? "",
                kana,
                katakanaKana,
                roma,
            ];
        }

        private static string ResolveSearchKana(MovieRecords item)
        {
            if (!string.IsNullOrWhiteSpace(item?.Kana))
            {
                return JapaneseKanaProvider.NormalizeToHiragana(item.Kana);
            }

            if (item == null)
            {
                return "";
            }

            return JapaneseKanaProvider.GetKana(item.Movie_Name, item.Movie_Path);
        }

        private static string ResolveSearchRoma(MovieRecords item, string kana)
        {
            if (!string.IsNullOrWhiteSpace(item?.Roma))
            {
                return item.Roma;
            }

            if (!string.IsNullOrWhiteSpace(kana))
            {
                return JapaneseKanaProvider.GetRomaFromKana(kana);
            }

            if (item == null)
            {
                return "";
            }

            return JapaneseKanaProvider.GetRoma(item.Movie_Name, item.Movie_Path);
        }

        private static string[] BuildNormalizedTags(MovieRecords item)
        {
            return TagTextParser.SplitDistinct(item?.Tags, StringComparer.CurrentCultureIgnoreCase);
        }
    }
}
