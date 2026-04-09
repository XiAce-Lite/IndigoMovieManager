using System;
using System.Collections.Generic;
using System.Linq;

namespace IndigoMovieManager.Infrastructure
{
    /// <summary>
    /// タグ文字列の改行揺れを吸収し、空要素を落とした配列へ整える。
    /// 呼び出し側ごとの重複判定ルールは comparer で選べるようにしている。
    /// </summary>
    internal static class TagTextParser
    {
        private static readonly string[] NewLineSeparators = ["\r\n", "\n", "\r"];

        public static string[] SplitNonEmpty(string tagText)
        {
            if (string.IsNullOrWhiteSpace(tagText))
            {
                return [];
            }

            return tagText
                .Split(
                    NewLineSeparators,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        public static string[] SplitDistinct(string tagText, IEqualityComparer<string> comparer)
        {
            return SplitNonEmpty(tagText).Distinct(comparer ?? StringComparer.CurrentCulture).ToArray();
        }

        public static string[] SplitDistinct(
            IEnumerable<string> tagItems,
            IEqualityComparer<string> comparer
        )
        {
            if (tagItems == null)
            {
                return [];
            }

            return tagItems
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(comparer ?? StringComparer.CurrentCulture)
                .ToArray();
        }
    }
}
