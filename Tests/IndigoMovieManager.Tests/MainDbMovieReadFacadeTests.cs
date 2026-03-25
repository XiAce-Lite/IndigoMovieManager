using System.Data;
using System.Data.SQLite;
using System.Text.RegularExpressions;
using IndigoMovieManager.Data;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class MainDbMovieReadFacadeTests
{
    [Test]
    public void ReadRegisteredMovieCount_movie件数を返す()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            SeedMovieRows(dbPath);
            MainDbMovieReadFacade facade = new();

            int count = facade.ReadRegisteredMovieCount(dbPath);

            Assert.That(count, Is.EqualTo(4));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void LoadSystemTable_systemテーブルをそのまま返す()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            SeedSystemRow(dbPath, "skin", "grid");
            MainDbMovieReadFacade facade = new();

            DataTable systemTable = facade.LoadSystemTable(dbPath);

            Assert.That(systemTable.Rows.Count, Is.EqualTo(1));
            Assert.That(systemTable.Rows[0]["param"]?.ToString(), Is.EqualTo("skin"));
            Assert.That(systemTable.Rows[0]["paramater"]?.ToString(), Is.EqualTo("grid"));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void LoadMovieTableForSort_指定sortでmovieテーブルを並べて返す()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            SeedMovieRows(dbPath);
            MainDbMovieReadFacade facade = new();

            DataTable movieTable = facade.LoadMovieTableForSort(dbPath, "16");

            long[] movieIds = movieTable.AsEnumerable().Select(row => (long)row["movie_id"]).ToArray();
            Assert.That(movieIds, Is.EqualTo(new long[] { 2, 3, 1, 4 }));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void ReadStartupPage_追加ページ判定と安定順序を返す()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            SeedMovieRows(dbPath);
            MainDbMovieReadFacade facade = new();
            MainDbMovieReadRequest request = new(
                dbPath,
                SortId: "0",
                FirstPageSize: 2,
                AppendPageSize: 2
            );

            // 同順位時も movie_id で揺れない順を返すことを先頭ページで固定する。
            MainDbMovieReadPageResult firstPage = facade.ReadStartupPage(request, pageIndex: 0);
            MainDbMovieReadPageResult secondPage = facade.ReadStartupPage(request, pageIndex: 1);

            Assert.That(firstPage.PageIndex, Is.EqualTo(0));
            Assert.That(firstPage.HasMore, Is.True);
            Assert.That(firstPage.Items.Select(item => item.MovieId), Is.EqualTo(new long[] { 4, 3 }));

            Assert.That(secondPage.PageIndex, Is.EqualTo(1));
            Assert.That(secondPage.HasMore, Is.False);
            Assert.That(secondPage.Items.Select(item => item.MovieId), Is.EqualTo(new long[] { 2, 1 }));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void TryReadMovieByPath_moviePath完全一致の1件だけを返す()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            SeedMovieRows(dbPath);
            MainDbMovieReadFacade facade = new();

            bool found = facade.TryReadMovieByPath(
                dbPath,
                @"C:\MOVIES\B.mp4",
                out MainDbMovieReadItemResult movie
            );
            bool missing = facade.TryReadMovieByPath(dbPath, @"C:\movies\missing.mp4", out _);

            Assert.That(found, Is.True);
            Assert.That(movie.MovieId, Is.EqualTo(2));
            Assert.That(movie.MoviePath, Is.EqualTo(@"C:\movies\b.mp4"));
            Assert.That(missing, Is.False);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void TryReadRenameBridgeOwnerCounts_hiddenOwnerをreadOnlyで拾って共有owner数を返す()
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
)
VALUES
    (1, 'old-name', 'C:\movies\old-name.mp4', 60, 100, '2026-03-18 10:00:00', '2026-03-18 10:00:00', '2026-03-18 10:00:00', 1, 10, 'hash-1', 'mp4', 'h264', 'aac', 'a', '', '', '', ''),
    (2, 'old-name', 'D:\hidden\old-name.mkv', 70, 200, '2026-03-19 10:00:00', '2026-03-19 10:00:00', '2026-03-19 10:00:00', 2, 20, 'hash-1', 'mkv', 'h264', 'aac', 'b', '', '', '', ''),
    (3, 'new-name', 'D:\hidden\new-name.mp4', 80, 300, '2026-03-20 10:00:00', '2026-03-20 10:00:00', '2026-03-20 10:00:00', 3, 30, 'hash-1', 'mp4', 'h264', 'aac', 'c', '', '', '', ''),
    (4, 'old-name', 'D:\hidden\old-name-otherhash.mp4', 90, 400, '2026-03-21 10:00:00', '2026-03-21 10:00:00', '2026-03-21 10:00:00', 4, 40, 'hash-2', 'mp4', 'h264', 'aac', 'd', '', '', '', '');";
            command.ExecuteNonQuery();

            MainDbMovieReadFacade facade = new();

            bool found = facade.TryReadRenameBridgeOwnerCounts(
                dbPath,
                @"C:\MOVIES\OLD-NAME.mp4",
                "old-name",
                "new-name",
                "hash-1",
                out MainDbRenameBridgeOwnerCountsResult result
            );

            Assert.That(found, Is.True);
            Assert.That(result.OtherOldMovieBodyOwnerCount, Is.EqualTo(2));
            Assert.That(result.OtherNewMovieBodyOwnerCount, Is.EqualTo(1));
            Assert.That(result.OtherOldThumbnailOwnerCount, Is.EqualTo(1));
            Assert.That(result.OtherNewThumbnailOwnerCount, Is.EqualTo(1));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [TestCase("999")]
    [TestCase("unexpected-sort")]
    public void unknownSortIdはstartupとfullReloadで同じ既定順を使う(string sortId)
    {
        string dbPath = CreateTempMainDb();

        try
        {
            SeedMovieRows(dbPath);
            MainDbMovieReadFacade facade = new();
            MainDbMovieReadRequest request = new(
                dbPath,
                SortId: sortId,
                FirstPageSize: 3,
                AppendPageSize: 2
            );

            // partial feed と full reload の raw source order を同じ既定順へそろえる。
            DataTable movieTable = facade.LoadMovieTableForSort(dbPath, sortId);
            MainDbMovieReadPageResult firstPage = facade.ReadStartupPage(request, pageIndex: 0);

            long[] movieIds = movieTable.AsEnumerable().Select(row => (long)row["movie_id"]).ToArray();

            Assert.That(movieIds, Is.EqualTo(new long[] { 4, 3, 2, 1 }));
            Assert.That(firstPage.Items.Select(item => item.MovieId), Is.EqualTo(movieIds.Take(3)));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void sortId28のstartupPageはmovieIdDescで安定化しunknownFallbackへ束ねない()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            SeedMovieRowsForSortId28Startup(dbPath);
            MainDbMovieReadFacade facade = new();
            MainDbMovieReadRequest errorSortRequest = new(
                dbPath,
                SortId: "28",
                FirstPageSize: 4,
                AppendPageSize: 2
            );
            MainDbMovieReadRequest unknownSortRequest = new(
                dbPath,
                SortId: "999",
                FirstPageSize: 4,
                AppendPageSize: 2
            );

            MainDbMovieReadPageResult errorSortPage = facade.ReadStartupPage(
                errorSortRequest,
                pageIndex: 0
            );
            MainDbMovieReadPageResult unknownSortPage = facade.ReadStartupPage(
                unknownSortRequest,
                pageIndex: 0
            );

            // 28 は起動直後だけ movie_id desc で seed し、unknown fallback とは別仕様で固定する。
            Assert.That(errorSortPage.Items.Select(item => item.MovieId), Is.EqualTo(new long[] { 4, 3, 2, 1 }));
            Assert.That(unknownSortPage.Items.Select(item => item.MovieId), Is.EqualTo(new long[] { 2, 1, 4, 3 }));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void sortId28のfullReloadはpublic経路でORDERBYなしを維持する()
    {
        string source = ReadMainDbMovieReadFacadeSource();
        string loadMovieTableForSortBody = ExtractMethodBody(
            source,
            "public DataTable LoadMovieTableForSort("
        );
        string buildMovieTableOrderBySqlBody = ExtractMethodBody(
            source,
            "private static string BuildMovieTableOrderBySql("
        );

        // full reload は public 経路でも 28 の時だけ ORDER BY を付けず、helper の結果を素通しする。
        Assert.That(
            Regex.IsMatch(
                loadMovieTableForSortBody,
                @"string\.IsNullOrWhiteSpace\(orderBySql\)\s*\?\s*""SELECT \* FROM movie""\s*:\s*\$""SELECT \* FROM movie order by \{orderBySql\}"""
            ),
            Is.True,
            "LoadMovieTableForSort の public 経路で ORDER BY なし分岐が崩れています。"
        );
        Assert.That(
            Regex.IsMatch(buildMovieTableOrderBySqlBody, @"""28""\s*=>\s*"""""),
            Is.True,
            "sortId=28 の full reload が unknown fallback へ束ねられています。"
        );
        Assert.That(
            Regex.IsMatch(buildMovieTableOrderBySqlBody, @"_\s*=>\s*DefaultFallbackOrderBySql"),
            Is.True,
            "unknown sort の既定順定義が見つかりません。"
        );
    }

    [Test]
    public void ReadStartupPage_不正なページ指定は空を返して安全に回る()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            SeedMovieRows(dbPath);
            MainDbMovieReadFacade facade = new();
            MainDbMovieReadRequest request = new(
                dbPath,
                SortId: "0",
                FirstPageSize: 10,
                AppendPageSize: 10
            );

            MainDbMovieReadPageResult page = facade.ReadStartupPage(request, pageIndex: -1);

            Assert.That(page.Items.Length, Is.EqualTo(0));
            Assert.That(page.HasMore, Is.False);
            Assert.That(page.ApproximateTotalCount, Is.EqualTo(0));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    private static string CreateTempMainDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-main-read-{Guid.NewGuid():N}.wb");
        SQLiteConnection.CreateFile(dbPath);

        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE system (
    param TEXT NOT NULL,
    paramater TEXT NOT NULL
);

CREATE TABLE movie (
    movie_id INTEGER PRIMARY KEY,
    movie_name TEXT NOT NULL,
    movie_path TEXT NOT NULL,
    movie_length INTEGER NOT NULL,
    movie_size INTEGER NOT NULL,
    last_date TEXT NOT NULL,
    file_date TEXT NOT NULL,
    regist_date TEXT NOT NULL,
    score INTEGER NOT NULL,
    view_count INTEGER NOT NULL,
    hash TEXT NOT NULL,
    container TEXT NOT NULL,
    video TEXT NOT NULL,
    audio TEXT NOT NULL,
    kana TEXT NOT NULL,
    tag TEXT NOT NULL,
    comment1 TEXT NOT NULL,
    comment2 TEXT NOT NULL,
    comment3 TEXT NOT NULL
);";
        command.ExecuteNonQuery();
        return dbPath;
    }

    private static void SeedSystemRow(string dbPath, string param, string value)
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO system (param, paramater)
VALUES (@param, @value);";
        command.Parameters.AddWithValue("@param", param);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
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
)
VALUES
    (1, 'movie-a', 'C:\movies\a.mp4', 60, 100, '2026-03-18 10:00:00', '2026-03-18 10:00:00', '2026-03-18 10:00:00', 1, 10, 'hash-a', 'mp4', 'h264', 'aac', 'a', '', '', '', ''),
    (2, 'movie-b', 'C:\movies\b.mp4', 70, 300, '2026-03-19 10:00:00', '2026-03-19 10:00:00', '2026-03-19 10:00:00', 2, 20, 'hash-b', 'mp4', 'h264', 'aac', 'b', '', '', '', ''),
    (3, 'movie-c', 'C:\movies\c.mp4', 80, 200, '2026-03-20 10:00:00', '2026-03-20 10:00:00', '2026-03-20 10:00:00', 3, 30, 'hash-c', 'mp4', 'h264', 'aac', 'c', '', '', '', ''),
    (4, 'movie-d', 'C:\movies\d.mp4', 90, 50, '2026-03-20 10:00:00', '2026-03-20 10:00:00', '2026-03-20 10:00:00', 4, 40, 'hash-d', 'mp4', 'h264', 'aac', 'd', '', '', '', '');";
        command.ExecuteNonQuery();
    }

    private static void SeedMovieRowsForSortId28Startup(string dbPath)
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
)
VALUES
    (1, 'movie-a', 'C:\movies\a.mp4', 60, 100, '2026-03-20 10:00:00', '2026-03-20 10:00:00', '2026-03-20 10:00:00', 1, 10, 'hash-a', 'mp4', 'h264', 'aac', 'a', '', '', '', ''),
    (2, 'movie-b', 'C:\movies\b.mp4', 70, 300, '2026-03-21 10:00:00', '2026-03-21 10:00:00', '2026-03-21 10:00:00', 2, 20, 'hash-b', 'mp4', 'h264', 'aac', 'b', '', '', '', ''),
    (3, 'movie-c', 'C:\movies\c.mp4', 80, 200, '2026-03-18 10:00:00', '2026-03-18 10:00:00', '2026-03-18 10:00:00', 3, 30, 'hash-c', 'mp4', 'h264', 'aac', 'c', '', '', '', ''),
    (4, 'movie-d', 'C:\movies\d.mp4', 90, 50, '2026-03-19 10:00:00', '2026-03-19 10:00:00', '2026-03-19 10:00:00', 4, 40, 'hash-d', 'mp4', 'h264', 'aac', 'd', '', '', '', '');";
        command.ExecuteNonQuery();
    }

    private static string ReadMainDbMovieReadFacadeSource()
    {
        string root = FindRepositoryRoot();
        string sourcePath = Path.Combine(root, "Data", "MainDbMovieReadFacade.cs");
        return File.ReadAllText(sourcePath);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IndigoMovieManager_fork.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("リポジトリルートを特定できませんでした。");
        return "";
    }

    private static string ExtractMethodBody(string source, string signaturePrefix)
    {
        int signatureIndex = source.IndexOf(signaturePrefix, StringComparison.Ordinal);
        Assert.That(signatureIndex, Is.GreaterThanOrEqualTo(0), $"{signaturePrefix} が見つかりません。");

        int bodyStart = source.IndexOf('{', signatureIndex);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signaturePrefix} の本体開始が見つかりません。");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[(bodyStart + 1)..index];
                }
            }
        }

        Assert.Fail($"{signaturePrefix} の本体終端が見つかりません。");
        return "";
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
