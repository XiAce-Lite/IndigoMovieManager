using System.Security.Cryptography;
using System.Text;

namespace IndigoMovieManager.Thumbnail
{
    // 固定ファイル名は維持したまま、0時境界で前日ログを退避する。
    public static class LogFileTimeWindowSeparator
    {
        private static readonly TimeSpan MutexWaitTimeout = TimeSpan.FromSeconds(1);

        public static string PrepareForWrite(string logPath)
        {
            return PrepareForWrite(logPath, DateTime.Now);
        }

        public static string PrepareForWrite(string logPath, long maxFileBytes)
        {
            return PrepareForWrite(logPath, DateTime.Now, maxFileBytes);
        }

        internal static string PrepareForWrite(string logPath, DateTime nowLocal)
        {
            return PrepareForWrite(logPath, nowLocal, maxFileBytes: 0);
        }

        internal static string PrepareForWrite(string logPath, DateTime nowLocal, long maxFileBytes)
        {
            if (string.IsNullOrWhiteSpace(logPath))
            {
                return logPath ?? "";
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(logPath);
            }
            catch
            {
                return logPath;
            }

            RotateIfNeeded(fullPath, nowLocal, maxFileBytes);
            return fullPath;
        }

        internal static string BuildWindowLabel(DateTime timestampLocal)
        {
            return timestampLocal.ToString("yyyyMMdd");
        }

        internal static string BuildArchivePath(string fullPath, DateTime previousWriteTimeLocal)
        {
            string directoryPath = Path.GetDirectoryName(fullPath) ?? "";
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);
            string extension = Path.GetExtension(fullPath);
            string label = BuildWindowLabel(previousWriteTimeLocal);
            string baseArchivePath = Path.Combine(
                directoryPath,
                $"{fileNameWithoutExtension}_{label}{extension}"
            );

            if (!File.Exists(baseArchivePath))
            {
                return baseArchivePath;
            }

            for (int index = 2; index < 1000; index++)
            {
                string candidatePath = Path.Combine(
                    directoryPath,
                    $"{fileNameWithoutExtension}_{label}_{index:00}{extension}"
                );
                if (!File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return Path.Combine(
                directoryPath,
                $"{fileNameWithoutExtension}_{label}_{Guid.NewGuid():N}{extension}"
            );
        }

        private static void RotateIfNeeded(string fullPath, DateTime nowLocal, long maxFileBytes)
        {
            string directoryPath = Path.GetDirectoryName(fullPath) ?? "";
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(directoryPath);
            }
            catch
            {
                return;
            }

            using Mutex mutex = new(false, BuildMutexName(fullPath));
            bool hasLock = false;

            try
            {
                try
                {
                    hasLock = mutex.WaitOne(MutexWaitTimeout);
                }
                catch (AbandonedMutexException)
                {
                    hasLock = true;
                }

                if (!hasLock || !File.Exists(fullPath))
                {
                    return;
                }

                FileInfo fileInfo = new(fullPath);
                if (fileInfo.Length <= 0)
                {
                    File.Delete(fullPath);
                    return;
                }

                DateTime previousWriteTimeLocal = fileInfo.LastWriteTime;
                bool shouldRotateByWindow =
                    BuildWindowLabel(previousWriteTimeLocal) != BuildWindowLabel(nowLocal);
                bool shouldRotateBySize = maxFileBytes > 0 && fileInfo.Length >= maxFileBytes;
                if (!shouldRotateByWindow && !shouldRotateBySize)
                {
                    return;
                }

                string archivePath = BuildArchivePath(fullPath, previousWriteTimeLocal);
                File.Move(fullPath, archivePath);
            }
            catch
            {
                // 分離に失敗しても、本体ログ書き込みは継続する。
            }
            finally
            {
                if (hasLock)
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private static string BuildMutexName(string fullPath)
        {
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()));
            return $"Local\\IndigoMovieManager_LogWindow_{Convert.ToHexString(hashBytes)}";
        }
    }
}
