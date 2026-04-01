using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// MainDB パスから、WebView 側で使う安定識別子を作る純粋ロジック。
    /// </summary>
    public static class WhiteBrowserSkinDbIdentity
    {
        public static string Build(string dbFullPath)
        {
            string normalizedPath = NormalizeMainDbPath(dbFullPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return "";
            }

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public static string BuildRecordKey(string dbIdentity, long movieId)
        {
            return string.IsNullOrWhiteSpace(dbIdentity) ? "" : $"{dbIdentity}:{movieId}";
        }

        public static string NormalizeMainDbPath(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return "";
            }

            string normalized = dbFullPath.Trim().Trim('"');
            try
            {
                normalized = Path.GetFullPath(normalized);
            }
            catch
            {
                // 壊れた入力は元文字列比較へ落とし、例外で API 全体を止めない。
            }

            return normalized.Replace('/', '\\').ToLowerInvariant();
        }
    }
}
