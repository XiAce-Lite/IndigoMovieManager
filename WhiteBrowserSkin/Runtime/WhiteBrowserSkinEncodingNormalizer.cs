using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// WhiteBrowser 由来 HTML を UTF-8 前提へ正規化する純粋ロジック。
    /// WebView2 実体へ依存させず、文字化け検証をここへ閉じ込める。
    /// </summary>
    public static partial class WhiteBrowserSkinEncodingNormalizer
    {
        static WhiteBrowserSkinEncodingNormalizer()
        {
            // Shift_JIS を安全に読めるよう、コードページを明示的に有効化する。
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [GeneratedRegex(
            "<meta[^>]+charset\\s*=\\s*[\"']?(?<charset>[A-Za-z0-9_\\-]+)[\"']?[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        )]
        private static partial Regex CharsetMetaRegex();

        [GeneratedRegex(
            "<head(?<attrs>[^>]*)>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        )]
        private static partial Regex HeadOpenTagRegex();

        [GeneratedRegex(
            "<script(?<attrs>[^>]*?)src\\s*=\\s*[\"'](?<src>[^\"']+)[\"'](?<tail>[^>]*)></script>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        )]
        private static partial Regex ScriptSrcRegex();

        public static WhiteBrowserSkinEncodingNormalizationResult NormalizeFromFile(
            string htmlPath,
            string skinBaseUri
        )
        {
            byte[] bytes = File.ReadAllBytes(htmlPath);
            return Normalize(bytes, skinBaseUri);
        }

        public static WhiteBrowserSkinEncodingNormalizationResult Normalize(
            byte[] htmlBytes,
            string skinBaseUri
        )
        {
            byte[] sourceBytes = htmlBytes ?? [];
            Encoding sourceEncoding = DetectEncoding(sourceBytes);
            string html = sourceEncoding.GetString(sourceBytes);

            bool rewroteCharsetMeta = false;
            Match charsetMatch = CharsetMetaRegex().Match(html);
            if (charsetMatch.Success)
            {
                html = CharsetMetaRegex().Replace(
                    html,
                    "<meta charset=\"utf-8\">",
                    1
                );
                rewroteCharsetMeta = true;
            }

            bool rewroteCompatibilityScripts = false;
            html = ScriptSrcRegex().Replace(
                html,
                match =>
                {
                    string src = match.Groups["src"].Value;
                    if (src.EndsWith("prototype.js", StringComparison.OrdinalIgnoreCase))
                    {
                        rewroteCompatibilityScripts = true;
                        return "<script src=\"https://skin.local/Compat/prototype.js\"></script>";
                    }

                    if (src.EndsWith("wblib.js", StringComparison.OrdinalIgnoreCase))
                    {
                        rewroteCompatibilityScripts = true;
                        return "<script src=\"https://skin.local/Compat/wblib-compat.js\"></script>";
                    }

                    return match.Value;
                }
            );

            string baseTag = $"<base href=\"{skinBaseUri}\">";
            if (HeadOpenTagRegex().IsMatch(html))
            {
                html = HeadOpenTagRegex().Replace(html, match => $"{match.Value}{baseTag}", 1);
            }
            else
            {
                html = $"{baseTag}{html}";
            }

            return new WhiteBrowserSkinEncodingNormalizationResult(
                html,
                sourceEncoding.WebName,
                skinBaseUri,
                rewroteCharsetMeta,
                rewroteCompatibilityScripts
            );
        }

        // BOM -> meta charset -> Shift_JIS 既定 の順で判定し、WB 由来ファイルを優先救済する。
        private static Encoding DetectEncoding(byte[] sourceBytes)
        {
            if (sourceBytes.Length >= 3
                && sourceBytes[0] == 0xEF
                && sourceBytes[1] == 0xBB
                && sourceBytes[2] == 0xBF)
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }

            if (sourceBytes.Length >= 2)
            {
                if (sourceBytes[0] == 0xFF && sourceBytes[1] == 0xFE)
                {
                    return Encoding.Unicode;
                }

                if (sourceBytes[0] == 0xFE && sourceBytes[1] == 0xFF)
                {
                    return Encoding.BigEndianUnicode;
                }
            }

            string ascii = Encoding.ASCII.GetString(sourceBytes);
            Match charsetMatch = CharsetMetaRegex().Match(ascii);
            if (charsetMatch.Success)
            {
                string charset = charsetMatch.Groups["charset"].Value;
                try
                {
                    return Encoding.GetEncoding(charset);
                }
                catch
                {
                    // 壊れた charset 指定は WB 由来の既定へ倒す。
                }
            }

            return Encoding.GetEncoding(932);
        }
    }
}
