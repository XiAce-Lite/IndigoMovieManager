using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ThumbnailQueueBatchRunnerTests
{
    [TestCase(128L * 1024L * 1024L, "normal", false)]
    [TestCase(4L * 1024L * 1024L * 1024L, "slow", false)]
    [TestCase(128L * 1024L * 1024L, "normal", true)]
    [TestCase(4L * 1024L * 1024L * 1024L, "slow", true)]
    public async Task RunAsync_失敗時もBatchRunnerからHandleFailedItemへLaneを渡す(
        long movieSizeBytes,
        string expectedLane,
        bool useOperationCanceledException
    )
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-batch-runner-lane-{expectedLane}-{Guid.NewGuid():N}.wb"
        );
        string runtimeRoot = Path.Combine(
            Path.GetTempPath(),
            $"imm-batch-runner-runtime-{expectedLane}-{Guid.NewGuid():N}"
        );
        string queueDbDirectoryPath = Path.Combine(runtimeRoot, "QueueDb");
        string failureDbDirectoryPath = Path.Combine(runtimeRoot, "FailureDb");
        ThumbnailQueueHostPathPolicy.Configure(
            queueDbDirectoryPath: queueDbDirectoryPath,
            failureDbDirectoryPath: failureDbDirectoryPath,
            logDirectoryPath: ""
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = CreateTempMovieFile(expectedLane);

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 2,
                        MovieSizeBytes = movieSizeBytes,
                    },
                ],
                new DateTime(2026, 3, 20, 9, 0, 0, DateTimeKind.Utc)
            );
            QueueDbLeaseItem leasedItem = queueDbService
                .GetPendingAndLease(
                    $"batch-runner-owner-{expectedLane}",
                    1,
                    TimeSpan.FromMinutes(5),
                    new DateTime(2026, 3, 20, 9, 1, 0, DateTimeKind.Utc)
                )
                .Single();

            ThumbnailQueueProgressPublisher progressPublisher = new(
                (_, _, _, _) => { },
                NoOpThumbnailQueueProgressPresenter.Instance,
                _ => { },
                _ => { },
                _ => { }
            );
            ThumbnailQueueBatchState batchState = new();
            ThumbnailParallelController parallelController = new(initialParallelism: 4);
            Exception createFailedException = useOperationCanceledException
                ? new OperationCanceledException("thumbnail create canceled")
                : new InvalidOperationException("thumbnail create failed");

            await ThumbnailQueueBatchRunner.RunAsync(
                queueDbService,
                ownerInstanceId: leasedItem.OwnerInstanceId,
                leasedItems: [leasedItem],
                runtimeLeaseBatchSize: 1,
                safeLeaseMinutes: 5,
                preferredTabIndexResolver: () => null,
                preferredMoviePathKeysResolver: static () => [],
                handoffLaneResolver: null,
                createThumbAsync: (_, _) => Task.FromException(createFailedException),
                progressPublisher,
                batchState,
                parallelController,
                resolveLatestConfiguredParallelism: static () => 4,
                log: null,
                cts: CancellationToken.None
            );

            ThumbnailFailureRecord persisted = failureDbService.GetFailureRecords().Single();

            Assert.That(persisted.Status, Is.EqualTo("pending_rescue"));
            Assert.That(persisted.Lane, Is.EqualTo(expectedLane));
            Assert.That(
                persisted.ExtraJson,
                Does.Contain(
                    useOperationCanceledException
                        ? "\"HandoffType\":\"canceled\""
                        : "\"HandoffType\":\"failure\""
                )
            );
        }
        finally
        {
            ThumbnailQueueHostPathPolicy.Configure(
                queueDbDirectoryPath: "",
                failureDbDirectoryPath: "",
                logDirectoryPath: ""
            );
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
            TryDeleteFile(moviePath);
            TryDeleteDirectory(runtimeRoot);
        }
    }

    [Test]
    public async Task RunAsync_handoffLaneResolver指定時はFailureDbへそのLaneを渡す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-batch-runner-handoff-{Guid.NewGuid():N}.wb"
        );
        string runtimeRoot = Path.Combine(
            Path.GetTempPath(),
            $"imm-batch-runner-handoff-runtime-{Guid.NewGuid():N}"
        );
        string queueDbDirectoryPath = Path.Combine(runtimeRoot, "QueueDb");
        string failureDbDirectoryPath = Path.Combine(runtimeRoot, "FailureDb");
        ThumbnailQueueHostPathPolicy.Configure(
            queueDbDirectoryPath: queueDbDirectoryPath,
            failureDbDirectoryPath: failureDbDirectoryPath,
            logDirectoryPath: ""
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = CreateTempMovieFile("handoff-slow");

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = moviePath,
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                        TabIndex = 2,
                        MovieSizeBytes = 128L * 1024L * 1024L,
                    },
                ],
                new DateTime(2026, 3, 20, 9, 0, 0, DateTimeKind.Utc)
            );
            QueueDbLeaseItem leasedItem = queueDbService
                .GetPendingAndLease(
                    "batch-runner-owner-handoff",
                    1,
                    TimeSpan.FromMinutes(5),
                    new DateTime(2026, 3, 20, 9, 1, 0, DateTimeKind.Utc)
                )
                .Single();

            ThumbnailQueueProgressPublisher progressPublisher = new(
                (_, _, _, _) => { },
                NoOpThumbnailQueueProgressPresenter.Instance,
                _ => { },
                _ => { },
                _ => { }
            );
            ThumbnailQueueBatchState batchState = new();
            ThumbnailParallelController parallelController = new(initialParallelism: 4);

            await ThumbnailQueueBatchRunner.RunAsync(
                queueDbService,
                ownerInstanceId: leasedItem.OwnerInstanceId,
                leasedItems: [leasedItem],
                runtimeLeaseBatchSize: 1,
                safeLeaseMinutes: 5,
                preferredTabIndexResolver: () => null,
                preferredMoviePathKeysResolver: static () => [],
                handoffLaneResolver: static _ => "slow",
                createThumbAsync: (_, _) =>
                    Task.FromException(new InvalidOperationException("thumbnail create failed")),
                progressPublisher,
                batchState,
                parallelController,
                resolveLatestConfiguredParallelism: static () => 4,
                log: null,
                cts: CancellationToken.None
            );

            ThumbnailFailureRecord persisted = failureDbService.GetFailureRecords().Single();

            Assert.That(persisted.Lane, Is.EqualTo("slow"));
        }
        finally
        {
            ThumbnailQueueHostPathPolicy.Configure(
                queueDbDirectoryPath: "",
                failureDbDirectoryPath: "",
                logDirectoryPath: ""
            );
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
            TryDeleteFile(moviePath);
            TryDeleteDirectory(runtimeRoot);
        }
    }

    private static string CreateTempMovieFile(string laneName)
    {
        string filePath = Path.Combine(
            Path.GetTempPath(),
            $"imm-batch-runner-{laneName}-{Guid.NewGuid():N}.mp4"
        );
        File.WriteAllBytes(filePath, [1, 2, 3, 4]);
        return filePath;
    }

    private static void TryDeleteSqliteFamily(string dbPath)
    {
        TryDeleteFile(dbPath);
        TryDeleteFile(dbPath + "-wal");
        TryDeleteFile(dbPath + "-shm");
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
            // テスト後の掃除失敗は握りつぶす。
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // テスト後の掃除失敗は握りつぶす。
        }
    }
}
