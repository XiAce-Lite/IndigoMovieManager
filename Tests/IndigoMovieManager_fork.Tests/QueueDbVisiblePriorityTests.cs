using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class QueueDbVisiblePriorityTests
{
    [Test]
    public void visible順のMoviePathKeyを同一タブ内で先にleaseする()
    {
        string mainDbPath = CreateMainDbPath("visible-order");
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string movieA = CreateMoviePath("A");
        string movieB = CreateMoviePath("B");
        string movieC = CreateMoviePath("C");
        string movieD = CreateMoviePath("D");
        DateTime nowUtc = DateTime.UtcNow;

        try
        {
            _ = queueDbService.Upsert(
                [
                    CreateUpsertItem(movieA, tabIndex: 0),
                    CreateUpsertItem(movieB, tabIndex: 1),
                    CreateUpsertItem(movieC, tabIndex: 0),
                    CreateUpsertItem(movieD, tabIndex: 0),
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                "TEST-OWNER",
                takeCount: 4,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc.AddSeconds(1),
                preferredTabIndex: 0,
                preferredMoviePathKeys: [movieC, movieA]
            );

            AssertLeaseOrder(leased, movieC, movieA, movieD, movieB);
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void visible優先は他タブよりも現在タブを先に保つ()
    {
        string mainDbPath = CreateMainDbPath("visible-tab-priority");
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string movieA = CreateMoviePath("A");
        string movieB = CreateMoviePath("B");
        string movieC = CreateMoviePath("C");
        DateTime nowUtc = DateTime.UtcNow;

        try
        {
            _ = queueDbService.Upsert(
                [
                    CreateUpsertItem(movieA, tabIndex: 0),
                    CreateUpsertItem(movieB, tabIndex: 2),
                    CreateUpsertItem(movieC, tabIndex: 0),
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                "TEST-OWNER",
                takeCount: 3,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc.AddSeconds(1),
                preferredTabIndex: 0,
                preferredMoviePathKeys: [movieC]
            );

            AssertLeaseOrder(leased, movieC, movieA, movieB);
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void preferredMoviePathKey未指定でもSQLエラーなくleaseできる()
    {
        string mainDbPath = CreateMainDbPath("visible-null-keys");
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string movieA = CreateMoviePath("A");
        string movieB = CreateMoviePath("B");
        DateTime nowUtc = DateTime.UtcNow;

        try
        {
            _ = queueDbService.Upsert(
                [
                    CreateUpsertItem(movieA, tabIndex: 0),
                    CreateUpsertItem(movieB, tabIndex: 1),
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                "TEST-OWNER",
                takeCount: 2,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc.AddSeconds(1),
                preferredTabIndex: 0,
                preferredMoviePathKeys: null
            );

            AssertLeaseOrder(leased, movieA, movieB);
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void 優先は現在タブ通常より先にleaseする()
    {
        string mainDbPath = CreateMainDbPath("priority-before-visible");
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string normalMovie = CreateMoviePath("normal");
        string preferredMovie = CreateMoviePath("preferred");
        DateTime nowUtc = DateTime.UtcNow;

        try
        {
            _ = queueDbService.Upsert(
                [
                    CreateUpsertItem(normalMovie, tabIndex: 0, priority: ThumbnailQueuePriority.Normal),
                    CreateUpsertItem(preferredMovie, tabIndex: 1, priority: ThumbnailQueuePriority.Preferred),
                ],
                nowUtc
            );

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                "TEST-OWNER",
                takeCount: 2,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc.AddSeconds(1),
                preferredTabIndex: 0,
                preferredMoviePathKeys: [normalMovie]
            );

            AssertLeaseOrder(leased, preferredMovie, normalMovie);
            Assert.That(leased[0].Priority, Is.EqualTo(ThumbnailQueuePriority.Preferred));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    [Test]
    public void 優先投入後に通常再投入しても降格しない()
    {
        string mainDbPath = CreateMainDbPath("priority-keep");
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string moviePath = CreateMoviePath("keep-preferred");
        DateTime nowUtc = DateTime.UtcNow;

        try
        {
            _ = queueDbService.Upsert(
                [
                    CreateUpsertItem(moviePath, tabIndex: 0, priority: ThumbnailQueuePriority.Preferred),
                ],
                nowUtc
            );
            _ = queueDbService.Upsert(
                [
                    CreateUpsertItem(moviePath, tabIndex: 0, priority: ThumbnailQueuePriority.Normal),
                ],
                nowUtc.AddSeconds(1)
            );

            List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
                "TEST-OWNER",
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: nowUtc.AddSeconds(2),
                minimumPriority: ThumbnailQueuePriority.Preferred
            );

            Assert.That(leased.Count, Is.EqualTo(1));
            Assert.That(leased[0].MoviePath, Is.EqualTo(moviePath));
            Assert.That(leased[0].Priority, Is.EqualTo(ThumbnailQueuePriority.Preferred));
        }
        finally
        {
            TryDeleteFile(queueDbPath);
        }
    }

    private static QueueDbUpsertItem CreateUpsertItem(
        string moviePath,
        int tabIndex,
        ThumbnailQueuePriority priority = ThumbnailQueuePriority.Normal
    )
    {
        return new QueueDbUpsertItem
        {
            MoviePath = moviePath,
            MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
            TabIndex = tabIndex,
            Priority = priority,
        };
    }

    private static string CreateMainDbPath(string suffix)
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"imm-main-{suffix}-{Guid.NewGuid():N}.wb"
        );
    }

    private static string CreateMoviePath(string label)
    {
        return Path.Combine(
            Path.GetTempPath(),
            "imm_queue_visible_priority",
            $"{label}-{Guid.NewGuid():N}.mp4"
        );
    }

    private static void AssertLeaseOrder(
        IReadOnlyList<QueueDbLeaseItem> leased,
        params string[] expectedMoviePaths
    )
    {
        Assert.That(leased.Count, Is.EqualTo(expectedMoviePaths.Length));
        for (int index = 0; index < expectedMoviePaths.Length; index++)
        {
            Assert.That(leased[index].MoviePath, Is.EqualTo(expectedMoviePaths[index]));
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
