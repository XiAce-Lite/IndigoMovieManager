using System;
using System.Collections.Generic;
using System.Data.SQLite;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager.Startup
{
    /// <summary>
    /// 起動時の first-page / append-page 用に、必要最小限の列だけをDBから読む。
    /// DataTable を経由せず reader で抜くことで、一覧前段の無駄メモリを減らす。
    /// </summary>
    internal static class StartupDbPageReader
    {
        public static StartupDbPage ReadPage(StartupFeedRequest request, int pageIndex, string orderBySql)
        {
            int pageSize = pageIndex == 0 ? request.FirstPageSize : request.AppendPageSize;
            int offset = pageIndex == 0
                ? 0
                : request.FirstPageSize + ((pageIndex - 1) * request.AppendPageSize);

            string sql =
                $@"SELECT
                        movie_id,
                        movie_name,
                        movie_path,
                        movie_length,
                        movie_size,
                        last_date,
                        file_date,
                        regist_date,
                        score,
                        view_count,
                        hash,
                        container,
                        video,
                        audio,
                        kana,
                        tag,
                        comment1,
                        comment2,
                        comment3
                    FROM movie
                    ORDER BY {orderBySql}
                    LIMIT {pageSize + 1} OFFSET {offset}";

            List<StartupMovieListItemSource> items = new(pageSize + 1);
            using SQLiteConnection connection = CreateReadOnlyConnection(request.DbPath);
            connection.Open();

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(
                    new StartupMovieListItemSource(
                        MovieId: ReadInt64(reader, "movie_id"),
                        MovieName: ReadString(reader, "movie_name"),
                        MoviePath: ReadString(reader, "movie_path"),
                        MovieLengthSeconds: ReadInt64(reader, "movie_length"),
                        MovieSize: ReadInt64(reader, "movie_size"),
                        LastDate: ReadDateTime(reader, "last_date"),
                        FileDate: ReadDateTime(reader, "file_date"),
                        RegistDate: ReadDateTime(reader, "regist_date"),
                        Score: ReadInt64(reader, "score"),
                        ViewCount: ReadInt64(reader, "view_count"),
                        Hash: ReadString(reader, "hash"),
                        Container: ReadString(reader, "container"),
                        Video: ReadString(reader, "video"),
                        Audio: ReadString(reader, "audio"),
                        Kana: ReadString(reader, "kana"),
                        TagRaw: ReadString(reader, "tag"),
                        Comment1: ReadString(reader, "comment1"),
                        Comment2: ReadString(reader, "comment2"),
                        Comment3: ReadString(reader, "comment3")
                    )
                );
            }

            bool hasMore = items.Count > pageSize;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }

            int approximateTotalCount = offset + items.Count + (hasMore ? 1 : 0);
            return new StartupDbPage(items.ToArray(), approximateTotalCount, hasMore, pageIndex);
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

        private static DateTime ReadDateTime(SQLiteDataReader reader, string columnName)
        {
            object value = reader[columnName];
            if (value == DBNull.Value || value == null)
            {
                return DateTime.MinValue;
            }

            if (value is DateTime dateTime)
            {
                return dateTime;
            }

            return DateTime.TryParse(value.ToString(), out DateTime parsed)
                ? parsed
                : DateTime.MinValue;
        }
    }

    internal readonly record struct StartupDbPage(
        StartupMovieListItemSource[] Items,
        int ApproximateTotalCount,
        bool HasMore,
        int PageIndex
    );

    internal readonly record struct StartupMovieListItemSource(
        long MovieId,
        string MovieName,
        string MoviePath,
        long MovieLengthSeconds,
        long MovieSize,
        DateTime LastDate,
        DateTime FileDate,
        DateTime RegistDate,
        long Score,
        long ViewCount,
        string Hash,
        string Container,
        string Video,
        string Audio,
        string Kana,
        string TagRaw,
        string Comment1,
        string Comment2,
        string Comment3
    );
}
