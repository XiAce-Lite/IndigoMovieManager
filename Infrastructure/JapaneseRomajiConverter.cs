using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IndigoMovieManager
{
    /// <summary>
    /// かな読みを検索しやすいローマ字へ寄せる小さな変換器。
    /// 厳密な表記統一より、WhiteBrowser 互換寄りの「引っかかりやすさ」を優先する。
    /// </summary>
    internal static class JapaneseRomajiConverter
    {
        private static readonly IReadOnlyDictionary<string, string> DigraphMap =
            new Dictionary<string, string>
            {
                ["きゃ"] = "kya",
                ["きゅ"] = "kyu",
                ["きょ"] = "kyo",
                ["しゃ"] = "sha",
                ["しゅ"] = "shu",
                ["しょ"] = "sho",
                ["ちゃ"] = "cha",
                ["ちゅ"] = "chu",
                ["ちょ"] = "cho",
                ["にゃ"] = "nya",
                ["にゅ"] = "nyu",
                ["にょ"] = "nyo",
                ["ひゃ"] = "hya",
                ["ひゅ"] = "hyu",
                ["ひょ"] = "hyo",
                ["みゃ"] = "mya",
                ["みゅ"] = "myu",
                ["みょ"] = "myo",
                ["りゃ"] = "rya",
                ["りゅ"] = "ryu",
                ["りょ"] = "ryo",
                ["ぎゃ"] = "gya",
                ["ぎゅ"] = "gyu",
                ["ぎょ"] = "gyo",
                ["じゃ"] = "ja",
                ["じゅ"] = "ju",
                ["じょ"] = "jo",
                ["びゃ"] = "bya",
                ["びゅ"] = "byu",
                ["びょ"] = "byo",
                ["ぴゃ"] = "pya",
                ["ぴゅ"] = "pyu",
                ["ぴょ"] = "pyo",
                ["ふぁ"] = "fa",
                ["ふぃ"] = "fi",
                ["ふぇ"] = "fe",
                ["ふぉ"] = "fo",
                ["てぃ"] = "ti",
                ["でぃ"] = "di",
                ["とぅ"] = "tu",
                ["どぅ"] = "du",
                ["うぃ"] = "wi",
                ["うぇ"] = "we",
                ["うぉ"] = "wo",
                ["ゔぁ"] = "va",
                ["ゔぃ"] = "vi",
                ["ゔ"] = "vu",
                ["ゔぇ"] = "ve",
                ["ゔぉ"] = "vo",
                ["しぇ"] = "she",
                ["じぇ"] = "je",
                ["ちぇ"] = "che",
                ["つぁ"] = "tsa",
                ["つぃ"] = "tsi",
                ["つぇ"] = "tse",
                ["つぉ"] = "tso",
                ["くぁ"] = "kwa",
                ["くぃ"] = "kwi",
                ["くぇ"] = "kwe",
                ["くぉ"] = "kwo",
                ["ぐぁ"] = "gwa",
                ["ぐぃ"] = "gwi",
                ["ぐぇ"] = "gwe",
                ["ぐぉ"] = "gwo",
            };

        private static readonly IReadOnlyDictionary<char, string> MonographMap =
            new Dictionary<char, string>
            {
                ['あ'] = "a",
                ['い'] = "i",
                ['う'] = "u",
                ['え'] = "e",
                ['お'] = "o",
                ['か'] = "ka",
                ['き'] = "ki",
                ['く'] = "ku",
                ['け'] = "ke",
                ['こ'] = "ko",
                ['さ'] = "sa",
                ['し'] = "shi",
                ['す'] = "su",
                ['せ'] = "se",
                ['そ'] = "so",
                ['た'] = "ta",
                ['ち'] = "chi",
                ['つ'] = "tsu",
                ['て'] = "te",
                ['と'] = "to",
                ['な'] = "na",
                ['に'] = "ni",
                ['ぬ'] = "nu",
                ['ね'] = "ne",
                ['の'] = "no",
                ['は'] = "ha",
                ['ひ'] = "hi",
                ['ふ'] = "fu",
                ['へ'] = "he",
                ['ほ'] = "ho",
                ['ま'] = "ma",
                ['み'] = "mi",
                ['む'] = "mu",
                ['め'] = "me",
                ['も'] = "mo",
                ['や'] = "ya",
                ['ゆ'] = "yu",
                ['よ'] = "yo",
                ['ら'] = "ra",
                ['り'] = "ri",
                ['る'] = "ru",
                ['れ'] = "re",
                ['ろ'] = "ro",
                ['わ'] = "wa",
                ['ゐ'] = "wi",
                ['ゑ'] = "we",
                ['を'] = "o",
                ['ん'] = "n",
                ['が'] = "ga",
                ['ぎ'] = "gi",
                ['ぐ'] = "gu",
                ['げ'] = "ge",
                ['ご'] = "go",
                ['ざ'] = "za",
                ['じ'] = "ji",
                ['ず'] = "zu",
                ['ぜ'] = "ze",
                ['ぞ'] = "zo",
                ['だ'] = "da",
                ['ぢ'] = "ji",
                ['づ'] = "zu",
                ['で'] = "de",
                ['ど'] = "do",
                ['ば'] = "ba",
                ['び'] = "bi",
                ['ぶ'] = "bu",
                ['べ'] = "be",
                ['ぼ'] = "bo",
                ['ぱ'] = "pa",
                ['ぴ'] = "pi",
                ['ぷ'] = "pu",
                ['ぺ'] = "pe",
                ['ぽ'] = "po",
                ['ぁ'] = "a",
                ['ぃ'] = "i",
                ['ぅ'] = "u",
                ['ぇ'] = "e",
                ['ぉ'] = "o",
                ['ゃ'] = "ya",
                ['ゅ'] = "yu",
                ['ょ'] = "yo",
                ['ゎ'] = "wa",
            };

        private static readonly (string Source, string Target)[] ImeAliasRules =
        [
            ("sha", "sya"),
            ("shu", "syu"),
            ("sho", "syo"),
            ("cha", "tya"),
            ("chu", "tyu"),
            ("cho", "tyo"),
            ("ja", "zya"),
            ("ju", "zyu"),
            ("jo", "zyo"),
            ("shi", "si"),
            ("chi", "ti"),
            ("tsu", "tu"),
            ("fu", "hu"),
            ("ji", "zi"),
        ];

        public static string BuildSearchableRomaji(string kana)
        {
            string primary = ConvertKanaToRomaji(kana);
            if (string.IsNullOrWhiteSpace(primary))
            {
                return "";
            }

            HashSet<string> variants = new(StringComparer.OrdinalIgnoreCase)
            {
                primary,
                SimplifyLongVowels(primary),
            };

            string imeAlias = ConvertToImeAlias(primary);
            if (!string.IsNullOrWhiteSpace(imeAlias))
            {
                variants.Add(imeAlias);
                variants.Add(SimplifyLongVowels(imeAlias));
            }

            return string.Join(' ', variants.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        public static string ConvertKanaToRomaji(string kana)
        {
            string normalizedKana = NormalizeKana(kana);
            if (string.IsNullOrWhiteSpace(normalizedKana))
            {
                return "";
            }

            StringBuilder builder = new(normalizedKana.Length * 2);

            for (int index = 0; index < normalizedKana.Length; index++)
            {
                char current = normalizedKana[index];

                if (current == 'っ')
                {
                    string nextRoma = ResolveRomajiSyllable(normalizedKana, index + 1, out _);
                    if (!string.IsNullOrWhiteSpace(nextRoma) && char.IsLetter(nextRoma[0]))
                    {
                        builder.Append(char.ToLowerInvariant(nextRoma[0]));
                    }

                    continue;
                }

                if (current == 'ー')
                {
                    AppendLongVowel(builder);
                    continue;
                }

                string roma = ResolveRomajiSyllable(normalizedKana, index, out int consumedLength);
                if (!string.IsNullOrWhiteSpace(roma))
                {
                    builder.Append(roma);
                    index += consumedLength - 1;
                    continue;
                }

                if (char.IsWhiteSpace(current))
                {
                    builder.Append(' ');
                    continue;
                }

                builder.Append(char.ToLowerInvariant(current));
            }

            return builder.ToString();
        }

        private static string NormalizeKana(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string normalized = value.Trim().Normalize(NormalizationForm.FormKC);
            StringBuilder builder = new(normalized.Length);

            foreach (char ch in normalized)
            {
                if (ch is >= '\u30A1' and <= '\u30F6')
                {
                    builder.Append((char)(ch - 0x60));
                    continue;
                }

                builder.Append(char.ToLowerInvariant(ch));
            }

            return builder.ToString();
        }

        private static string ResolveRomajiSyllable(string kana, int index, out int consumedLength)
        {
            consumedLength = 1;
            if (string.IsNullOrEmpty(kana) || index < 0 || index >= kana.Length)
            {
                return "";
            }

            if (index + 1 < kana.Length)
            {
                string digraph = kana.Substring(index, 2);
                if (DigraphMap.TryGetValue(digraph, out string romaDigraph))
                {
                    consumedLength = 2;
                    return romaDigraph;
                }
            }

            return MonographMap.TryGetValue(kana[index], out string romaMonograph)
                ? romaMonograph
                : "";
        }

        private static void AppendLongVowel(StringBuilder builder)
        {
            for (int index = builder.Length - 1; index >= 0; index--)
            {
                char ch = builder[index];
                if (IsVowel(ch))
                {
                    builder.Append(ch);
                    return;
                }

                if (!char.IsLetter(ch))
                {
                    return;
                }
            }
        }

        private static string SimplifyLongVowels(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            StringBuilder builder = new(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char current = char.ToLowerInvariant(value[index]);
                char previous = builder.Length > 0 ? builder[^1] : '\0';

                if (IsVowel(current) && current == previous)
                {
                    continue;
                }

                if (current == 'u' && previous == 'o')
                {
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private static string ConvertToImeAlias(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string converted = value;
            foreach ((string source, string target) in ImeAliasRules)
            {
                converted = converted.Replace(source, target, StringComparison.Ordinal);
            }

            return converted;
        }

        private static bool IsVowel(char ch)
        {
            return ch is 'a' or 'i' or 'u' or 'e' or 'o';
        }
    }
}