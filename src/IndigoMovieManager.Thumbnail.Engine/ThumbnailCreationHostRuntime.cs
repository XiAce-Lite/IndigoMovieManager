using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    // app 側の配置規約を service 本体へ直書きしないための host 境界。
    public interface IThumbnailCreationHostRuntime
    {
        string ResolveMissingMoviePlaceholderPath(int tabIndex);
        string ResolveProcessLogPath(string fileName);
    }

    // host 未注入の後方互換だけは engine 側で吸収し、app 固有の既定実装は持ち込まない。
    internal sealed class FallbackThumbnailCreationHostRuntime : IThumbnailCreationHostRuntime
    {
        internal static FallbackThumbnailCreationHostRuntime Instance { get; } = new();

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
            string baseDir = string.IsNullOrWhiteSpace(AppContext.BaseDirectory)
                ? Directory.GetCurrentDirectory()
                : AppContext.BaseDirectory;
            return Path.Combine(baseDir, "logs", fileName ?? "");
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
