using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    // app / worker が使う既定 host runtime は runtime project 側へ置き、engine から切り離す。
    public sealed class DefaultThumbnailCreationHostRuntime : IThumbnailCreationHostRuntime
    {
        public static DefaultThumbnailCreationHostRuntime Instance { get; } = new();
        private readonly string processLogDirectoryPath;

        public DefaultThumbnailCreationHostRuntime()
            : this(null) { }

        public DefaultThumbnailCreationHostRuntime(string processLogDirectoryPath)
        {
            this.processLogDirectoryPath = processLogDirectoryPath?.Trim() ?? "";
        }

        public string ResolveMissingMoviePlaceholderPath(int tabIndex)
        {
            string[] fileNames = tabIndex switch
            {
                1 => ["noFileBig.jpg"],
                2 => ["noFileGrid.jpg"],
                3 => ["noFileList.jpg", "nofileList.jpg"],
                4 => ["noFileBig.jpg"],
                99 => ["noFileGrid.jpg"],
                _ => ["noFileSmall.jpg"],
            };

            return ResolveBundledImagePath(fileNames);
        }

        public string ResolveProcessLogPath(string fileName)
        {
            return Path.Combine(ResolveProcessLogDirectoryPath(), fileName ?? "");
        }

        private string ResolveProcessLogDirectoryPath()
        {
            if (!string.IsNullOrWhiteSpace(processLogDirectoryPath))
            {
                return processLogDirectoryPath;
            }

            string baseDir = string.IsNullOrWhiteSpace(AppContext.BaseDirectory)
                ? Directory.GetCurrentDirectory()
                : AppContext.BaseDirectory;
            return Path.Combine(baseDir, "logs");
        }

        private static string ResolveBundledImagePath(string[] fileNames)
        {
            string[] baseDirs = [AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
            for (int i = 0; i < baseDirs.Length; i++)
            {
                string baseDir = baseDirs[i];
                if (string.IsNullOrWhiteSpace(baseDir))
                {
                    continue;
                }

                string imagesDir = Path.Combine(baseDir, "Images");
                for (int j = 0; j < fileNames.Length; j++)
                {
                    string candidate = Path.Combine(imagesDir, fileNames[j]);
                    if (Path.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            string fallbackBaseDir = string.IsNullOrWhiteSpace(AppContext.BaseDirectory)
                ? Directory.GetCurrentDirectory()
                : AppContext.BaseDirectory;
            return Path.Combine(fallbackBaseDir, "Images", fileNames[0]);
        }
    }
}
