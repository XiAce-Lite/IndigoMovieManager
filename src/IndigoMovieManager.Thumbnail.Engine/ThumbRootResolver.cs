using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    // DB配置と明示設定から実運用のサムネ根を決める責務を TabInfo から切り出す。
    public static class ThumbRootResolver
    {
        public static string GetDefaultThumbRoot(string dbName)
        {
            return Path.Combine(System.AppContext.BaseDirectory, "Thumb", dbName ?? "");
        }

        public static string GetDefaultThumbRoot(string dbName, string baseDirectory)
        {
            string normalizedBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? System.AppContext.BaseDirectory
                : baseDirectory;
            return Path.Combine(normalizedBaseDirectory, "Thumb", dbName ?? "");
        }

        public static string ResolveRuntimeThumbRoot(
            string dbFullPath,
            string dbName,
            string thumbFolder = "",
            string defaultBaseDirectory = ""
        )
        {
            string normalizedThumbFolder = thumbFolder?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(normalizedThumbFolder))
            {
                return normalizedThumbFolder;
            }

            string resolvedDbName = string.IsNullOrWhiteSpace(dbName)
                ? Path.GetFileNameWithoutExtension(dbFullPath) ?? ""
                : dbName;
            string whiteBrowserCompatibleRoot = TryResolveWhiteBrowserCompatibleThumbRoot(
                dbFullPath,
                resolvedDbName
            );
            if (!string.IsNullOrWhiteSpace(whiteBrowserCompatibleRoot))
            {
                return whiteBrowserCompatibleRoot;
            }

            return GetDefaultThumbRoot(resolvedDbName, defaultBaseDirectory);
        }

        // WhiteBrowser.exe と同居するDBだけ従来互換の thum 配下を既定扱いにする。
        private static string TryResolveWhiteBrowserCompatibleThumbRoot(
            string dbFullPath,
            string dbName
        )
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || string.IsNullOrWhiteSpace(dbName))
            {
                return "";
            }

            string dbDirectory = Path.GetDirectoryName(dbFullPath) ?? "";
            if (string.IsNullOrWhiteSpace(dbDirectory))
            {
                return "";
            }

            string whiteBrowserExePath = Path.Combine(dbDirectory, "WhiteBrowser.exe");
            if (!File.Exists(whiteBrowserExePath))
            {
                return "";
            }

            return Path.Combine(dbDirectory, "thum", dbName);
        }
    }
}
