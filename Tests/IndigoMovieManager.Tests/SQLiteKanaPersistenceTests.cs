using System.Data.SQLite;
using IndigoMovieManager.DB;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SQLiteKanaPersistenceTests
{
    [Test]
    public async Task InsertMovieTable_日本語名ならkana列へ値が入る()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            MovieCore movie = new()
            {
                MovieName = "東京ラブストーリー",
                MoviePath = @"C:\movies\東京ラブストーリー.mp4",
                MovieLength = 120,
                MovieSize = 1024 * 1024,
                LastDate = new DateTime(2026, 4, 1, 12, 0, 0),
                FileDate = new DateTime(2026, 4, 1, 12, 0, 0),
                RegistDate = new DateTime(2026, 4, 1, 12, 0, 0),
            };

            int inserted = await SQLite.InsertMovieTable(dbPath, movie);

            Assert.That(inserted, Is.EqualTo(1));
            Assert.That(ReadSingleValue(dbPath, "SELECT kana FROM movie WHERE movie_id = 1"), Is.Not.Empty);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void InsertBookmarkTable_かな未設定でもkana列へ保存する()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            MovieCore movie = new()
            {
                MovieName = "かなテスト",
                MoviePath = @"C:\movies\かなテスト.mp4",
            };

            SQLite.InsertBookmarkTable(dbPath, movie);

            Assert.That(
                ReadSingleValue(dbPath, "SELECT kana FROM bookmark WHERE movie_id = 1"),
                Is.EqualTo("カナテスト")
            );
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void UpdateBookmarkRename_新しい名前へkanaも追従する()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            using SQLiteConnection connection = new($"Data Source={dbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO bookmark (
    movie_id,
    movie_name,
    movie_path,
    last_date,
    file_date,
    regist_date,
    kana
)
VALUES (
    1,
    'before',
    'c:\movies\before.mp4',
    '2026-04-01 00:00:00',
    '2026-04-01 00:00:00',
    '2026-04-01 00:00:00',
    ''
);";
            command.ExecuteNonQuery();

            SQLite.UpdateBookmarkRename(dbPath, "before", "かなテスト");

            Assert.That(
                ReadSingleValue(dbPath, "SELECT movie_name FROM bookmark WHERE movie_id = 1"),
                Is.EqualTo("かなテスト")
            );
            Assert.That(
                ReadSingleValue(dbPath, "SELECT kana FROM bookmark WHERE movie_id = 1"),
                Is.EqualTo("カナテスト")
            );
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void ReadAndUpdateMovieKanaBackfillTargets_空kanaだけをまとめて更新できる()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            using SQLiteConnection connection = new($"Data Source={dbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO movie (
    movie_id,
    movie_name,
    movie_path,
    movie_length,
    movie_size,
    last_date,
    file_date,
    regist_date,
    hash,
    container,
    video,
    audio,
    kana
)
VALUES
    (1, 'かな一', 'c:\movies\a.mp4', 1, 1, '2026-04-01 00:00:00', '2026-04-01 00:00:00', '2026-04-01 00:00:00', '', '', '', '', ''),
    (2, 'かな二', 'c:\movies\b.mp4', 1, 1, '2026-04-01 00:00:00', '2026-04-01 00:00:00', '2026-04-01 00:00:00', '', '', '', '', 'ミギ'),
    (3, '', '', 1, 1, '2026-04-01 00:00:00', '2026-04-01 00:00:00', '2026-04-01 00:00:00', '', '', '', '', '');";
            command.ExecuteNonQuery();

            List<KanaBackfillTarget> targets = SQLite.ReadMovieKanaBackfillTargets(dbPath, 10);
            int updated = SQLite.UpdateMovieKanaBatch(
                dbPath,
                [new KanaBackfillUpdate(1, "カナイチ")]
            );

            Assert.That(targets.Select(x => x.MovieId), Is.EqualTo(new long[] { 1 }));
            Assert.That(updated, Is.EqualTo(1));
            Assert.That(ReadSingleValue(dbPath, "SELECT kana FROM movie WHERE movie_id = 1"), Is.EqualTo("カナイチ"));
            Assert.That(ReadSingleValue(dbPath, "SELECT kana FROM movie WHERE movie_id = 2"), Is.EqualTo("ミギ"));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    private static string CreateTempMainDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-kana-{Guid.NewGuid():N}.wb");

        bool created = SQLite.TryCreateDatabase(dbPath, out string errorMessage);
        Assert.That(created, Is.True, errorMessage);
        return dbPath;
    }

    private static string ReadSingleValue(string dbPath, string sql)
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString() ?? "";
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
            // 一時DBの掃除失敗は本体判定を優先する。
        }
    }
}
