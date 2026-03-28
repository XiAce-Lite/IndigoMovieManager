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
}
