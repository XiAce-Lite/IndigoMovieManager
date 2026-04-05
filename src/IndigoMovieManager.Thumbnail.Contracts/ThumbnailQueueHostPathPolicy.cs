using System.IO;

namespace IndigoMovieManager
{
    // Queue / FailureDb / 補助ログの保存先は host から注入し、Queue 本体は path policy を固定しない。
    public static class ThumbnailQueueHostPathPolicy
    {
        private const string FallbackRootFolderName = "thumbnail-runtime";
        private const string QueueDbFolderName = "QueueDb";
        private const string FailureDbFolderName = "FailureDb";
        private const string LogsFolderName = "logs";
        private static readonly object SyncRoot = new();
        private static string configuredQueueDbDirectoryPath = "";
        private static string configuredFailureDbDirectoryPath = "";
        private static string configuredLogDirectoryPath = "";

        public static void Configure(
            string queueDbDirectoryPath = null,
            string failureDbDirectoryPath = null,
            string logDirectoryPath = null
        )
        {
            lock (SyncRoot)
            {
                if (queueDbDirectoryPath != null)
                {
                    configuredQueueDbDirectoryPath = NormalizeDirectoryPath(
                        queueDbDirectoryPath
                    );
                }

                if (failureDbDirectoryPath != null)
                {
                    configuredFailureDbDirectoryPath = NormalizeDirectoryPath(
                        failureDbDirectoryPath
                    );
                }

                if (logDirectoryPath != null)
                {
                    configuredLogDirectoryPath = NormalizeDirectoryPath(logDirectoryPath);
                }
            }
        }

        public static string ResolveQueueDbDirectoryPath()
        {
            lock (SyncRoot)
            {
                return !string.IsNullOrWhiteSpace(configuredQueueDbDirectoryPath)
                    ? configuredQueueDbDirectoryPath
                    : BuildFallbackDirectoryPath(QueueDbFolderName);
            }
        }

        public static string ResolveFailureDbDirectoryPath()
        {
            lock (SyncRoot)
            {
                return !string.IsNullOrWhiteSpace(configuredFailureDbDirectoryPath)
                    ? configuredFailureDbDirectoryPath
                    : BuildFallbackDirectoryPath(FailureDbFolderName);
            }
        }

        public static string ResolveLogDirectoryPath()
        {
            lock (SyncRoot)
            {
                return !string.IsNullOrWhiteSpace(configuredLogDirectoryPath)
                    ? configuredLogDirectoryPath
                    : BuildFallbackDirectoryPath(LogsFolderName);
            }
        }

        // 空文字を受けた時は host 指定を解除し、fallback へ戻せるようにする。
        private static string NormalizeDirectoryPath(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return "";
            }

            string trimmed = directoryPath.Trim();
            if (
                trimmed.Length >= 2
                && trimmed.StartsWith('"')
                && trimmed.EndsWith('"')
            )
            {
                trimmed = trimmed[1..^1].Trim();
            }

            try
            {
                return Path.GetFullPath(trimmed, AppContext.BaseDirectory);
            }
            catch
            {
                return trimmed;
            }
        }

        private static string BuildFallbackDirectoryPath(string leafFolderName)
        {
            return Path.Combine(AppContext.BaseDirectory, FallbackRootFolderName, leafFolderName);
        }
    }
}
