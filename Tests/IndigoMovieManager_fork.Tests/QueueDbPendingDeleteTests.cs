using System.Data.SQLite;
using IndigoMovieManager.Thumbnail.QueueDb;
using NUnit.Framework;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class QueueDbPendingDeleteTests
{
    [Test]
    public void DeletePending_未着手Pendingだけ削除してProcessingとDoneは残す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-pending-delete-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;

        try
        {
            DateTime nowUtc = DateTime.UtcNow;
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = @"C:\movie\pending.mp4",
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(@"C:\movie\pending.mp4"),
                        TabIndex = 0,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = @"C:\movie\processing.mp4",
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(@"C:\movie\processing.mp4"),
                        TabIndex = 1,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = @"C:\movie\done.mp4",
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(@"C:\movie\done.mp4"),
                        TabIndex = 2,
                    },
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                "TEST-OWNER",
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc,
                preferredTabIndex: 1
            );
            Assert.That(leased.Count, Is.EqualTo(1));

            int updated = queueDbService.UpdateStatus(
                leased[0].QueueId,
                "TEST-OWNER",
                ThumbnailQueueStatus.Done,
                DateTime.UtcNow
            );
            Assert.That(updated, Is.EqualTo(1));

            // TabIndex=2 を Done にしたいので、その行も一度だけ lease して完了へ寄せる。
            List<QueueDbLeaseItem> doneLease = queueDbService.GetPendingAndLease(
                "TEST-OWNER",
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: DateTime.UtcNow,
                preferredTabIndex: 2
            );
            Assert.That(doneLease.Count, Is.EqualTo(1));
            updated = queueDbService.UpdateStatus(
                doneLease[0].QueueId,
                "TEST-OWNER",
                ThumbnailQueueStatus.Done,
                DateTime.UtcNow
            );
            Assert.That(updated, Is.EqualTo(1));

            // processing 行を別オーナーで lease し直し、処理中として残す。
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = @"C:\movie\processing.mp4",
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(@"C:\movie\processing.mp4"),
                        TabIndex = 1,
                    },
                ],
                DateTime.UtcNow
            );
            leased = queueDbService.GetPendingAndLease(
                "WORKER-2",
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: DateTime.UtcNow,
                preferredTabIndex: 1
            );
            Assert.That(leased.Count, Is.EqualTo(1));

            int deleted = queueDbService.DeletePending();
            Assert.That(deleted, Is.EqualTo(1));

            using SQLiteConnection connection = new($"Data Source={queueDbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT Status
FROM ThumbnailQueue
ORDER BY TabIndex;";

            using SQLiteDataReader reader = command.ExecuteReader();
            List<int> statuses = [];
            while (reader.Read())
            {
                statuses.Add(reader.GetInt32(0));
            }

            Assert.That(
                statuses,
                Is.EqualTo(
                    new[]
                    {
                        (int)ThumbnailQueueStatus.Processing,
                        (int)ThumbnailQueueStatus.Done,
                    }
                )
            );
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void DeletePendingUpperTabsExcept_未選択上側タブのPendingだけ削除する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-upper-pending-delete-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;

        try
        {
            DateTime nowUtc = DateTime.UtcNow;
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = @"C:\movie\tab0-pending.mp4",
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(@"C:\movie\tab0-pending.mp4"),
                        TabIndex = 0,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = @"C:\movie\tab1-pending.mp4",
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(@"C:\movie\tab1-pending.mp4"),
                        TabIndex = 1,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = @"C:\movie\tab2-processing.mp4",
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(@"C:\movie\tab2-processing.mp4"),
                        TabIndex = 2,
                    },
                    new QueueDbUpsertItem
                    {
                        MoviePath = @"C:\movie\detail-pending.mp4",
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(@"C:\movie\detail-pending.mp4"),
                        TabIndex = 99,
                    },
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                "TEST-OWNER",
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc,
                preferredTabIndex: 2
            );
            Assert.That(leased.Count, Is.EqualTo(1));
            Assert.That(leased[0].TabIndex, Is.EqualTo(2));

            int deleted = queueDbService.DeletePendingUpperTabsExcept(selectedTabIndex: 1);
            Assert.That(deleted, Is.EqualTo(1));

            using SQLiteConnection connection = new($"Data Source={queueDbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT TabIndex, Status
FROM ThumbnailQueue
ORDER BY TabIndex ASC;";

            using SQLiteDataReader reader = command.ExecuteReader();
            List<(int TabIndex, int Status)> rows = [];
            while (reader.Read())
            {
                rows.Add((reader.GetInt32(0), reader.GetInt32(1)));
            }

            Assert.That(
                rows,
                Is.EqualTo(
                    new[]
                    {
                        (1, (int)ThumbnailQueueStatus.Pending),
                        (2, (int)ThumbnailQueueStatus.Processing),
                        (99, (int)ThumbnailQueueStatus.Pending),
                    }
                )
            );
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
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
            // 一時DB削除失敗はテスト結果に影響しないため握る。
        }
    }
}
