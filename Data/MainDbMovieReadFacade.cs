using System;
using System.Data;
using System.Data.SQLite;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager.Data
{
    internal interface IMainDbMovieReadFacade
    {
        int ReadRegisteredMovieCount(string dbFullPath);
        DataTable LoadSystemTable(string dbPath);
        DataTable LoadMovieTableForSort(string dbPath, string sortId);
        MainDbMovieReadPageResult ReadStartupPage(MainDbMovieReadRequest request, int pageIndex);
        bool TryReadRenameBridgeOwnerCounts(
            string dbFullPath,
            string excludedMoviePath,
            string oldMovieBody,
            string newMovieBody,
            string hash,
            out MainDbRenameBridgeOwnerCountsResult result
        );
        bool TryReadMovieByPath(
            string dbFullPath,
            string moviePath,
            out MainDbMovieReadItemResult result
        );
        bool TryReadMovieTag(string dbFullPath, long movieId, out string tag);
    }

    internal sealed class MainDbMovieReadFacade : IMainDbMovieReadFacade
    {
        private const string DefaultFallbackOrderBySql = "last_date desc, movie_id desc";
        private const string ErrorSortStartupSeedOrderBySql = "movie_id desc";
        private const int StartupPagePrefetchExtra = 1;

        public int ReadRegisteredMovieCount(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return 0;
            }

            using SQLiteConnection connection = CreateReadOnlyConnection(dbFullPath);
            connection.Open();

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "select count(*) from movie";
            object scalar = command.ExecuteScalar();
            return int.TryParse(scalar?.ToString(), out int count) ? count : 0;
        }

        public DataTable LoadSystemTable(string dbPath)
        {
            return GetData(dbPath, "SELECT * FROM system");
        }

        public DataTable LoadMovieTableForSort(string dbPath, string sortId)
        {
            string orderBySql = BuildMovieTableOrderBySql(sortId);
            string sql = string.IsNullOrWhiteSpace(orderBySql)
                ? "SELECT * FROM movie"
                : $"SELECT * FROM movie order by {orderBySql}";
            return GetData(dbPath, sql);
        }

        public MainDbMovieReadPageResult ReadStartupPage(
            MainDbMovieReadRequest request,
            int pageIndex
        )
        {
            int pageSize = pageIndex == 0 ? request.FirstPageSize : request.AppendPageSize;
            if (pageIndex < 0 || pageSize <= 0)
            {
                return new MainDbMovieReadPageResult(
                    Array.Empty<MainDbMovieReadItemResult>(),
                    0,
                    false,
                    pageIndex
                );
            }

            long offset = pageIndex == 0
                ? 0
                : (long)request.FirstPageSize + ((long)(pageIndex - 1) * request.AppendPageSize);
            int takeCount = pageSize + StartupPagePrefetchExtra;
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
                    ORDER BY {BuildStartupOrderBySql(request.SortId)}
                    LIMIT @takeCount OFFSET @offset";

            MainDbMovieReadItemResult[] rawItems = new MainDbMovieReadItemResult[takeCount];
            int rawCount = 0;
            using SQLiteConnection connection = CreateReadOnlyConnection(request.DbPath);
            connection.Open();

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@takeCount", takeCount);
            command.Parameters.AddWithValue("@offset", offset);

            using SQLiteDataReader reader = command.ExecuteReader();

            int movieIdOrdinal = reader.GetOrdinal("movie_id");
            int movieNameOrdinal = reader.GetOrdinal("movie_name");
            int moviePathOrdinal = reader.GetOrdinal("movie_path");
            int movieLengthOrdinal = reader.GetOrdinal("movie_length");
            int movieSizeOrdinal = reader.GetOrdinal("movie_size");
            int lastDateOrdinal = reader.GetOrdinal("last_date");
            int fileDateOrdinal = reader.GetOrdinal("file_date");
            int registDateOrdinal = reader.GetOrdinal("regist_date");
            int scoreOrdinal = reader.GetOrdinal("score");
            int viewCountOrdinal = reader.GetOrdinal("view_count");
            int hashOrdinal = reader.GetOrdinal("hash");
            int containerOrdinal = reader.GetOrdinal("container");
            int videoOrdinal = reader.GetOrdinal("video");
            int audioOrdinal = reader.GetOrdinal("audio");
            int kanaOrdinal = reader.GetOrdinal("kana");
            int tagOrdinal = reader.GetOrdinal("tag");
            int comment1Ordinal = reader.GetOrdinal("comment1");
            int comment2Ordinal = reader.GetOrdinal("comment2");
            int comment3Ordinal = reader.GetOrdinal("comment3");

            while (reader.Read())
            {
                if (rawCount >= rawItems.Length)
                {
                    break;
                }

                rawItems[rawCount] =
                    new MainDbMovieReadItemResult(
                        MovieId: ReadInt64(reader, movieIdOrdinal),
                        MovieName: ReadString(reader, movieNameOrdinal),
                        MoviePath: ReadString(reader, moviePathOrdinal),
                        MovieLengthSeconds: ReadInt64(reader, movieLengthOrdinal),
                        MovieSize: ReadInt64(reader, movieSizeOrdinal),
                        LastDate: ReadDateTime(reader, lastDateOrdinal),
                        FileDate: ReadDateTime(reader, fileDateOrdinal),
                        RegistDate: ReadDateTime(reader, registDateOrdinal),
                        Score: ReadInt64(reader, scoreOrdinal),
                        ViewCount: ReadInt64(reader, viewCountOrdinal),
                        Hash: ReadString(reader, hashOrdinal),
                        Container: ReadString(reader, containerOrdinal),
                        Video: ReadString(reader, videoOrdinal),
                        Audio: ReadString(reader, audioOrdinal),
                        Kana: ReadString(reader, kanaOrdinal),
                        TagRaw: ReadString(reader, tagOrdinal),
                        Comment1: ReadString(reader, comment1Ordinal),
                        Comment2: ReadString(reader, comment2Ordinal),
                        Comment3: ReadString(reader, comment3Ordinal)
                    )
                ;
                rawCount++;
            }

            bool hasMore = rawCount > pageSize;
            int outputCount = hasMore ? pageSize : rawCount;

            MainDbMovieReadItemResult[] items = outputCount == 0
                ? Array.Empty<MainDbMovieReadItemResult>()
                : new MainDbMovieReadItemResult[outputCount];

            if (outputCount > 0)
            {
                Array.Copy(rawItems, items, outputCount);
            }

            long approximateTotal = offset + outputCount + (hasMore ? 1 : 0);
            if (approximateTotal > int.MaxValue)
            {
                approximateTotal = int.MaxValue;
            }

            int approximateTotalCount = (int)approximateTotal;
            return new MainDbMovieReadPageResult(
                items,
                approximateTotalCount,
                hasMore,
                pageIndex
            );
        }

        public bool TryReadRenameBridgeOwnerCounts(
            string dbFullPath,
            string excludedMoviePath,
            string oldMovieBody,
            string newMovieBody,
            string hash,
            out MainDbRenameBridgeOwnerCountsResult result
        )
        {
            result = default;
            if (
                string.IsNullOrWhiteSpace(dbFullPath)
                || string.IsNullOrWhiteSpace(excludedMoviePath)
            )
            {
                return false;
            }

            using SQLiteConnection connection = CreateReadOnlyConnection(dbFullPath);
            connection.Open();

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText =
                @"SELECT
                        SUM(CASE WHEN movie_name = @oldMovieBody COLLATE NOCASE THEN 1 ELSE 0 END),
                        SUM(CASE WHEN movie_name = @newMovieBody COLLATE NOCASE THEN 1 ELSE 0 END),
                        SUM(
                            CASE
                                WHEN movie_name = @oldMovieBody COLLATE NOCASE
                                    AND (@hasHash = 0 OR hash = @hash COLLATE NOCASE)
                                THEN 1
                                ELSE 0
                            END
                        ),
                        SUM(
                            CASE
                                WHEN movie_name = @newMovieBody COLLATE NOCASE
                                    AND (@hasHash = 0 OR hash = @hash COLLATE NOCASE)
                                THEN 1
                                ELSE 0
                            END
                        )
                    FROM movie
                    WHERE NOT (movie_path = @excludedMoviePath COLLATE NOCASE)";
            command.Parameters.AddWithValue("@oldMovieBody", oldMovieBody ?? "");
            command.Parameters.AddWithValue("@newMovieBody", newMovieBody ?? "");
            command.Parameters.AddWithValue("@hash", hash ?? "");
            command.Parameters.AddWithValue(
                "@hasHash",
                string.IsNullOrWhiteSpace(hash) ? 0 : 1
            );
            command.Parameters.AddWithValue("@excludedMoviePath", excludedMoviePath);

            using SQLiteDataReader reader = command.ExecuteReader(CommandBehavior.SingleRow);
            if (!reader.Read())
            {
                return false;
            }

            // hidden owner を 1 回の read-only 読みで拾い、partial snapshot の誤判定を防ぐ。
            result = new MainDbRenameBridgeOwnerCountsResult(
                OtherOldMovieBodyOwnerCount: ReadAggregateCount(reader, 0),
                OtherNewMovieBodyOwnerCount: ReadAggregateCount(reader, 1),
                OtherOldThumbnailOwnerCount: ReadAggregateCount(reader, 2),
                OtherNewThumbnailOwnerCount: ReadAggregateCount(reader, 3)
            );
            return true;
        }

        public bool TryReadMovieByPath(
            string dbFullPath,
            string moviePath,
            out MainDbMovieReadItemResult result
        )
        {
            result = default;
            if (string.IsNullOrWhiteSpace(dbFullPath) || string.IsNullOrWhiteSpace(moviePath))
            {
                return false;
            }

            using SQLiteConnection connection = CreateReadOnlyConnection(dbFullPath);
            connection.Open();

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText =
                @"SELECT
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
                    WHERE movie_path = @moviePath COLLATE NOCASE
                    LIMIT 1";
            command.Parameters.AddWithValue("@moviePath", moviePath);

            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return false;
            }

            result = ReadMovieItem(reader);
            return true;
        }

        public bool TryReadMovieTag(string dbFullPath, long movieId, out string tag)
        {
            tag = "";
            if (string.IsNullOrWhiteSpace(dbFullPath) || movieId <= 0)
            {
                return false;
            }

            using SQLiteConnection connection = CreateReadOnlyConnection(dbFullPath);
            connection.Open();

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "select tag from movie where movie_id = @movieId limit 1";
            command.Parameters.AddWithValue("@movieId", movieId);

            object scalar = command.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
            {
                return false;
            }

            tag = scalar.ToString() ?? "";
            return true;
        }

        // UI 側へ SQL を漏らさないため、sortId から許可済みの ORDER BY だけを組み立てる。
        private static string BuildMovieTableOrderBySql(string sortId)
        {
            return sortId switch
            {
                "0" => "last_date desc",
                "1" => "last_date",
                "2" => "file_date desc",
                "3" => "file_date",
                "6" => "score desc",
                "7" => "score",
                "8" => "view_count desc",
                "9" => "view_count",
                "10" => "kana",
                "11" => "kana desc",
                "12" => "movie_name",
                "13" => "movie_name desc",
                "14" => "movie_path",
                "15" => "movie_path desc",
                "16" => "movie_size desc",
                "17" => "movie_size",
                "18" => "regist_date desc",
                "19" => "regist_date",
                "20" => "movie_length desc",
                "21" => "movie_length",
                "22" => "comment1",
                "23" => "comment1 desc",
                "24" => "comment2",
                "25" => "comment2 desc",
                "26" => "comment3",
                "27" => "comment3 desc",
                // ERROR 並びは UI 側の特別ソートへ委ね、ここでは生順のまま返す。
                "28" => "",
                _ => DefaultFallbackOrderBySql,
            };
        }

        // 起動時 partial 表示は同値の並び順が揺れないよう movie_id で最後に固定する。
        private static string BuildStartupOrderBySql(string sortId)
        {
            return sortId switch
            {
                "0" => "last_date desc, movie_id desc",
                "1" => "last_date, movie_id",
                "2" => "file_date desc, movie_id desc",
                "3" => "file_date, movie_id",
                "6" => "score desc, movie_id desc",
                "7" => "score, movie_id",
                "8" => "view_count desc, movie_id desc",
                "9" => "view_count, movie_id",
                "10" => "kana, movie_id",
                "11" => "kana desc, movie_id desc",
                "12" => "movie_name, movie_id",
                "13" => "movie_name desc, movie_id desc",
                "14" => "movie_path, movie_id",
                "15" => "movie_path desc, movie_id desc",
                "16" => "movie_size desc, movie_id desc",
                "17" => "movie_size, movie_id",
                "18" => "regist_date desc, movie_id desc",
                "19" => "regist_date, movie_id",
                "20" => "movie_length desc, movie_id desc",
                "21" => "movie_length, movie_id",
                "22" => "comment1, movie_id",
                "23" => "comment1 desc, movie_id desc",
                "24" => "comment2, movie_id",
                "25" => "comment2 desc, movie_id desc",
                "26" => "comment3, movie_id",
                "27" => "comment3 desc, movie_id desc",
                // 起動直後だけは安定表示を優先しつつ、unknown sort と同一仕様へ固定しない。
                "28" => ErrorSortStartupSeedOrderBySql,
                _ => DefaultFallbackOrderBySql,
            };
        }

        private static MainDbMovieReadItemResult ReadMovieItem(SQLiteDataReader reader)
        {
            return new MainDbMovieReadItemResult(
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
            );
        }

        private static int ReadAggregateCount(SQLiteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
            {
                return 0;
            }

            return Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static string ReadString(SQLiteDataReader reader, int ordinal)
        {
            object value = reader[ordinal];
            return value == DBNull.Value ? "" : value?.ToString() ?? "";
        }

        private static string ReadString(SQLiteDataReader reader, string columnName)
        {
            object value = reader[columnName];
            return value == DBNull.Value ? "" : value?.ToString() ?? "";
        }

        private static long ReadInt64(SQLiteDataReader reader, int ordinal)
        {
            object value = reader[ordinal];
            if (value == DBNull.Value || value == null)
            {
                return 0;
            }

            return Convert.ToInt64(value);
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

        private static DateTime ReadDateTime(SQLiteDataReader reader, int ordinal)
        {
            return ReadDbDateTimeOrDefault(reader[ordinal], DateTime.MinValue);
        }

        private static DateTime ReadDateTime(SQLiteDataReader reader, string columnName)
        {
            return ReadDbDateTimeOrDefault(reader[columnName], DateTime.MinValue);
        }
    }

    internal readonly record struct MainDbMovieReadRequest(
        string DbPath,
        string SortId,
        int FirstPageSize,
        int AppendPageSize
    );

    internal readonly record struct MainDbMovieReadPageResult(
        MainDbMovieReadItemResult[] Items,
        int ApproximateTotalCount,
        bool HasMore,
        int PageIndex
    );

    internal readonly record struct MainDbMovieReadItemResult(
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

    internal readonly record struct MainDbRenameBridgeOwnerCountsResult(
        int OtherOldMovieBodyOwnerCount,
        int OtherNewMovieBodyOwnerCount,
        int OtherOldThumbnailOwnerCount,
        int OtherNewThumbnailOwnerCount
    );
}
