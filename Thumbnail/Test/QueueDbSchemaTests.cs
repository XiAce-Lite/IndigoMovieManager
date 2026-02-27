using System;
using System.Data.SQLite;
using System.IO;
using NUnit.Framework;

namespace IndigoMovieManager.Thumbnail.Test
{
    [TestFixture]
    public class QueueDbSchemaTests
    {
        private string testDbPath;
        private string connectionString;

        [SetUp]
        public void Setup()
        {
            testDbPath = Path.Combine(Path.GetTempPath(), $"TestQueueDb_{Guid.NewGuid()}.db");
            connectionString = $"Data Source={testDbPath};Version=3;";
        }

        [TearDown]
        public void Teardown()
        {
            if (File.Exists(testDbPath))
            {
                // SQLite のファイルロックを確実に解放させる為の GC / File 削除
                GC.Collect();
                GC.WaitForPendingFinalizers();
                try
                {
                    File.Delete(testDbPath);
                }
                catch
                { /* Ignore */
                }
            }
        }

        [Test]
        public void EnsureSchemaCreated_CreatesTableAndIndexes()
        {
            // Act
            QueueDbSchema.EnsureSchemaCreated(connectionString);

            // Assert
            Assert.That(File.Exists(testDbPath), Is.True);

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                // 1. テーブルの存在確認
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT name FROM sqlite_master WHERE type='table' AND name='ThumbnailQueue';";
                    var tableName = cmd.ExecuteScalar() as string;
                    Assert.That(tableName, Is.EqualTo("ThumbnailQueue"));
                }

                // 2. インデックスの存在確認
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT name FROM sqlite_master WHERE type='index' AND name='IX_ThumbnailQueue_Status_Lease';";
                    var index1 = cmd.ExecuteScalar() as string;
                    Assert.That(index1, Is.EqualTo("IX_ThumbnailQueue_Status_Lease"));

                    cmd.CommandText =
                        "SELECT name FROM sqlite_master WHERE type='index' AND name='IX_ThumbnailQueue_MainDb';";
                    var index2 = cmd.ExecuteScalar() as string;
                    Assert.That(index2, Is.EqualTo("IX_ThumbnailQueue_MainDb"));

                    cmd.CommandText =
                        "SELECT name FROM sqlite_master WHERE type='index' AND name='IX_ThumbnailQueue_DoneRetention';";
                    var index3 = cmd.ExecuteScalar() as string;
                    Assert.That(index3, Is.EqualTo("IX_ThumbnailQueue_DoneRetention"));
                }

                // 3. PRAGMA の確認 (WAL mode 等は接続時に評価されるか確認)
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA journal_mode;";
                    var journalMode = cmd.ExecuteScalar() as string;
                    // WAL モードになっているはず
                    Assert.That(journalMode, Is.EqualTo("wal").IgnoreCase);
                }
            }
        }
    }
}
