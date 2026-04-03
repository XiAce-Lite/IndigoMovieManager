using System.IO;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Thumbnail.QueueDb
{
    // メインDBパスを基準に、キューDBの保存先と比較用キーを一元化する。
    public static class QueueDbPathResolver
    {
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
            string hash8 = ThumbnailPathKeyHelper.GetMainDbPathHash8(safeMainDbPath);

            string baseDir = ThumbnailQueueHostPathPolicy.ResolveQueueDbDirectoryPath();
            Directory.CreateDirectory(baseDir);

            return Path.Combine(baseDir, $"{normalizedDbName}.{hash8}.queue.imm");
        }

        // MainDbPathHash8を仕様どおり「正規化+小文字化+SHA-256先頭8文字」で作る。
        public static string GetMainDbPathHash8(string mainDbFullPath)
        {
            return ThumbnailPathKeyHelper.GetMainDbPathHash8(mainDbFullPath);
        }

        // MoviePathKeyを仕様どおり「正規化+小文字化」で作る。
        public static string CreateMoviePathKey(string moviePath)
        {
            return ThumbnailPathKeyHelper.CreateMoviePathKey(moviePath);
        }

        // 比較用キーの正規化ルールを共通化し、表記ゆれを減らす。
        public static string NormalizePathForCompare(string rawPath)
        {
            return ThumbnailPathKeyHelper.NormalizePathForCompare(rawPath);
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
