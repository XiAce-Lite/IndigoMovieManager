using System;
using System.Data.SQLite;
using IndigoMovieManager.Data;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainDbMovieMutationFacadeTests
{
    [Test]
    public void Update群_対象7列を更新できる()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            MainDbMovieMutationFacade facade = new();
            DateTime expectedLastDate = new(2026, 3, 20, 12, 34, 56);

            facade.UpdateTag(dbPath, 1, "alpha");
            facade.UpdateScore(dbPath, 1, 9);
            facade.UpdateViewCount(dbPath, 1, 12);
            facade.UpdateLastDate(dbPath, 1, expectedLastDate);
            facade.UpdateMoviePath(dbPath, 1, @"C:\movies\renamed.mp4");
            facade.UpdateMovieName(dbPath, 1, "renamed");
            facade.UpdateMovieLength(dbPath, 1, 321);

            using SQLiteConnection connection = new($"Data Source={dbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    tag,
    score,
    view_count,
    last_date,
    movie_path,
    movie_name,
    movie_length
FROM movie
WHERE movie_id = 1;";

            using SQLiteDataReader reader = command.ExecuteReader();
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader["tag"]?.ToString(), Is.EqualTo("alpha"));
            Assert.That(Convert.ToInt64(reader["score"]), Is.EqualTo(9));
            Assert.That(Convert.ToInt64(reader["view_count"]), Is.EqualTo(12));
            Assert.That(Convert.ToDateTime(reader["last_date"]), Is.EqualTo(expectedLastDate));
            Assert.That(reader["movie_path"]?.ToString(), Is.EqualTo(@"C:\movies\renamed.mp4"));
            Assert.That(reader["movie_name"]?.ToString(), Is.EqualTo("renamed"));
            Assert.That(Convert.ToDouble(reader["movie_length"]), Is.EqualTo(321d));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void UpdateTag_DBパスが空なら例外を投げる()
    {
        MainDbMovieMutationFacade facade = new();

        Assert.That(
            () => facade.UpdateTag("", 1, "alpha"),
            Throws.InstanceOf<ArgumentException>()
        );
    }

    [Test]
    public void UpdateScore_movieIdが0以下なら例外を投げる()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            MainDbMovieMutationFacade facade = new();

            Assert.That(
                () => facade.UpdateScore(dbPath, 0, 1),
                Throws.InstanceOf<ArgumentOutOfRangeException>()
            );
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    private static string CreateTempMainDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-main-mutation-{Guid.NewGuid():N}.wb");
        SQLiteConnection.CreateFile(dbPath);

        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE movie (
    movie_id INTEGER PRIMARY KEY,
    tag TEXT NOT NULL,
    score INTEGER NOT NULL,
    view_count INTEGER NOT NULL,
    last_date TEXT NOT NULL,
    movie_path TEXT NOT NULL,
    movie_name TEXT NOT NULL,
    movie_length INTEGER NOT NULL
);

INSERT INTO movie (
    movie_id,
    tag,
    score,
    view_count,
    last_date,
    movie_path,
    movie_name,
    movie_length
)
VALUES (
    1,
    '',
    0,
    0,
    '2026-03-20 00:00:00',
    'C:\movies\before.mp4',
    'before',
    0
);";
        command.ExecuteNonQuery();
        return dbPath;
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // 一時DBの掃除失敗は、テスト本体の判定を優先する。
        }
    }
}
