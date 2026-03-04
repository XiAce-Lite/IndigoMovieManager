using System.Data.SQLite;
using System.Globalization;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class QueueDbPathResolverTests
{
    [Test]
    public void ResolveQueueDbPath_拡張子はQueueImmになる()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-ext-{Guid.NewGuid():N}.wb"
        );

        string resolved = QueueDbPathResolver.ResolveQueueDbPath(mainDbPath);
        string fileName = Path.GetFileName(resolved);

        Assert.That(fileName, Does.EndWith(".queue.imm"));
    }

    [Test]
    public void CreateMoviePathKey_拡張長パス接頭辞ありでも同一キーを返す()
    {
        // 同じ実体パスを通常表記と \\?\ 表記で用意する。
        string normalPath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_key_test",
            "movie.mp4"
        );
        string extendedPath = $@"\\?\{normalPath}";

        string normalKey = QueueDbPathResolver.CreateMoviePathKey(normalPath);
        string extendedKey = QueueDbPathResolver.CreateMoviePathKey(extendedPath);

        Assert.That(extendedKey, Is.EqualTo(normalKey));
    }

    [Test]
    public void Upsert_同一動画の表記ゆれは1行に集約される()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);

        string queueDbPath = queueDbService.QueueDbFullPath;
        string normalPath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_upsert_test",
            $"movie-{Guid.NewGuid():N}.mp4"
        );
        string extendedPath = $@"\\?\{normalPath}";

        try
        {
            // 同じ動画を表記違いで2回投入し、QueueDB上で1行に保たれることを確認する。
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = normalPath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(normalPath),
                        TabIndex = 2,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = extendedPath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(extendedPath),
                        TabIndex = 2,
                    },
                ],
                DateTime.UtcNow
            );

            using SQLiteConnection connection = new($"Data Source={queueDbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM ThumbnailQueue
WHERE TabIndex = @TabIndex;";
            command.Parameters.AddWithValue("@TabIndex", 2);
            int count = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);

            Assert.That(count, Is.EqualTo(1));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void UpsertAndLease_動画サイズを保持して取得できる()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-size-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_size_test",
            $"movie-{Guid.NewGuid():N}.mkv"
        );
        long expectedSizeBytes = 72L * 1024 * 1024 * 1024;

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 0,
                        MovieSizeBytes = expectedSizeBytes,
                    },
                ],
                DateTime.UtcNow
            );

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                "TEST-OWNER",
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: DateTime.UtcNow
            );
            Assert.That(leased.Count, Is.EqualTo(1));
            Assert.That(leased[0].MovieSizeBytes, Is.EqualTo(expectedSizeBytes));

            using SQLiteConnection connection = new($"Data Source={queueDbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT MovieSizeBytes
FROM ThumbnailQueue
WHERE TabIndex = @TabIndex
LIMIT 1;";
            command.Parameters.AddWithValue("@TabIndex", 0);
            long stored = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            Assert.That(stored, Is.EqualTo(expectedSizeBytes));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void EnsureInitialized_旧QueueDBでもMovieSizeBytes列を自動追加する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-legacy-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_legacy_test",
            $"movie-{Guid.NewGuid():N}.mp4"
        );

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(queueDbPath) ?? "");
            using (SQLiteConnection legacyConnection = new($"Data Source={queueDbPath}"))
            {
                legacyConnection.Open();
                using SQLiteCommand createTableCommand = legacyConnection.CreateCommand();
            // 旧形式: MovieSizeBytes 列なし。
                createTableCommand.CommandText = @"
CREATE TABLE IF NOT EXISTS ThumbnailQueue (
    QueueId INTEGER PRIMARY KEY AUTOINCREMENT,
    MainDbPathHash TEXT NOT NULL,
    MoviePath TEXT NOT NULL,
    MoviePathKey TEXT NOT NULL,
    TabIndex INTEGER NOT NULL,
    ThumbPanelPos INTEGER,
    ThumbTimePos INTEGER,
    Status INTEGER NOT NULL DEFAULT 0,
    AttemptCount INTEGER NOT NULL DEFAULT 0,
    LastError TEXT NOT NULL DEFAULT '',
    OwnerInstanceId TEXT NOT NULL DEFAULT '',
    LeaseUntilUtc TEXT NOT NULL DEFAULT '',
    CreatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UpdatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UNIQUE (MainDbPathHash, MoviePathKey, TabIndex)
);";
                createTableCommand.ExecuteNonQuery();
            }

            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 1,
                        MovieSizeBytes = 123456789,
                    },
                ],
                DateTime.UtcNow
            );

            using SQLiteConnection verifyConnection = new($"Data Source={queueDbPath}");
            verifyConnection.Open();
            using SQLiteCommand columnCheckCommand = verifyConnection.CreateCommand();
            columnCheckCommand.CommandText = @"
SELECT COUNT(1)
FROM pragma_table_info('ThumbnailQueue')
WHERE name = 'MovieSizeBytes';";
            int columnCount = Convert.ToInt32(
                columnCheckCommand.ExecuteScalar(),
                CultureInfo.InvariantCulture
            );
            Assert.That(columnCount, Is.EqualTo(1));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    // テスト後のQueueDBを掃除して、ローカル環境を汚さない。
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
            // 一時ファイル削除失敗はテスト結果に影響しないため握りつぶす。
        }
    }
}
