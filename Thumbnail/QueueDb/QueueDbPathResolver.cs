using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IndigoMovieManager.Thumbnail.QueueDb
{
    // メインDBパスを基準に、キューDBの保存先と比較用キーを一元化する。
    public static class QueueDbPathResolver
    {
        private const string QueueDbRootFolderName = "IndigoMovieManager_fork";
        private const string QueueDbFolderName = "QueueDb";

        // メインDBフルパスから、QueueDBの保存先パスを決定する。
        public static string ResolveQueueDbPath(string mainDbFullPath)
        {
            string safeMainDbPath = mainDbFullPath ?? "";
            string dbName = Path.GetFileNameWithoutExtension(safeMainDbPath);
            if (string.IsNullOrWhiteSpace(dbName))
            {
                dbName = "main";
            }

            string normalizedDbName = SanitizeFileName(dbName);
            string hash8 = GetMainDbPathHash8(safeMainDbPath);

            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                QueueDbRootFolderName,
                QueueDbFolderName);
            Directory.CreateDirectory(baseDir);

            return Path.Combine(baseDir, $"{normalizedDbName}.{hash8}.queue.db");
        }

        // MainDbPathHash8を仕様どおり「正規化+小文字化+SHA-256先頭8文字」で作る。
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

        // MoviePathKeyを仕様どおり「正規化+小文字化」で作る。
        public static string CreateMoviePathKey(string moviePath)
        {
            return NormalizePathForCompare(moviePath);
        }

        // 比較用キーの正規化ルールを共通化し、表記ゆれを減らす。
        public static string NormalizePathForCompare(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return "";
            }

            string normalized = rawPath.Trim();
            if (normalized.Length >= 2 &&
                normalized.StartsWith('"') &&
                normalized.EndsWith('"'))
            {
                normalized = normalized[1..^1].Trim();
            }

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

            normalized = normalized.Replace('/', '\\');
            return normalized.ToLowerInvariant();
        }

        // ファイル名として使えない文字は "_" へ置き換える。
        private static string SanitizeFileName(string fileName)
        {
            string result = fileName;
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalidChar, '_');
            }
            return result;
        }
    }
}
