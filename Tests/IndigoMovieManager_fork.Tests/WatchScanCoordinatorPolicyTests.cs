using System.Runtime.CompilerServices;
using IndigoMovieManager;
using IndigoMovieManager.Data;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WatchScanCoordinatorPolicyTests
{
    [Test]
    public void EvaluateWatchFolderMoviePreCheck_visible_only_gate縺ｯfirst_hit蜑阪↓豁｢繧√ｋ()
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
    public void EvaluateWatchFolderMoviePreCheck_zero_byte縺ｯfirst_hit騾夂衍蠕後↓豁｢繧√ｋ()
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
    public void EvaluateWatchFolderMoviePreCheck_騾壼ｸｸ蜍慕判縺ｯ邯咏ｶ壹＠蛻晏屓縺縺素irst_hit騾夂衍縺吶ｋ()
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
    public void EvaluateWatchFolderMoviePreCheck_empty_body縺ｯ霑ｽ蜉騾夂衍縺帙★豁｢繧√ｋ()
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
    public async Task ProcessScannedMovieAsync_mid_pass_suppression縺ｧUIAppend繧堤峩蜑榊●豁｢縺吶ｋ()
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
                [moviePath] = new WatchMainDbMovieSnapshot(1, "hash-1"),
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
    public async Task ProcessScannedMovieAsync_mid_pass_suppression縺ｧIncrementalFlush繧堤峩蜑榊●豁｢縺吶ｋ()
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
                [moviePath] = new WatchMainDbMovieSnapshot(10, "hash-10"),
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
    public async Task ProcessScannedMovieAsync_new_movie縺ｮDB逋ｻ骭ｲ蠕茎uppression縺ｪ繧営urrent_item繧壇eferred縺ｸ謌ｻ縺・)
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
    public async Task ProcessScannedMovieAsync_existing_db縺ｮsuppression縺ｪ繧営urrent_item繧壇eferred縺ｸ謌ｻ縺・)
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
                [moviePath] = new WatchMainDbMovieSnapshot(10, "hash-10"),
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
    public async Task FlushPendingNewMoviesAsync_mid_pass_stale縺ｪ繧餌ppend繧Ｅeferred謌ｻ縺励ｂ縺励↑縺・)
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
    public async Task ProcessScannedMovieAsync_mid_pass_stale縺ｪ繧永ncremental_flush繧Ｅeferred謌ｻ縺励ｂ縺励↑縺・)
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
                [moviePath] = new WatchMainDbMovieSnapshot(10, "hash-10"),
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
    public void FlushFinalWatchFolderQueue_suppression荳ｭ縺ｯ譛ｪflush縺ｮ縺ｾ縺ｾ蜻ｼ縺ｳ蜃ｺ縺怜・縺ｸ霑斐☆()
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
    }

    [Test]
    public void FlushFinalWatchFolderQueue_stale_scope縺ｪ繧映lush縺励↑縺・)
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
}
