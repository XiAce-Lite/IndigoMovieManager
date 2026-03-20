using System;
using System.Collections.Generic;
using System.Data.SQLite;
using IndigoMovieManager.DB;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager.Data
{
    internal interface IWatchMainDbFacade
    {
        Dictionary<string, WatchMainDbMovieSnapshot> LoadExistingMovieSnapshot(string dbFullPath);
        Task<int> InsertMoviesBatchAsync(string dbFullPath, IReadOnlyList<MovieCore> moviesToInsert);
    }

    internal sealed class WatchMainDbFacade : IWatchMainDbFacade
    {
        // watch 走査中の存在確認を軽くするため、必要最小限列だけを path 辞書へ詰める。
        public Dictionary<string, WatchMainDbMovieSnapshot> LoadExistingMovieSnapshot(
            string dbFullPath
        )
        {
            Dictionary<string, WatchMainDbMovieSnapshot> result =
                new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return result;
            }

            try
            {
                using SQLiteConnection connection = SQLite.CreateReadOnlyConnection(dbFullPath);
                connection.Open();

                using SQLiteCommand command = connection.CreateCommand();
                command.CommandText = "select movie_id, movie_path, hash from movie";

                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string moviePath = reader["movie_path"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(moviePath))
                    {
                        continue;
                    }

                    if (!long.TryParse(reader["movie_id"]?.ToString(), out long movieId))
                    {
                        continue;
                    }

                    result[moviePath] = new WatchMainDbMovieSnapshot(
                        movieId,
                        reader["hash"]?.ToString() ?? ""
                    );
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"LoadExistingMovieSnapshot failed: {ex.GetType().Name}"
                );
            }

            return result;
        }

        // 登録本体の insert と採番は既存 DB 実装へ任せ、watch 側からは batch 契約だけ見せる。
        public Task<int> InsertMoviesBatchAsync(
            string dbFullPath,
            IReadOnlyList<MovieCore> moviesToInsert
        )
        {
            if (
                string.IsNullOrWhiteSpace(dbFullPath)
                || moviesToInsert == null
                || moviesToInsert.Count < 1
            )
            {
                return Task.FromResult(0);
            }

            List<MovieCore> batch = moviesToInsert as List<MovieCore> ?? [.. moviesToInsert];
            return Task.Run(() => InsertMovieTableBatch(dbFullPath, batch).GetAwaiter().GetResult());
        }
    }

    internal readonly record struct WatchMainDbMovieSnapshot(long MovieId, string Hash);
}
