using System;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager.Data
{
    internal interface IMainDbMovieMutationFacade
    {
        void UpdateTag(string dbFullPath, long movieId, string tag);
        void UpdateScore(string dbFullPath, long movieId, long score);
        void UpdateViewCount(string dbFullPath, long movieId, long viewCount);
        void UpdateLastDate(string dbFullPath, long movieId, DateTime lastDate);
        void UpdateMoviePath(string dbFullPath, long movieId, string moviePath);
        void UpdateMovieName(string dbFullPath, long movieId, string movieName);
        void UpdateMovieLength(string dbFullPath, long movieId, double movieLengthSeconds);
        void UpdateKana(string dbFullPath, long movieId, string kana);
        void UpdateRoma(string dbFullPath, long movieId, string roma);
    }

    internal sealed class MainDbMovieMutationFacade : IMainDbMovieMutationFacade
    {
        public void UpdateTag(string dbFullPath, long movieId, string tag)
        {
            UpdateSingleColumn(dbFullPath, movieId, "tag", tag ?? "");
        }

        public void UpdateScore(string dbFullPath, long movieId, long score)
        {
            UpdateSingleColumn(dbFullPath, movieId, "score", score);
        }

        public void UpdateViewCount(string dbFullPath, long movieId, long viewCount)
        {
            UpdateSingleColumn(dbFullPath, movieId, "view_count", viewCount);
        }

        public void UpdateLastDate(string dbFullPath, long movieId, DateTime lastDate)
        {
            UpdateSingleColumn(dbFullPath, movieId, "last_date", lastDate);
        }

        public void UpdateMoviePath(string dbFullPath, long movieId, string moviePath)
        {
            UpdateSingleColumn(dbFullPath, movieId, "movie_path", moviePath ?? "");
        }

        public void UpdateMovieName(string dbFullPath, long movieId, string movieName)
        {
            UpdateSingleColumn(dbFullPath, movieId, "movie_name", movieName ?? "");
        }

        public void UpdateMovieLength(string dbFullPath, long movieId, double movieLengthSeconds)
        {
            UpdateSingleColumn(dbFullPath, movieId, "movie_length", movieLengthSeconds);
        }

        public void UpdateKana(string dbFullPath, long movieId, string kana)
        {
            UpdateSingleColumn(dbFullPath, movieId, "kana", kana ?? "");
        }

        public void UpdateRoma(string dbFullPath, long movieId, string roma)
        {
            UpdateSingleColumn(dbFullPath, movieId, "roma", roma ?? "");
        }

        // 呼び出し元から列名文字列を隠しつつ、既存の SQLite 実装へ更新を委譲する。
        private static void UpdateSingleColumn(
            string dbFullPath,
            long movieId,
            string columnName,
            object value
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dbFullPath);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(movieId);

            UpdateMovieSingleColumn(dbFullPath, movieId, columnName, value);
        }
    }
}
