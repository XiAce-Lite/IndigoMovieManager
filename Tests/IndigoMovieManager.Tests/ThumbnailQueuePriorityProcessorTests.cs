using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailQueuePriorityProcessorTests
{
    [Test]
    public void SortLeasedItemsByLane_LeaseBucketRankを保って同一bucket内でlane順になる()
    {
        List<QueueDbLeaseItem> leasedItems =
        [
            new QueueDbLeaseItem
            {
                MoviePath = "bucket1-normal.mp4",
                MovieSizeBytes = 10,
                Priority = ThumbnailQueuePriority.Preferred,
                LeaseBucketRank = 1,
                LeaseOrder = 0,
            },
            new QueueDbLeaseItem
            {
                MoviePath = "bucket0-slow.mp4",
                MovieSizeBytes = 9L * 1024 * 1024 * 1024,
                Priority = ThumbnailQueuePriority.Preferred,
                LeaseBucketRank = 0,
                LeaseOrder = 1,
            },
            new QueueDbLeaseItem
            {
                MoviePath = "bucket0-normal.mp4",
                MovieSizeBytes = 10,
                Priority = ThumbnailQueuePriority.Preferred,
                LeaseBucketRank = 0,
                LeaseOrder = 2,
            },
        ];

        ThumbnailLeaseAcquirer.SortLeasedItemsByLane(leasedItems);

        Assert.That(
            leasedItems.Select(static item => item.MoviePath).ToArray(),
            Is.EqualTo(new[] { "bucket0-normal.mp4", "bucket0-slow.mp4", "bucket1-normal.mp4" })
        );
    }

    [Test]
    public void TryFrontInsertPreferredLeaseItems_未着手通常bufferの前へ優先を差し込む()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-main-priority-buffer-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string normalMoviePath = Path.Combine(Path.GetTempPath(), $"normal-{Guid.NewGuid():N}.mp4");
        string preferredMoviePath = Path.Combine(
            Path.GetTempPath(),
            $"preferred-{Guid.NewGuid():N}.mp4"
        );

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = normalMoviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(normalMoviePath),
                        TabIndex = 0,
                        Priority = ThumbnailQueuePriority.Normal,
                    },
                ],
                DateTime.UtcNow
            );

            List<QueueDbLeaseItem> normalLease = queueDbService.GetPendingAndLease(
                "TEST-OWNER",
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: DateTime.UtcNow.AddSeconds(1)
            );
            Assert.That(normalLease.Count, Is.EqualTo(1));

            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = preferredMoviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(preferredMoviePath),
                        TabIndex = 0,
                        Priority = ThumbnailQueuePriority.Preferred,
                    },
                ],
                DateTime.UtcNow.AddSeconds(2)
            );

            LinkedList<QueueDbLeaseItem> buffer = new();
            buffer.AddLast(normalLease[0]);

            bool result = ThumbnailLeaseBuffer.TryFrontInsertPreferredLeaseItems(
                queueDbService,
                "TEST-OWNER",
                5,
                new Func<int?>(() => 0),
                new Func<IReadOnlyList<string>>(() => Array.Empty<string>()),
                new Action<string>(_ => { }),
                buffer
            );

            Assert.That(result, Is.EqualTo(true));
            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.First?.Value.MoviePath, Is.EqualTo(preferredMoviePath));
            Assert.That(buffer.First?.Value.Priority, Is.EqualTo(ThumbnailQueuePriority.Preferred));
            Assert.That(buffer.Last?.Value.MoviePath, Is.EqualTo(normalMoviePath));
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
