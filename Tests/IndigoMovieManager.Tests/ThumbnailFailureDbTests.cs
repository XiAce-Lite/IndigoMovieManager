using System.Data.SQLite;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailFailureDbTests
{
    [Test]
    public void ResolveFailureDbPath_拡張子はFailureImmになる()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-main-{Guid.NewGuid():N}.wb"
        );

        string resolved = ThumbnailFailureDbPathResolver.ResolveFailureDbPath(mainDbPath);

        Assert.That(Path.GetFileName(resolved), Does.EndWith(".failure.imm"));
    }

    [Test]
    public void AppendFailureRecord_GetFailureRecordsで新しい順に返る()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-records-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string dbPath = service.FailureDbFullPath;

        try
        {
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\older.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\older.mkv"),
                    TabIndex = 2,
                    Lane = "normal",
                    AttemptGroupId = Guid.NewGuid().ToString("N"),
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "older",
                    SourcePath = @"E:\movies\older.mkv",
                    CreatedAtUtc = new DateTime(2026, 3, 14, 1, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc = new DateTime(2026, 3, 14, 1, 0, 1, DateTimeKind.Utc),
                }
            );
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\newer.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\newer.mkv"),
                    TabIndex = 3,
                    Lane = "slow",
                    AttemptGroupId = Guid.NewGuid().ToString("N"),
                    AttemptNo = 5,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.FileMissing,
                    FailureReason = "newer",
                    SourcePath = @"E:\movies\newer.mkv",
                    CreatedAtUtc = new DateTime(2026, 3, 14, 2, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc = new DateTime(2026, 3, 14, 2, 0, 1, DateTimeKind.Utc),
                }
            );

            List<ThumbnailFailureRecord> records = service.GetFailureRecords();

            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That(records[0].MoviePath, Is.EqualTo(@"E:\movies\newer.mkv"));
            Assert.That(records[0].FailureKind, Is.EqualTo(ThumbnailFailureKind.FileMissing));
            Assert.That(records[0].Status, Is.EqualTo("pending_rescue"));
            Assert.That(records[1].MoviePath, Is.EqualTo(@"E:\movies\older.mkv"));
            Assert.That(records[1].MainDbPathHash, Is.EqualTo(QueueDbPathResolver.GetMainDbPathHash8(mainDbPath)));
        }
        finally
        {
            TryDeleteSqliteFamily(dbPath);
        }
    }

    [Test]
    public void GetFailureRecordById_現在DB配下の対象行だけ返す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-record-by-id-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string dbPath = service.FailureDbFullPath;

        try
        {
            long failureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\manual-success.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(
                        @"E:\movies\manual-success.mkv"
                    ),
                    TabIndex = 2,
                    Lane = "normal",
                    AttemptGroupId = Guid.NewGuid().ToString("N"),
                    AttemptNo = 1,
                    Status = "processing_rescue",
                    LeaseOwner = "manual-slot",
                    OutputThumbPath = @"E:\thumbs\grid.#hash.jpg",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "processing",
                    SourcePath = @"E:\movies\manual-success.mkv",
                }
            );

            ThumbnailFailureRecord record = service.GetFailureRecordById(failureId);

            Assert.That(record, Is.Not.Null);
            Assert.That(record!.FailureId, Is.EqualTo(failureId));
            Assert.That(record.TabIndex, Is.EqualTo(2));
            Assert.That(record.Status, Is.EqualTo("processing_rescue"));
            Assert.That(record.MoviePath, Is.EqualTo(@"E:\movies\manual-success.mkv"));
            Assert.That(record.OutputThumbPath, Is.EqualTo(@"E:\thumbs\grid.#hash.jpg"));
            Assert.That(service.GetFailureRecordById(failureId + 9999), Is.Null);
        }
        finally
        {
            TryDeleteSqliteFamily(dbPath);
        }
    }

    [Test]
    public void DeleteMainFailureRecords_大量対象でも分割して削除できる()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-delete-many-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string dbPath = service.FailureDbFullPath;

        try
        {
            const int targetCount = 1200;
            List<(string MoviePathKey, int TabIndex)> targets = [];

            for (int i = 0; i < targetCount; i++)
            {
                string moviePath = $@"E:\movies\bulk-delete-{i:D4}.mkv";
                string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath);
                int tabIndex = i % 8;
                string lane = i % 2 == 0 ? "normal" : "slow";

                _ = service.AppendFailureRecord(
                    new ThumbnailFailureRecord
                    {
                        MoviePath = moviePath,
                        MoviePathKey = moviePathKey,
                        TabIndex = tabIndex,
                        Lane = lane,
                        AttemptGroupId = $"group-{i:D4}",
                        AttemptNo = 1,
                        Status = "pending_rescue",
                        FailureKind = ThumbnailFailureKind.Unknown,
                        FailureReason = "bulk delete test",
                        SourcePath = moviePath,
                    }
                );

                targets.Add((moviePathKey, tabIndex));
            }

            string rescueMoviePath = @"E:\movies\bulk-delete-rescue.mkv";
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = rescueMoviePath,
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(rescueMoviePath),
                    TabIndex = 3,
                    Lane = "rescue",
                    AttemptGroupId = "rescue-group",
                    AttemptNo = 1,
                    Status = "attempt_failed",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "rescue should remain",
                    SourcePath = rescueMoviePath,
                }
            );

            int deleted = service.DeleteMainFailureRecords(targets);

            Assert.That(deleted, Is.EqualTo(targetCount));

            List<ThumbnailFailureRecord> remaining = service.GetFailureRecords(limit: targetCount + 10);
            Assert.That(remaining.Count, Is.EqualTo(1));
            Assert.That(remaining[0].Lane, Is.EqualTo("rescue"));
            Assert.That(remaining[0].MoviePath, Is.EqualTo(rescueMoviePath));
        }
        finally
        {
            TryDeleteSqliteFamily(dbPath);
        }
    }

    [Test]
    public void HasOpenRescueRequest_未完了状態だけTrueを返す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-open-request-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string dbPath = service.FailureDbFullPath;
        string moviePath = @"E:\movies\open-request.mkv";
        string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath);

        try
        {
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = moviePath,
                    MoviePathKey = moviePathKey,
                    TabIndex = 2,
                    Lane = "slow",
                    AttemptGroupId = "",
                    AttemptNo = 1,
                    Status = "reflected",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "already reflected",
                    SourcePath = moviePath,
                }
            );

            Assert.That(service.HasOpenRescueRequest(moviePathKey, 2), Is.False);

            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = moviePath,
                    MoviePathKey = moviePathKey,
                    TabIndex = 2,
                    Lane = "slow",
                    AttemptGroupId = "",
                    AttemptNo = 2,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "manual request",
                    SourcePath = moviePath,
                }
            );

            Assert.That(service.HasOpenRescueRequest(moviePathKey, 2), Is.True);
        }
        finally
        {
            TryDeleteSqliteFamily(dbPath);
        }
    }

    [Test]
    public void HasOpenRescueRequest_救済試行ログだけではTrueにならない()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-open-request-rescue-log-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string dbPath = service.FailureDbFullPath;
        string moviePath = @"E:\movies\rescue-log-only.mkv";
        string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath);

        try
        {
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = moviePath,
                    MoviePathKey = moviePathKey,
                    TabIndex = 2,
                    Lane = "rescue",
                    AttemptGroupId = Guid.NewGuid().ToString("N"),
                    AttemptNo = 3,
                    Status = "processing_rescue",
                    LeaseOwner = "legacy-worker",
                    LeaseUntilUtc = "2026-03-14T12:00:00.000Z",
                    Engine = "ffmpeg1pass",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "legacy attempt row",
                    SourcePath = moviePath,
                }
            );

            Assert.That(service.HasOpenRescueRequest(moviePathKey, 2), Is.False);
        }
        finally
        {
            TryDeleteSqliteFamily(dbPath);
        }
    }

    [Test]
    public void HasFailureHistory_同一動画同一タブの履歴有無を返す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-history-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string dbPath = service.FailureDbFullPath;
        string moviePath = @"E:\movies\history-target.mkv";
        string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath);

        try
        {
            Assert.That(service.HasFailureHistory(moviePathKey, 4), Is.False);

            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = moviePath,
                    MoviePathKey = moviePathKey,
                    TabIndex = 4,
                    Lane = "normal",
                    AttemptGroupId = "",
                    AttemptNo = 1,
                    Status = "attempt_failed",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "history",
                    SourcePath = moviePath,
                    CreatedAtUtc = new DateTime(2026, 3, 19, 1, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc = new DateTime(2026, 3, 19, 1, 0, 1, DateTimeKind.Utc),
                }
            );

            Assert.That(service.HasFailureHistory(moviePathKey, 4), Is.True);
            Assert.That(service.HasFailureHistory(moviePathKey, 2), Is.False);
        }
        finally
        {
            TryDeleteSqliteFamily(dbPath);
        }
    }

    [Test]
    public void GetOpenRescueRequestKeys_未完了main行だけを重複なく返す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-open-request-keys-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string dbPath = service.FailureDbFullPath;
        string pendingMoviePath = @"E:\movies\pending-target.mkv";
        string rescuedMoviePath = @"E:\movies\rescued-target.mkv";
        string pendingMovieKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(pendingMoviePath);
        string rescuedMovieKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(rescuedMoviePath);

        try
        {
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = pendingMoviePath,
                    MoviePathKey = pendingMovieKey,
                    TabIndex = 2,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "pending",
                    SourcePath = pendingMoviePath,
                }
            );
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = pendingMoviePath,
                    MoviePathKey = pendingMovieKey,
                    TabIndex = 2,
                    Lane = "normal",
                    AttemptNo = 2,
                    Status = "processing_rescue",
                    LeaseOwner = "worker-1",
                    LeaseUntilUtc = "2026-03-14T12:00:00.000Z",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "processing",
                    SourcePath = pendingMoviePath,
                }
            );
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = rescuedMoviePath,
                    MoviePathKey = rescuedMovieKey,
                    TabIndex = 4,
                    Lane = "slow",
                    AttemptNo = 1,
                    Status = "rescued",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "rescued",
                    SourcePath = rescuedMoviePath,
                }
            );
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\ignored-rescue-attempt.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(
                        @"E:\movies\ignored-rescue-attempt.mkv"
                    ),
                    TabIndex = 1,
                    Lane = "rescue",
                    AttemptNo = 3,
                    Status = "processing_rescue",
                    LeaseOwner = "worker-2",
                    LeaseUntilUtc = "2026-03-14T12:00:00.000Z",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "attempt row",
                    SourcePath = @"E:\movies\ignored-rescue-attempt.mkv",
                }
            );
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\reflected-target.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(
                        @"E:\movies\reflected-target.mkv"
                    ),
                    TabIndex = 3,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "reflected",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "done",
                    SourcePath = @"E:\movies\reflected-target.mkv",
                }
            );

            HashSet<string> keys = service.GetOpenRescueRequestKeys();

            Assert.Multiple(() =>
            {
                Assert.That(keys.Count, Is.EqualTo(2));
                Assert.That(keys.Contains($"{pendingMovieKey}|2"), Is.True);
                Assert.That(keys.Contains($"{rescuedMovieKey}|4"), Is.True);
            });
        }
        finally
        {
            TryDeleteSqliteFamily(dbPath);
        }
    }

    [Test]
    public void EnsureCreated_旧rescue試行processingはAttemptFailedへ移行する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-migrate-attempt-status-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service1 = new(mainDbPath);
        string dbPath = service1.FailureDbFullPath;

        try
        {
            long failureId = service1.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\legacy-attempt.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\legacy-attempt.mkv"),
                    TabIndex = 1,
                    Lane = "rescue",
                    AttemptGroupId = Guid.NewGuid().ToString("N"),
                    AttemptNo = 4,
                    Status = "processing_rescue",
                    LeaseOwner = "legacy-worker",
                    LeaseUntilUtc = "2026-03-14T12:00:00.000Z",
                    Engine = "opencv",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "legacy processing rescue attempt",
                    SourcePath = @"E:\movies\legacy-attempt.mkv",
                }
            );

            ThumbnailFailureDbService service2 = new(mainDbPath);
            ThumbnailFailureRecord persisted = service2
                .GetFailureRecords()
                .Single(x => x.FailureId == failureId);

            Assert.That(persisted.Lane, Is.EqualTo("rescue"));
            Assert.That(persisted.Status, Is.EqualTo("attempt_failed"));
            Assert.That(persisted.LeaseOwner, Is.Empty);
            Assert.That(persisted.LeaseUntilUtc, Is.Empty);
        }
        finally
        {
            TryDeleteSqliteFamily(dbPath);
        }
    }

    [Test]
    public void EnsureInitialized_旧FailureDbでもPriority列とPriorityUntilUtc列を自動追加する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-priority-columns-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string dbPath = service.FailureDbFullPath;

        try
        {
            CreateLegacyFailureDbWithoutPriorityColumns(dbPath);

            service.EnsureInitialized();

            using SQLiteConnection connection = new($"Data Source={dbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT name
FROM pragma_table_info('ThumbnailFailure')
WHERE name IN ('Priority', 'PriorityUntilUtc')
ORDER BY name ASC;";

            List<string> columns = [];
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }

            Assert.That(columns, Is.EquivalentTo(new[] { "Priority", "PriorityUntilUtc" }));
        }
        finally
        {
            TryDeleteSqliteFamily(dbPath);
        }
    }

    [Test]
    public void HandleFailedItem_最終失敗時はFailureDbへPendingRescueを追記する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-failuredb-final-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_failuredb",
            $"missing-{Guid.NewGuid():N}.mkv"
        );

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 3);
            leasedItem.AttemptCount = 0;

            ThumbnailFailureRecorder.HandleFailedItem(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                new FileNotFoundException("movie file not found"),
                laneName: "normal",
                _ => { }
            );

            List<ThumbnailFailureRecord> records = failureDbService.GetFailureRecords();
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].Status, Is.EqualTo("pending_rescue"));
            Assert.That(records[0].Lane, Is.EqualTo("normal"));
            Assert.That(records[0].AttemptNo, Is.EqualTo(1));
            Assert.That(records[0].FailureKind, Is.EqualTo(ThumbnailFailureKind.FileMissing));
            Assert.That(records[0].MoviePath, Is.EqualTo(moviePath));
        }
        finally
        {
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void HandleFailedItem_大容量Timeout時はSlowLaneでHangSuspectedを追記する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-failuredb-slow-timeout-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_failuredb",
            $"slow-timeout-{Guid.NewGuid():N}.mkv"
        );

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(moviePath)!);
            File.WriteAllText(moviePath, "dummy");

            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 1);
            leasedItem.MovieSizeBytes = 2L * 1024L * 1024L * 1024L * 1024L;
            leasedItem.AttemptCount = 0;

            ThumbnailFailureRecorder.HandleFailedItem(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                new TimeoutException("thumbnail timeout"),
                laneName: "slow",
                _ => { }
            );

            ThumbnailFailureRecord record = failureDbService.GetFailureRecords().Single();
            Assert.That(record.Status, Is.EqualTo("pending_rescue"));
            Assert.That(record.Lane, Is.EqualTo("slow"));
            Assert.That(record.FailureKind, Is.EqualTo(ThumbnailFailureKind.HangSuspected));
            Assert.That(record.AttemptNo, Is.EqualTo(1));
        }
        finally
        {
            TryDeleteFile(moviePath);
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void GetPendingRescueAndLease_通常失敗行をProcessingRescueで取得しAttemptGroupIdを採番する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-lease-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;

        try
        {
            long failureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\lease-target.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\lease-target.mkv"),
                    TabIndex = 1,
                    Lane = "normal",
                    AttemptGroupId = "",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    LeaseOwner = "",
                    FailureKind = ThumbnailFailureKind.IndexCorruption,
                    FailureReason = "moov atom not found",
                    SourcePath = @"E:\movies\lease-target.mkv",
                    CreatedAtUtc = new DateTime(2026, 3, 14, 3, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc = new DateTime(2026, 3, 14, 3, 0, 1, DateTimeKind.Utc),
                }
            );

            ThumbnailFailureRecord leased = service.GetPendingRescueAndLease(
                "rescue-worker-1",
                TimeSpan.FromMinutes(5),
                new DateTime(2026, 3, 14, 3, 5, 0, DateTimeKind.Utc)
            );

            Assert.That(leased, Is.Not.Null);
            Assert.That(leased.FailureId, Is.EqualTo(failureId));
            Assert.That(leased.Status, Is.EqualTo("processing_rescue"));
            Assert.That(leased.LeaseOwner, Is.EqualTo("rescue-worker-1"));
            Assert.That(leased.AttemptGroupId, Is.Not.Empty);

            ThumbnailFailureRecord persisted = service
                .GetFailureRecords()
                .Single(x => x.FailureId == failureId);
            Assert.That(persisted.Status, Is.EqualTo("processing_rescue"));
            Assert.That(persisted.LeaseOwner, Is.EqualTo("rescue-worker-1"));
            Assert.That(persisted.AttemptGroupId, Is.EqualTo(leased.AttemptGroupId));
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void PromotePendingRescueRequest_通常pendingを優先へ昇格できる()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-promote-pending-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;
        string moviePath = @"E:\movies\promote-target.mkv";
        string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath);

        try
        {
            long failureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = moviePath,
                    MoviePathKey = moviePathKey,
                    TabIndex = 2,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "seed",
                    SourcePath = moviePath,
                    Priority = ThumbnailQueuePriority.Normal,
                }
            );

            int promoted = service.PromotePendingRescueRequest(
                moviePathKey,
                tabIndex: 2,
                priority: ThumbnailQueuePriority.Preferred,
                utcNow: new DateTime(2026, 3, 17, 1, 0, 0, DateTimeKind.Utc),
                extraJson: "{\"priority\":\"preferred\"}",
                priorityUntilUtc: new DateTime(2026, 3, 17, 1, 0, 45, DateTimeKind.Utc)
            );

            Assert.That(promoted, Is.EqualTo(1));

            ThumbnailFailureRecord persisted = service
                .GetFailureRecords()
                .Single(x => x.FailureId == failureId);
            Assert.That(persisted.Priority, Is.EqualTo(ThumbnailQueuePriority.Preferred));
            Assert.That(
                persisted.PriorityUntilUtc,
                Is.EqualTo("2026-03-17T01:00:45.000Z")
            );
            Assert.That(persisted.ExtraJson, Does.Contain("preferred"));
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void GetPendingRescueAndLease_優先pendingを通常より先にleaseする()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-priority-lease-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;

        try
        {
            long normalFailureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\normal-target.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\normal-target.mkv"),
                    TabIndex = 1,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "normal",
                    SourcePath = @"E:\movies\normal-target.mkv",
                    Priority = ThumbnailQueuePriority.Normal,
                    UpdatedAtUtc = new DateTime(2026, 3, 17, 1, 0, 0, DateTimeKind.Utc),
                }
            );
            long preferredFailureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\preferred-target.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\preferred-target.mkv"),
                    TabIndex = 1,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "preferred",
                    SourcePath = @"E:\movies\preferred-target.mkv",
                    Priority = ThumbnailQueuePriority.Preferred,
                    UpdatedAtUtc = new DateTime(2026, 3, 17, 1, 1, 0, DateTimeKind.Utc),
                }
            );

            ThumbnailFailureRecord leased = service.GetPendingRescueAndLease(
                "rescue-worker-priority",
                TimeSpan.FromMinutes(5),
                new DateTime(2026, 3, 17, 1, 2, 0, DateTimeKind.Utc)
            );

            Assert.That(leased, Is.Not.Null);
            Assert.That(leased.FailureId, Is.EqualTo(preferredFailureId));
            Assert.That(leased.Priority, Is.EqualTo(ThumbnailQueuePriority.Preferred));
            Assert.That(leased.FailureId, Is.Not.EqualTo(normalFailureId));
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void GetPendingRescueAndLease_期限切れ一時優先は通常扱いに落ちる()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-expired-priority-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;

        try
        {
            long olderNormalFailureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\older-normal.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\older-normal.mkv"),
                    TabIndex = 0,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "older normal",
                    SourcePath = @"E:\movies\older-normal.mkv",
                    Priority = ThumbnailQueuePriority.Normal,
                    UpdatedAtUtc = new DateTime(2026, 3, 17, 1, 0, 0, DateTimeKind.Utc),
                }
            );
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\expired-preferred.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\expired-preferred.mkv"),
                    TabIndex = 0,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "expired preferred",
                    SourcePath = @"E:\movies\expired-preferred.mkv",
                    Priority = ThumbnailQueuePriority.Preferred,
                    PriorityUntilUtc = "2026-03-17T01:00:30.000Z",
                    UpdatedAtUtc = new DateTime(2026, 3, 17, 1, 1, 0, DateTimeKind.Utc),
                }
            );

            ThumbnailFailureRecord leased = service.GetPendingRescueAndLease(
                "rescue-worker-expired-priority",
                TimeSpan.FromMinutes(5),
                new DateTime(2026, 3, 17, 1, 2, 0, DateTimeKind.Utc)
            );

            Assert.That(leased, Is.Not.Null);
            Assert.That(leased.FailureId, Is.EqualTo(olderNormalFailureId));
            Assert.That(leased.Priority, Is.EqualTo(ThumbnailQueuePriority.Normal));
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void UpdateFailureStatus_救済成功時はLeaseを解放し出力先を保持する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-update-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;

        try
        {
            long failureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\rescued.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\rescued.mkv"),
                    TabIndex = 2,
                    Lane = "normal",
                    AttemptGroupId = "",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "initial fail",
                    SourcePath = @"E:\movies\rescued.mkv",
                }
            );
            ThumbnailFailureRecord leased = service.GetPendingRescueAndLease(
                "rescue-worker-2",
                TimeSpan.FromMinutes(5),
                DateTime.UtcNow
            );

            int updated = service.UpdateFailureStatus(
                failureId,
                "rescue-worker-2",
                "rescued",
                new DateTime(2026, 3, 14, 4, 0, 0, DateTimeKind.Utc),
                outputThumbPath: @"E:\thumb\rescued.#hash.jpg",
                resultSignature: "rescued:ffmpeg1pass",
                extraJson: "{\"phase\":\"direct\"}",
                clearLease: true
            );

            Assert.That(leased, Is.Not.Null);
            Assert.That(updated, Is.EqualTo(1));

            ThumbnailFailureRecord persisted = service
                .GetFailureRecords()
                .Single(x => x.FailureId == failureId);
            Assert.That(persisted.Status, Is.EqualTo("rescued"));
            Assert.That(persisted.LeaseOwner, Is.Empty);
            Assert.That(persisted.LeaseUntilUtc, Is.Empty);
            Assert.That(persisted.OutputThumbPath, Is.EqualTo(@"E:\thumb\rescued.#hash.jpg"));
            Assert.That(persisted.ResultSignature, Is.EqualTo("rescued:ffmpeg1pass"));
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void UpdateProcessingSnapshot_processing中の親行へ進行情報を書ける()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-progress-snapshot-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;

        try
        {
            long failureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\snapshot-target.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\snapshot-target.mkv"),
                    TabIndex = 2,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "seed",
                    SourcePath = @"E:\movies\snapshot-target.mkv",
                }
            );
            _ = service.GetPendingRescueAndLease(
                "rescue-worker-snapshot",
                TimeSpan.FromMinutes(5),
                new DateTime(2026, 3, 15, 1, 0, 0, DateTimeKind.Utc)
            );

            int updated = service.UpdateProcessingSnapshot(
                failureId,
                "rescue-worker-snapshot",
                new DateTime(2026, 3, 15, 1, 0, 5, DateTimeKind.Utc),
                "{\"RouteId\":\"fixed\",\"CurrentPhase\":\"direct_engine_failed\",\"CurrentEngine\":\"ffmpeg1pass\",\"CurrentFailureKind\":\"HangSuspected\",\"CurrentFailureReason\":\"timeout\"}"
            );

            Assert.That(updated, Is.EqualTo(1));

            ThumbnailFailureRecord persisted = service
                .GetFailureRecords()
                .Single(x => x.FailureId == failureId);
            Assert.That(persisted.Status, Is.EqualTo("processing_rescue"));
            Assert.That(persisted.ExtraJson, Does.Contain("direct_engine_failed"));
            Assert.That(persisted.ExtraJson, Does.Contain("ffmpeg1pass"));
            Assert.That(persisted.ExtraJson, Does.Contain("HangSuspected"));
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void GetLatestRescueDisplayRecord_有効なprocessingを優先して返す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-display-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;

        try
        {
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\pending-display.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\pending-display.mkv"),
                    TabIndex = 2,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "pending",
                    SourcePath = @"E:\movies\pending-display.mkv",
                    CreatedAtUtc = new DateTime(2026, 3, 15, 2, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc = new DateTime(2026, 3, 15, 2, 0, 1, DateTimeKind.Utc),
                }
            );
            long activeFailureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\processing-display.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\processing-display.mkv"),
                    TabIndex = 3,
                    Lane = "slow",
                    AttemptNo = 1,
                    Status = "processing_rescue",
                    LeaseOwner = "rescue-worker-display",
                    LeaseUntilUtc = "2026-03-15T02:11:00.000Z",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "seed",
                    SourcePath = @"E:\movies\processing-display.mkv",
                    CreatedAtUtc = new DateTime(2026, 3, 15, 2, 5, 0, DateTimeKind.Utc),
                    UpdatedAtUtc = new DateTime(2026, 3, 15, 2, 5, 1, DateTimeKind.Utc),
                }
            );

            _ = service.UpdateProcessingSnapshot(
                activeFailureId,
                "rescue-worker-display",
                new DateTime(2026, 3, 15, 2, 6, 5, DateTimeKind.Utc),
                "{\"CurrentPhase\":\"direct_engine_failed\",\"CurrentEngine\":\"ffmpeg1pass\"}"
            );

            ThumbnailFailureRecord display = service.GetLatestRescueDisplayRecord(
                new DateTime(2026, 3, 15, 2, 6, 6, DateTimeKind.Utc)
            );

            Assert.That(display, Is.Not.Null);
            Assert.That(display.FailureId, Is.EqualTo(activeFailureId));
            Assert.That(display.Status, Is.EqualTo("processing_rescue"));
            Assert.That(display.ExtraJson, Does.Contain("direct_engine_failed"));
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public async Task GetPendingRescueAndLease_同時取得でも二重leaseにならない()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-race-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService seedService = new(mainDbPath);
        ThumbnailFailureDbService service1 = new(mainDbPath);
        ThumbnailFailureDbService service2 = new(mainDbPath);
        string failureDbPath = seedService.FailureDbFullPath;

        try
        {
            _ = seedService.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\race-target.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\race-target.mkv"),
                    TabIndex = 4,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "seed",
                    SourcePath = @"E:\movies\race-target.mkv",
                }
            );

            using ManualResetEventSlim startGate = new(false);
            Task<ThumbnailFailureRecord> task1 = Task.Run(() =>
            {
                startGate.Wait();
                return service1.GetPendingRescueAndLease(
                    "rescue-worker-race-1",
                    TimeSpan.FromMinutes(5),
                    DateTime.UtcNow
                );
            });
            Task<ThumbnailFailureRecord> task2 = Task.Run(() =>
            {
                startGate.Wait();
                return service2.GetPendingRescueAndLease(
                    "rescue-worker-race-2",
                    TimeSpan.FromMinutes(5),
                    DateTime.UtcNow
                );
            });

            startGate.Set();
            ThumbnailFailureRecord[] leased = await Task.WhenAll(task1, task2);

            int acquiredCount = leased.Count(x => x != null);
            Assert.That(acquiredCount, Is.EqualTo(1));

            ThumbnailFailureRecord persisted = seedService.GetFailureRecords()
                .Single(x => x.Lane == "normal");
            Assert.That(persisted.Status, Is.EqualTo("processing_rescue"));
            Assert.That(
                persisted.LeaseOwner,
                Is.EqualTo("rescue-worker-race-1").Or.EqualTo("rescue-worker-race-2")
            );
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void HasPendingRescueWork_pendingと期限切れprocessingを検出する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-has-pending-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;

        try
        {
            _ = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\pending-target.mkv",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\pending-target.mkv"),
                    TabIndex = 0,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "seed",
                    SourcePath = @"E:\movies\pending-target.mkv",
                }
            );

            Assert.That(service.HasPendingRescueWork(DateTime.UtcNow), Is.True);

            ThumbnailFailureRecord leased = service.GetPendingRescueAndLease(
                "rescue-worker-pending",
                TimeSpan.FromMinutes(5),
                new DateTime(2026, 3, 14, 5, 0, 0, DateTimeKind.Utc)
            );
            Assert.That(leased, Is.Not.Null);
            Assert.That(
                service.HasPendingRescueWork(new DateTime(2026, 3, 14, 5, 1, 0, DateTimeKind.Utc)),
                Is.False
            );
            Assert.That(
                service.HasPendingRescueWork(new DateTime(2026, 3, 14, 5, 6, 0, DateTimeKind.Utc)),
                Is.True
            );
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void RecoverExpiredProcessingToPendingRescue_期限切れleaseだけpendingへ戻す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-recover-stale-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;

        try
        {
            long expiredFailureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\stale-processing.mp4",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\stale-processing.mp4"),
                    TabIndex = 1,
                    Lane = "normal",
                    AttemptNo = 2,
                    Status = "processing_rescue",
                    LeaseOwner = "rescue-worker-stale",
                    LeaseUntilUtc = "2026-03-15T00:05:00.000Z",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "stale processing",
                    SourcePath = @"E:\movies\stale-processing.mp4",
                }
            );
            long activeFailureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\active-processing.mp4",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\active-processing.mp4"),
                    TabIndex = 2,
                    Lane = "slow",
                    AttemptNo = 2,
                    Status = "processing_rescue",
                    LeaseOwner = "rescue-worker-active",
                    LeaseUntilUtc = "2026-03-15T00:15:00.000Z",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "active processing",
                    SourcePath = @"E:\movies\active-processing.mp4",
                }
            );

            int recovered = service.RecoverExpiredProcessingToPendingRescue(
                new DateTime(2026, 3, 15, 0, 10, 0, DateTimeKind.Utc)
            );

            Assert.That(recovered, Is.EqualTo(1));

            ThumbnailFailureRecord expired = service
                .GetFailureRecords()
                .Single(x => x.FailureId == expiredFailureId);
            ThumbnailFailureRecord active = service
                .GetFailureRecords()
                .Single(x => x.FailureId == activeFailureId);

            Assert.That(expired.Status, Is.EqualTo("pending_rescue"));
            Assert.That(expired.LeaseOwner, Is.Empty);
            Assert.That(expired.LeaseUntilUtc, Is.Empty);
            Assert.That(active.Status, Is.EqualTo("processing_rescue"));
            Assert.That(active.LeaseOwner, Is.EqualTo("rescue-worker-active"));
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void GetRescuedRecordsForSync_未反映rescuedを返しMarkRescuedAsReflectedで閉じる()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-reflect-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;

        try
        {
            long failureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\rescued-sync.mp4",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\rescued-sync.mp4"),
                    TabIndex = 2,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "seed",
                    SourcePath = @"E:\movies\rescued-sync.mp4",
                }
            );

            _ = service.GetPendingRescueAndLease(
                "rescue-worker-sync",
                TimeSpan.FromMinutes(5),
                new DateTime(2026, 3, 14, 6, 0, 0, DateTimeKind.Utc)
            );
            _ = service.UpdateFailureStatus(
                failureId,
                "rescue-worker-sync",
                "rescued",
                new DateTime(2026, 3, 14, 6, 1, 0, DateTimeKind.Utc),
                outputThumbPath: @"E:\thumb\rescued-sync.#hash.jpg",
                extraJson: "{\"phase\":\"rescued\"}",
                clearLease: true
            );

            List<ThumbnailFailureRecord> rescued = service.GetRescuedRecordsForSync(limit: 10);

            Assert.That(rescued.Count, Is.EqualTo(1));
            Assert.That(rescued[0].FailureId, Is.EqualTo(failureId));
            Assert.That(rescued[0].Status, Is.EqualTo("rescued"));

            int updated = service.MarkRescuedAsReflected(
                failureId,
                new DateTime(2026, 3, 14, 6, 2, 0, DateTimeKind.Utc),
                extraJson: "{\"phase\":\"reflected\"}"
            );

            Assert.That(updated, Is.EqualTo(1));
            ThumbnailFailureRecord persisted = service
                .GetFailureRecords()
                .Single(x => x.FailureId == failureId);
            Assert.That(persisted.Status, Is.EqualTo("reflected"));
            Assert.That(persisted.ExtraJson, Does.Contain("reflected"));
            Assert.That(service.GetRescuedRecordsForSync(limit: 10), Is.Empty);
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void SlowLaneFailure_救済leaseからrescued反映まで通る()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-slow-lane-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;

        try
        {
            long failureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\slow-target.mp4",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\slow-target.mp4"),
                    TabIndex = 1,
                    Lane = "slow",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "seed",
                    SourcePath = @"E:\movies\slow-target.mp4",
                }
            );

            Assert.That(
                service.HasPendingRescueWork(new DateTime(2026, 3, 14, 8, 0, 0, DateTimeKind.Utc)),
                Is.True
            );

            ThumbnailFailureRecord leased = service.GetPendingRescueAndLease(
                "rescue-worker-slow",
                TimeSpan.FromMinutes(5),
                new DateTime(2026, 3, 14, 8, 0, 0, DateTimeKind.Utc)
            );
            Assert.That(leased, Is.Not.Null);
            Assert.That(leased.FailureId, Is.EqualTo(failureId));
            Assert.That(leased.Lane, Is.EqualTo("slow"));

            int rescued = service.UpdateFailureStatus(
                failureId,
                "rescue-worker-slow",
                "rescued",
                new DateTime(2026, 3, 14, 8, 1, 0, DateTimeKind.Utc),
                outputThumbPath: @"E:\thumb\slow-target.#hash.jpg",
                clearLease: true
            );
            Assert.That(rescued, Is.EqualTo(1));

            List<ThumbnailFailureRecord> rescuedRecords = service.GetRescuedRecordsForSync(limit: 10);
            Assert.That(rescuedRecords.Count, Is.EqualTo(1));
            Assert.That(rescuedRecords[0].FailureId, Is.EqualTo(failureId));
            Assert.That(rescuedRecords[0].Lane, Is.EqualTo("slow"));

            int reflected = service.MarkRescuedAsReflected(
                failureId,
                new DateTime(2026, 3, 14, 8, 2, 0, DateTimeKind.Utc),
                extraJson: "{\"phase\":\"reflected\"}"
            );
            Assert.That(reflected, Is.EqualTo(1));

            ThumbnailFailureRecord persisted = service
                .GetFailureRecords()
                .Single(x => x.FailureId == failureId);
            Assert.That(persisted.Status, Is.EqualTo("reflected"));
            Assert.That(service.GetRescuedRecordsForSync(limit: 10), Is.Empty);
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void HandleFailedItem_受け取ったLane名でterminalFailureを記録する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-recorder-lane-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = @"E:\movies\terminal-failure.mp4",
                        MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(
                            @"E:\movies\terminal-failure.mp4"
                        ),
                        TabIndex = 2,
                        MovieSizeBytes = 128L * 1024L * 1024L,
                    },
                ],
                new DateTime(2026, 3, 20, 9, 0, 0, DateTimeKind.Utc)
            );
            QueueDbLeaseItem leasedItem = queueDbService
                .GetPendingAndLease(
                    "terminal-failure-owner",
                    1,
                    TimeSpan.FromMinutes(5),
                    new DateTime(2026, 3, 20, 9, 1, 0, DateTimeKind.Utc)
                )
                .Single();

            ThumbnailFailureRecorder.HandleFailedItem(
                queueDbService,
                leasedItem,
                "terminal-failure-owner",
                new InvalidOperationException("thumbnail create failed"),
                laneName: "slow",
                log: null
            );

            ThumbnailFailureRecord persisted = failureDbService.GetFailureRecords().Single();

            Assert.That(persisted.Lane, Is.EqualTo("slow"));
            Assert.That(persisted.Status, Is.EqualTo("pending_rescue"));
            Assert.That(persisted.ExtraJson, Does.Contain("\"HandoffType\":\"failure\""));
        }
        finally
        {
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void ResetRescuedToPendingRescue_出力欠損時はpendingへ戻せる()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-requeue-{Guid.NewGuid():N}.wb"
        );
        ThumbnailFailureDbService service = new(mainDbPath);
        string failureDbPath = service.FailureDbFullPath;

        try
        {
            long failureId = service.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = @"E:\movies\rescued-requeue.mp4",
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(@"E:\movies\rescued-requeue.mp4"),
                    TabIndex = 0,
                    Lane = "normal",
                    AttemptNo = 1,
                    Status = "pending_rescue",
                    FailureKind = ThumbnailFailureKind.Unknown,
                    FailureReason = "seed",
                    SourcePath = @"E:\movies\rescued-requeue.mp4",
                }
            );

            _ = service.GetPendingRescueAndLease(
                "rescue-worker-requeue",
                TimeSpan.FromMinutes(5),
                new DateTime(2026, 3, 14, 7, 0, 0, DateTimeKind.Utc)
            );
            _ = service.UpdateFailureStatus(
                failureId,
                "rescue-worker-requeue",
                "rescued",
                new DateTime(2026, 3, 14, 7, 1, 0, DateTimeKind.Utc),
                outputThumbPath: @"E:\thumb\rescued-requeue.#hash.jpg",
                clearLease: true
            );

            int updated = service.ResetRescuedToPendingRescue(
                failureId,
                new DateTime(2026, 3, 14, 7, 2, 0, DateTimeKind.Utc),
                failureReason: "rescued output missing during sync",
                extraJson: "{\"phase\":\"requeue_output_missing\"}"
            );

            Assert.That(updated, Is.EqualTo(1));
            ThumbnailFailureRecord persisted = service
                .GetFailureRecords()
                .Single(x => x.FailureId == failureId);
            Assert.That(persisted.Status, Is.EqualTo("pending_rescue"));
            Assert.That(persisted.FailureReason, Is.EqualTo("rescued output missing during sync"));
            Assert.That(persisted.ExtraJson, Does.Contain("requeue_output_missing"));
        }
        finally
        {
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    private static QueueDbLeaseItem CreateLeasedItem(
        QueueDbService queueDbService,
        string moviePath,
        int tabIndex
    )
    {
        _ = queueDbService.Upsert(
            [
                new QueueDbUpsertItem
                {
                    MoviePath = moviePath,
                    MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                    TabIndex = tabIndex,
                },
            ],
            DateTime.UtcNow
        );

        List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
            $"LEASE-{Guid.NewGuid():N}",
            takeCount: 1,
            leaseDuration: TimeSpan.FromMinutes(5),
            utcNow: DateTime.UtcNow
        );
        Assert.That(leased.Count, Is.EqualTo(1));
        return leased[0];
    }

    private static void TryDeleteSqliteFamily(string dbPath)
    {
        TryDeleteFile(dbPath);
        TryDeleteFile(dbPath + "-wal");
        TryDeleteFile(dbPath + "-shm");
    }

    private static void CreateLegacyFailureDbWithoutPriorityColumns(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE ThumbnailFailure (
    FailureId INTEGER PRIMARY KEY AUTOINCREMENT,
    MainDbFullPath TEXT NOT NULL DEFAULT '',
    MainDbPathHash TEXT NOT NULL DEFAULT '',
    MoviePath TEXT NOT NULL DEFAULT '',
    MoviePathKey TEXT NOT NULL DEFAULT '',
    TabIndex INTEGER NOT NULL DEFAULT 0,
    Lane TEXT NOT NULL DEFAULT '',
    AttemptGroupId TEXT NOT NULL DEFAULT '',
    AttemptNo INTEGER NOT NULL DEFAULT 0,
    Status TEXT NOT NULL DEFAULT '',
    LeaseOwner TEXT NOT NULL DEFAULT '',
    LeaseUntilUtc TEXT NOT NULL DEFAULT '',
    Engine TEXT NOT NULL DEFAULT '',
    FailureKind TEXT NOT NULL DEFAULT 'Unknown',
    FailureReason TEXT NOT NULL DEFAULT '',
    ElapsedMs INTEGER NOT NULL DEFAULT 0,
    SourcePath TEXT NOT NULL DEFAULT '',
    OutputThumbPath TEXT NOT NULL DEFAULT '',
    RepairApplied INTEGER NOT NULL DEFAULT 0,
    ResultSignature TEXT NOT NULL DEFAULT '',
    ExtraJson TEXT NOT NULL DEFAULT '',
    CreatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UpdatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);";
        command.ExecuteNonQuery();
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
}
