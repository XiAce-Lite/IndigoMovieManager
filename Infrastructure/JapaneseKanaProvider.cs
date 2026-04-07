using System.IO;
using System.Text;
using Windows.Globalization;

namespace IndigoMovieManager
{
    /// <summary>
    /// 動画名からかな読みを安定して作るための小さな入口。
    /// DB保存の正本はひらがなに寄せ、検索側だけ必要な別表記を足す。
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
                string normalizedAnalyzed = NormalizeKana(analyzed);
                if (CanPersistKanaText(normalizedAnalyzed))
                {
                    return normalizedAnalyzed;
                }
            }

            return NormalizeKana(source);
        }

        public static string GetKanaForPersistence(string movieName, string moviePath = "")
        {
            string source = ResolveSourceText(movieName, moviePath);
            if (string.IsNullOrWhiteSpace(source))
            {
                return "";
            }

            string analyzed = TryAnalyze(source);
            if (!string.IsNullOrWhiteSpace(analyzed))
            {
                string normalizedAnalyzed = NormalizeKana(analyzed);
                return CanPersistKanaText(normalizedAnalyzed) ? normalizedAnalyzed : "";
            }

            string normalizedSource = NormalizeKana(source);
            return CanPersistKanaText(normalizedSource) ? normalizedSource : "";
        }

        public static string GetRoma(string movieName, string moviePath = "")
        {
            return JapaneseRomajiConverter.BuildSearchableRomaji(
                GetKana(movieName, moviePath)
            );
        }

        public static string NormalizeToHiragana(string value)
        {
            return NormalizeKana(value);
        }

        public static string ConvertToKatakana(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string normalized = NormalizeKana(value);
            StringBuilder builder = new(normalized.Length);
            foreach (char ch in normalized)
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

        public static string GetRomaForPersistence(string movieName, string moviePath = "")
        {
            return GetRomaFromKanaForPersistence(GetKanaForPersistence(movieName, moviePath));
        }

        public static string GetRomaFromKana(string kana)
        {
            return JapaneseRomajiConverter.BuildSearchableRomaji(kana);
        }

        public static string GetRomaFromKanaForPersistence(string kana)
        {
            string normalizedKana = NormalizeKana(kana);
            if (string.IsNullOrWhiteSpace(normalizedKana) || !CanPersistKanaText(normalizedKana))
            {
                return "";
            }

            return JapaneseRomajiConverter.BuildSearchableRomaji(normalizedKana);
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
                return AnalyzeWithJapanesePhoneticAnalyzer(source);
            }
            catch (Exception firstException)
            {
                try
                {
                    return AnalyzeOnStaThread(source);
                }
                catch (Exception secondException)
                {
                    DebugRuntimeLog.Write(
                        "kana",
                        $"phonetic analyzer fallback: reason={firstException.GetType().Name}/{secondException.GetType().Name}"
                    );
                    return "";
                }
            }
        }

        private static string AnalyzeWithJapanesePhoneticAnalyzer(string source)
        {
            IReadOnlyList<JapanesePhoneme> words = JapanesePhoneticAnalyzer.GetWords(source, false);
            if (words == null || words.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new();
            foreach (JapanesePhoneme word in words)
            {
                string yomiText = word?.YomiText ?? "";
                if (!string.IsNullOrWhiteSpace(yomiText))
                {
                    builder.Append(yomiText);
                }
            }

            return builder.ToString();
        }

        private static string AnalyzeOnStaThread(string source)
        {
            string analyzed = "";
            Exception capturedException = null;
            Thread thread = new(() =>
            {
                try
                {
                    analyzed = AnalyzeWithJapanesePhoneticAnalyzer(source);
                }
                catch (Exception ex)
                {
                    capturedException = ex;
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (capturedException != null)
            {
                throw new InvalidOperationException("STA phonetic analyzer failed.", capturedException);
            }

            return analyzed ?? "";
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
                if (ch is >= '\u30A1' and <= '\u30F6')
                {
                    builder.Append((char)(ch - 0x60));
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private static bool CanPersistKanaText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (char ch in value)
            {
                if (char.IsWhiteSpace(ch) || char.IsDigit(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
                {
                    continue;
                }

                if (ch is >= '\u3040' and <= '\u309F')
                {
                    continue;
                }

                if (ch == 'ー')
                {
                    continue;
                }

                return false;
            }

            return true;
        }
    }
}
