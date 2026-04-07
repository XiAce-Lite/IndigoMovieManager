using System.Data.SQLite;
using IndigoMovieManager.DB;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SQLiteKanaPersistenceTests
{
    [Test]
    public async Task InsertMovieTable_かな主体の名前ならkana列とroma列へ保存する()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            MovieCore movie = new()
            {
                MovieName = "けものフレンズ01-02",
                MoviePath = @"C:\movies\けものフレンズ01-02.mp4",
                MovieLength = 120,
                MovieSize = 1024 * 1024,
                LastDate = new DateTime(2026, 4, 1, 12, 0, 0),
                FileDate = new DateTime(2026, 4, 1, 12, 0, 0),
                RegistDate = new DateTime(2026, 4, 1, 12, 0, 0),
            };

            int inserted = await SQLite.InsertMovieTable(dbPath, movie);

            Assert.That(inserted, Is.EqualTo(1));
            Assert.That(
                ReadSingleValue(dbPath, "SELECT kana FROM movie WHERE movie_id = 1"),
                Is.EqualTo("けものふれんず01-02")
            );
            Assert.That(
                ReadSingleValue(dbPath, "SELECT roma FROM movie WHERE movie_id = 1"),
                Does.Contain("kemonofurenzu01-02")
            );
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public async Task InsertMovieTable_漢字混じりの題名でもkana列へひらがな保存する()
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
            Assert.That(
                ReadSingleValue(dbPath, "SELECT kana FROM movie WHERE movie_id = 1"),
                Is.EqualTo("とうきょうらぶすとーりー")
            );
            Assert.That(
                ReadSingleValue(dbPath, "SELECT roma FROM movie WHERE movie_id = 1"),
                Does.Contain("toukyourabusutoorii")
            );
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public async Task InsertMovieTable_英語副題混在の実パスでも日本語部分のkanaとromaを保存する()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            MovieCore movie = new()
            {
                MovieName = "",
                MoviePath = @"E:\copy1\【公式】新・エースをねらえ！ 第1話「ひろみとお蝶と鬼コーチ」”AIM FOR THE BEST THE REMAKE VERSION” EP011978.mp4",
                MovieLength = 120,
                MovieSize = 1024 * 1024,
                LastDate = new DateTime(2026, 4, 1, 12, 0, 0),
                FileDate = new DateTime(2026, 4, 1, 12, 0, 0),
                RegistDate = new DateTime(2026, 4, 1, 12, 0, 0),
            };

            int inserted = await SQLite.InsertMovieTable(dbPath, movie);

            Assert.That(inserted, Is.EqualTo(1));
            Assert.That(
                ReadSingleValue(dbPath, "SELECT kana FROM movie WHERE movie_id = 1"),
                Is.EqualTo("こうしきしんえーすをねらえだい1わひろみとおちょうとおにこーち")
            );
            Assert.That(
                ReadSingleValue(dbPath, "SELECT roma FROM movie WHERE movie_id = 1"),
                Does.Contain("koushikishineesuoneraedai1wa")
            );
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public async Task InsertMovieTable_英数主体な題名をkanaとromaへ生で保存しない()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            MovieCore movie = new()
            {
                MovieName = "one piece film red",
                MoviePath = @"C:\movies\one piece film red.mp4",
                MovieLength = 120,
                MovieSize = 1024 * 1024,
                LastDate = new DateTime(2026, 4, 1, 12, 0, 0),
                FileDate = new DateTime(2026, 4, 1, 12, 0, 0),
                RegistDate = new DateTime(2026, 4, 1, 12, 0, 0),
            };

            int inserted = await SQLite.InsertMovieTable(dbPath, movie);

            Assert.That(inserted, Is.EqualTo(1));
            Assert.That(
                ReadSingleValue(dbPath, "SELECT kana FROM movie WHERE movie_id = 1"),
                Is.EqualTo("")
            );
            Assert.That(
                ReadSingleValue(dbPath, "SELECT roma FROM movie WHERE movie_id = 1"),
                Is.EqualTo("")
            );
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public async Task InsertMovieTable_日時をWB互換のISO文字列で保存する()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            MovieCore movie = new()
            {
                MovieName = "date-test",
                MoviePath = @"C:\movies\date-test.mp4",
                MovieLength = 120,
                MovieSize = 1024 * 1024,
                LastDate = new DateTime(2026, 4, 1, 12, 34, 56),
                FileDate = new DateTime(2026, 4, 1, 1, 2, 3),
                RegistDate = new DateTime(2026, 4, 1, 23, 45, 6),
            };

            int inserted = await SQLite.InsertMovieTable(dbPath, movie);

            Assert.That(inserted, Is.EqualTo(1));

            using SQLiteConnection connection = new($"Data Source={dbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    typeof(last_date),
    quote(last_date),
    typeof(file_date),
    quote(file_date),
    typeof(regist_date),
    quote(regist_date)
FROM movie
WHERE movie_id = 1;";

            using SQLiteDataReader reader = command.ExecuteReader();
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("text"));
            Assert.That(reader.GetString(1), Is.EqualTo("'2026-04-01 12:34:56'"));
            Assert.That(reader.GetString(2), Is.EqualTo("text"));
            Assert.That(reader.GetString(3), Is.EqualTo("'2026-04-01 01:02:03'"));
            Assert.That(reader.GetString(4), Is.EqualTo("text"));
            Assert.That(reader.GetString(5), Is.EqualTo("'2026-04-01 23:45:06'"));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void InsertBookmarkTable_かな未設定でもkana列とroma列へ保存する()
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
                Is.EqualTo("かなてすと")
            );
            Assert.That(
                ReadSingleValue(dbPath, "SELECT roma FROM bookmark WHERE movie_id = 1"),
                Does.Contain("kanatesuto")
            );
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void UpdateBookmarkRename_新しい名前へkanaとromaも追従する()
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
    kana,
    roma
)
VALUES (
    1,
    'before',
    'c:\movies\before.mp4',
    '2026-04-01 00:00:00',
    '2026-04-01 00:00:00',
    '2026-04-01 00:00:00',
    '',
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
                Is.EqualTo("かなてすと")
            );
            Assert.That(
                ReadSingleValue(dbPath, "SELECT roma FROM bookmark WHERE movie_id = 1"),
                Does.Contain("kanatesuto")
            );
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void ReadAndUpdateMovieKanaBackfillTargets_空kanaと空romaをまとめて更新できる()
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
    kana,
    roma
)
VALUES
    (1, 'かな一', 'c:\movies\a.mp4', 1, 1, '2026-04-01 00:00:00', '2026-04-01 00:00:00', '2026-04-01 00:00:00', '', '', '', '', '', ''),
    (2, 'かな二', 'c:\movies\b.mp4', 1, 1, '2026-04-01 00:00:00', '2026-04-01 00:00:00', '2026-04-01 00:00:00', '', '', '', '', 'ミギ', ''),
    (3, '', '', 1, 1, '2026-04-01 00:00:00', '2026-04-01 00:00:00', '2026-04-01 00:00:00', '', '', '', '', '', '');";
            command.ExecuteNonQuery();

            List<KanaBackfillTarget> targets = SQLite.ReadMovieKanaBackfillTargets(dbPath, 10);
            int updated = SQLite.UpdateMovieKanaBatch(
                dbPath,
                [
                    new KanaBackfillUpdate(1, "かないち", "kanaichi"),
                    new KanaBackfillUpdate(2, "みぎ", "migi")
                ]
            );

            Assert.That(targets.Select(x => x.MovieId), Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(updated, Is.EqualTo(2));
            Assert.That(ReadSingleValue(dbPath, "SELECT kana FROM movie WHERE movie_id = 1"), Is.EqualTo("かないち"));
            Assert.That(ReadSingleValue(dbPath, "SELECT roma FROM movie WHERE movie_id = 1"), Is.EqualTo("kanaichi"));
            Assert.That(ReadSingleValue(dbPath, "SELECT kana FROM movie WHERE movie_id = 2"), Is.EqualTo("みぎ"));
            Assert.That(ReadSingleValue(dbPath, "SELECT roma FROM movie WHERE movie_id = 2"), Is.EqualTo("migi"));
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
