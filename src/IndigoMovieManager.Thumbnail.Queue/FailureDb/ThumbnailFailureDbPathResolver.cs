using System.IO;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Thumbnail.FailureDb
{
    // MainDBごとにFailureDbの保存先と比較キーを決める。
    public static class ThumbnailFailureDbPathResolver
    {
        public static string ResolveFailureDbPath(string mainDbFullPath)
        {
            string safeMainDbPath = mainDbFullPath ?? "";
            string dbName = Path.GetFileNameWithoutExtension(safeMainDbPath);
            if (string.IsNullOrWhiteSpace(dbName))
            {
                dbName = "main";
            }

            string normalizedDbName = SanitizeFileName(dbName);
            string hash8 = QueueDbPathResolver.GetMainDbPathHash8(safeMainDbPath);
            string baseDir = ThumbnailQueueHostPathPolicy.ResolveFailureDbDirectoryPath();
            Directory.CreateDirectory(baseDir);

            return Path.Combine(baseDir, $"{normalizedDbName}.{hash8}.failure.imm");
        }

        public static string CreateMoviePathKey(string moviePath)
        {
            return QueueDbPathResolver.CreateMoviePathKey(moviePath);
        }

        private static string SanitizeFileName(string fileName)
        {
            string result = fileName ?? "";
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalidChar, '_');
            }

            return result;
        }
    }
}
