using System.Data.SQLite;
using System.Globalization;
using IndigoMovieManager.UpperTabs.DuplicateVideos;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabDuplicateVideoAnalyzerTests
{
    [Test]
    public void ExtractProbText_prob付きファイル名から抽出する()
    {
        string result = UpperTabDuplicateVideoAnalyzer.ExtractProbText(
            "sample_scale_2x_prob-3",
            @"C:\movies\sample_scale_2x_prob-3.mp4"
        );

        Assert.That(result, Is.EqualTo("prob-3"));
    }

    [Test]
    public void BuildGroupSummaries_件数優先と代表選定を行う()
    {
        UpperTabDuplicateMovieRecord[] records =
        [
            new(1, "a", @"C:\movies\a.mp4", 100, "2026-03-20 10:00:00", 60, 0, "hash-a"),
            new(2, "b", @"C:\movies\b.mp4", 250, "2026-03-20 11:00:00", 60, 0, "hash-a"),
            new(3, "c", @"C:\movies\c.mp4", 150, "2026-03-20 12:00:00", 60, 0, "hash-b"),
            new(4, "d", @"C:\movies\d.mp4", 140, "2026-03-20 09:00:00", 60, 0, "hash-b"),
            new(5, "e", @"C:\movies\e.mp4", 130, "2026-03-20 08:00:00", 60, 0, "hash-b"),
        ];

        UpperTabDuplicateGroupSummary[] groups = UpperTabDuplicateVideoAnalyzer.BuildGroupSummaries(records);

        Assert.That(groups.Length, Is.EqualTo(2));
        Assert.That(groups[0].Hash, Is.EqualTo("hash-b"));
        Assert.That(groups[0].DuplicateCount, Is.EqualTo(3));
        Assert.That(groups[0].Representative.MovieId, Is.EqualTo(3));
        Assert.That(groups[1].Representative.MovieId, Is.EqualTo(2));
    }

    [Test]
    public void BuildSizeCompareText_最大と差分を短く返す()
    {
        Assert.That(
            UpperTabDuplicateVideoAnalyzer.BuildSizeCompareText(3000, 3000, 1000),
            Is.EqualTo("最大")
        );
        Assert.That(
            UpperTabDuplicateVideoAnalyzer.BuildSizeCompareText(1000, 3000, 1000),
            Is.EqualTo("最小")
        );
        Assert.That(
            UpperTabDuplicateVideoAnalyzer.BuildSizeCompareText(2000, 3000, 1000),
            Is.EqualTo("-1.0 MB")
        );
    }
}

[TestFixture]
public sealed class UpperTabDuplicateVideoReadServiceTests
{
    [Test]
    public void ReadDuplicateMovieRecords_空hashを除いて重複群だけ返す()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            SeedMovieRows(dbPath);
            UpperTabDuplicateVideoReadService service = new();

            UpperTabDuplicateMovieRecord[] records = service.ReadDuplicateMovieRecords(dbPath);

            Assert.That(records.Length, Is.EqualTo(4));
            Assert.That(records.All(x => !string.IsNullOrWhiteSpace(x.Hash)), Is.True);
            Assert.That(records.Select(x => x.Hash).Distinct().OrderBy(x => x), Is.EqualTo(new[] { "hash-a", "hash-c" }));
            Assert.That(records.First().MovieId, Is.EqualTo(2));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void ReadDuplicateMovieRecords_特殊カルチャでもISO日付文字列を崩さない()
    {
        string dbPath = CreateTempMainDb();
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;

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
    file_date,
    score,
    hash
)
VALUES
    (1, 'movie-a', 'C:\movies\a.mp4', 60, 100, '2026-04-01 12:34:56', 1, 'hash-a'),
    (2, 'movie-b', 'C:\movies\b.mp4', 70, 300, '2026-04-01 01:02:03', 2, 'hash-a');";
            command.ExecuteNonQuery();

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("th-TH");

            UpperTabDuplicateVideoReadService service = new();
            UpperTabDuplicateMovieRecord[] records = service.ReadDuplicateMovieRecords(dbPath);

            Assert.That(records.Length, Is.EqualTo(2));
            Assert.That(
                records.First(x => x.MovieId == 2).FileDateText,
                Is.EqualTo("2026-04-01 01:02:03")
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            TryDeleteFile(dbPath);
        }
    }

    private static string CreateTempMainDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-dup-tab-{Guid.NewGuid():N}.wb");
        SQLiteConnection.CreateFile(dbPath);

        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE movie (
    movie_id INTEGER PRIMARY KEY,
    movie_name TEXT NOT NULL,
    movie_path TEXT NOT NULL,
    movie_length INTEGER NOT NULL,
    movie_size INTEGER NOT NULL,
    file_date TEXT NOT NULL,
    score INTEGER NOT NULL,
    hash TEXT NOT NULL
);";
        command.ExecuteNonQuery();
        return dbPath;
    }

    private static void SeedMovieRows(string dbPath)
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
    file_date,
    score,
    hash
)
VALUES
    (1, 'movie-a', 'C:\movies\a.mp4', 60, 100, '2026-03-18 10:00:00', 1, 'hash-a'),
    (2, 'movie-b', 'C:\movies\b.mp4', 70, 300, '2026-03-19 10:00:00', 2, 'hash-a'),
    (3, 'movie-c', 'C:\movies\c.mp4', 80, 200, '2026-03-20 10:00:00', 3, ''),
    (4, 'movie-d', 'C:\movies\d.mp4', 90, 50, '2026-03-20 10:00:00', 4, 'hash-c'),
    (5, 'movie-e', 'C:\movies\e.mp4', 95, 40, '2026-03-20 09:00:00', 5, 'hash-c');";
        command.ExecuteNonQuery();
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
        }
    }
}
