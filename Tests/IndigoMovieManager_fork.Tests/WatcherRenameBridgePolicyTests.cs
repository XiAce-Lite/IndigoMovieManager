using IndigoMovieManager;
using IndigoMovieManager.Data;
using IndigoMovieManager.DB;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.ViewModels;
using System.Reflection;
using System.Runtime.CompilerServices;
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
            Assert.That(movie.Movie_Path_Normalized, Is.EqualTo(@"D:\archive\new-name.mkv"));
            Assert.That(movie.IsExists, Is.True);
        });
    }

    [Test]
    public void ApplyMovieRenameStateCore_rollback時は存在フラグを明示値へ戻せる()
    {
        MovieRecords movie = new()
        {
            Movie_Path = @"D:\archive\new-name.mkv",
            Movie_Name = "new-name.mkv",
            Movie_Body = "new-name",
            IsExists = true,
        };

        MainWindow.ApplyMovieRenameStateCore(
            movie,
            @"C:\movie\old-name.mp4",
            "old-name.mp4",
            "old-name",
            isExists: false
        );

        Assert.Multiple(() =>
        {
            Assert.That(movie.Movie_Path, Is.EqualTo(@"C:\movie\old-name.mp4"));
            Assert.That(movie.Movie_Name, Is.EqualTo("old-name.mp4"));
            Assert.That(movie.Movie_Body, Is.EqualTo("old-name"));
            Assert.That(movie.IsExists, Is.False);
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
    public void BuildRenameBridgeRollbackSteps_全段実施済みならtry順の完全逆順で戻す()
    {
        List<string> calls = [];

        List<Action> rollbackSteps = MainWindow.BuildRenameBridgeRollbackSteps(
            bookmarkDbUpdated: true,
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
            moviePathUpdatedInDb: true,
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
                    "bookmark-db",
                    "bookmark-move-2",
                    "bookmark-move-1",
                    "thumbnail-move-2",
                    "thumbnail-move-1",
                    "movie-name",
                    "movie-path",
                    "movie-state",
                }
            )
        );
    }

    [Test]
    public void RenameSingleMovieBridge_途中失敗でも実施済み段だけrollbackする()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-rename-runtime-{Guid.NewGuid():N}");
        string thumbnailRoot = Path.Combine(tempRoot, "thumb");
        string bookmarkRoot = Path.Combine(tempRoot, "bookmark");
        string oldFullPath = @"C:\movies\old-name.mp4";
        string newFullPath = @"D:\archive\new-name.mkv";
        string oldThumbnailPath = Path.Combine(thumbnailRoot, "small", "old-name.jpg");
        string newThumbnailPath = Path.Combine(thumbnailRoot, "small", "new-name.jpg");
        string oldBookmarkPath = Path.Combine(bookmarkRoot, "old-name[(120)12-34-56].jpg");
        string newBookmarkPath = Path.Combine(bookmarkRoot, "new-name[(120)12-34-56].jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(oldThumbnailPath)!);
        Directory.CreateDirectory(bookmarkRoot);
        File.WriteAllBytes(oldThumbnailPath, [0x1]);
        File.WriteAllBytes(oldBookmarkPath, [0x2]);
        string dbPath = CreateTempRenameBridgeDb(createBookmarkTable: false);

        try
        {
            SeedRenameBridgeMovieRow(dbPath, 1, "old-name", oldFullPath, "hash-1");
            MovieRecords movie = new()
            {
                Movie_Id = 1,
                Movie_Path = oldFullPath,
                Movie_Name = "old-name.mp4",
                Movie_Body = "old-name",
                Hash = "hash-1",
                ThumbPathSmall = oldThumbnailPath,
                IsExists = true,
            };
            MainWindow window = CreateRenameBridgeRuntimeWindow(
                dbPath,
                thumbnailRoot,
                bookmarkRoot,
                [movie]
            );
            object context = CreateRenameBridgeExecutionContext(
                dbPath,
                thumbnailRoot,
                bookmarkRoot,
                MainWindow.CaptureRenameBridgeOwnerSnapshots([movie]),
                [movie]
            );

            TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() =>
                InvokeRenameSingleMovieBridge(window, movie, newFullPath, oldFullPath, context)
            )!;

            Assert.That(ex.InnerException, Is.TypeOf<SQLiteException>());
            Assert.That(ex.InnerException?.Message, Does.Contain("bookmark"));

            (string movieName, string moviePath) = ReadRenameBridgeMovieRow(dbPath, 1);
            Assert.Multiple(() =>
            {
                Assert.That(movie.Movie_Path, Is.EqualTo(oldFullPath));
                Assert.That(movie.Movie_Name, Is.EqualTo("old-name.mp4"));
                Assert.That(movie.Movie_Body, Is.EqualTo("old-name"));
                Assert.That(movie.IsExists, Is.True);
                Assert.That(movie.ThumbPathSmall, Is.EqualTo(oldThumbnailPath));
                Assert.That(movieName, Is.EqualTo("old-name"));
                Assert.That(moviePath, Is.EqualTo(oldFullPath));
                Assert.That(File.Exists(oldThumbnailPath), Is.True);
                Assert.That(File.Exists(newThumbnailPath), Is.False);
                Assert.That(File.Exists(oldBookmarkPath), Is.True);
                Assert.That(File.Exists(newBookmarkPath), Is.False);
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
            TryDeleteDirectory(Path.GetDirectoryName(dbPath));
        }
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
    public void ResolveRenameBridgeOwnerCounts_snapshot固定ならliveMovie更新後もowner誤判定しない()
    {
        MovieRecords sourceMovie = new()
        {
            Movie_Id = 1,
            Movie_Path = @"C:\movies\old-name.mp4",
            Movie_Body = "old-name",
            Hash = "hash-1",
        };
        MovieRecords otherMovie = new()
        {
            Movie_Id = 2,
            Movie_Path = @"D:\movies\new-name.mp4",
            Movie_Body = "new-name",
            Hash = "hash-1",
        };

        List<MainWindow.RenameBridgeOwnerSnapshot> ownerSnapshot =
            MainWindow.CaptureRenameBridgeOwnerSnapshots([sourceMovie, otherMovie]);

        // rename 実行中に live UI モデルが更新されても、owner 判定は開始時点 snapshot を使い続ける。
        MainWindow.ApplyMovieRenameStateCore(
            sourceMovie,
            @"C:\movies\new-name.mp4",
            "new-name.mp4",
            "new-name"
        );

        MainWindow.RenameBridgeOwnerCounts ownerCounts = MainWindow.ResolveRenameBridgeOwnerCounts(
            ownerSnapshot,
            snapshotDbFullPath: "",
            mainDbMovieReadFacade: null,
            oldMovieBody: "old-name",
            newMovieBody: "new-name",
            hash: "hash-1",
            excludedMoviePath: @"C:\movies\old-name.mp4"
        );

        Assert.Multiple(() =>
        {
            Assert.That(ownerCounts.OtherOldMovieBodyOwnerCount, Is.EqualTo(0));
            Assert.That(ownerCounts.OtherNewMovieBodyOwnerCount, Is.EqualTo(1));
            Assert.That(ownerCounts.OtherOldThumbnailOwnerCount, Is.EqualTo(0));
            Assert.That(ownerCounts.OtherNewThumbnailOwnerCount, Is.EqualTo(1));
        });
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
    public void RenameThumb_context構築後にscopeがstale化したらrenameしない()
    {
        string dbPath = CreateTempRenameBridgeDb();
        string thumbnailRoot = Path.Combine(Path.GetTempPath(), $"imm-rename-thumb-{Guid.NewGuid():N}");
        string bookmarkRoot = Path.Combine(Path.GetTempPath(), $"imm-rename-bookmark-{Guid.NewGuid():N}");
        string oldFullPath = @"E:\Movies\old-name.mp4";
        string newFullPath = @"E:\Movies\new-name.mp4";
        MovieRecords movie = new()
        {
            Movie_Id = 1,
            Movie_Path = oldFullPath,
            Movie_Name = "old-name.mp4",
            Movie_Body = "old-name",
            Hash = "hash-1",
            IsExists = true,
        };
        int guardCallCount = 0;
        bool[] guardResults = [true, true, false];
        List<string> logs = [];

        try
        {
            Directory.CreateDirectory(thumbnailRoot);
            Directory.CreateDirectory(bookmarkRoot);
            SeedRenameBridgeMovieRow(dbPath, 1, "old-name", oldFullPath, "hash-1");
            MainWindow window = CreateRenameBridgeRuntimeWindow(
                dbPath,
                thumbnailRoot,
                bookmarkRoot,
                [movie]
            );

            InvokeRenameThumb(
                window,
                newFullPath,
                oldFullPath,
                () =>
                {
                    bool result = guardResults[Math.Min(guardCallCount, guardResults.Length - 1)];
                    guardCallCount++;
                    return result;
                },
                message => logs.Add(message)
            );

            (string movieName, string moviePath) = ReadRenameBridgeMovieRow(dbPath, 1);
            Assert.Multiple(() =>
            {
                Assert.That(guardCallCount, Is.EqualTo(3));
                Assert.That(movie.Movie_Path, Is.EqualTo(oldFullPath));
                Assert.That(movie.Movie_Name, Is.EqualTo("old-name.mp4"));
                Assert.That(movieName, Is.EqualTo("old-name"));
                Assert.That(moviePath, Is.EqualTo(oldFullPath));
                Assert.That(logs, Has.Count.EqualTo(1));
                Assert.That(logs[0], Does.Contain("stale watch scope"));
            });
        }
        finally
        {
            TryDeleteDirectory(thumbnailRoot);
            TryDeleteDirectory(bookmarkRoot);
            TryDeleteDirectory(Path.GetDirectoryName(dbPath));
        }
    }

    [Test]
    public void RenameThumb_複数target中にscopeがstale化したら残りtargetを止める()
    {
        string dbPath = CreateTempRenameBridgeDb();
        string thumbnailRoot = Path.Combine(Path.GetTempPath(), $"imm-rename-thumb-{Guid.NewGuid():N}");
        string bookmarkRoot = Path.Combine(Path.GetTempPath(), $"imm-rename-bookmark-{Guid.NewGuid():N}");
        string oldFullPath = @"E:\Movies\old-name.mp4";
        string newFullPath = @"E:\Movies\new-name.mp4";
        MovieRecords firstMovie = new()
        {
            Movie_Id = 1,
            Movie_Path = oldFullPath,
            Movie_Name = "old-name.mp4",
            Movie_Body = "old-name",
            Hash = "hash-1",
            IsExists = true,
        };
        MovieRecords secondMovie = new()
        {
            Movie_Id = 2,
            Movie_Path = oldFullPath,
            Movie_Name = "old-name.mp4",
            Movie_Body = "old-name",
            Hash = "hash-1",
            IsExists = true,
        };
        int guardCallCount = 0;
        int refreshCount = 0;
        bool[] guardResults = [true, true, true, false];
        List<string> logs = [];

        try
        {
            Directory.CreateDirectory(thumbnailRoot);
            Directory.CreateDirectory(bookmarkRoot);
            SeedRenameBridgeMovieRow(dbPath, 1, "old-name", oldFullPath, "hash-1");
            SeedRenameBridgeMovieRow(dbPath, 2, "old-name", oldFullPath, "hash-1");
            MainWindow window = CreateRenameBridgeRuntimeWindow(
                dbPath,
                thumbnailRoot,
                bookmarkRoot,
                [firstMovie, secondMovie],
                refreshRenameBridgeUiForTesting: () => refreshCount++
            );

            InvokeRenameThumb(
                window,
                newFullPath,
                oldFullPath,
                () =>
                {
                    bool result = guardResults[Math.Min(guardCallCount, guardResults.Length - 1)];
                    guardCallCount++;
                    return result;
                },
                message => logs.Add(message)
            );

            (string firstMovieName, string firstMoviePath) = ReadRenameBridgeMovieRow(dbPath, 1);
            (string secondMovieName, string secondMoviePath) = ReadRenameBridgeMovieRow(dbPath, 2);
            Assert.Multiple(() =>
            {
                Assert.That(guardCallCount, Is.EqualTo(4));
                Assert.That(refreshCount, Is.EqualTo(1));
                Assert.That(firstMovie.Movie_Path, Is.EqualTo(newFullPath));
                Assert.That(firstMovie.Movie_Name, Is.EqualTo("new-name.mp4"));
                Assert.That(firstMovie.Movie_Body, Is.EqualTo("new-name"));
                Assert.That(firstMovieName, Is.EqualTo("new-name"));
                Assert.That(firstMoviePath, Is.EqualTo(newFullPath));
                Assert.That(secondMovie.Movie_Path, Is.EqualTo(oldFullPath));
                Assert.That(secondMovie.Movie_Name, Is.EqualTo("old-name.mp4"));
                Assert.That(secondMovie.Movie_Body, Is.EqualTo("old-name"));
                Assert.That(secondMovieName, Is.EqualTo("old-name"));
                Assert.That(secondMoviePath, Is.EqualTo(oldFullPath));
                Assert.That(logs, Has.Count.EqualTo(1));
                Assert.That(logs[0], Does.Contain("stale watch scope"));
            });
        }
        finally
        {
            TryDeleteDirectory(thumbnailRoot);
            TryDeleteDirectory(bookmarkRoot);
            TryDeleteDirectory(Path.GetDirectoryName(dbPath));
        }
    }

    private static MainWindow CreateRenameBridgeRuntimeWindow(
        string dbPath,
        string thumbnailRoot,
        string bookmarkRoot,
        IEnumerable<MovieRecords> movies,
        Action? refreshRenameBridgeUiForTesting = null
    )
    {
        MainWindow window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        MainWindowViewModel mainVm =
            (MainWindowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainWindowViewModel));
        mainVm.DbInfo = new DBInfo
        {
            DBFullPath = dbPath,
            DBName = Path.GetFileNameWithoutExtension(dbPath) ?? "",
            ThumbFolder = thumbnailRoot,
            BookmarkFolder = bookmarkRoot,
            Sort = "0",
        };
        mainVm.MovieRecs = new ResettableObservableCollection<MovieRecords>();
        foreach (MovieRecords movie in movies ?? [])
        {
            mainVm.MovieRecs.Add(movie);
        }

        SetPrivateField(window, "MainVM", mainVm);
        SetPrivateField(window, "_mainDbMovieReadFacade", new MainDbMovieReadFacade());
        SetPrivateField(window, "_mainDbMovieMutationFacade", new MainDbMovieMutationFacade());
        window.RenameBridgeUiActionInvokerForTesting = action => action();
        window.RefreshRenameBridgeUiForTesting = refreshRenameBridgeUiForTesting;
        return window;
    }

    private static object CreateRenameBridgeExecutionContext(
        string dbPath,
        string thumbnailRoot,
        string bookmarkRoot,
        IReadOnlyList<MainWindow.RenameBridgeOwnerSnapshot> ownerSnapshot,
        IReadOnlyList<MovieRecords> targets
    )
    {
        Type contextType = GetRenameBridgeExecutionContextType();
        ConstructorInfo constructor = contextType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            types:
            [
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(IReadOnlyList<MainWindow.RenameBridgeOwnerSnapshot>),
                typeof(IReadOnlyList<MovieRecords>),
            ],
            modifiers: null
        )!;
        Assert.That(constructor, Is.Not.Null);
        return constructor.Invoke([dbPath, thumbnailRoot, bookmarkRoot, ownerSnapshot, targets]);
    }

    private static void InvokeRenameSingleMovieBridge(
        MainWindow window,
        MovieRecords movie,
        string newFullPath,
        string oldFullPath,
        object context
    )
    {
        Type contextType = GetRenameBridgeExecutionContextType();
        MethodInfo method = typeof(MainWindow).GetMethod(
            "RenameSingleMovieBridge",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(MovieRecords), typeof(string), typeof(string), contextType],
            modifiers: null
        )!;
        Assert.That(method, Is.Not.Null);
        method.Invoke(window, [movie, newFullPath, oldFullPath, context]);
    }

    private static void InvokeRenameThumb(
        MainWindow window,
        string newFullPath,
        string oldFullPath,
        Func<bool> canStartRenameBridge,
        Action<string> logWatchMessage
    )
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "RenameThumb",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string), typeof(string), typeof(Func<bool>), typeof(Action<string>)],
            modifiers: null
        )!;
        Assert.That(method, Is.Not.Null);
        method.Invoke(window, [newFullPath, oldFullPath, canStartRenameBridge, logWatchMessage]);
    }

    private static Type GetRenameBridgeExecutionContextType()
    {
        Type contextType = typeof(MainWindow).GetNestedType(
            "RenameBridgeExecutionContext",
            BindingFlags.NonPublic
        )!;
        Assert.That(contextType, Is.Not.Null);
        return contextType;
    }

    private static (string MovieName, string MoviePath) ReadRenameBridgeMovieRow(string dbPath, long movieId)
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = "select movie_name, movie_path from movie where movie_id = @movieId";
        command.Parameters.AddWithValue("@movieId", movieId);

        using SQLiteDataReader reader = command.ExecuteReader();
        Assert.That(reader.Read(), Is.True, movieId.ToString());
        return (reader.GetString(0), reader.GetString(1));
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

    private static string CreateTempRenameBridgeDb(bool createBookmarkTable = true)
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
        if (createBookmarkTable)
        {
            command.CommandText =
                "CREATE TABLE bookmark (movie_name TEXT NOT NULL, movie_path TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }
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

    private static void SetPrivateField(MainWindow window, string fieldName, object value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        )!;
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(window, value);
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
