using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【サムネイルのレイアウト設定定義】
    /// 画面のタブ（表示形式）ごとのサムネイルのサイズや分割数（列数・行数）を管理するクラスです。
    /// UI（MainWindow）から渡される `tabIndex` に応じて自動的に最適な縦横比・解像度が決定されます。
    /// </summary>
    public class TabInfo
    {
        private readonly int columns = 3;
        private readonly int rows = 1;
        private readonly int width = 120;
        private readonly int height = 90;
        private readonly int divCount = 0;
        private readonly string outPath = "";

        public int Columns => columns;
        public int Rows => rows;
        public int Width => width;
        public int Height => height;
        public int DivCount => divCount;
        public string OutPath => outPath;

        // 既定のサムネイル保存先は、実行中exeと同じディレクトリ配下のThumbへ固定する。
        public static string GetDefaultThumbRoot(string dbName)
        {
            return Path.Combine(System.AppContext.BaseDirectory, "Thumb", dbName ?? "");
        }

        // 呼び出し側が既定基準ディレクトリを持つ時は、その基準へ揃えてThumb配下を組み立てる。
        public static string GetDefaultThumbRoot(string dbName, string baseDirectory)
        {
            string normalizedBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? System.AppContext.BaseDirectory
                : baseDirectory;
            return Path.Combine(normalizedBaseDirectory, "Thumb", dbName ?? "");
        }

        // DBに明示保存先が無い時だけ、WhiteBrowser同居配置を優先して実運用のサムネ根を決める。
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

        // WhiteBrowser.exe と同じ場所にあるDBだけ、従来どおり dbDir\thum\<DB名> を既定扱いにする。
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

        public TabInfo(int tabIndex, string dbName, string thumbFolder = "")
        {
            switch (tabIndex)
            {
                case 0:
                    width = 120;
                    height = 90;
                    columns = 3;
                    rows = 1;
                    break;
                case 1:
                    width = 200;
                    height = 150;
                    columns = 3;
                    rows = 1;
                    break;
                case 2:
                    width = 160;
                    height = 120;
                    columns = 1;
                    rows = 1;
                    break;
                case 3:
                    width = 56;
                    height = 42;
                    columns = 5;
                    rows = 1;
                    break;
                case 4:
                    width = 120;
                    height = 90;
                    columns = 5;
                    rows = 2;
                    break;
                case 99:
                    width = 120;
                    height = 90;
                    columns = 1;
                    rows = 1;
                    break;
                default:
                    break;
            }
            divCount = columns * rows;
            if (thumbFolder == "")
            {
                outPath = Path.Combine(
                    GetDefaultThumbRoot(dbName),
                    $"{width}x{height}x{columns}x{rows}"
                );
            }
            else
            {
                outPath = Path.Combine(thumbFolder, $"{width}x{height}x{columns}x{rows}");
            }
        }
    }
}
