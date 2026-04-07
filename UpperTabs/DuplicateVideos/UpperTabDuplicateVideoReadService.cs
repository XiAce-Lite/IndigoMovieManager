using System;
using System.Collections.Generic;
using System.Data.SQLite;
using IndigoMovieManager.DB;

namespace IndigoMovieManager.UpperTabs.DuplicateVideos
{
    internal interface IUpperTabDuplicateVideoReadService
    {
        UpperTabDuplicateMovieRecord[] ReadDuplicateMovieRecords(string dbFullPath);
    }

    internal sealed class UpperTabDuplicateVideoReadService : IUpperTabDuplicateVideoReadService
    {
        // 検出時だけDBを直読みし、同一hashが2件以上ある行だけを返す。
        public UpperTabDuplicateMovieRecord[] ReadDuplicateMovieRecords(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return [];
            }

            List<UpperTabDuplicateMovieRecord> result = [];
            using SQLiteConnection connection = SQLite.CreateReadOnlyConnection(dbFullPath);
            connection.Open();

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    movie_id,
    movie_name,
    movie_path,
    movie_size,
    file_date,
    movie_length,
    score,
    hash
FROM movie
WHERE COALESCE(hash, '') <> ''
  AND hash IN (
      SELECT hash
      FROM movie
      WHERE COALESCE(hash, '') <> ''
      GROUP BY hash
      HAVING COUNT(*) >= 2
  )
ORDER BY hash, movie_size DESC, file_date DESC, movie_id DESC;";

            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(
                    new UpperTabDuplicateMovieRecord(
                        MovieId: ReadInt64(reader, "movie_id"),
                        MovieName: ReadString(reader, "movie_name"),
                        MoviePath: ReadString(reader, "movie_path"),
                        MovieSize: ReadInt64(reader, "movie_size"),
                        FileDateText: ReadDateTimeText(reader, "file_date"),
                        MovieLengthSeconds: ReadInt64(reader, "movie_length"),
                        Score: ReadInt64(reader, "score"),
                        Hash: ReadString(reader, "hash")
                    )
                );
            }

            return [.. result];
        }

        private static string ReadString(SQLiteDataReader reader, string columnName)
        {
            object value = reader[columnName];
            return value == DBNull.Value ? "" : value?.ToString() ?? "";
        }

        private static long ReadInt64(SQLiteDataReader reader, string columnName)
        {
            object value = reader[columnName];
            if (value == DBNull.Value || value == null)
            {
                return 0;
            }

            return Convert.ToInt64(value);
        }

        private static string ReadDateTimeText(SQLiteDataReader reader, string columnName)
        {
            object value = reader[columnName];
            if (value == DBNull.Value || value == null)
            {
                return "";
            }

            if (value is DateTime dateTime)
            {
                return SQLite.FormatDbDateTime(dateTime);
            }

            return SQLite.TryParseDbDateTimeText(value.ToString() ?? "", out DateTime parsed)
                ? SQLite.FormatDbDateTime(parsed)
                : value.ToString() ?? "";
        }
    }

    internal readonly record struct UpperTabDuplicateMovieRecord(
        long MovieId,
        string MovieName,
        string MoviePath,
        long MovieSize,
        string FileDateText,
        long MovieLengthSeconds,
        long Score,
        string Hash
    );
}
