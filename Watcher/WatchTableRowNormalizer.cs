using System.Data;
using System.IO;

namespace IndigoMovieManager
{
    internal static class WatchTableRowNormalizer
    {
        // 古い .wb や手動追加の不完全行でも、実運用向けの既定値へ寄せて取りこぼしを防ぐ。
        public static void Normalize(DataTable watchTable)
        {
            if (watchTable == null || watchTable.Rows.Count < 1)
            {
                return;
            }

            foreach (DataRow row in watchTable.Rows)
            {
                NormalizeRow(row);
            }
        }

        // UI追加時の既定が auto/watch/sub=true なので、既存DBの弱い行も同じ方向へ救済する。
        private static void NormalizeRow(DataRow row)
        {
            if (row == null)
            {
                return;
            }

            string directoryPath = row["dir"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(directoryPath) || !Path.Exists(directoryPath))
            {
                return;
            }

            bool auto = ReadFlag(row, "auto");
            bool watch = ReadFlag(row, "watch");
            bool sub = ReadFlag(row, "sub");

            if (!auto && !watch && !sub)
            {
                row["auto"] = 1L;
                row["watch"] = 1L;
                row["sub"] = 1L;
                return;
            }

            if ((auto || watch) && !sub)
            {
                row["sub"] = 1L;
            }
        }

        private static bool ReadFlag(DataRow row, string columnName)
        {
            if (row.Table?.Columns.Contains(columnName) != true)
            {
                return false;
            }

            object value = row[columnName];
            if (value == null || value == DBNull.Value)
            {
                return false;
            }

            return value switch
            {
                long longValue => longValue == 1,
                int intValue => intValue == 1,
                short shortValue => shortValue == 1,
                byte byteValue => byteValue == 1,
                bool boolValue => boolValue,
                _ => value.ToString() == "1",
            };
        }
    }
}
