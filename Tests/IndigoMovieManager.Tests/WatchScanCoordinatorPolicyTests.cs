using System.Data;
using System.Runtime.CompilerServices;
using System.Reflection;
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
    public void ApplyWatchScannedMovieProcessResult_計測値とchanged_movieをまとめて反映する()
    {
        MainWindow.WatchScannedMovieProcessResult processResult = new()
        {
            DbLookupElapsedMs = 10,
            MovieInfoElapsedMs = 20,
            DbInsertElapsedMs = 30,
            UiReflectElapsedMs = 40,
            EnqueueFlushElapsedMs = 50,
            AddedByFolderCount = 2,
            EnqueuedCount = 3,
            HasFolderUpdate = true,
        };
        processResult.AddChangedMovie(
            @"E:\Movies\sample.mp4",
            MainWindow.WatchMovieChangeKind.SourceInserted,
            MainWindow.WatchMovieDirtyFields.FileDate
        );

        long dbLookupTotalMs = 1;
        long movieInfoTotalMs = 2;
        long dbInsertTotalMs = 3;
        long uiReflectTotalMs = 4;
        long enqueueFlushTotalMs = 5;
        int addedByFolderCount = 6;
        int enqueuedCount = 7;
        bool folderCheckflg = false;
        List<MainWindow.WatchChangedMovie> changedMoviesForUiReload = [];

        MainWindow.ApplyWatchScannedMovieProcessResult(
            processResult,
            ref dbLookupTotalMs,
            ref movieInfoTotalMs,
            ref dbInsertTotalMs,
            ref uiReflectTotalMs,
            ref enqueueFlushTotalMs,
            ref addedByFolderCount,
            ref enqueuedCount,
            ref folderCheckflg,
            ref changedMoviesForUiReload
        );

        Assert.That(dbLookupTotalMs, Is.EqualTo(11));
        Assert.That(movieInfoTotalMs, Is.EqualTo(22));
        Assert.That(dbInsertTotalMs, Is.EqualTo(33));
        Assert.That(uiReflectTotalMs, Is.EqualTo(44));
        Assert.That(enqueueFlushTotalMs, Is.EqualTo(55));
        Assert.That(addedByFolderCount, Is.EqualTo(8));
        Assert.That(enqueuedCount, Is.EqualTo(10));
        Assert.That(folderCheckflg, Is.True);
        Assert.That(changedMoviesForUiReload, Has.Count.EqualTo(1));
        Assert.That(changedMoviesForUiReload[0].MoviePath, Is.EqualTo(@"E:\Movies\sample.mp4"));
    }

    [Test]
    public void TryApplyDeferredPathsFromProcessResult_deferred_pathがある時だけmergeを呼ぶ()
    {
        MainWindow.WatchScannedMovieProcessResult processResult = new();
        List<MainWindow.PendingMovieRegistration> pendingNewMovies = [];
        List<QueueObj> addFilesByFolder = [];
        string[] remainingScanPaths = [@"E:\Movies\remain.mp4"];
        List<string> capturedDeferredMoviePaths = null;
        List<string> capturedRemainingScanPaths = null;

        processResult.AddDeferredMoviePath(@"E:\Movies\sample.mp4", null, "");

        bool merged = MainWindow.TryApplyDeferredPathsFromProcessResult(
            processResult,
            @"D:\Db\Main.wb",
            123,
            @"E:\Movies",
            includeSubfolders: true,
            remainingScanPaths,
            pendingNewMovies,
            addFilesByFolder,
            (_, _, _, _, deferredMoviePaths, remainingPaths, _, _) =>
            {
                capturedDeferredMoviePaths = deferredMoviePaths.ToList();
                capturedRemainingScanPaths = remainingPaths.ToList();
            }
        );

        Assert.That(merged, Is.True);
        Assert.That(capturedDeferredMoviePaths, Is.EqualTo([@"E:\Movies\sample.mp4"]));
        Assert.That(capturedRemainingScanPaths, Is.EqualTo([@"E:\Movies\remain.mp4"]));
    }

    [Test]
    public void TryApplyDeferredPathsFromMovieLoop_現在位置以降の残りpathだけを渡す()
    {
        MainWindow.WatchScannedMovieProcessResult processResult = new();
        List<MainWindow.PendingMovieRegistration> pendingNewMovies = [];
        List<QueueObj> addFilesByFolder = [];
        List<string> capturedRemainingScanPaths = null;
        string[] scanMoviePaths =
        [
            @"E:\Movies\current.mp4",
            @"E:\Movies\remain1.mp4",
            @"E:\Movies\remain2.mp4",
        ];

        processResult.AddDeferredMoviePath(@"E:\Movies\current.mp4", null, "");

        bool merged = MainWindow.TryApplyDeferredPathsFromMovieLoop(
            processResult,
            @"D:\Db\Main.wb",
            123,
            @"E:\Movies",
            includeSubfolders: true,
            scanMoviePaths,
            currentMovieIndex: 0,
            pendingNewMovies,
            addFilesByFolder,
            (_, _, _, _, _, remainingPaths, _, _) =>
            {
                capturedRemainingScanPaths = remainingPaths.ToList();
            }
        );

        Assert.That(merged, Is.True);
        Assert.That(
            capturedRemainingScanPaths,
            Is.EqualTo([@"E:\Movies\remain1.mp4", @"E:\Movies\remain2.mp4"])
        );
    }

    [Test]
    public void RefreshWatchVisibleMovieGate_閾値到達でvisible_onlyを有効化する()
    {
        (bool restrictWatchWorkToVisibleMovies, int currentWatchQueueActiveCount) =
            MainWindow.RefreshWatchVisibleMovieGate(
                isWatchMode: true,
                visibleMoviePaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    @"E:\Movies\Sample.mp4",
                },
                threshold: 500,
                currentTabIndex: 2,
                getCurrentQueueActiveCount: () => 500,
                currentRestrictWatchWorkToVisibleMovies: false,
                currentWatchQueueActiveCount: 0,
                reason: "initial"
            );

        Assert.That(restrictWatchWorkToVisibleMovies, Is.True);
        Assert.That(currentWatchQueueActiveCount, Is.EqualTo(500));
    }

    [Test]
    public void InitializeWatchVisibleMovieGate_初期状態からvisible_onlyを判定できる()
    {
        (bool restrictWatchWorkToVisibleMovies, int currentWatchQueueActiveCount) =
            MainWindow.InitializeWatchVisibleMovieGate(
                isWatchMode: true,
                visibleMoviePaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    @"E:\Movies\Sample.mp4",
                },
                threshold: 500,
                currentTabIndex: 2,
                getCurrentQueueActiveCount: () => 500
            );

        Assert.That(restrictWatchWorkToVisibleMovies, Is.True);
        Assert.That(currentWatchQueueActiveCount, Is.EqualTo(500));
    }

    [Test]
    public void ResolveWatchScanUiReloadMode_watch大量追加時はquery_onlyをfullへ落とす()
    {
        (
            bool useIncrementalUiMode,
            bool canUseQueryOnlyWatchReload,
            bool wasDowngradedToFull
        ) = InvokeResolveWatchScanUiReloadMode(
            "Watch",
            newMovieCount: 21,
            incrementalUiUpdateThreshold: 20,
            canUseQueryOnlyWatchReload: true
        );

        Assert.That(useIncrementalUiMode, Is.False);
        Assert.That(canUseQueryOnlyWatchReload, Is.False);
        Assert.That(wasDowngradedToFull, Is.True);
    }

    [Test]
    public void ResolveWatchScanUiReloadMode_manual大量追加時はquery_only状態を変えない()
    {
        (
            bool useIncrementalUiMode,
            bool canUseQueryOnlyWatchReload,
            bool wasDowngradedToFull
        ) = InvokeResolveWatchScanUiReloadMode(
            "Manual",
            newMovieCount: 21,
            incrementalUiUpdateThreshold: 20,
            canUseQueryOnlyWatchReload: true
        );

        Assert.That(useIncrementalUiMode, Is.False);
        Assert.That(canUseQueryOnlyWatchReload, Is.True);
        Assert.That(wasDowngradedToFull, Is.False);
    }

    [TestCase("Auto", "SELECT * FROM watch where auto = 1")]
    [TestCase("Watch", "SELECT * FROM watch where watch = 1")]
    [TestCase("Manual", "SELECT * FROM watch")]
    public void ResolveWatchFolderQuerySql_modeごとの抽出条件を返す(
        string modeName,
        string expectedSql
    )
    {
        Assert.That(InvokeResolveWatchFolderQuerySql(modeName), Is.EqualTo(expectedSql));
    }

    [Test]
    public void ResolveWatchFolderTarget_dirとsub設定を返す()
    {
        DataTable table = new();
        table.Columns.Add("dir", typeof(string));
        table.Columns.Add("sub", typeof(long));
        DataRow row = table.NewRow();
        row["dir"] = @"E:\Movies";
        row["sub"] = 1L;
        table.Rows.Add(row);

        (string checkFolder, bool sub) = InvokeResolveWatchFolderTarget(row);

        Assert.That(checkFolder, Is.EqualTo(@"E:\Movies"));
        Assert.That(sub, Is.True);
    }

    [Test]
    public void ResolveWatchScanStrategyDetail_detail一式を返す()
    {
        (
            string detailCode,
            string detailMessage,
            string detailCategory,
            string detailAxis
        ) = InvokeResolveWatchScanStrategyDetail("everything-cache");

        Assert.That(detailCode, Is.Not.Empty);
        Assert.That(detailMessage, Is.Not.Empty);
        Assert.That(detailCategory, Is.Not.Empty);
        Assert.That(detailAxis, Is.Not.Empty);
    }

    [Test]
    public void TryHandlePendingFlushGuardResult_stale時はreturn判定を返す()
    {
        MainWindow.WatchPendingNewMovieFlushResult flushResult = new()
        {
            AddedByFolderCount = 2,
        };
        MainWindow.WatchPendingNewMovieGuardResult guardResult = new(
            flushResult,
            WasDroppedByStaleScope: true,
            WasStoppedByUiSuppression: false
        );

        (bool shouldReturn, MainWindow.WatchPendingNewMovieFlushResult returnedFlushResult, bool shouldBreak) =
            InvokeTryHandlePendingFlushGuardResult(guardResult);

        Assert.That(shouldReturn, Is.True);
        Assert.That(returnedFlushResult, Is.SameAs(flushResult));
        Assert.That(shouldBreak, Is.False);
    }

    [Test]
    public void TryHandlePendingFlushGuardResult_suppression時はbreak判定を返す()
    {
        MainWindow.WatchPendingNewMovieGuardResult guardResult = new(
            MainWindow.WatchPendingNewMovieFlushResult.None,
            WasDroppedByStaleScope: false,
            WasStoppedByUiSuppression: true
        );

        (bool shouldReturn, _, bool shouldBreak) =
            InvokeTryHandlePendingFlushGuardResult(guardResult);

        Assert.That(shouldReturn, Is.False);
        Assert.That(shouldBreak, Is.True);
    }

    [Test]
    public void TryHandleFinalQueueFlushResult_経過時間を加算しsuppression時はbreakを返す()
    {
        MainWindow.WatchFinalQueueFlushResult flushResult = new(
            ElapsedMs: 25,
            WasDeferredBySuppression: false,
            WasDroppedByStaleScope: false,
            WasStoppedByUiSuppression: true
        );
        long enqueueFlushTotalMs = 10;

        bool shouldBreak = MainWindow.TryHandleFinalQueueFlushResult(
            flushResult,
            ref enqueueFlushTotalMs
        );

        Assert.That(shouldBreak, Is.True);
        Assert.That(enqueueFlushTotalMs, Is.EqualTo(35));
    }

    [Test]
    public void ShouldDelayAfterWatchFolderFailure_io時だけ待機対象にする()
    {
        Assert.That(
            MainWindow.ShouldDelayAfterWatchFolderFailure(new IOException("locked")),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldDelayAfterWatchFolderFailure(new InvalidOperationException("other")),
            Is.False
        );
    }

    [Test]
    public void TryHandleRecoveryFlushResult_件数反映とsuppression返却をまとめて扱う()
    {
        MainWindow.WatchPendingNewMovieFlushResult flushResult = new()
        {
            DbInsertElapsedMs = 10,
            UiReflectElapsedMs = 20,
            EnqueueFlushElapsedMs = 30,
            AddedByFolderCount = 1,
            EnqueuedCount = 2,
        };
        flushResult.AddChangedMovie(
            @"E:\Movies\sample.mp4",
            MainWindow.WatchMovieChangeKind.SourceInserted,
            MainWindow.WatchMovieDirtyFields.FileDate
        );
        flushResult.AddDeferredMoviePath(@"E:\Movies\sample.mp4", null, "");

        long dbInsertTotalMs = 1;
        long uiReflectTotalMs = 2;
        long enqueueFlushTotalMs = 3;
        int addedByFolderCount = 4;
        int enqueuedCount = 5;
        bool folderCheckflg = false;
        List<MainWindow.WatchChangedMovie> changedMoviesForUiReload = [];
        List<string> mergedDeferredMoviePaths = null;

        bool wasDeferred = MainWindow.TryHandleRecoveryFlushResult(
            flushResult,
            @"D:\Db\Main.wb",
            123,
            @"E:\Movies",
            includeSubfolders: true,
            pendingNewMovies: [],
            addFilesByFolder: [],
            (_, _, _, _, deferredMoviePaths, _, _, _) =>
            {
                mergedDeferredMoviePaths = deferredMoviePaths.ToList();
            },
            ref dbInsertTotalMs,
            ref uiReflectTotalMs,
            ref enqueueFlushTotalMs,
            ref addedByFolderCount,
            ref enqueuedCount,
            ref folderCheckflg,
            ref changedMoviesForUiReload
        );

        Assert.That(wasDeferred, Is.True);
        Assert.That(dbInsertTotalMs, Is.EqualTo(11));
        Assert.That(uiReflectTotalMs, Is.EqualTo(22));
        Assert.That(enqueueFlushTotalMs, Is.EqualTo(33));
        Assert.That(addedByFolderCount, Is.EqualTo(5));
        Assert.That(enqueuedCount, Is.EqualTo(7));
        Assert.That(folderCheckflg, Is.True);
        Assert.That(changedMoviesForUiReload, Has.Count.EqualTo(1));
        Assert.That(mergedDeferredMoviePaths, Is.EqualTo([@"E:\Movies\sample.mp4"]));
    }

    [Test]
    public void BuildWatchScanFileSummaryMessage_件数を含む文言を返す()
    {
        string message = MainWindow.BuildWatchScanFileSummaryMessage(@"E:\Movies", 12, 3);

        Assert.That(message, Is.EqualTo("scan file summary: folder='E:\\Movies' scanned=12 new=3"));
    }

    [Test]
    public void BuildWatchCheckTaskEndMessage_要約文言を返す()
    {
        string message = InvokeBuildWatchCheckTaskEndMessage(
            "Watch",
            checkedFolderCount: 4,
            enqueuedCount: 7,
            hasFolderUpdate: true,
            elapsedMs: 1234
        );

        Assert.That(
            message,
            Is.EqualTo("mode=Watch folders=4 enqueued=7 updated=True elapsed_ms=1234")
        );
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

    private static (
        bool UseIncrementalUiMode,
        bool CanUseQueryOnlyWatchReload,
        bool WasDowngradedToFull
    ) InvokeResolveWatchScanUiReloadMode(
        string modeName,
        int newMovieCount,
        int incrementalUiUpdateThreshold,
        bool canUseQueryOnlyWatchReload
    )
    {
        Type checkModeType = typeof(MainWindow).GetNestedType(
            "CheckMode",
            BindingFlags.NonPublic
        )!;
        Assert.That(checkModeType, Is.Not.Null);

        MethodInfo method = typeof(MainWindow).GetMethod(
            "ResolveWatchScanUiReloadMode",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);

        object mode = Enum.Parse(checkModeType, modeName);
        return ((bool, bool, bool))
            method.Invoke(
                null,
                [mode, newMovieCount, incrementalUiUpdateThreshold, canUseQueryOnlyWatchReload]
            )!;
    }

    private static string InvokeResolveWatchFolderQuerySql(string modeName)
    {
        Type checkModeType = typeof(MainWindow).GetNestedType(
            "CheckMode",
            BindingFlags.NonPublic
        )!;
        Assert.That(checkModeType, Is.Not.Null);

        MethodInfo method = typeof(MainWindow).GetMethod(
            "ResolveWatchFolderQuerySql",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);

        object mode = Enum.Parse(checkModeType, modeName);
        return (string)method.Invoke(null, [mode])!;
    }

    private static (string CheckFolder, bool Sub) InvokeResolveWatchFolderTarget(DataRow row)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "ResolveWatchFolderTarget",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);
        return ((string, bool))method.Invoke(null, [row])!;
    }

    private static (
        string DetailCode,
        string DetailMessage,
        string DetailCategory,
        string DetailAxis
    ) InvokeResolveWatchScanStrategyDetail(string detail)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "ResolveWatchScanStrategyDetail",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);
        return ((string, string, string, string))method.Invoke(null, [detail])!;
    }

    private static (
        bool ShouldReturn,
        MainWindow.WatchPendingNewMovieFlushResult FlushResult,
        bool ShouldBreakByUiSuppression
    ) InvokeTryHandlePendingFlushGuardResult(MainWindow.WatchPendingNewMovieGuardResult guardResult)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "TryHandlePendingFlushGuardResult",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);

        object[] args =
        [
            guardResult,
            null!,
            false,
        ];
        bool shouldReturn = (bool)method.Invoke(null, args)!;
        return (
            shouldReturn,
            (MainWindow.WatchPendingNewMovieFlushResult)args[1],
            (bool)args[2]
        );
    }

    private static string InvokeBuildWatchCheckTaskEndMessage(
        string modeName,
        int checkedFolderCount,
        int enqueuedCount,
        bool hasFolderUpdate,
        long elapsedMs
    )
    {
        Type checkModeType = typeof(MainWindow).GetNestedType(
            "CheckMode",
            BindingFlags.NonPublic
        )!;
        Assert.That(checkModeType, Is.Not.Null);

        MethodInfo method = typeof(MainWindow).GetMethod(
            "BuildWatchCheckTaskEndMessage",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);

        object mode = Enum.Parse(checkModeType, modeName);
        return (string)method.Invoke(
            null,
            [mode, checkedFolderCount, enqueuedCount, hasFolderUpdate, elapsedMs]
        )!;
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
    public void TryHandleWatchFolderMoviePreCheck_zero_byteなら通知とerror_markerをまとめて処理する()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        int notifyCount = 0;
        (string Path, int TabIndex, string Reason)? capturedErrorMarker = null;
        MainWindow.WatchFolderScanMovieResult result = new();
        MainWindow.WatchFolderScanContext context = new()
        {
            HasNotifiedFolderHit = false,
            NotifyFolderFirstHit = () => notifyCount++,
            AllowMissingTabAutoEnqueue = true,
            AutoEnqueueTabIndex = 3,
            CreateErrorMarkerForSkippedMovieAction = (path, tabIndex, reason) =>
                capturedErrorMarker = (path, tabIndex, reason),
        };

        bool handled = window.TryHandleWatchFolderMoviePreCheck(
            context,
            @"E:\Movies\sample.mp4",
            0,
            new MainWindow.WatchFolderMoviePreCheckDecision(
                "skip_zero_byte",
                ShouldNotifyFolderHit: true,
                ShouldContinueProcessing: false,
                IsZeroByteMovie: true
            ),
            result
        );

        Assert.That(handled, Is.True);
        Assert.That(notifyCount, Is.EqualTo(1));
        Assert.That(context.HasNotifiedFolderHit, Is.True);
        Assert.That(result.Outcome, Is.EqualTo("skip_zero_byte"));
        Assert.That(capturedErrorMarker, Is.Not.Null);
        Assert.That(capturedErrorMarker?.Path, Is.EqualTo(@"E:\Movies\sample.mp4"));
        Assert.That(capturedErrorMarker?.TabIndex, Is.EqualTo(3));
        Assert.That(capturedErrorMarker?.Reason, Is.EqualTo("zero-byte movie(folder scan)"));
    }

    [Test]
    public void TryHandleWatchFolderMoviePreCheck_continueなら処理を継続する()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        int notifyCount = 0;
        MainWindow.WatchFolderScanMovieResult result = new();
        MainWindow.WatchFolderScanContext context = new()
        {
            HasNotifiedFolderHit = false,
            NotifyFolderFirstHit = () => notifyCount++,
            AllowMissingTabAutoEnqueue = false,
        };

        bool handled = window.TryHandleWatchFolderMoviePreCheck(
            context,
            @"E:\Movies\sample.mp4",
            123,
            new MainWindow.WatchFolderMoviePreCheckDecision(
                "continue",
                ShouldNotifyFolderHit: true,
                ShouldContinueProcessing: true,
                IsZeroByteMovie: false
            ),
            result
        );

        Assert.That(handled, Is.False);
        Assert.That(notifyCount, Is.EqualTo(1));
        Assert.That(context.HasNotifiedFolderHit, Is.True);
        Assert.That(result.Outcome, Is.EqualTo(""));
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
    public void TryFlushFinalWatchFolderQueueWithGuards_suppression再退避callback成功なら停止を返す()
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

        MainWindow.WatchFinalQueueFlushResult result =
            window.TryFlushFinalWatchFolderQueueWithGuards(context);

        Assert.That(result.WasDeferredBySuppression, Is.True);
        Assert.That(result.WasStoppedByUiSuppression, Is.True);
        Assert.That(result.WasDroppedByStaleScope, Is.False);
        Assert.That(capturedTrigger, Is.EqualTo("folder-final-queue:E:\\Movies"));
    }

    [Test]
    public async Task TryFlushPendingNewMoviesWithGuardsAsync_suppression再退避callback成功なら停止を返す()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string? capturedTrigger = null;
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.CheckFolder = @"E:\Movies";
        MainWindow.WatchFolderScanContext context = new()
        {
            ScannedMovieContext = new MainWindow.WatchScannedMovieContext
            {
                PendingMovieFlushContext = pendingContext,
            },
            TryDeferWatchFolderWorkByUiSuppressionAction = trigger =>
            {
                capturedTrigger = trigger;
                return true;
            },
        };

        MainWindow.WatchPendingNewMovieGuardResult result =
            await window.TryFlushPendingNewMoviesWithGuardsAsync(context);

        Assert.That(result.WasStoppedByUiSuppression, Is.True);
        Assert.That(result.WasDroppedByStaleScope, Is.False);
        Assert.That(capturedTrigger, Is.EqualTo("folder-before-final-flush:E:\\Movies"));
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
    public void TryAbortWatchFolderForStaleScope_current_scopeならfalse()
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

        bool result = MainWindow.TryAbortWatchFolderForStaleScope(
            context,
            @"E:\Movies",
            "mid folder"
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryAbortWatchFolderForStaleScope_stale_scopeならtrue()
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

        bool result = MainWindow.TryAbortWatchFolderForStaleScope(
            context,
            @"E:\Movies",
            "after background scan"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void TryAbortWatchFolderForCoordinatorStaleResult_stale_scopeならtrue()
    {
        MainWindow.WatchScannedMovieProcessResult processResult = new()
        {
            WasDroppedByStaleScope = true,
        };

        bool result = MainWindow.TryAbortWatchFolderForCoordinatorStaleResult(
            processResult,
            @"E:\Movies",
            @"E:\Movies\sample.mp4"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void TryAbortWatchFolderForCoordinatorStaleResult_current_scopeならfalse()
    {
        MainWindow.WatchScannedMovieProcessResult processResult = new()
        {
            WasDroppedByStaleScope = false,
        };

        bool result = MainWindow.TryAbortWatchFolderForCoordinatorStaleResult(
            processResult,
            @"E:\Movies",
            @"E:\Movies\sample.mp4"
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task TryFlushPendingNewMoviesWithGuardsAsync_stale_scopeならdropする()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        MainWindow.WatchPendingNewMovieFlushContext pendingContext = CreatePendingFlushContext();
        pendingContext.CheckFolder = @"E:\Movies";
        pendingContext.IsCurrentWatchScanScope = () => false;
        MainWindow.WatchFolderScanContext context = new()
        {
            ScannedMovieContext = new MainWindow.WatchScannedMovieContext
            {
                PendingMovieFlushContext = pendingContext,
            },
        };

        MainWindow.WatchPendingNewMovieGuardResult result =
            await window.TryFlushPendingNewMoviesWithGuardsAsync(context);

        Assert.That(result.WasDroppedByStaleScope, Is.True);
        Assert.That(result.WasStoppedByUiSuppression, Is.False);
        Assert.That(result.FlushResult, Is.EqualTo(MainWindow.WatchPendingNewMovieFlushResult.None));
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

    [Test]
    public void TryFlushFinalWatchFolderQueueWithGuards_flush後にstale化してもdropする()
    {
        MainWindow window = CreateMainWindowForCoordinatorTests();
        string moviePath = @"E:\Movies\sample.mp4";
        Queue<bool> scopeStates = new([true, true, true, false]);
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
        pendingContext.IsCurrentWatchScanScope = () => ReadNextSuppressionState(scopeStates);
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

        MainWindow.WatchFinalQueueFlushResult result =
            window.TryFlushFinalWatchFolderQueueWithGuards(context);

        Assert.That(flushCount, Is.EqualTo(1));
        Assert.That(result.WasDeferredBySuppression, Is.False);
        Assert.That(result.WasDroppedByStaleScope, Is.True);
        Assert.That(result.WasStoppedByUiSuppression, Is.False);
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
