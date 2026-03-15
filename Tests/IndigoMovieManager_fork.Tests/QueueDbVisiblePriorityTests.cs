using IndigoMovieManager.Thumbnail.QueueDb;

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

    private static QueueDbUpsertItem CreateUpsertItem(string moviePath, int tabIndex)
    {
        return new QueueDbUpsertItem
        {
            MoviePath = moviePath,
            MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
            TabIndex = tabIndex,
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
