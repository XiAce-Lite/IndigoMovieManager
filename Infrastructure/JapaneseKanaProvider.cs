using System.IO;
using System.Text;
using Windows.Globalization;

namespace IndigoMovieManager
{
    /// <summary>
    /// 動画名から検索用かなを安定して作るための小さな入口。
    /// Windows標準APIを優先しつつ、失敗時も最低限の並び替え材料を返す。
    /// </summary>
    internal static class JapaneseKanaProvider
    {
        public static string GetKana(string movieName, string moviePath = "")
        {
            string source = ResolveSourceText(movieName, moviePath);
            if (string.IsNullOrWhiteSpace(source))
            {
                return "";
            }

            string analyzed = TryAnalyze(source);
            if (!string.IsNullOrWhiteSpace(analyzed))
            {
                return NormalizeKana(analyzed);
            }

            return NormalizeKana(source);
        }

        private static string ResolveSourceText(string movieName, string moviePath)
        {
            string preferred = (movieName ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                return preferred;
            }

            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return "";
            }

            return Path.GetFileNameWithoutExtension(moviePath.Trim()) ?? "";
        }

        private static string TryAnalyze(string source)
        {
            try
            {
                var words = JapanesePhoneticAnalyzer.GetWords(source, false);
                if (words == null || words.Count == 0)
                {
                    return "";
                }

                StringBuilder builder = new();
                foreach (var word in words)
                {
                    if (!string.IsNullOrWhiteSpace(word.YomiText))
                    {
                        builder.Append(word.YomiText);
                    }
                }

                return builder.ToString();
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "kana",
                    $"phonetic analyzer fallback: reason={ex.GetType().Name}"
                );
                return "";
            }
        }

        private static string NormalizeKana(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string trimmed = value.Trim().Normalize(NormalizationForm.FormKC);
            StringBuilder builder = new(trimmed.Length);
            foreach (char ch in trimmed)
            {
                if (ch is >= '\u3041' and <= '\u3096')
                {
                    builder.Append((char)(ch + 0x60));
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }
    }
}
