using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;

namespace IndigoMovieManager
{
    /// <summary>
    /// 動画名から検索用かなを安定して作るための小さな入口。
    /// Windows標準APIを優先しつつ、失敗時も最低限の並び替え材料を返す。
    /// </summary>
    internal static class JapaneseKanaProvider
    {
        private static readonly object AnalyzerSync = new();
        private static bool _analyzerResolved;
        private static MethodInfo _getWordsMethod;

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
                MethodInfo getWordsMethod = EnsureGetWordsMethod();
                if (getWordsMethod == null)
                {
                    return "";
                }

                // Windows 標準かな解析が使える時だけ結果を拾う。
                object words = getWordsMethod.Invoke(null, new object[] { source, false });
                if (words is not IEnumerable enumerable)
                {
                    return "";
                }

                StringBuilder builder = new();
                foreach (object word in enumerable)
                {
                    string yomiText = TryGetYomiText(word);
                    if (!string.IsNullOrWhiteSpace(yomiText))
                    {
                        builder.Append(yomiText);
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

        private static MethodInfo EnsureGetWordsMethod()
        {
            if (_analyzerResolved)
            {
                return _getWordsMethod;
            }

            lock (AnalyzerSync)
            {
                if (_analyzerResolved)
                {
                    return _getWordsMethod;
                }

                try
                {
                    // compile-time 依存を避けるため、Windows Runtime 型は文字列から解決する。
                    Type analyzerType = TryResolveAnalyzerType();
                    if (analyzerType != null)
                    {
                        _getWordsMethod = analyzerType.GetMethod(
                            "GetWords",
                            BindingFlags.Public | BindingFlags.Static,
                            binder: null,
                            types: new[] { typeof(string), typeof(bool) },
                            modifiers: null
                        );
                    }
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "kana",
                        $"phonetic analyzer fallback: reason={ex.GetType().Name}"
                    );
                    _getWordsMethod = null;
                }
                finally
                {
                    _analyzerResolved = true;
                }
            }

            return _getWordsMethod;
        }

        private static Type TryResolveAnalyzerType()
        {
            return
                Type.GetType(
                    "Windows.Globalization.JapanesePhoneticAnalyzer, Windows, ContentType=WindowsRuntime",
                    throwOnError: false
                )
                ?? Type.GetType(
                    "Windows.Globalization.JapanesePhoneticAnalyzer, Windows",
                    throwOnError: false
                );
        }

        private static string TryGetYomiText(object word)
        {
            if (word == null)
            {
                return "";
            }

            PropertyInfo yomiTextProperty = word.GetType().GetProperty("YomiText");
            if (yomiTextProperty == null)
            {
                return "";
            }

            return yomiTextProperty.GetValue(word) as string ?? "";
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
