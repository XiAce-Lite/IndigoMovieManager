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

            // タグ専用構文は通常検索へ混ぜず、先にここで受け止める。
            if (TryFilterTagQuery(query, searchText, out IEnumerable<MovieRecords> tagResult))
            {
                return tagResult;
            }

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
            var orGroups = searchText.Split([" | "], StringSplitOptions.RemoveEmptyEntries);
            return query.Where(item =>
            {
                string[] fields = BuildSearchFields(item);

                return orGroups.Any(group =>
                {
                    var andTerms = group.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    return andTerms.All(term =>
                    {
                        if (term.StartsWith('-'))
                        {
                            var keyword = term[1..];
                            return fields.All(field =>
                                !field.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                            );
                        }

                        return fields.Any(field =>
                            field.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                        );
                    });
                });
            });
        }

        private static bool TryFilterTagQuery(
            IEnumerable<MovieRecords> query,
            string searchText,
            out IEnumerable<MovieRecords> result
        )
        {
            if (searchText.Equals("!notag", StringComparison.CurrentCultureIgnoreCase))
            {
                result = query.Where(item => !BuildNormalizedTags(item).Any());
                return true;
            }

            if (!searchText.StartsWith("!tag:", StringComparison.CurrentCultureIgnoreCase))
            {
                result = Enumerable.Empty<MovieRecords>();
                return false;
            }

            string tagKeyword = Unquote(searchText[5..].Trim());
            if (string.IsNullOrWhiteSpace(tagKeyword))
            {
                result = Enumerable.Empty<MovieRecords>();
                return true;
            }

            result = query.Where(item =>
                BuildNormalizedTags(item).Any(tag =>
                    tag.Equals(tagKeyword, StringComparison.CurrentCultureIgnoreCase)
                )
            );
            return true;
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
            if (string.IsNullOrWhiteSpace(item?.Tags))
            {
                return [];
            }

            return item
                .Tags.Split(
                    ["\r\n", "\n", "\r"],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        private static string Unquote(string text)
        {
            if (text.Length < 2)
            {
                return text;
            }

            bool isDoubleQuoted = text.StartsWith('"') && text.EndsWith('"');
            bool isSingleQuoted = text.StartsWith('\'') && text.EndsWith('\'');
            return isDoubleQuoted || isSingleQuoted ? text[1..^1] : text;
        }
    }
}
