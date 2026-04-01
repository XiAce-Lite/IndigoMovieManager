using System.Text;

namespace IndigoMovieManager.Thumbnail.SQLite;

internal static class SQLiteConnectionStringPathHelper
{
    // System.Data.SQLite の公式コメントでは、UNC のような連続 "\" を
    // Data Source へ入れる際は連続部分を二重化するよう案内されている。
    // そのため実ファイルパス自体は変えず、接続文字列へ載せる瞬間だけ加工する。
    // 例:
    //[変換前] \\Network\Share\test.db
    //[変換後] \\\\Network\Share\test.db
    internal static string EscapeDataSourcePath(string dbPath)
    {
        if (string.IsNullOrEmpty(dbPath) || !dbPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return dbPath;
        }

        StringBuilder builder = new(dbPath.Length + 4);
        int index = 0;
        while (index < dbPath.Length)
        {
            if (dbPath[index] != '\\')
            {
                builder.Append(dbPath[index]);
                index++;
                continue;
            }

            int slashStart = index;
            while (index < dbPath.Length && dbPath[index] == '\\')
            {
                index++;
            }

            int slashCount = index - slashStart;
            if (slashCount > 1)
            {
                // UNC 先頭の "\\" や、将来もし連続 "\" を含む区間があっても
                // 接続文字列側では連続数を二倍にして渡す。
                builder.Append('\\', slashCount * 2);
            }
            else
            {
                // 単独 "\" は通常パス区切りとしてそのまま維持する。
                builder.Append('\\');
            }
        }

        return builder.ToString();
    }
}
