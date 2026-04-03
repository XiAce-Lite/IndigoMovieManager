using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IndigoMovieManager.Thumbnail
{
    // Queue / FailureDb / worker で使う比較キー生成をここへ寄せ、正規化規約を1箇所に固定する。
    public static class ThumbnailPathKeyHelper
    {
        // MainDbPathHash8は「正規化+小文字化+SHA-256先頭8文字」で作る。
        public static string GetMainDbPathHash8(string mainDbFullPath)
        {
            string normalized = NormalizePathForCompare(mainDbFullPath);
            byte[] bytes = Encoding.UTF8.GetBytes(normalized);
            byte[] hashBytes = SHA256.HashData(bytes);

            string hex = Convert.ToHexString(hashBytes);
            if (hex.Length < 8)
            {
                return hex;
            }

            return hex[..8];
        }

        // MoviePathKeyは「正規化+小文字化」で作り、表記ゆれをここで吸収する。
        public static string CreateMoviePathKey(string moviePath)
        {
            return NormalizePathForCompare(moviePath);
        }

        // 比較用キーの正規化ルールを共通化し、Queue と FailureDb でズレないようにする。
        public static string NormalizePathForCompare(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return "";
            }

            string normalized = rawPath.Trim();
            if (
                normalized.Length >= 2
                && normalized.StartsWith('"')
                && normalized.EndsWith('"')
            )
            {
                normalized = normalized[1..^1].Trim();
            }

            // Win32拡張パス接頭辞(\\?\ / \\?\UNC\)は同一実体でも表記が揺れるため、
            // キー生成前に通常表記へ寄せて重複登録を防ぐ。
            normalized = RemoveWindowsExtendedPathPrefix(normalized);

            try
            {
                if (Path.IsPathFullyQualified(normalized))
                {
                    normalized = Path.GetFullPath(normalized);
                }
            }
            catch
            {
                // 不正文字が混じるケースは上位での失敗判定に任せ、ここでは文字列を保持する。
            }

            normalized = RemoveWindowsExtendedPathPrefix(normalized);
            normalized = normalized.Replace('/', '\\');
            return normalized.ToLowerInvariant();
        }

        // \\?\C:\... / \\?\UNC\server\share\... の揺れを通常表記へ戻す。
        private static string RemoveWindowsExtendedPathPrefix(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path ?? "";
            }

            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + path[@"\\?\UNC\".Length..];
            }

            if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            {
                return path[@"\\?\".Length..];
            }

            return path;
        }
    }
}
