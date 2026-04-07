using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using IndigoMovieManager.DB;

namespace IndigoMovieManager.Infrastructure
{
    /// <summary>
    /// 検索履歴の読込と保存を UI から剥がし、DB 直呼びを 1 か所へ寄せる。
    /// </summary>
    public static class SearchHistoryService
    {
        public static History[] LoadLatestHistory(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return [];
            }

            const string sql =
                @"SELECT find_id, find_text, find_date
                    FROM (
                        SELECT *,
                               ROW_NUMBER() OVER (PARTITION BY find_text ORDER BY find_date DESC) AS rn
                        FROM history
                    )
                    WHERE rn = 1
                    ORDER BY find_date DESC";

            DataTable historyData = SQLite.GetData(dbFullPath, sql);
            if (historyData == null)
            {
                return [];
            }

            List<History> result = [];
            HashSet<string> seenTexts = new(StringComparer.CurrentCultureIgnoreCase);
            foreach (DataRow row in historyData.AsEnumerable())
            {
                string findText = row["find_text"]?.ToString() ?? "";
                if (!seenTexts.Add(findText))
                {
                    continue;
                }

                result.Add(
                    new History
                    {
                        Find_Id = row.Field<long>("find_id"),
                        Find_Text = findText,
                        Find_Date = SQLite.ReadDbDateTimeTextOrEmpty(row["find_date"]),
                    }
                );
            }

            return [.. result];
        }

        public static void RecordSearchUsage(string dbFullPath, string keyword)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            SQLite.InsertFindFactTable(dbFullPath, keyword);
            SQLite.InsertHistoryTable(dbFullPath, keyword);
        }

        public static void PersistSuccessfulSearch(string dbFullPath, string keyword, int searchCount)
        {
            if (
                string.IsNullOrWhiteSpace(dbFullPath)
                || string.IsNullOrWhiteSpace(keyword)
                || searchCount <= 0
            )
            {
                return;
            }

            SQLite.InsertHistoryTable(dbFullPath, keyword);
        }

        public static void DeleteHistoryEntry(string dbFullPath, long findId)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || findId <= 0)
            {
                return;
            }

            SQLite.DeleteHistoryTable(dbFullPath, findId);
        }
    }
}
