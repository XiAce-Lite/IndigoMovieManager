using System.Data.SQLite;
using IndigoMovieManager.BottomTabs.TagEditor;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class TagIndexCacheServiceTests
{
    [Test]
    public async Task EnsureSnapshot_movieTagから件数付き一覧を構築できる()
    {
        string dbPath = CreateTempMainDb();
        try
        {
            SeedMovie(dbPath, 1, "猫\r\n出演者/日本人\r\n猫");
            SeedMovie(dbPath, 2, "出演者/日本人\nライブ");
            SeedMovie(dbPath, 3, "");

            TagIndexCacheService service = new();
            TaskCompletionSource<TagIndexSnapshotChangedEventArgs> tcs = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            service.SnapshotUpdated += (_, e) => tcs.TrySetResult(e);

            service.EnsureSnapshot(dbPath);

            TagIndexSnapshotChangedEventArgs changed = await tcs.Task.WaitAsync(
                TimeSpan.FromSeconds(5)
            );
            TagIndexSnapshot snapshot = service.TryGetSnapshot(dbPath);

            Assert.Multiple(() =>
            {
                Assert.That(changed.DbFullPath, Does.EndWith(".wb"));
                Assert.That(snapshot, Is.Not.Null);
                Assert.That(snapshot.TagCounts["猫"], Is.EqualTo(1));
                Assert.That(snapshot.TagCounts["出演者/日本人"], Is.EqualTo(2));
                Assert.That(snapshot.TagCounts["ライブ"], Is.EqualTo(1));
                Assert.That(snapshot.MovieTags[1], Is.EqualTo(["猫", "出演者/日本人"]));
                Assert.That(snapshot.MovieTags[2], Is.EqualTo(["出演者/日本人", "ライブ"]));
            });
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public async Task UpdateMovieTags_追加削除の差分だけ件数を更新できる()
    {
        string dbPath = CreateTempMainDb();
        try
        {
            SeedMovie(dbPath, 1, "anime\r\nidol");
            SeedMovie(dbPath, 2, "idol\nlive");

            TagIndexCacheService service = new();
            TaskCompletionSource<TagIndexSnapshotChangedEventArgs> tcs = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            service.SnapshotUpdated += (_, e) => tcs.TrySetResult(e);

            service.EnsureSnapshot(dbPath);
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            service.UpdateMovieTags(dbPath, 1, ["anime", "fresh"]);
            service.UpdateMovieTags(dbPath, 2, []);

            TagIndexSnapshot snapshot = service.TryGetSnapshot(dbPath);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.TagCounts.ContainsKey("idol"), Is.False);
                Assert.That(snapshot.TagCounts.ContainsKey("live"), Is.False);
                Assert.That(snapshot.TagCounts["anime"], Is.EqualTo(1));
                Assert.That(snapshot.TagCounts["fresh"], Is.EqualTo(1));
                Assert.That(snapshot.MovieTags[1], Is.EqualTo(["anime", "fresh"]));
                Assert.That(snapshot.MovieTags.ContainsKey(2), Is.False);
            });
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    private static string CreateTempMainDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-tag-index-{Guid.NewGuid():N}.wb");
        SQLiteConnection.CreateFile(dbPath);

        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText =
            @"create table movie (
                movie_id integer primary key,
                tag text not null
            );";
        command.ExecuteNonQuery();
        return dbPath;
    }

    private static void SeedMovie(string dbPath, long movieId, string tags)
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText =
            @"insert into movie (movie_id, tag)
              values (@movie_id, @tag);";
        command.Parameters.AddWithValue("@movie_id", movieId);
        command.Parameters.AddWithValue("@tag", tags ?? "");
        command.ExecuteNonQuery();
    }

    private static void TryDeleteFile(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // 一時DB削除失敗はテスト本体の判定を優先する。
            }
        }
    }
}
