using System.Runtime.CompilerServices;
using IndigoMovieManager;
using IndigoMovieManager.Data;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchScanCoordinatorPolicyTests
{
    [Test]
    public void EvaluateWatchFolderMoviePreCheck_visible_only_gateはfirst_hit前に止める()
    {
        MainWindow.WatchFolderMoviePreCheckDecision result =
            MainWindow.EvaluateWatchFolderMoviePreCheck(
                hasNotifiedFolderHit: false,
                skipByVisibleOnlyGate: true,
                isZeroByteMovie: false,
                fileBody: "sample"
            );

        Assert.That(result.Outcome, Is.EqualTo("skip_visible_only_gate"));
        Assert.That(result.ShouldNotifyFolderHit, Is.False);
        Assert.That(result.ShouldContinueProcessing, Is.False);
        Assert.That(result.IsZeroByteMovie, Is.False);
    }

    [Test]
    public void DeferredMoviePathsByUiSuppressionはcasing違いでも1件へ潰す()
    {
        MainWindow.WatchPendingNewMovieFlushResult flushResult = new();
        MainWindow.WatchScannedMovieProcessResult processResult = new();
        string moviePath = @"E:\Movies\Sample.mp4";

        flushResult.AddDeferredMoviePath(moviePath, null, "");
        flushResult.AddDeferredMoviePath(@"e:\movies\sample.mp4", null, "");
        processResult.AddDeferredMoviePath(moviePath, null, "");
        processResult.AddDeferredMoviePath(@"e:\movies\sample.mp4", null, "");

        Assert.That(flushResult.DeferredMoviePathsByUiSuppression, Is.EqualTo([moviePath]));
        Assert.That(processResult.DeferredMoviePathsByUiSuppression, Is.EqualTo([moviePath]));
    }

    [Test]
    public void EvaluateWatchFolderMoviePreCheck_zero_byteはfirst_hit通知後に止める()
    {
        MainWindow.WatchFolderMoviePreCheckDecision result =
            MainWindow.EvaluateWatchFolderMoviePreCheck(
                hasNotifiedFolderHit: false,
                skipByVisibleOnlyGate: false,
                isZeroByteMovie: true,
                fileBody: "sample"
            );

        Assert.That(result.Outcome, Is.EqualTo("skip_zero_byte"));
        Assert.That(result.ShouldNotifyFolderHit, Is.True);
        Assert.That(result.ShouldContinueProcessing, Is.False);
        Assert.That(result.IsZeroByteMovie, Is.True);
    }

    [Test]
    public void EvaluateWatchFolderMoviePreCheck_通常動画は継続し初回だけfirst_hit通知する()
    {
        MainWindow.WatchFolderMoviePreCheckDecision result =
            MainWindow.EvaluateWatchFolderMoviePreCheck(
                hasNotifiedFolderHit: false,
                skipByVisibleOnlyGate: false,
                isZeroByteMovie: false,
                fileBody: "sample"
            );

        Assert.That(result.Outcome, Is.EqualTo("continue"));
        Assert.That(result.ShouldNotifyFolderHit, Is.True);
        Assert.That(result.ShouldContinueProcessing, Is.True);
        Assert.That(result.IsZeroByteMovie, Is.False);
    }

    [Test]
    public void EvaluateWatchFolderMoviePreCheck_empty_bodyは追加通知せず止める()
    {
        MainWindow.WatchFolderMoviePreCheckDecision result =
            MainWindow.EvaluateWatchFolderMoviePreCheck(
                hasNotifiedFolderHit: true,
                skipByVisibleOnlyGate: false,
                isZeroByteMovie: false,
                fileBody: ""
            );

        Assert.That(result.Outcome, Is.EqualTo("skip_empty_body"));
        Assert.That(result.ShouldNotifyFolderHit, Is.False);
        Assert.That(result.ShouldContinueProcessing, Is.False);
        Assert.That(result.IsZeroByteMovie, Is.False);
    }

    [Test]
    public async Task ProcessScannedMovieAsync_mid_pass_suppressionでUIAppendを直前停止する()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = @"E:\Movies\sample.mp4";
        int appendCount = 0;
        Queue<bool> suppressionStates = new([false, true]);
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        MainWindow.WatchScannedMovieContext context = new()
        {
            SnapshotDbFullPath = @"D:\Db\Main.wb",
            SnapshotTabIndex = 2,
            ExistingMovieByPath = new Dictionary<string, WatchMainDbMovieSnapshot>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                [moviePath] = new WatchMainDbMovieSnapshot(1, "hash-1", "", 0, 0),
            },
            ExistingViewMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            DisplayedMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SearchKeyword = "",
            AllowViewConsistencyRepair = true,
            UseIncrementalUiMode = true,
            AllowMissingTabAutoEnqueue = false,
            ExistingThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            OpenRescueRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            PendingMovieFlushContext = pendingContext,
            ShouldSuppressWatchWork = () => ReadNextSuppressionState(suppressionStates),
            AppendMovieToViewAsync = (_, _) =>
            {
                appendCount++;
                return Task.CompletedTask;
            },
        };

        MainWindow.WatchScannedMovieProcessResult result = await window.ProcessScannedMovieAsync(
            context,
            moviePath,
            "sample"
        );

        Assert.That(result.Outcome, Is.EqualTo("skip_non_upper_tab"));
        Assert.That(appendCount, Is.EqualTo(0));
        Assert.That(result.UiReflectElapsedMs, Is.EqualTo(0));
    }

    [Test]
    public async Task ProcessScannedMovieAsync_mid_pass_suppressionでIncrementalFlushを直前停止する()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = @"E:\Movies\sample.mp4";
        int flushCount = 0;
        Queue<bool> suppressionStates = new([false, true]);
        List<QueueObj> pendingQueueItems = [];
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.AddFilesByFolder = pendingQueueItems;
        pendingContext.CheckFolder = @"E:\Movies";
        pendingContext.FlushPendingQueueItemsAction = (items, _) =>
        {
            flushCount += items.Count;
            items.Clear();
        };
        MainWindow.WatchScannedMovieContext context = new()
        {
            SnapshotDbFullPath = @"D:\Db\Main.wb",
            SnapshotTabIndex = 2,
            ExistingMovieByPath = new Dictionary<string, WatchMainDbMovieSnapshot>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                [moviePath] = new WatchMainDbMovieSnapshot(10, "hash-10", "", 0, 0),
            },
            ExistingViewMoviePaths = MainWindow.BuildMoviePathLookup([moviePath]),
            DisplayedMoviePaths = MainWindow.BuildMoviePathLookup([moviePath]),
            SearchKeyword = "",
            AllowViewConsistencyRepair = true,
            UseIncrementalUiMode = true,
            AllowMissingTabAutoEnqueue = true,
            AutoEnqueueTabIndex = 2,
            ThumbnailOutPath = @"E:\Thumbs",
            ExistingThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            OpenRescueRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            PendingMovieFlushContext = pendingContext,
            ShouldSuppressWatchWork = () => ReadNextSuppressionState(suppressionStates),
        };

        MainWindow.WatchScannedMovieProcessResult result = await window.ProcessScannedMovieAsync(
            context,
            moviePath,
            "sample"
        );

        Assert.That(result.Outcome, Is.EqualTo("enqueue_missing_thumb"));
        Assert.That(flushCount, Is.EqualTo(0));
        Assert.That(pendingQueueItems.Select(x => x.MovieFullPath), Is.EqualTo([moviePath]));
        Assert.That(result.EnqueueFlushElapsedMs, Is.EqualTo(0));
    }

    [Test]
    public async Task ProcessScannedMovieAsync_new_movieのDB登録後suppressionならcurrent_itemをdeferredへ戻す()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = @"E:\Movies\new-sample.mp4";
        Queue<bool> suppressionStates = new([true]);
        List<string> deferredTriggers = [];
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.MarkWatchWorkDeferredWhileSuppressedAction = trigger =>
            deferredTriggers.Add(trigger);
        pendingContext.ShouldSuppressWatchWork = () => ReadNextSuppressionState(suppressionStates);
        MovieInfo movie =
            (MovieInfo)RuntimeHelpers.GetUninitializedObject(typeof(MovieInfo));
        movie.MovieId = 21;
        movie.MoviePath = moviePath;
        movie.Hash = "hash-21";
        pendingContext.PendingNewMovies.Add(
            new MainWindow.PendingMovieRegistration(moviePath, "sample", movie)
        );

        MainWindow.WatchPendingNewMovieFlushResult result =
            await window.FlushPendingNewMoviesAsync(pendingContext);

        Assert.That(result.DeferredMoviePathsByUiSuppression, Is.EqualTo([moviePath]));
        Assert.That(deferredTriggers, Is.EqualTo(["pending_movie_flush"]));
    }

    [Test]
    public async Task ProcessScannedMovieAsync_existing_dbのsuppressionならcurrent_itemをdeferredへ戻す()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = @"E:\Movies\sample.mp4";
        Queue<bool> suppressionStates = new([false, true, true]);
        List<string> deferredTriggers = [];
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.MarkWatchWorkDeferredWhileSuppressedAction = trigger =>
            deferredTriggers.Add(trigger);
        MainWindow.WatchScannedMovieContext context = new()
        {
            SnapshotDbFullPath = @"D:\Db\Main.wb",
            SnapshotTabIndex = 2,
            ExistingMovieByPath = new Dictionary<string, WatchMainDbMovieSnapshot>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                [moviePath] = new WatchMainDbMovieSnapshot(10, "hash-10", "", 0, 0),
            },
            ExistingViewMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            DisplayedMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SearchKeyword = "",
            AllowViewConsistencyRepair = true,
            UseIncrementalUiMode = true,
            AllowMissingTabAutoEnqueue = true,
            AutoEnqueueTabIndex = 2,
            ThumbnailOutPath = @"E:\Thumbs",
            ExistingThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            OpenRescueRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            PendingMovieFlushContext = pendingContext,
            ShouldSuppressWatchWork = () => ReadNextSuppressionState(suppressionStates),
            AppendMovieToViewAsync = (_, _) => Task.CompletedTask,
        };

        MainWindow.WatchScannedMovieProcessResult result = await window.ProcessScannedMovieAsync(
            context,
            moviePath,
            "sample"
        );

        Assert.That(result.Outcome, Is.EqualTo("skip_enqueue_by_ui_suppression"));
        Assert.That(result.DeferredMoviePathsByUiSuppression, Is.EqualTo([moviePath]));
        Assert.That(deferredTriggers, Is.EqualTo(["existing_movie"]));
    }

    [Test]
    public async Task ProcessScannedMovieAsync_existing_dbのfile属性差分をDirtyFieldsへ積む()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = CreateTempMovieFile(4096, new DateTime(2026, 4, 17, 10, 11, 12));

        try
        {
            MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
            MainWindow.WatchScannedMovieContext context = new()
            {
                SnapshotDbFullPath = @"D:\Db\Main.wb",
                SnapshotTabIndex = 2,
                ExistingMovieByPath = new Dictionary<string, WatchMainDbMovieSnapshot>(
                    StringComparer.OrdinalIgnoreCase
                )
                {
                    [moviePath] = new WatchMainDbMovieSnapshot(
                        10,
                        "hash-10",
                        "2026-03-20 10:00:00",
                        1,
                        60
                    ),
                },
                ExistingViewMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                DisplayedMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                SearchKeyword = "",
                AllowViewConsistencyRepair = false,
                UseIncrementalUiMode = false,
                AllowExistingMovieDirtyTracking = true,
                AllowMissingTabAutoEnqueue = false,
                ExistingThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                OpenRescueRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                PendingMovieFlushContext = pendingContext,
                ShouldSuppressWatchWork = () => false,
                IsCurrentWatchScanScope = () => true,
            };

            MainWindow.WatchScannedMovieProcessResult result = await window.ProcessScannedMovieAsync(
                context,
                moviePath,
                "sample"
            );

            Assert.That(result.Outcome, Is.EqualTo("skip_non_upper_tab"));
            Assert.That(result.ChangedMovies.Count, Is.EqualTo(1));
            Assert.That(
                result.ChangedMovies[0].DirtyFields,
                Is.EqualTo(
                    MainWindow.WatchMovieDirtyFields.FileDate
                    | MainWindow.WatchMovieDirtyFields.MovieSize
                )
            );
            Assert.That(result.ChangedMovies[0].ObservedState.HasValue, Is.True);
            Assert.That(
                result.ChangedMovies[0].ObservedState!.Value.FileDateText,
                Is.EqualTo("2026-04-17 10:11:12")
            );
            Assert.That(result.ChangedMovies[0].ObservedState!.Value.MovieSizeKb, Is.EqualTo(4));
        }
        finally
        {
            TryDeleteFile(moviePath);
        }
    }

    [Test]
    public async Task ProcessScannedMovieAsync_length未確定ならmetadata_probeでDirtyFieldsへ積む()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        DateTime snapshotFileDate = new(2026, 4, 17, 10, 11, 12);
        string moviePath = CreateTempMovieFile(4096, snapshotFileDate);

        try
        {
            MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
            int probeCallCount = 0;
            MainWindow.WatchScannedMovieContext context = new()
            {
                SnapshotDbFullPath = @"D:\Db\Main.wb",
                SnapshotTabIndex = 2,
                ExistingMovieByPath = new Dictionary<string, WatchMainDbMovieSnapshot>(
                    StringComparer.OrdinalIgnoreCase
                )
                {
                    [moviePath] = new WatchMainDbMovieSnapshot(
                        10,
                        "hash-10",
                        "2026-04-17 10:11:12",
                        4,
                        0
                    ),
                },
                ExistingViewMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                DisplayedMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                SearchKeyword = "",
                AllowViewConsistencyRepair = false,
                UseIncrementalUiMode = true,
                AllowExistingMovieDirtyTracking = true,
                AllowMissingTabAutoEnqueue = false,
                ExistingThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                OpenRescueRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                PendingMovieFlushContext = pendingContext,
                ShouldSuppressWatchWork = () => false,
                IsCurrentWatchScanScope = () => true,
                ProbeExistingMovieObservedStateAsync = _ =>
                {
                    probeCallCount++;
                    return Task.FromResult<MainWindow.WatchMovieObservedState?>(
                        new MainWindow.WatchMovieObservedState(
                            "2026-04-17 10:11:12",
                            4,
                            120
                        )
                    );
                },
            };

            MainWindow.WatchScannedMovieProcessResult result = await window.ProcessScannedMovieAsync(
                context,
                moviePath,
                "sample"
            );

            Assert.That(result.Outcome, Is.EqualTo("skip_non_upper_tab"));
            Assert.That(probeCallCount, Is.EqualTo(1));
            Assert.That(result.ChangedMovies.Count, Is.EqualTo(1));
            Assert.That(
                result.ChangedMovies[0].DirtyFields,
                Is.EqualTo(MainWindow.WatchMovieDirtyFields.MovieLength)
            );
            Assert.That(result.ChangedMovies[0].ObservedState.HasValue, Is.True);
            Assert.That(result.ChangedMovies[0].ObservedState!.Value.MovieLengthSeconds, Is.EqualTo(120));
        }
        finally
        {
            TryDeleteFile(moviePath);
        }
    }

    [Test]
    public async Task ProcessScannedMovieAsync_length確定かつcheap差分なしならprobeしない()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        DateTime snapshotFileDate = new(2026, 4, 17, 10, 11, 12);
        string moviePath = CreateTempMovieFile(4096, snapshotFileDate);

        try
        {
            MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
            int probeCallCount = 0;
            MainWindow.WatchScannedMovieContext context = new()
            {
                SnapshotDbFullPath = @"D:\Db\Main.wb",
                SnapshotTabIndex = 2,
                ExistingMovieByPath = new Dictionary<string, WatchMainDbMovieSnapshot>(
                    StringComparer.OrdinalIgnoreCase
                )
                {
                    [moviePath] = new WatchMainDbMovieSnapshot(
                        10,
                        "hash-10",
                        "2026-04-17 10:11:12",
                        4,
                        60
                    ),
                },
                ExistingViewMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                DisplayedMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                SearchKeyword = "",
                AllowViewConsistencyRepair = false,
                UseIncrementalUiMode = true,
                AllowExistingMovieDirtyTracking = true,
                AllowMissingTabAutoEnqueue = false,
                ExistingThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                OpenRescueRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                PendingMovieFlushContext = pendingContext,
                ShouldSuppressWatchWork = () => false,
                IsCurrentWatchScanScope = () => true,
                ProbeExistingMovieObservedStateAsync = _ =>
                {
                    probeCallCount++;
                    return Task.FromResult<MainWindow.WatchMovieObservedState?>(
                        new MainWindow.WatchMovieObservedState(
                            "2026-04-17 10:11:12",
                            4,
                            120
                        )
                    );
                },
            };

            MainWindow.WatchScannedMovieProcessResult result = await window.ProcessScannedMovieAsync(
                context,
                moviePath,
                "sample"
            );

            Assert.That(result.Outcome, Is.EqualTo("skip_non_upper_tab"));
            Assert.That(probeCallCount, Is.EqualTo(0));
            Assert.That(result.ChangedMovies, Is.Empty);
        }
        finally
        {
            TryDeleteFile(moviePath);
        }
    }

    [Test]
    public async Task ProcessScannedMovieAsync_existing_dirty_tracking無効ならmetadata_probeしない()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        DateTime snapshotFileDate = new(2026, 4, 17, 10, 11, 12);
        string moviePath = CreateTempMovieFile(4096, snapshotFileDate);

        try
        {
            MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
            int probeCallCount = 0;
            MainWindow.WatchScannedMovieContext context = new()
            {
                SnapshotDbFullPath = @"D:\Db\Main.wb",
                SnapshotTabIndex = 2,
                ExistingMovieByPath = new Dictionary<string, WatchMainDbMovieSnapshot>(
                    StringComparer.OrdinalIgnoreCase
                )
                {
                    [moviePath] = new WatchMainDbMovieSnapshot(
                        10,
                        "hash-10",
                        "2026-04-17 10:11:12",
                        4,
                        0
                    ),
                },
                ExistingViewMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                DisplayedMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                SearchKeyword = "",
                AllowViewConsistencyRepair = false,
                UseIncrementalUiMode = true,
                AllowExistingMovieDirtyTracking = false,
                AllowMissingTabAutoEnqueue = false,
                ExistingThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                OpenRescueRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                PendingMovieFlushContext = pendingContext,
                ShouldSuppressWatchWork = () => false,
                IsCurrentWatchScanScope = () => true,
                ProbeExistingMovieObservedStateAsync = _ =>
                {
                    probeCallCount++;
                    return Task.FromResult<MainWindow.WatchMovieObservedState?>(
                        new MainWindow.WatchMovieObservedState(
                            "2026-04-17 10:11:12",
                            4,
                            120
                        )
                    );
                },
            };

            MainWindow.WatchScannedMovieProcessResult result = await window.ProcessScannedMovieAsync(
                context,
                moviePath,
                "sample"
            );

            Assert.That(result.Outcome, Is.EqualTo("skip_non_upper_tab"));
            Assert.That(probeCallCount, Is.EqualTo(0));
            Assert.That(result.ChangedMovies, Is.Empty);
        }
        finally
        {
            TryDeleteFile(moviePath);
        }
    }

    [Test]
    public async Task ProcessScannedMovieAsync_probe失敗でもcheap差分は落とさない()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        DateTime snapshotFileDate = new(2026, 4, 17, 10, 11, 12);
        string moviePath = CreateTempMovieFile(8192, snapshotFileDate.AddMinutes(1));

        try
        {
            MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
            int probeCallCount = 0;
            MainWindow.WatchScannedMovieContext context = new()
            {
                SnapshotDbFullPath = @"D:\Db\Main.wb",
                SnapshotTabIndex = 2,
                ExistingMovieByPath = new Dictionary<string, WatchMainDbMovieSnapshot>(
                    StringComparer.OrdinalIgnoreCase
                )
                {
                    [moviePath] = new WatchMainDbMovieSnapshot(
                        10,
                        "hash-10",
                        "2026-04-17 10:11:12",
                        4,
                        60
                    ),
                },
                ExistingViewMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                DisplayedMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                SearchKeyword = "",
                AllowViewConsistencyRepair = false,
                UseIncrementalUiMode = true,
                AllowExistingMovieDirtyTracking = true,
                AllowMissingTabAutoEnqueue = false,
                ExistingThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                OpenRescueRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                PendingMovieFlushContext = pendingContext,
                ShouldSuppressWatchWork = () => false,
                IsCurrentWatchScanScope = () => true,
                ProbeExistingMovieObservedStateAsync = _ =>
                {
                    probeCallCount++;
                    throw new InvalidOperationException("probe failed");
                },
            };

            MainWindow.WatchScannedMovieProcessResult result = await window.ProcessScannedMovieAsync(
                context,
                moviePath,
                "sample"
            );

            Assert.That(result.Outcome, Is.EqualTo("skip_non_upper_tab"));
            Assert.That(probeCallCount, Is.EqualTo(1));
            Assert.That(result.ChangedMovies.Count, Is.EqualTo(1));
            Assert.That(
                result.ChangedMovies[0].DirtyFields,
                Is.EqualTo(
                    MainWindow.WatchMovieDirtyFields.FileDate
                        | MainWindow.WatchMovieDirtyFields.MovieSize
                )
            );
            Assert.That(result.ChangedMovies[0].ObservedState.HasValue, Is.True);
            Assert.That(
                result.ChangedMovies[0].ObservedState!.Value.MovieLengthSeconds,
                Is.Null
            );
        }
        finally
        {
            TryDeleteFile(moviePath);
        }
    }

    [Test]
    public async Task FlushPendingNewMoviesAsync_mid_pass_staleならappendもdeferred戻しもしない()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = @"E:\Movies\new-sample.mp4";
        int appendCount = 0;
        List<string> deferredTriggers = [];
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.UseIncrementalUiMode = true;
        pendingContext.IsCurrentWatchScanScope = CreateScopeGuardThatTurnsStaleAfter(3);
        pendingContext.MarkWatchWorkDeferredWhileSuppressedAction = trigger =>
            deferredTriggers.Add(trigger);
        pendingContext.InsertMoviesBatchAsync = (_, movies) => Task.FromResult(movies.Count);
        pendingContext.AppendMovieToViewAsync = (_, _) =>
        {
            appendCount++;
            return Task.CompletedTask;
        };

        MovieInfo movie =
            (MovieInfo)RuntimeHelpers.GetUninitializedObject(typeof(MovieInfo));
        movie.MovieId = 21;
        movie.MoviePath = moviePath;
        movie.Hash = "hash-21";
        pendingContext.PendingNewMovies.Add(
            new MainWindow.PendingMovieRegistration(moviePath, "sample", movie)
        );

        MainWindow.WatchPendingNewMovieFlushResult result =
            await window.FlushPendingNewMoviesAsync(pendingContext);

        Assert.That(result.WasDroppedByStaleScope, Is.True);
        Assert.That(appendCount, Is.EqualTo(0));
        Assert.That(result.DeferredMoviePathsByUiSuppression, Is.Empty);
        Assert.That(deferredTriggers, Is.Empty);
    }

    [Test]
    public async Task ProcessScannedMovieAsync_mid_pass_staleならincremental_flushもdeferred戻しもしない()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = @"E:\Movies\sample.mp4";
        int flushCount = 0;
        List<string> deferredTriggers = [];
        List<QueueObj> pendingQueueItems = [];
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.AddFilesByFolder = pendingQueueItems;
        pendingContext.CheckFolder = @"E:\Movies";
        pendingContext.IsCurrentWatchScanScope = CreateScopeGuardThatTurnsStaleAfter(2);
        pendingContext.MarkWatchWorkDeferredWhileSuppressedAction = trigger =>
            deferredTriggers.Add(trigger);
        pendingContext.FlushPendingQueueItemsAction = (items, _) =>
        {
            flushCount += items.Count;
            items.Clear();
        };
        MainWindow.WatchScannedMovieContext context = new()
        {
            SnapshotDbFullPath = @"D:\Db\Main.wb",
            SnapshotTabIndex = 2,
            ExistingMovieByPath = new Dictionary<string, WatchMainDbMovieSnapshot>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                [moviePath] = new WatchMainDbMovieSnapshot(10, "hash-10", "", 0, 0),
            },
            ExistingViewMoviePaths = MainWindow.BuildMoviePathLookup([moviePath]),
            DisplayedMoviePaths = MainWindow.BuildMoviePathLookup([moviePath]),
            SearchKeyword = "",
            AllowViewConsistencyRepair = true,
            UseIncrementalUiMode = true,
            AllowMissingTabAutoEnqueue = true,
            AutoEnqueueTabIndex = 2,
            ThumbnailOutPath = @"E:\Thumbs",
            ExistingThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            OpenRescueRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            PendingMovieFlushContext = pendingContext,
            IsCurrentWatchScanScope = pendingContext.IsCurrentWatchScanScope,
            ShouldSuppressWatchWork = () => false,
        };

        MainWindow.WatchScannedMovieProcessResult result = await window.ProcessScannedMovieAsync(
            context,
            moviePath,
            "sample"
        );

        Assert.That(result.WasDroppedByStaleScope, Is.True);
        Assert.That(result.Outcome, Is.EqualTo("drop_stale_scope"));
        Assert.That(flushCount, Is.EqualTo(0));
        Assert.That(result.DeferredMoviePathsByUiSuppression, Is.Empty);
        Assert.That(deferredTriggers, Is.Empty);
    }

    [Test]
    public async Task ProcessScannedMovieAsync_movieinfo例外でも全体を落とさずskip扱いで返す()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = @"Z:\missing\sample.mp4";
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.CheckFolder = @"Z:\missing";
        MainWindow.WatchScannedMovieContext context = new()
        {
            SnapshotDbFullPath = @"D:\Db\Main.wb",
            SnapshotTabIndex = 2,
            ExistingMovieByPath = new Dictionary<string, WatchMainDbMovieSnapshot>(
                StringComparer.OrdinalIgnoreCase
            ),
            ExistingViewMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            DisplayedMoviePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SearchKeyword = "",
            AllowViewConsistencyRepair = true,
            UseIncrementalUiMode = false,
            AllowMissingTabAutoEnqueue = false,
            ExistingThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            OpenRescueRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            PendingMovieFlushContext = pendingContext,
            ShouldSuppressWatchWork = () => false,
            IsCurrentWatchScanScope = () => true,
        };

        MainWindow.WatchScannedMovieProcessResult result = await window.ProcessScannedMovieAsync(
            context,
            moviePath,
            "sample"
        );

        Assert.That(result.Outcome, Is.EqualTo("skip_movieinfo_exception"));
        Assert.That(result.HasFolderUpdate, Is.False);
        Assert.That(pendingContext.PendingNewMovies, Is.Empty);
    }

    [Test]
    public void FlushFinalWatchFolderQueue_suppression中は未flushのまま呼び出し側へ返す()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = @"E:\Movies\sample.mp4";
        List<QueueObj> pendingQueueItems =
        [
            new QueueObj
            {
                MovieId = 10,
                MovieFullPath = moviePath,
                Hash = "hash-10",
                Tabindex = 2,
                Priority = ThumbnailQueuePriority.Normal,
            },
        ];
        int flushCount = 0;
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.AddFilesByFolder = pendingQueueItems;
        pendingContext.CheckFolder = @"E:\Movies";
        pendingContext.FlushPendingQueueItemsAction = (items, _) =>
        {
            flushCount += items.Count;
            items.Clear();
        };
        MainWindow.WatchFolderScanContext context = new()
        {
            ScannedMovieContext = new MainWindow.WatchScannedMovieContext
            {
                PendingMovieFlushContext = pendingContext,
                ShouldSuppressWatchWork = () => true,
            },
        };

        MainWindow.WatchFinalQueueFlushResult result = window.FlushFinalWatchFolderQueue(context);

        Assert.That(flushCount, Is.EqualTo(0));
        Assert.That(pendingQueueItems.Select(x => x.MovieFullPath), Is.EqualTo([moviePath]));
        Assert.That(result.WasDeferredBySuppression, Is.True);
        Assert.That(result.WasStoppedByUiSuppression, Is.False);
    }

    [Test]
    public void FlushFinalWatchFolderQueue_suppression再退避callback成功なら停止を返す()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = @"E:\Movies\sample.mp4";
        string? capturedTrigger = null;
        List<QueueObj> pendingQueueItems =
        [
            new QueueObj
            {
                MovieId = 10,
                MovieFullPath = moviePath,
                Hash = "hash-10",
                Tabindex = 2,
                Priority = ThumbnailQueuePriority.Normal,
            },
        ];
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.AddFilesByFolder = pendingQueueItems;
        pendingContext.CheckFolder = @"E:\Movies";
        MainWindow.WatchFolderScanContext context = new()
        {
            ScannedMovieContext = new MainWindow.WatchScannedMovieContext
            {
                PendingMovieFlushContext = pendingContext,
                ShouldSuppressWatchWork = () => true,
            },
            TryDeferWatchFolderWorkByUiSuppressionAction = trigger =>
            {
                capturedTrigger = trigger;
                return true;
            },
        };

        MainWindow.WatchFinalQueueFlushResult result = window.FlushFinalWatchFolderQueue(context);

        Assert.That(result.WasDeferredBySuppression, Is.True);
        Assert.That(result.WasStoppedByUiSuppression, Is.True);
        Assert.That(capturedTrigger, Is.EqualTo("folder-final-queue:E:\\Movies"));
    }

    [Test]
    public void TryDeferWatchFolderPreprocess_callbackへremaining_pathsとtriggerを渡す()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string[] remainingPaths = [@"E:\Movies\a.mp4", @"E:\Movies\b.mp4"];
        string? capturedTrigger = null;
        string[] capturedPaths = [];
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.CheckFolder = @"E:\Movies";
        MainWindow.WatchFolderScanContext context = new()
        {
            ScannedMovieContext = new MainWindow.WatchScannedMovieContext
            {
                PendingMovieFlushContext = pendingContext,
            },
            TryDeferWatchFolderPreprocessByUiSuppressionAction = (paths, trigger) =>
            {
                capturedTrigger = trigger;
                capturedPaths = paths.ToArray();
                return true;
            },
        };

        bool result = window.TryDeferWatchFolderPreprocess(context, remainingPaths);

        Assert.That(result, Is.True);
        Assert.That(capturedTrigger, Is.EqualTo("folder-preprocess:E:\\Movies"));
        Assert.That(capturedPaths, Is.EqualTo(remainingPaths));
    }

    [Test]
    public void TryDeferWatchFolderMid_callbackへremaining_pathsとtriggerを渡す()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string[] remainingPaths = [@"E:\Movies\c.mp4", @"E:\Movies\d.mp4"];
        string? capturedTrigger = null;
        string[] capturedPaths = [];
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.CheckFolder = @"E:\Movies";
        MainWindow.WatchFolderScanContext context = new()
        {
            ScannedMovieContext = new MainWindow.WatchScannedMovieContext
            {
                PendingMovieFlushContext = pendingContext,
            },
            TryDeferWatchFolderMidByUiSuppressionAction = (paths, trigger) =>
            {
                capturedTrigger = trigger;
                capturedPaths = paths.ToArray();
                return true;
            },
        };

        bool result = window.TryDeferWatchFolderMid(context, remainingPaths);

        Assert.That(result, Is.True);
        Assert.That(capturedTrigger, Is.EqualTo("folder-mid:E:\\Movies"));
        Assert.That(capturedPaths, Is.EqualTo(remainingPaths));
    }

    [Test]
    public void IsWatchFolderScopeStale_current_scopeならfalse()
    {
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.IsCurrentWatchScanScope = () => true;
        MainWindow.WatchFolderScanContext context = new()
        {
            ScannedMovieContext = new MainWindow.WatchScannedMovieContext
            {
                PendingMovieFlushContext = pendingContext,
            },
        };

        bool result = MainWindow.IsWatchFolderScopeStale(context);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsWatchFolderScopeStale_stale_scopeならtrue()
    {
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.IsCurrentWatchScanScope = () => false;
        MainWindow.WatchFolderScanContext context = new()
        {
            ScannedMovieContext = new MainWindow.WatchScannedMovieContext
            {
                PendingMovieFlushContext = pendingContext,
            },
        };

        bool result = MainWindow.IsWatchFolderScopeStale(context);

        Assert.That(result, Is.True);
    }

    [Test]
    public void FlushFinalWatchFolderQueue_stale_scopeならflushしない()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = @"E:\Movies\sample.mp4";
        List<QueueObj> pendingQueueItems =
        [
            new QueueObj
            {
                MovieId = 10,
                MovieFullPath = moviePath,
                Hash = "hash-10",
                Tabindex = 2,
                Priority = ThumbnailQueuePriority.Normal,
            },
        ];
        int flushCount = 0;
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.AddFilesByFolder = pendingQueueItems;
        pendingContext.CheckFolder = @"E:\Movies";
        pendingContext.IsCurrentWatchScanScope = () => false;
        pendingContext.FlushPendingQueueItemsAction = (items, _) =>
        {
            flushCount += items.Count;
            items.Clear();
        };
        MainWindow.WatchFolderScanContext context = new()
        {
            ScannedMovieContext = new MainWindow.WatchScannedMovieContext
            {
                PendingMovieFlushContext = pendingContext,
                ShouldSuppressWatchWork = () => false,
            },
        };

        MainWindow.WatchFinalQueueFlushResult result = window.FlushFinalWatchFolderQueue(context);

        Assert.That(flushCount, Is.EqualTo(0));
        Assert.That(pendingQueueItems.Select(x => x.MovieFullPath), Is.EqualTo([moviePath]));
        Assert.That(result.WasDeferredBySuppression, Is.False);
        Assert.That(result.WasDroppedByStaleScope, Is.True);
    }

    private static MainWindow CreateMainWindowForCoordinatorTests()
    {
        return (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
    }

    private static bool ReadNextSuppressionState(Queue<bool> suppressionStates)
    {
        return suppressionStates.Count > 0 && suppressionStates.Dequeue();
    }

    private static MainWindow.WatchPendingNewMovieFlushContext CreatePendingFlushContext()
    {
        return new MainWindow.WatchPendingNewMovieFlushContext
        {
            ExistingMovieByPath = new Dictionary<string, WatchMainDbMovieSnapshot>(
                StringComparer.OrdinalIgnoreCase
            ),
            PendingNewMovies = [],
            ExistingThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            OpenRescueRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AddFilesByFolder = [],
            InsertMoviesBatchAsync = (_, _) => Task.FromResult(0),
            RemovePendingMoviePlaceholderAction = _ => { },
        };
    }

    private static Func<bool> CreateScopeGuardThatTurnsStaleAfter(int allowedCalls)
    {
        int callCount = 0;
        return () =>
        {
            callCount++;
            return callCount <= allowedCalls;
        };
    }

    private static string CreateTempMovieFile(long sizeBytes, DateTime lastWriteTime)
    {
        string path = Path.Combine(Path.GetTempPath(), $"imm-watch-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(path, new byte[sizeBytes]);
        File.SetLastWriteTime(path, lastWriteTime);
        return path;
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
            // 一時ファイル掃除失敗は本体判定を優先する。
        }
    }
}
