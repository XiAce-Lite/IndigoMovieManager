using IndigoMovieManager;
using IndigoMovieManager.Data;
using System.Data.SQLite;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WatcherRenameBridgePolicyTests
{
    [Test]
    public void TryBuildRenamedThumbnailPath_ハッシュ付きjpgは本体名だけ差し替える()
    {
        string result = ThumbnailRenameAssetTransferHelper.TryBuildRenamedThumbnailPath(
            @"C:\thumb\small\old-name.#abc12345.jpg",
            @"C:\movie\old-name.mp4",
            @"C:\movie\new-name.mkv"
        );

        Assert.That(result, Is.EqualTo(@"C:\thumb\small\new-name.#abc12345.jpg"));
    }

    [Test]
    public void TryBuildRenamedThumbnailPath_無関係なファイル名は空を返す()
    {
        string result = ThumbnailRenameAssetTransferHelper.TryBuildRenamedThumbnailPath(
            @"C:\thumb\small\other-video.#abc12345.jpg",
            @"C:\movie\old-name.mp4",
            @"C:\movie\new-name.mkv"
        );

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildRenameOperations_ERRORマーカーもexact一致で改名対象に含める()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-thumb-{Guid.NewGuid():N}");
        string sourcePath = Path.Combine(tempRoot, "small", "old-name.#ERROR.jpg");
        string unrelatedPath = Path.Combine(tempRoot, "small", "other-name.#ERROR.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllBytes(sourcePath, [0x1]);
        File.WriteAllBytes(unrelatedPath, [0x2]);

        try
        {
            MovieRecords movie = new() { Hash = "hash-123" };

            List<ThumbnailRenameAssetTransferHelper.ThumbnailRenameOperation> operations =
                ThumbnailRenameAssetTransferHelper.BuildRenameOperations(
                    movie,
                    tempRoot,
                    @"C:\movie\old-name.mp4",
                    @"C:\movie\new-name.mkv",
                    canRenameHashedThumbnailAssets: true,
                    canRenameErrorMarkerAssets: true
                );

            Assert.That(
                operations,
                Has.One.Matches<ThumbnailRenameAssetTransferHelper.ThumbnailRenameOperation>(
                    operation =>
                        string.Equals(
                            operation.SourcePath,
                            sourcePath,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && string.Equals(
                            operation.DestinationPath,
                            Path.Combine(tempRoot, "small", "new-name.#ERROR.jpg"),
                            StringComparison.OrdinalIgnoreCase
                        )
                )
            );
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void BuildRenameOperations_legacyHashJpgは所有判定できる物だけ改名対象に含める()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-thumb-{Guid.NewGuid():N}");
        string legacyOwnedPath = Path.Combine(tempRoot, "small", "old-name.#hash-123.legacy.jpg");
        string unrelatedExtendedHashPath = Path.Combine(tempRoot, "small", "old-name.#hash-123x.jpg");
        string unrelatedBodyPath = Path.Combine(tempRoot, "small", "other-name.#hash-123.legacy.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyOwnedPath)!);
        File.WriteAllBytes(legacyOwnedPath, [0x1]);
        File.WriteAllBytes(unrelatedExtendedHashPath, [0x2]);
        File.WriteAllBytes(unrelatedBodyPath, [0x3]);

        try
        {
            MovieRecords movie = new() { Hash = "hash-123" };

            List<ThumbnailRenameAssetTransferHelper.ThumbnailRenameOperation> operations =
                ThumbnailRenameAssetTransferHelper.BuildRenameOperations(
                    movie,
                    tempRoot,
                    @"C:\movie\old-name.mp4",
                    @"C:\movie\new-name.mkv",
                    canRenameHashedThumbnailAssets: true,
                    canRenameErrorMarkerAssets: false
                );

            Assert.That(
                operations,
                Has.One.Matches<ThumbnailRenameAssetTransferHelper.ThumbnailRenameOperation>(
                    operation =>
                        string.Equals(
                            operation.SourcePath,
                            legacyOwnedPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && string.Equals(
                            operation.DestinationPath,
                            Path.Combine(tempRoot, "small", "new-name.#hash-123.legacy.jpg"),
                            StringComparison.OrdinalIgnoreCase
                        )
                )
            );
            Assert.That(
                operations.Any(operation =>
                    string.Equals(
                        operation.SourcePath,
                        unrelatedExtendedHashPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
                Is.False
            );
            Assert.That(
                operations.Any(operation =>
                    string.Equals(
                        operation.SourcePath,
                        unrelatedBodyPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
                Is.False
            );
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void BuildRenameOperations_sharedOwnerGuard中はhashjpgとERRORマーカーを改名対象に含めない()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-thumb-{Guid.NewGuid():N}");
        string hashThumbPath = Path.Combine(tempRoot, "small", "old-name.#hash-123.jpg");
        string errorMarkerPath = Path.Combine(tempRoot, "small", "old-name.#ERROR.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(hashThumbPath)!);
        File.WriteAllBytes(hashThumbPath, [0x1]);
        File.WriteAllBytes(errorMarkerPath, [0x2]);

        try
        {
            MovieRecords movie = new()
            {
                Hash = "hash-123",
                ThumbPathSmall = hashThumbPath,
                ThumbDetail = errorMarkerPath,
            };

            List<ThumbnailRenameAssetTransferHelper.ThumbnailRenameOperation> operations =
                ThumbnailRenameAssetTransferHelper.BuildRenameOperations(
                    movie,
                    tempRoot,
                    @"C:\movie\old-name.mp4",
                    @"C:\movie\new-name.mkv",
                    canRenameHashedThumbnailAssets: false,
                    canRenameErrorMarkerAssets: false
                );

            Assert.That(operations, Is.Empty);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void TryBuildRenamedBookmarkAssetPath_旧動画名プレフィックスだけ改名する()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-bookmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string sourcePath = Path.Combine(tempRoot, "old-name[(120)12-34-56].jpg");
        File.WriteAllBytes(sourcePath, [0x1]);

        try
        {
            string result = MainWindow.TryBuildRenamedBookmarkAssetPath(
                sourcePath,
                tempRoot,
                "old-name",
                "new-name"
            );

            Assert.That(
                result,
                Is.EqualTo(Path.Combine(tempRoot, "new-name[(120)12-34-56].jpg"))
            );
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void TryBuildRenamedBookmarkAssetPath_部分一致の別動画名は対象外にする()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-bookmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string sourcePath = Path.Combine(tempRoot, "wild-old-name[(120)12-34-56].jpg");
        File.WriteAllBytes(sourcePath, [0x1]);

        try
        {
            string result = MainWindow.TryBuildRenamedBookmarkAssetPath(
                sourcePath,
                tempRoot,
                "old-name",
                "new-name"
            );

            Assert.That(result, Is.Empty);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void CanRenameBookmarkAssetsSafely_他所有者がいなければtrue()
    {
        bool result = MainWindow.CanRenameBookmarkAssetsSafely(0, 0);

        Assert.That(result, Is.True);
    }

    [Test]
    public void CanRenameBookmarkAssetsSafely_旧名か新名に他所有者がいればfalse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MainWindow.CanRenameBookmarkAssetsSafely(1, 0), Is.False);
            Assert.That(MainWindow.CanRenameBookmarkAssetsSafely(0, 1), Is.False);
        });
    }

    [Test]
    public void CanRenameThumbnailAssetsSafely_同名同hashの他所有者がいればfalse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MainWindow.CanRenameThumbnailAssetsSafely(1, 0), Is.False);
            Assert.That(MainWindow.CanRenameThumbnailAssetsSafely(0, 1), Is.False);
            Assert.That(MainWindow.CanRenameThumbnailAssetsSafely(0, 0), Is.True);
        });
    }

    [Test]
    public void ResolveMovieNameForRenameBridge_Movie_Nameは表示名を返す()
    {
        string result = MainWindow.ResolveMovieNameForRenameBridge(@"C:\movie\new-name.mkv");

        Assert.That(result, Is.EqualTo("new-name.mkv"));
    }

    [Test]
    public void ResolveMovieBodyForRenameBridge_DB更新用にはbodyだけを返す()
    {
        string result = MainWindow.ResolveMovieBodyForRenameBridge(@"C:\movie\new-name.mkv");

        Assert.That(result, Is.EqualTo("new-name"));
    }

    [Test]
    public void ApplyMovieRenameStateCore_拡張子変更とフォルダ移動後のメタを新pathへ揃える()
    {
        MovieRecords movie = new()
        {
            Movie_Path = @"C:\movie\old-name.mp4",
            Movie_Name = "old-name.mp4",
            Movie_Body = "old-name",
            Ext = ".mp4",
            Drive = "C:\\",
            Dir = @"C:\movie",
        };

        MainWindow.ApplyMovieRenameStateCore(
            movie,
            @"D:\archive\new-name.mkv",
            "new-name.mkv",
            "new-name"
        );

        Assert.Multiple(() =>
        {
            Assert.That(movie.Movie_Path, Is.EqualTo(@"D:\archive\new-name.mkv"));
            Assert.That(movie.Movie_Name, Is.EqualTo("new-name.mkv"));
            Assert.That(movie.Movie_Body, Is.EqualTo("new-name"));
            Assert.That(movie.Ext, Is.EqualTo(".mkv"));
            Assert.That(movie.Drive, Is.EqualTo("D:\\"));
            Assert.That(movie.Dir, Is.EqualTo(@"D:\archive"));
        });
    }

    [Test]
    public void ShouldRenameBookmarkTableEntries_jpg有無を見ずに安全判定だけで決める()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                MainWindow.ShouldRenameBookmarkTableEntries(true, "old-name", "new-name"),
                Is.True
            );
            Assert.That(
                MainWindow.ShouldRenameBookmarkTableEntries(true, "same-name", "same-name"),
                Is.False
            );
            Assert.That(
                MainWindow.ShouldRenameBookmarkTableEntries(false, "old-name", "new-name"),
                Is.False
            );
        });
    }

    [Test]
    public void RenameBookmarkTableEntries_LIKEワイルドカードをescapeして別動画行を巻き込まない()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-bookmark-db-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string dbPath = Path.Combine(tempRoot, "bookmark.db");

        try
        {
            using (SQLiteConnection connection = new($"Data Source={dbPath}"))
            {
                connection.Open();

                using SQLiteCommand create = connection.CreateCommand();
                create.CommandText =
                    "create table bookmark (movie_name text not null, movie_path text not null);";
                create.ExecuteNonQuery();

                InsertBookmarkRow(
                    connection,
                    "old%_name[1[(120)12-34-56]",
                    "old%_name[1[(120)12-34-56]"
                );
                InsertBookmarkRow(
                    connection,
                    "oldZZname[1[(120)12-34-56]",
                    "oldZZname[1[(120)12-34-56]"
                );
            }

            MainWindow.RenameBookmarkTableEntries(dbPath, "old%_name[1", "new%_name[1");

            using SQLiteConnection verifyConnection = new($"Data Source={dbPath}");
            verifyConnection.Open();

            using SQLiteCommand select = verifyConnection.CreateCommand();
            select.CommandText = "select movie_name, movie_path from bookmark order by rowid";
            using SQLiteDataReader reader = select.ExecuteReader();

            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("new%_name[1[(120)12-34-56]"));
            Assert.That(reader.GetString(1), Is.EqualTo("new%_name[1[(120)12-34-56]"));

            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("oldZZname[1[(120)12-34-56]"));
            Assert.That(reader.GetString(1), Is.EqualTo("oldZZname[1[(120)12-34-56]"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void BuildRenameOperations_既存ThumbPathに別hashjpgが残っていてもrename対象へ混ぜない()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-thumb-{Guid.NewGuid():N}");
        string staleSmallPath = Path.Combine(tempRoot, "small", "old-name.#other-hash.jpg");
        string staleDetailPath = Path.Combine(tempRoot, "detail", "old-name.#other-hash.legacy.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(staleSmallPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(staleDetailPath)!);
        File.WriteAllBytes(staleSmallPath, [0x1]);
        File.WriteAllBytes(staleDetailPath, [0x2]);

        try
        {
            MovieRecords movie = new()
            {
                Hash = "hash-123",
                ThumbPathSmall = staleSmallPath,
                ThumbDetail = staleDetailPath,
            };

            List<ThumbnailRenameAssetTransferHelper.ThumbnailRenameOperation> operations =
                ThumbnailRenameAssetTransferHelper.BuildRenameOperations(
                    movie,
                    tempRoot,
                    @"C:\movie\old-name.mp4",
                    @"D:\archive\new-name.mkv",
                    canRenameHashedThumbnailAssets: true,
                    canRenameErrorMarkerAssets: false
                );

            Assert.That(operations, Is.Empty);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void BuildRenameBridgeRollbackSteps_実施済み段だけ逆順で戻す()
    {
        List<string> calls = [];

        List<Action> rollbackSteps = MainWindow.BuildRenameBridgeRollbackSteps(
            bookmarkDbUpdated: false,
            rollbackBookmarkDb: () => calls.Add("bookmark-db"),
            bookmarkMoveRollbacks:
            [
                () => calls.Add("bookmark-move-1"),
                () => calls.Add("bookmark-move-2"),
            ],
            thumbnailMoveRollbacks:
            [
                () => calls.Add("thumbnail-move-1"),
                () => calls.Add("thumbnail-move-2"),
            ],
            movieNameUpdatedInDb: true,
            rollbackMovieName: () => calls.Add("movie-name"),
            moviePathUpdatedInDb: false,
            rollbackMoviePath: () => calls.Add("movie-path"),
            movieStateUpdated: true,
            rollbackMovieState: () => calls.Add("movie-state")
        );

        foreach (Action rollbackStep in rollbackSteps)
        {
            rollbackStep();
        }

        Assert.That(
            calls,
            Is.EqualTo(
                new[]
                {
                    "bookmark-move-2",
                    "bookmark-move-1",
                    "thumbnail-move-2",
                    "thumbnail-move-1",
                    "movie-name",
                    "movie-state",
                }
            )
        );
    }

    [Test]
    public void ExecuteRenameBridgeRollbackSteps_先頭失敗後も後続rollbackを継続する()
    {
        List<string> calls = [];

        List<Exception> failures = MainWindow.ExecuteRenameBridgeRollbackSteps(
            [
                () =>
                {
                    calls.Add("rollback-1");
                    throw new IOException("rollback-1 failed");
                },
                () => calls.Add("rollback-2"),
                () =>
                {
                    calls.Add("rollback-3");
                    throw new InvalidOperationException("rollback-3 failed");
                },
            ]
        );

        Assert.That(calls, Is.EqualTo(["rollback-1", "rollback-2", "rollback-3"]));
        Assert.That(
            failures.Select(ex => ex.Message),
            Is.EqualTo(["rollback-1 failed", "rollback-3 failed"])
        );
    }

    [Test]
    public void BuildRenameBridgeRollbackFailure_元例外を先頭にしてrollback失敗を束ねる()
    {
        AggregateException ex = MainWindow.BuildRenameBridgeRollbackFailure(
            new ApplicationException("rename failed"),
            [
                new IOException("rollback-1 failed"),
                new InvalidOperationException("rollback-2 failed"),
            ]
        );

        Assert.That(
            ex.InnerExceptions.Select(item => item.Message),
            Is.EqualTo(["rename failed", "rollback-1 failed", "rollback-2 failed"])
        );
    }

    [Test]
    public void ResolveRenameBridgeTargets_snapshot未命中時だけfallbackの完全一致を採る()
    {
        MovieRecords fallbackMovie = new() { Movie_Path = @"C:\movie\old-name.mp4", Movie_Id = 42 };

        List<MovieRecords> targets = MainWindow.ResolveRenameBridgeTargets(
            [new MovieRecords { Movie_Path = @"C:\movie\other.mp4", Movie_Id = 7 }],
            @"C:\movie\OLD-NAME.mp4",
            fallbackMovie
        );

        Assert.That(targets, Has.Count.EqualTo(1));
        Assert.That(targets[0], Is.SameAs(fallbackMovie));
    }

    [Test]
    public void ResolveRenameBridgeTargets_fallback不一致なら別動画を巻き込まない()
    {
        List<MovieRecords> targets = MainWindow.ResolveRenameBridgeTargets(
            [],
            @"C:\movie\old-name.mp4",
            new MovieRecords { Movie_Path = @"C:\movie\other.mp4", Movie_Id = 7 }
        );

        Assert.That(targets, Is.Empty);
    }

    [Test]
    public void ResolveRenameBridgeOwnerCounts_UIpartialでもDBhiddenOwnerを見てbookmarkとthumbnailrenameを止める()
    {
        string dbPath = CreateTempRenameBridgeDb();

        try
        {
            SeedRenameBridgeMovieRow(dbPath, 1, "old-name", @"C:\movies\old-name.mp4", "hash-1");
            SeedRenameBridgeMovieRow(dbPath, 2, "old-name", @"D:\hidden\old-name.mkv", "hash-1");
            SeedRenameBridgeMovieRow(dbPath, 3, "new-name", @"D:\hidden\new-name.mp4", "hash-1");

            MainDbMovieReadFacade facade = new();
            MainWindow.RenameBridgeOwnerCounts ownerCounts = MainWindow.ResolveRenameBridgeOwnerCounts(
                [new MovieRecords { Movie_Id = 1, Movie_Path = @"C:\movies\old-name.mp4", Hash = "hash-1" }],
                dbPath,
                facade,
                "old-name",
                "new-name",
                "hash-1",
                @"C:\movies\old-name.mp4"
            );

            Assert.Multiple(() =>
            {
                Assert.That(ownerCounts.OtherOldMovieBodyOwnerCount, Is.EqualTo(1));
                Assert.That(ownerCounts.OtherNewMovieBodyOwnerCount, Is.EqualTo(1));
                Assert.That(ownerCounts.OtherOldThumbnailOwnerCount, Is.EqualTo(1));
                Assert.That(ownerCounts.OtherNewThumbnailOwnerCount, Is.EqualTo(1));
                Assert.That(
                    MainWindow.CanRenameBookmarkAssetsSafely(
                        ownerCounts.OtherOldMovieBodyOwnerCount,
                        ownerCounts.OtherNewMovieBodyOwnerCount
                    ),
                    Is.False
                );
                Assert.That(
                    MainWindow.CanRenameThumbnailAssetsSafely(
                        ownerCounts.OtherOldThumbnailOwnerCount,
                        ownerCounts.OtherNewThumbnailOwnerCount
                    ),
                    Is.False
                );
            });
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(dbPath));
        }
    }

    [Test]
    public void ResolveRenameBridgeOwnerCounts_fallback映画を拾えてもhiddenOwnerがいれば共有資産を動かさない()
    {
        string dbPath = CreateTempRenameBridgeDb();

        try
        {
            SeedRenameBridgeMovieRow(dbPath, 10, "old-name", @"C:\movies\old-name.mp4", "hash-2");
            SeedRenameBridgeMovieRow(dbPath, 11, "old-name", @"D:\hidden\old-name.avi", "hash-2");
            SeedRenameBridgeMovieRow(dbPath, 12, "new-name", @"D:\hidden\new-name.avi", "hash-2");

            MainDbMovieReadFacade facade = new();
            bool found = facade.TryReadMovieByPath(
                dbPath,
                @"C:\movies\old-name.mp4",
                out MainDbMovieReadItemResult fallbackSource
            );
            MovieRecords fallbackMovie = new()
            {
                Movie_Id = fallbackSource.MovieId,
                Movie_Path = fallbackSource.MoviePath,
                Hash = fallbackSource.Hash,
            };

            List<MovieRecords> targets = MainWindow.ResolveRenameBridgeTargets(
                [],
                @"C:\movies\old-name.mp4",
                fallbackMovie
            );
            MainWindow.RenameBridgeOwnerCounts ownerCounts = MainWindow.ResolveRenameBridgeOwnerCounts(
                targets,
                dbPath,
                facade,
                "old-name",
                "new-name",
                "hash-2",
                @"C:\movies\old-name.mp4"
            );

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(targets, Has.Count.EqualTo(1));
                Assert.That(targets[0], Is.SameAs(fallbackMovie));
                Assert.That(ownerCounts.OtherOldMovieBodyOwnerCount, Is.EqualTo(1));
                Assert.That(ownerCounts.OtherNewMovieBodyOwnerCount, Is.EqualTo(1));
                Assert.That(ownerCounts.OtherOldThumbnailOwnerCount, Is.EqualTo(1));
                Assert.That(ownerCounts.OtherNewThumbnailOwnerCount, Is.EqualTo(1));
                Assert.That(
                    MainWindow.CanRenameBookmarkAssetsSafely(
                        ownerCounts.OtherOldMovieBodyOwnerCount,
                        ownerCounts.OtherNewMovieBodyOwnerCount
                    ),
                    Is.False
                );
                Assert.That(
                    MainWindow.CanRenameSharedOwnerAssetsSafely(
                        ownerCounts.OtherOldMovieBodyOwnerCount,
                        ownerCounts.OtherNewMovieBodyOwnerCount
                    ),
                    Is.False
                );
            });
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(dbPath));
        }
    }

    [Test]
    public void ResolveWatchEventQueueGuardAction_DB切替後やscope更新後はstaleをdropする()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                MainWindow.ResolveWatchEventQueueGuardAction(
                    currentDbFullPath: @"D:\Db\current.wb",
                    snapshotDbFullPath: @"D:\Db\current.wb",
                    requestScopeStamp: 7,
                    currentScopeStamp: 7
                ),
                Is.EqualTo(MainWindow.WatchEventQueueGuardAction.Continue)
            );
            Assert.That(
                MainWindow.ResolveWatchEventQueueGuardAction(
                    currentDbFullPath: @"D:\Db\current.wb",
                    snapshotDbFullPath: @"D:\Db\old.wb",
                    requestScopeStamp: 7,
                    currentScopeStamp: 7
                ),
                Is.EqualTo(MainWindow.WatchEventQueueGuardAction.DropStale)
            );
            Assert.That(
                MainWindow.ResolveWatchEventQueueGuardAction(
                    currentDbFullPath: @"D:\Db\current.wb",
                    snapshotDbFullPath: @"D:\Db\current.wb",
                    requestScopeStamp: 7,
                    currentScopeStamp: 8
                ),
                Is.EqualTo(MainWindow.WatchEventQueueGuardAction.DropStale)
            );
        });
    }

    [Test]
    public void TryEnterRenameBridgeForWatchScope_scope不一致ならrename本体へ入れない()
    {
        List<string> logs = [];

        bool result = MainWindow.TryEnterRenameBridgeForWatchScope(
            @"E:\Movies\new-name.mp4",
            @"E:\Movies\old-name.mp4",
            canStartRenameBridge: () => false,
            logWatchMessage: message => logs.Add(message)
        );

        Assert.That(result, Is.False);
        Assert.That(logs, Has.Count.EqualTo(1));
        Assert.That(logs[0], Does.Contain("stale watch scope"));
    }

    [Test]
    public async Task ProcessCreatedWatchEventDirectAsync_ready待ち中にscopeがstale化したら登録せず落とす()
    {
        List<string> calls = [];

        MainWindow.CreatedWatchEventDirectResult result =
            await MainWindow.ProcessCreatedWatchEventDirectAsync(
                createdFullPath: @"E:\Movies\created.mp4",
                resolveCurrentState: () => (@"D:\Db\main.wb", 2),
                waitForReadyAsync: _ =>
                {
                    calls.Add("ready");
                    return Task.FromResult(true);
                },
                resolveZeroByteState: _ => (false, 0L),
                createErrorMarkerForSkippedMovie: (_, _, _) => calls.Add("error-marker"),
                createMovieInfoAsync: _ =>
                {
                    calls.Add("movie-info");
                    return Task.FromResult<MovieInfo>(null);
                },
                insertMovieAsync: (_, _) =>
                {
                    calls.Add("insert");
                    return Task.FromResult(1);
                },
                adjustRegisteredMovieCount: (_, _) => calls.Add("adjust"),
                appendMovieToViewAsync: (_, _) =>
                {
                    calls.Add("append");
                    return Task.CompletedTask;
                },
                tryEnqueueThumbnailJob: _ =>
                {
                    calls.Add("enqueue");
                    return true;
                },
                logWatchMessage: message => calls.Add(message),
                canContinueWatchScope: guardPoint =>
                    guardPoint != MainWindow.CreatedWatchEventDirectGuardPoint.AfterReady
            );

        Assert.That(result, Is.EqualTo(MainWindow.CreatedWatchEventDirectResult.Ignored));
        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.That(calls[0], Is.EqualTo("ready"));
        Assert.That(calls[1], Does.Contain("after-ready"));
    }

    [Test]
    public async Task ProcessCreatedWatchEventDirectAsync_insert前にscopeがstale化したら旧DBへ登録しない()
    {
        List<string> calls = [];
        MovieInfo movieInfo = CreateMovieInfo(@"E:\Movies\created.mp4", movieId: 7, hash: "hash-7");

        MainWindow.CreatedWatchEventDirectResult result =
            await MainWindow.ProcessCreatedWatchEventDirectAsync(
                createdFullPath: @"E:\Movies\created.mp4",
                resolveCurrentState: () => (@"D:\Db\main.wb", 2),
                waitForReadyAsync: _ =>
                {
                    calls.Add("ready");
                    return Task.FromResult(true);
                },
                resolveZeroByteState: _ => (false, 123L),
                createErrorMarkerForSkippedMovie: (_, _, _) => calls.Add("error-marker"),
                createMovieInfoAsync: _ =>
                {
                    calls.Add("movie-info");
                    return Task.FromResult(movieInfo);
                },
                insertMovieAsync: (_, _) =>
                {
                    calls.Add("insert");
                    return Task.FromResult(1);
                },
                adjustRegisteredMovieCount: (_, _) => calls.Add("adjust"),
                appendMovieToViewAsync: (_, _) =>
                {
                    calls.Add("append");
                    return Task.CompletedTask;
                },
                tryEnqueueThumbnailJob: _ =>
                {
                    calls.Add("enqueue");
                    return true;
                },
                logWatchMessage: message => calls.Add(message),
                canContinueWatchScope: guardPoint =>
                    guardPoint != MainWindow.CreatedWatchEventDirectGuardPoint.BeforeInsert
            );

        Assert.That(result, Is.EqualTo(MainWindow.CreatedWatchEventDirectResult.Ignored));
        Assert.That(calls, Does.Contain("movie-info"));
        Assert.That(calls, Has.None.EqualTo("insert"));
        Assert.That(calls, Has.None.EqualTo("append"));
        Assert.That(calls[^1], Does.Contain("before-insert"));
    }

    [Test]
    public async Task ProcessCreatedWatchEventDirectAsync_append前にscopeがstale化したら現在UIへ追加しない()
    {
        List<string> calls = [];
        MovieInfo movieInfo = CreateMovieInfo(@"E:\Movies\created.mp4", movieId: 9, hash: "hash-9");

        MainWindow.CreatedWatchEventDirectResult result =
            await MainWindow.ProcessCreatedWatchEventDirectAsync(
                createdFullPath: @"E:\Movies\created.mp4",
                resolveCurrentState: () => (@"D:\Db\main.wb", 2),
                waitForReadyAsync: _ =>
                {
                    calls.Add("ready");
                    return Task.FromResult(true);
                },
                resolveZeroByteState: _ => (false, 123L),
                createErrorMarkerForSkippedMovie: (_, _, _) => calls.Add("error-marker"),
                createMovieInfoAsync: _ =>
                {
                    calls.Add("movie-info");
                    return Task.FromResult(movieInfo);
                },
                insertMovieAsync: (_, _) =>
                {
                    calls.Add("insert");
                    return Task.FromResult(1);
                },
                adjustRegisteredMovieCount: (_, _) => calls.Add("adjust"),
                appendMovieToViewAsync: (_, _) =>
                {
                    calls.Add("append");
                    return Task.CompletedTask;
                },
                tryEnqueueThumbnailJob: _ =>
                {
                    calls.Add("enqueue");
                    return true;
                },
                logWatchMessage: message => calls.Add(message),
                canContinueWatchScope: guardPoint =>
                    guardPoint != MainWindow.CreatedWatchEventDirectGuardPoint.BeforeAppend
            );

        Assert.That(result, Is.EqualTo(MainWindow.CreatedWatchEventDirectResult.Ignored));
        Assert.That(calls, Does.Contain("insert"));
        Assert.That(calls, Does.Contain("adjust"));
        Assert.That(calls, Has.None.EqualTo("append"));
        Assert.That(calls, Has.None.EqualTo("enqueue"));
        Assert.That(calls[^1], Does.Contain("before-append"));
    }

    [Test]
    public async Task ProcessCreatedWatchEventDirectAsync_zeroByteでもerrorMarkerはsnapshotのタブを使う()
    {
        int resolveCount = 0;
        int errorMarkerTab = -1;
        List<string> calls = [];

        MainWindow.CreatedWatchEventDirectResult result =
            await MainWindow.ProcessCreatedWatchEventDirectAsync(
                createdFullPath: @"E:\Movies\created.mp4",
                resolveCurrentState: () =>
                {
                    resolveCount++;
                    return resolveCount == 1 ? (@"D:\Db\main.wb", 2) : (@"D:\Db\other.wb", 4);
                },
                waitForReadyAsync: _ => Task.FromResult(true),
                resolveZeroByteState: _ => (true, 0L),
                createErrorMarkerForSkippedMovie: (_, tabIndex, _) =>
                {
                    errorMarkerTab = tabIndex;
                    calls.Add("error-marker");
                },
                createMovieInfoAsync: _ =>
                {
                    calls.Add("movie-info");
                    return Task.FromResult<MovieInfo>(null);
                },
                insertMovieAsync: (_, _) =>
                {
                    calls.Add("insert");
                    return Task.FromResult(1);
                },
                adjustRegisteredMovieCount: (_, _) => calls.Add("adjust"),
                appendMovieToViewAsync: (_, _) =>
                {
                    calls.Add("append");
                    return Task.CompletedTask;
                },
                tryEnqueueThumbnailJob: _ =>
                {
                    calls.Add("enqueue");
                    return true;
                },
                logWatchMessage: message => calls.Add(message),
                canContinueWatchScope: _ => true
            );

        Assert.That(result, Is.EqualTo(MainWindow.CreatedWatchEventDirectResult.Ignored));
        Assert.That(resolveCount, Is.EqualTo(1));
        Assert.That(errorMarkerTab, Is.EqualTo(2));
        Assert.That(calls, Does.Contain("error-marker"));
        Assert.That(calls, Has.None.EqualTo("movie-info"));
    }

    [Test]
    public async Task ProcessCreatedWatchEventDirectAsync_helper契約としてはbypassTabGateを要求する()
    {
        MovieInfo movieInfo = CreateMovieInfo(@"E:\Movies\created.mp4", movieId: 12, hash: "hash-12");
        bool actualBypassTabGate = false;

        MainWindow.CreatedWatchEventDirectResult result =
            await MainWindow.ProcessCreatedWatchEventDirectAsync(
                createdFullPath: movieInfo.MoviePath,
                resolveCurrentState: () => (@"D:\Db\main.wb", 2),
                waitForReadyAsync: _ => Task.FromResult(true),
                resolveZeroByteState: _ => (false, 123L),
                createErrorMarkerForSkippedMovie: (_, _, _) => { },
                createMovieInfoAsync: _ => Task.FromResult(movieInfo),
                insertMovieAsync: (_, _) => Task.FromResult(1),
                adjustRegisteredMovieCount: (_, _) => { },
                appendMovieToViewAsync: (_, _) => Task.CompletedTask,
                tryEnqueueThumbnailJob: enqueueRequest =>
                {
                    actualBypassTabGate = enqueueRequest.BypassTabGate;
                    return enqueueRequest.BypassTabGate;
                },
                logWatchMessage: _ => { },
                canContinueWatchScope: _ => true
            );

        Assert.That(
            result,
            Is.EqualTo(MainWindow.CreatedWatchEventDirectResult.RegisteredAndEnqueued)
        );
        Assert.That(actualBypassTabGate, Is.True);
    }

    private static MovieInfo CreateMovieInfo(string moviePath, long movieId, string hash)
    {
        MovieInfo movieInfo = (MovieInfo)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MovieInfo));
        movieInfo.MovieId = movieId;
        movieInfo.MoviePath = moviePath;
        movieInfo.Hash = hash;
        return movieInfo;
    }

    private static void InsertBookmarkRow(
        SQLiteConnection connection,
        string movieName,
        string moviePath
    )
    {
        using SQLiteCommand insert = connection.CreateCommand();
        insert.CommandText = "insert into bookmark (movie_name, movie_path) values (@name, @path)";
        insert.Parameters.AddWithValue("@name", movieName);
        insert.Parameters.AddWithValue("@path", moviePath);
        insert.ExecuteNonQuery();
    }

    private static string CreateTempRenameBridgeDb()
    {
        string root = Path.Combine(Path.GetTempPath(), $"imm-rename-bridge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string dbPath = Path.Combine(root, "main.wb");
        SQLiteConnection.CreateFile(dbPath);

        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE movie (
    movie_id INTEGER PRIMARY KEY,
    movie_name TEXT NOT NULL,
    movie_path TEXT NOT NULL,
    movie_length INTEGER NOT NULL,
    movie_size INTEGER NOT NULL,
    last_date TEXT NOT NULL,
    file_date TEXT NOT NULL,
    regist_date TEXT NOT NULL,
    score INTEGER NOT NULL,
    view_count INTEGER NOT NULL,
    hash TEXT NOT NULL,
    container TEXT NOT NULL,
    video TEXT NOT NULL,
    audio TEXT NOT NULL,
    kana TEXT NOT NULL,
    tag TEXT NOT NULL,
    comment1 TEXT NOT NULL,
    comment2 TEXT NOT NULL,
    comment3 TEXT NOT NULL
);";
        command.ExecuteNonQuery();
        return dbPath;
    }

    private static void SeedRenameBridgeMovieRow(
        string dbPath,
        long movieId,
        string movieName,
        string moviePath,
        string hash
    )
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO movie (
    movie_id,
    movie_name,
    movie_path,
    movie_length,
    movie_size,
    last_date,
    file_date,
    regist_date,
    score,
    view_count,
    hash,
    container,
    video,
    audio,
    kana,
    tag,
    comment1,
    comment2,
    comment3
)
VALUES (
    @movieId,
    @movieName,
    @moviePath,
    60,
    100,
    '2026-03-18 10:00:00',
    '2026-03-18 10:00:00',
    '2026-03-18 10:00:00',
    1,
    10,
    @hash,
    'mp4',
    'h264',
    'aac',
    '',
    '',
    '',
    '',
    ''
);";
        command.Parameters.AddWithValue("@movieId", movieId);
        command.Parameters.AddWithValue("@movieName", movieName);
        command.Parameters.AddWithValue("@moviePath", moviePath);
        command.Parameters.AddWithValue("@hash", hash);
        command.ExecuteNonQuery();
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
