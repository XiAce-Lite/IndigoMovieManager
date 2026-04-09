using System;
using System.Data;
using System.Linq;
using IndigoMovieManager.DB;

namespace IndigoMovieManager.BottomTabs.SavedSearch
{
    /// <summary>
    /// tagbar の読込だけを担当し、UI 側へ SQL や DataTable を漏らさない。
    /// </summary>
    public static class SavedSearchService
    {
        public static SavedSearchItem[] LoadItems(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return [];
            }

            const string sql =
                @"select item_id, parent_id, order_id, group_id, title, contents
                    from tagbar
                    order by parent_id, group_id, order_id, item_id";

            DataTable data = SQLite.GetData(dbFullPath, sql);
            if (data == null)
            {
                return [];
            }

            return data
                .AsEnumerable()
                .Select(row => new SavedSearchItem
                {
                    ItemId = ReadInt64(row, "item_id"),
                    ParentId = ReadInt64(row, "parent_id"),
                    OrderId = ReadInt64(row, "order_id"),
                    GroupId = ReadInt64(row, "group_id"),
                    Title = row["title"]?.ToString() ?? "",
                    Contents = row["contents"]?.ToString() ?? "",
                })
                .ToArray();
        }

        private static long ReadInt64(DataRow row, string columnName)
        {
            object value = row[columnName];
            return value switch
            {
                long longValue => longValue,
                int intValue => intValue,
                short shortValue => shortValue,
                byte byteValue => byteValue,
                _ => long.TryParse(value?.ToString(), out long parsed) ? parsed : 0,
            };
        }
    }
}
