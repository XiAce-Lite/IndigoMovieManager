using System.Data.SQLite;
using IndigoMovieManager;
using IndigoMovieManager.Data;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchMainDbFacadeTests
{
    [Test]
    public void LoadExistingMovieSnapshot_path基準でfile属性込みsnapshotを返す()
    {
        string dbPath = CreateTempWatchMainDb();

        try
        {
            SeedSnapshotRows(dbPath);
            WatchMainDbFacade facade = new();

            Dictionary<string, WatchMainDbMovieSnapshot> snapshot = facade.LoadExistingMovieSnapshot(
                dbPath
            );

            Assert.That(snapshot.Count, Is.EqualTo(2));
            Assert.That(snapshot[@"C:\movies\a.mp4"].MovieId, Is.EqualTo(1));
            Assert.That(snapshot[@"C:\movies\a.mp4"].Hash, Is.EqualTo("hash-a"));
            Assert.That(
                snapshot[@"C:\movies\a.mp4"].FileDateText,
                Is.EqualTo("2026-03-20 10:00:00")
            );
            Assert.That(snapshot[@"C:\movies\a.mp4"].MovieSizeKb, Is.EqualTo(100));
            Assert.That(snapshot[@"C:\movies\a.mp4"].MovieLengthSeconds, Is.EqualTo(60));
            Assert.That(snapshot[@"C:\movies\b.mp4"].MovieId, Is.EqualTo(2));
            Assert.That(snapshot[@"C:\movies\b.mp4"].Hash, Is.EqualTo("hash-b"));
            Assert.That(
                snapshot[@"C:\movies\b.mp4"].FileDateText,
                Is.EqualTo("2026-03-20 11:00:00")
            );
            Assert.That(snapshot[@"C:\movies\b.mp4"].MovieSizeKb, Is.EqualTo(200));
            Assert.That(snapshot[@"C:\movies\b.mp4"].MovieLengthSeconds, Is.EqualTo(70));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public async Task InsertMoviesBatchAsync_movieをまとめて登録し採番を戻す()
    {
        string dbPath = CreateTempWatchMainDb();

        try
        {
            WatchMainDbFacade facade = new();
            List<MovieCore> movies =
            [
                CreateMovie(@"C:\movies\new-a.mp4", "hash-new-a"),
                CreateMovie(@"C:\movies\new-b.mp4", "hash-new-b"),
            ];

            int insertedCount = await facade.InsertMoviesBatchAsync(dbPath, movies);

            Assert.That(insertedCount, Is.EqualTo(2));
            Assert.That(movies.Select(movie => movie.MovieId), Is.EqualTo(new long[] { 1, 2 }));

            using SQLiteConnection connection = new($"Data Source={dbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText =
                "select movie_name, movie_path, hash from movie order by movie_id";
            using SQLiteDataReader reader = command.ExecuteReader();

            List<(string Name, string Path, string Hash)> actualRows = [];
            while (reader.Read())
            {
                actualRows.Add(
                    (
                        reader["movie_name"]?.ToString() ?? "",
                        reader["movie_path"]?.ToString() ?? "",
                        reader["hash"]?.ToString() ?? ""
                    )
                );
            }

            Assert.That(
                actualRows,
                Is.EqualTo(
                    new[]
                    {
                        ("new-a.mp4", @"C:\movies\new-a.mp4", "hash-new-a"),
                        ("new-b.mp4", @"C:\movies\new-b.mp4", "hash-new-b"),
                    }
                )
            );
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void LoadExistingMovieSnapshot_不正パスは空辞書を返す()
    {
        WatchMainDbFacade facade = new();
        string missingDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-watch-missing-{Guid.NewGuid():N}",
            "watch-main-db.wb"
        );

        Dictionary<string, WatchMainDbMovieSnapshot> snapshot = facade.LoadExistingMovieSnapshot(
            missingDbPath
        );

        Assert.That(snapshot, Is.Empty);
    }

    [Test]
    public void LoadExistingMovieSnapshot_movieテーブル読取失敗時は空辞書を返す()
    {
        string dbPath = CreateTempEmptyDb();

        try
        {
            WatchMainDbFacade facade = new();

            Dictionary<string, WatchMainDbMovieSnapshot> snapshot =
                facade.LoadExistingMovieSnapshot(dbPath);

            Assert.That(snapshot, Is.Empty);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public async Task InsertMoviesBatchAsync_null入力は0を返す()
    {
        string dbPath = CreateTempWatchMainDb();

        try
        {
            WatchMainDbFacade facade = new();

            int insertedCount = await facade.InsertMoviesBatchAsync(dbPath, null!);

            Assert.That(insertedCount, Is.EqualTo(0));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public async Task InsertMoviesBatchAsync_empty入力は0を返す()
    {
        string dbPath = CreateTempWatchMainDb();

        try
        {
            WatchMainDbFacade facade = new();

            int insertedCount = await facade.InsertMoviesBatchAsync(dbPath, []);

            Assert.That(insertedCount, Is.EqualTo(0));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    private static string CreateTempWatchMainDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-watch-main-db-{Guid.NewGuid():N}.wb");
        SQLiteConnection.CreateFile(dbPath);

        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE movie (
    movie_id INTEGER PRIMARY KEY,
    movie_name TEXT NOT NULL DEFAULT '',
    movie_path TEXT NOT NULL DEFAULT '',
    movie_length INTEGER NOT NULL DEFAULT 0,
    movie_size INTEGER NOT NULL DEFAULT 0,
    last_date TEXT NOT NULL,
    file_date TEXT NOT NULL,
    regist_date TEXT NOT NULL,
    hash TEXT NOT NULL DEFAULT '',
    container TEXT NOT NULL DEFAULT '',
    video TEXT NOT NULL DEFAULT '',
    audio TEXT NOT NULL DEFAULT '',
    extra TEXT NOT NULL DEFAULT ''
);";
        command.ExecuteNonQuery();
        return dbPath;
    }

    private static string CreateTempEmptyDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-watch-empty-{Guid.NewGuid():N}.wb");
        SQLiteConnection.CreateFile(dbPath);
        return dbPath;
    }

    private static void SeedSnapshotRows(string dbPath)
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
    extra
)
VALUES
    (1, 'movie-a', 'C:\movies\a.mp4', 60, 100, '2026-03-20 10:00:00', '2026-03-20 10:00:00', '2026-03-20 10:00:00', 'hash-a', '', '', '', ''),
    (2, 'movie-b', 'C:\movies\b.mp4', 70, 200, '2026-03-20 11:00:00', '2026-03-20 11:00:00', '2026-03-20 11:00:00', 'hash-b', '', '', '', '');";
        command.ExecuteNonQuery();
    }

    private static MovieCore CreateMovie(string moviePath, string hash)
    {
        DateTime now = new(2026, 3, 20, 12, 0, 0, DateTimeKind.Local);
        return new MovieCore
        {
            MovieName = Path.GetFileName(moviePath),
            MoviePath = moviePath,
            MovieLength = 120,
            MovieSize = 2048,
            LastDate = now,
            FileDate = now,
            RegistDate = now,
            Hash = hash,
        };
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
