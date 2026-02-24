using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using NUnit.Framework;

namespace IndigoMovieManager.Thumbnail.Test
{
    // ダミーのモデルクラス群 (実装に合わせて後で調整)
    public class QueueRequest
    {
        public string MainDbPath { get; set; }
        public string MoviePath { get; set; }
        public int TabIndex { get; set; }
        public int ThumbPanelPos { get; set; }
        public int ThumbTimePos { get; set; }
    }

    public class QueueRecord
    {
        public long QueueId { get; set; }
        public string MainDbPathHash { get; set; }
        public string MoviePathKey { get; set; }
        public int TabIndex { get; set; }
        public int Status { get; set; }
        public int AttemptCount { get; set; }
        public string OwnerInstanceId { get; set; }
        public DateTime LeaseUntilUtc { get; set; }
    }

    [TestFixture]
    public class QueueDbServiceTests
    {
        private string testDbPath;
        private string connectionString;

        // DbService本体はプロジェクト本体のクラスを参照する想定ですが、
        // テストプロジェクトとして仮のモックではなく、実体としての統合テストを記述します

        [SetUp]
        public void Setup()
        {
            testDbPath = Path.Combine(Path.GetTempPath(), $"TestServiceDb_{Guid.NewGuid()}.db");
            connectionString = $"Data Source={testDbPath};Version=3;";
            QueueDbSchema.EnsureSchemaCreated(connectionString);
        }

        [TearDown]
        public void Teardown()
        {
            if (File.Exists(testDbPath))
            {
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
        public void Upsert_InsertsNewRecord_AndUpdatesExisting()
        {
            // Arrange
            var service = new QueueDbService(connectionString);
            var req1 = new QueueRequest
            {
                MainDbPath = @"D:\Movies\Test.bw",
                MoviePath = @"D:\Movies\video1.mp4",
                TabIndex = 1,
                ThumbPanelPos = 10,
                ThumbTimePos = 20,
            };

            // Act 1: 最初は Insert になるはず
            int rowsAffected = service.UpsertBatch(new[] { req1 });
            Assert.That(rowsAffected, Is.GreaterThan(0));

            // Act 2: 同じキーで再度 Upsert (Status が Pending(0) にリセットされるかの確認用)
            // 先にダミーで Status を 100 とかに手動更新しておく
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE ThumbnailQueue SET Status = 100 WHERE TabIndex = 1;";
                    cmd.ExecuteNonQuery();
                }
            }

            int rowsAffected2 = service.UpsertBatch(new[] { req1 });

            // Assert 2: UPDATE されて Status = 0 (Pending) に戻っているか
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Status FROM ThumbnailQueue WHERE TabIndex = 1;";
                    long status = (long)cmd.ExecuteScalar();
                    Assert.That(status, Is.EqualTo(0));
                }
            }
        }

        [Test]
        public void GetPendingAndLease_AssignsLease_AndUpdatesStatus()
        {
            // Arrange
            var service = new QueueDbService(connectionString);
            var req1 = new QueueRequest
            {
                MainDbPath = @"D:\DB1.bw",
                MoviePath = @"V1.mp4",
                TabIndex = 1,
            };
            service.UpsertBatch(new[] { req1 });

            string instanceId = Guid.NewGuid().ToString();
            int leaseMinutes = 5;

            // Act
            var leasedRecords = service.GetPendingAndLease(instanceId, count: 1, leaseMinutes);

            // Assert
            Assert.That(leasedRecords.Count, Is.EqualTo(1));
            var record = leasedRecords[0];
            Assert.That(record.Status, Is.EqualTo(1)); // Processing
            Assert.That(record.OwnerInstanceId, Is.EqualTo(instanceId));
            Assert.That(record.LeaseUntilUtc, Is.GreaterThan(DateTime.UtcNow));
            Assert.That(
                record.LeaseUntilUtc,
                Is.LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(leaseMinutes + 1))
            );
        }

        [Test]
        public void GetPendingAndLease_CanStealExpiredLease()
        {
            // Arrange
            var service = new QueueDbService(connectionString);
            var req1 = new QueueRequest
            {
                MainDbPath = @"D:\DB1.bw",
                MoviePath = @"V1.mp4",
                TabIndex = 1,
            };
            service.UpsertBatch(new[] { req1 });

            // 強制的に「誰かがリース中で、かつ期限切れ」の状態を作る
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "UPDATE ThumbnailQueue SET Status = 1, OwnerInstanceId = 'OLD_GUY', LeaseUntilUtc = @past WHERE TabIndex = 1;";
                    cmd.Parameters.AddWithValue(
                        "@past",
                        DateTime.UtcNow.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    );
                    cmd.ExecuteNonQuery();
                }
            }

            string newInstanceId = "NEW_GUY";

            // Act: 新しいやつがリースを奪えるか
            var leasedRecords = service.GetPendingAndLease(
                newInstanceId,
                count: 1,
                leaseMinutes: 5
            );

            // Assert
            Assert.That(leasedRecords.Count, Is.EqualTo(1));
            Assert.That(leasedRecords[0].OwnerInstanceId, Is.EqualTo(newInstanceId));
            Assert.That(leasedRecords[0].Status, Is.EqualTo(1));
        }

        [Test]
        public void UpdateStatus_TransitionsStateCorrectly()
        {
            // Arrange
            var service = new QueueDbService(connectionString);
            var req1 = new QueueRequest
            {
                MainDbPath = @"test",
                MoviePath = @"test",
                TabIndex = 1,
            };
            service.UpsertBatch(new[] { req1 });
            var leased = service.GetPendingAndLease("TEST", 1, 5);
            long qId = leased[0].QueueId;

            // Act: Done に成功した場合
            service.UpdateStatus(qId, status: 2, errorMsg: null);

            // Assert
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Status FROM ThumbnailQueue WHERE QueueId = @id;";
                    cmd.Parameters.AddWithValue("@id", qId);
                    long status = (long)cmd.ExecuteScalar();
                    Assert.That(status, Is.EqualTo(2));
                }
            }
        }
    }
}
