using System.Reflection;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Windows;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class MissingThumbnailRescuePolicyTests
{
    private const string ThumbnailNormalLaneTimeoutSecEnvName = "IMM_THUMB_NORMAL_TIMEOUT_SEC";

    [Test]
    public void ShouldSkipMissingThumbnailRescueForBusyQueue_Watch高負荷時は抑止する()
    {
        bool result = MainWindow.ShouldSkipMissingThumbnailRescueForBusyQueue(
            isManualRequest: false,
            activeCount: 14,
            busyThreshold: 14
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldSkipMissingThumbnailRescueForBusyQueue_Manual高負荷時でも抑止しない()
    {
        bool result = MainWindow.ShouldSkipMissingThumbnailRescueForBusyQueue(
            isManualRequest: true,
            activeCount: 14,
            busyThreshold: 14
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ResolveMissingThumbnailRescueBusyThreshold_Watch時は1を返す()
    {
        int result = MainWindow.ResolveMissingThumbnailRescueBusyThreshold(
            isWatchRequest: true,
            defaultBusyThreshold: 200
        );

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void ResolveMissingThumbnailRescueBusyThreshold_Manual系は既定値を維持する()
    {
        int result = MainWindow.ResolveMissingThumbnailRescueBusyThreshold(
            isWatchRequest: false,
            defaultBusyThreshold: 200
        );

        Assert.That(result, Is.EqualTo(200));
    }

    [Test]
    public void ReleaseMissingThumbnailRescueWindowReservation_defer_drop時は同じ予約だけ巻き戻せる()
    {
        MainWindow window = CreateMainWindowForRescueReservationTests();
        string scopeKey = @"c:\db\main.wb|tab=2";
        DateTime reservedAtUtc = new(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);

        bool firstReserved = InvokeTryReserveMissingThumbnailRescueWindow(
            window,
            scopeKey,
            reservedAtUtc,
            out _
        );

        InvokeVoid(
            window,
            "ReleaseMissingThumbnailRescueWindowReservation",
            scopeKey,
            reservedAtUtc
        );

        bool secondReserved = InvokeTryReserveMissingThumbnailRescueWindow(
            window,
            scopeKey,
            reservedAtUtc.AddSeconds(1),
            out TimeSpan nextIn
        );

        Assert.That(firstReserved, Is.True);
        Assert.That(secondReserved, Is.True);
        Assert.That(nextIn, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ReleaseMissingThumbnailRescueWindowReservation_新しい予約は巻き戻さない()
    {
        MainWindow window = CreateMainWindowForRescueReservationTests();
        string scopeKey = @"c:\db\main.wb|tab=2";
        DateTime firstReservedAtUtc = new(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        DateTime secondReservedAtUtc = firstReservedAtUtc.AddMinutes(2);

        Assert.That(
            InvokeTryReserveMissingThumbnailRescueWindow(
                window,
                scopeKey,
                firstReservedAtUtc,
                out _
            ),
            Is.True
        );

        InvokeVoid(
            window,
            "ReleaseMissingThumbnailRescueWindowReservation",
            scopeKey,
            firstReservedAtUtc
        );

        Assert.That(
            InvokeTryReserveMissingThumbnailRescueWindow(
                window,
                scopeKey,
                secondReservedAtUtc,
                out _
            ),
            Is.True
        );

        InvokeVoid(
            window,
            "ReleaseMissingThumbnailRescueWindowReservation",
            scopeKey,
            firstReservedAtUtc
        );

        bool throttled = InvokeTryReserveMissingThumbnailRescueWindow(
            window,
            scopeKey,
            secondReservedAtUtc.AddSeconds(1),
            out TimeSpan nextIn
        );

        Assert.That(throttled, Is.False);
        Assert.That(nextIn, Is.GreaterThan(TimeSpan.Zero));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void ResolveWatchMissingThumbnailTabIndex_通常上側タブはそのまま返す(int currentTabIndex)
    {
        int? result = MainWindow.ResolveWatchMissingThumbnailTabIndex(currentTabIndex);

        Assert.That(result, Is.EqualTo(currentTabIndex));
    }

    [TestCase(-1)]
    [TestCase(5)]
    [TestCase(99)]
    public void ResolveWatchMissingThumbnailTabIndex_通常上側タブ以外はnullを返す(int currentTabIndex)
    {
        int? result = MainWindow.ResolveWatchMissingThumbnailTabIndex(currentTabIndex);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ShouldUseThumbnailNormalLaneTimeout_手動または明示無効時だけFalseを返す()
    {
        QueueObj normalQueueObj = new()
        {
            MovieFullPath = @"E:\movies\normal.mp4",
            Tabindex = 0,
        };

        Assert.That(
            MainWindow.ShouldUseThumbnailNormalLaneTimeout(normalQueueObj, isManual: false),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldUseThumbnailNormalLaneTimeout(
                normalQueueObj,
                isManual: false,
                disableNormalLaneTimeout: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldUseThumbnailNormalLaneTimeout(normalQueueObj, isManual: true),
            Is.False
        );
    }

    [Test]
    public void ResolveThumbnailNormalLaneTimeout_環境変数が有効ならその秒数を返す()
    {
        string? rawBackup = Environment.GetEnvironmentVariable(ThumbnailNormalLaneTimeoutSecEnvName);
        bool hadBackup = rawBackup != null;
        string backup = rawBackup ?? string.Empty;
        try
        {
            Environment.SetEnvironmentVariable(ThumbnailNormalLaneTimeoutSecEnvName, "15");

            TimeSpan actual = MainWindow.ResolveThumbnailNormalLaneTimeout();

            Assert.That(actual, Is.EqualTo(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ThumbnailNormalLaneTimeoutSecEnvName,
                hadBackup ? backup : null
            );
        }
    }

    [Test]
    public void ResolveThumbnailNormalLaneTimeout_不正値なら既定10秒へ戻す()
    {
        string? rawBackup = Environment.GetEnvironmentVariable(ThumbnailNormalLaneTimeoutSecEnvName);
        bool hadBackup = rawBackup != null;
        string backup = rawBackup ?? string.Empty;
        try
        {
            Environment.SetEnvironmentVariable(ThumbnailNormalLaneTimeoutSecEnvName, "abc");

            TimeSpan actual = MainWindow.ResolveThumbnailNormalLaneTimeout();

            Assert.That(actual, Is.EqualTo(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ThumbnailNormalLaneTimeoutSecEnvName,
                hadBackup ? backup : null
            );
        }
    }

    [Test]
    public void ShouldSkipThumbnailUiReflection_入力停止や終了準備中はTrueを返す()
    {
        Assert.That(
            MainWindow.ShouldSkipThumbnailUiReflection(
                isInputEnabled: false,
                dispatcherHasShutdownStarted: false,
                dispatcherHasShutdownFinished: false,
                isCancellationRequested: false
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldSkipThumbnailUiReflection(
                isInputEnabled: true,
                dispatcherHasShutdownStarted: true,
                dispatcherHasShutdownFinished: false,
                isCancellationRequested: false
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldSkipThumbnailUiReflection(
                isInputEnabled: true,
                dispatcherHasShutdownStarted: false,
                dispatcherHasShutdownFinished: true,
                isCancellationRequested: false
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldSkipThumbnailUiReflection(
                isInputEnabled: true,
                dispatcherHasShutdownStarted: false,
                dispatcherHasShutdownFinished: false,
                isCancellationRequested: true
            ),
            Is.True
        );
    }

    [Test]
    public void ShouldSkipThumbnailUiReflection_通常稼働中だけFalseを返す()
    {
        bool result = MainWindow.ShouldSkipThumbnailUiReflection(
            isInputEnabled: true,
            dispatcherHasShutdownStarted: false,
            dispatcherHasShutdownFinished: false,
            isCancellationRequested: false
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldTryThumbnailIndexRepair_対象拡張子かつ失敗文言でTrueを返す()
    {
        bool result = MainWindow.ShouldTryThumbnailIndexRepair(
            @"E:\movies\broken.wmv",
            "No frames decoded"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldTryThumbnailIndexRepair_対象外拡張子や通常失敗ではFalseを返す()
    {
        bool wrongExtension = MainWindow.ShouldTryThumbnailIndexRepair(
            @"E:\movies\normal.flv",
            "No frames decoded"
        );
        bool wrongReason = MainWindow.ShouldTryThumbnailIndexRepair(
            @"E:\movies\normal.mp4",
            "manual target thumbnail does not exist"
        );

        Assert.That(wrongExtension, Is.False);
        Assert.That(wrongReason, Is.False);
    }

    [Test]
    public void ShouldTryThumbnailIndexRepair_大文字小文字混在でも条件一致ならTrueを返す()
    {
        bool result = MainWindow.ShouldTryThumbnailIndexRepair(
            @"E:\movies\BROKEN.WMV",
            "AVFormat_Open_Input Failed"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void CanTryThumbnailIndexRepair_救済レーンは対象拡張子なら失敗理由に依存せずprobe可能()
    {
        bool supported = MainWindow.CanTryThumbnailIndexRepair(@"E:\movies\broken.wmv");
        bool unsupported = MainWindow.CanTryThumbnailIndexRepair(@"E:\movies\broken.flv");

        Assert.That(supported, Is.True);
        Assert.That(unsupported, Is.False);
    }

    [Test]
    public void IsThumbnailErrorPlaceholderPath_組み込みerror画像だけTrueを返す()
    {
        Assert.That(MainWindow.IsThumbnailErrorPlaceholderPath(@"C:\app\Images\errorGrid.jpg"), Is.True);
        Assert.That(MainWindow.IsThumbnailErrorPlaceholderPath(@"C:\app\Images\ERRORBIG.JPG"), Is.True);
        Assert.That(MainWindow.IsThumbnailErrorPlaceholderPath(@"C:\videos\my_error_movie.jpg"), Is.False);
        Assert.That(MainWindow.IsThumbnailErrorPlaceholderPath(@"C:\thumb\movie.#ERROR.jpg"), Is.False);
        Assert.That(MainWindow.IsThumbnailErrorPlaceholderPath(""), Is.False);
    }

    [Test]
    public void ShouldPreferNormalQueueForDisplayError_tab_error_placeholder初回だけ通常キュー優先に戻す()
    {
        bool result = MainWindow.ShouldPreferNormalQueueForDisplayError(
            "tab-error-placeholder",
            hasFailureHistory: false
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldPreferNormalQueueForDisplayError_履歴ありや別理由では救済キューを維持する()
    {
        bool withHistory = MainWindow.ShouldPreferNormalQueueForDisplayError(
            "tab-error-placeholder",
            hasFailureHistory: true
        );
        bool anotherReason = MainWindow.ShouldPreferNormalQueueForDisplayError(
            "detail-error-placeholder",
            hasFailureHistory: false
        );

        Assert.That(withHistory, Is.False);
        Assert.That(anotherReason, Is.False);
    }

    [Test]
    public void ResolveMissingThumbnailAutoEnqueueBlockReason_ERRORマーカーがあれば自動再投入を止める()
    {
        HashSet<string> existingThumbnailFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ThumbnailPathResolver.BuildErrorMarkerFileName(@"E:\movies\movie1.mp4"),
        };

        MainWindow.MissingThumbnailAutoEnqueueBlockReason result =
            MainWindow.ResolveMissingThumbnailAutoEnqueueBlockReason(
                @"E:\movies\movie1.mp4",
                2,
                existingThumbnailFileNames,
                []
            );

        Assert.That(
            result,
            Is.EqualTo(MainWindow.MissingThumbnailAutoEnqueueBlockReason.ErrorMarkerExists)
        );
    }

    [Test]
    public void ResolveMissingThumbnailAutoEnqueueBlockReason_FailureDbに未完了解析があれば自動再投入を止める()
    {
        string moviePath = @"E:\movies\movie2.mp4";
        string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath);
        HashSet<string> openRescueRequestKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            $"{moviePathKey}|4",
        };

        MainWindow.MissingThumbnailAutoEnqueueBlockReason result =
            MainWindow.ResolveMissingThumbnailAutoEnqueueBlockReason(
                moviePath,
                4,
                [],
                openRescueRequestKeys
            );

        Assert.That(
            result,
            Is.EqualTo(MainWindow.MissingThumbnailAutoEnqueueBlockReason.OpenRescueRequestExists)
        );
    }

    [Test]
    public void ShouldCreateErrorMarkerForSkippedMovie_正常jpgがあればFalseを返す()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-precheck-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string successPath = Path.Combine(tempRoot, "movie1.#abc12345.jpg");
            using Bitmap bmp = new(8, 8);
            using Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            bmp.Save(successPath, ImageFormat.Jpeg);

            bool result = MainWindow.ShouldCreateErrorMarkerForSkippedMovie(
                tempRoot,
                "movie1.mp4",
                out string existingSuccessThumbnailPath
            );

            Assert.That(result, Is.False);
            Assert.That(existingSuccessThumbnailPath, Is.EqualTo(successPath));
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
    public void CleanupStaleErrorMarkersInDirectory_成功jpgがあるERRORだけ削除する()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string staleErrorPath = Path.Combine(tempRoot, "movie1.#ERROR.jpg");
            string successPath = Path.Combine(tempRoot, "movie1.#abc12345.jpg");
            File.WriteAllBytes(staleErrorPath, []);

            using Bitmap bmp = new(8, 8);
            using Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            bmp.Save(successPath, ImageFormat.Jpeg);

            int deletedCount = MainWindow.CleanupStaleErrorMarkersInDirectory(tempRoot);

            Assert.That(deletedCount, Is.EqualTo(1));
            Assert.That(File.Exists(staleErrorPath), Is.False);
            Assert.That(File.Exists(successPath), Is.True);
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
    public void CleanupStaleErrorMarkersInDirectory_成功jpgが無いERRORは残す()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string errorPath = Path.Combine(tempRoot, "movie1.#ERROR.jpg");
            File.WriteAllBytes(errorPath, []);

            int deletedCount = MainWindow.CleanupStaleErrorMarkersInDirectory(tempRoot);

            Assert.That(deletedCount, Is.EqualTo(0));
            Assert.That(File.Exists(errorPath), Is.True);
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
    public void ShouldDeleteStaleMainFailureRecord_成功jpgがあればTrueを返す()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-cleanup-main-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string moviePath = @"E:\_サムネイル作成困難動画\big\sango72GB.mkv";
            ThumbnailLayoutProfile profile = ThumbnailLayoutProfileResolver.Resolve(
                0,
                ThumbnailDetailModeRuntime.ReadRuntimeMode()
            );
            string outPath = ThumbnailMovieMetaResolver.ResolveThumbnailOutPath(
                profile,
                "big",
                tempRoot
            );
            Directory.CreateDirectory(outPath);
            string successPath = ThumbnailPathResolver.BuildThumbnailPath(
                outPath,
                moviePath,
                "016448a1"
            );

            using Bitmap bmp = new(8, 8);
            using Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            bmp.Save(successPath, ImageFormat.Jpeg);

            bool result = MainWindow.ShouldDeleteStaleMainFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = moviePath,
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath),
                    TabIndex = 0,
                    Status = "pending_rescue",
                },
                "big",
                tempRoot
            );

            Assert.That(result, Is.True);
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
    public void CleanupStaleMainFailureRecordsForDb_成功jpgがあるmain行を削除する()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-cleanup-main-{Guid.NewGuid():N}");
        string mainDbPath = Path.Combine(tempRoot, "big.wb");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string moviePath = @"E:\_サムネイル作成困難動画\big\sango72GB.mkv";
            ThumbnailLayoutProfile profile = ThumbnailLayoutProfileResolver.Resolve(
                0,
                ThumbnailDetailModeRuntime.ReadRuntimeMode()
            );
            string outPath = ThumbnailMovieMetaResolver.ResolveThumbnailOutPath(
                profile,
                "big",
                tempRoot
            );
            Directory.CreateDirectory(outPath);
            string successPath = ThumbnailPathResolver.BuildThumbnailPath(
                outPath,
                moviePath,
                "016448a1"
            );
            using Bitmap bmp = new(8, 8);
            using Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            bmp.Save(successPath, ImageFormat.Jpeg);

            ThumbnailFailureDbService failureDbService = new(mainDbPath);
            DateTime nowUtc = DateTime.UtcNow;
            failureDbService.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MoviePath = moviePath,
                    MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath),
                    TabIndex = 0,
                    Lane = "slow",
                    Status = "pending_rescue",
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                }
            );

            int deletedCount = MainWindow.CleanupStaleMainFailureRecordsForDb(
                failureDbService,
                "big",
                tempRoot
            );

            Assert.That(deletedCount, Is.EqualTo(1));
            Assert.That(failureDbService.GetLatestMainFailureRecords(), Is.Empty);
        }
        finally
        {
            string failureDbPath = ThumbnailFailureDbPathResolver.ResolveFailureDbPath(mainDbPath);
            if (File.Exists(failureDbPath))
            {
                File.Delete(failureDbPath);
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void CanReflectRescuedThumbnailRecord_出力存在と対応tabならTrueを返す()
    {
        string tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"imm-rescued-sync-{Guid.NewGuid():N}.jpg"
        );

        try
        {
            File.WriteAllText(tempFilePath, "rescued");
            bool result = MainWindow.CanReflectRescuedThumbnailRecord(
                new ThumbnailFailureRecord
                {
                    TabIndex = 2,
                    OutputThumbPath = tempFilePath,
                }
            );

            Assert.That(result, Is.True);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    [Test]
    public void CanReflectRescuedThumbnailRecord_未対応tabや出力欠損ならFalseを返す()
    {
        string tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"imm-rescued-sync-invalid-{Guid.NewGuid():N}.jpg"
        );

        try
        {
            File.WriteAllText(tempFilePath, "rescued");

            bool invalidTab = MainWindow.CanReflectRescuedThumbnailRecord(
                new ThumbnailFailureRecord
                {
                    TabIndex = 88,
                    OutputThumbPath = tempFilePath,
                }
            );
            bool missingFile = MainWindow.CanReflectRescuedThumbnailRecord(
                new ThumbnailFailureRecord
                {
                    TabIndex = 2,
                    OutputThumbPath = tempFilePath + ".missing",
                }
            );

            Assert.That(invalidTab, Is.False);
            Assert.That(missingFile, Is.False);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    [Test]
    public void ShouldCountRescuedThumbnailForSession_この起動以降のrescuedだけTrueを返す()
    {
        DateTime sessionStartedUtc = new(2026, 3, 15, 1, 0, 0, DateTimeKind.Utc);

        bool result = MainWindow.ShouldCountRescuedThumbnailForSession(
            new ThumbnailFailureRecord
            {
                Status = "rescued",
                UpdatedAtUtc = new DateTime(2026, 3, 15, 1, 0, 1, DateTimeKind.Utc),
            },
            sessionStartedUtc
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldCountRescuedThumbnailForSession_起動前成功や非rescuedはFalseを返す()
    {
        DateTime sessionStartedUtc = new(2026, 3, 15, 1, 0, 0, DateTimeKind.Utc);

        bool oldRescued = MainWindow.ShouldCountRescuedThumbnailForSession(
            new ThumbnailFailureRecord
            {
                Status = "rescued",
                UpdatedAtUtc = new DateTime(2026, 3, 15, 0, 59, 59, DateTimeKind.Utc),
            },
            sessionStartedUtc
        );
        bool wrongStatus = MainWindow.ShouldCountRescuedThumbnailForSession(
            new ThumbnailFailureRecord
            {
                Status = "processing_rescue",
                UpdatedAtUtc = new DateTime(2026, 3, 15, 1, 5, 0, DateTimeKind.Utc),
            },
            sessionStartedUtc
        );

        Assert.That(oldRescued, Is.False);
        Assert.That(wrongStatus, Is.False);
    }

    [Test]
    public void ShouldRunPeriodicThumbnailFailureSync_入力有効かつ間隔超過ならTrueを返す()
    {
        DateTime lastScheduledUtc = new(2026, 3, 14, 14, 0, 0, DateTimeKind.Utc);
        DateTime nowUtc = lastScheduledUtc.AddSeconds(5);

        bool result = MainWindow.ShouldRunPeriodicThumbnailFailureSync(
            nowUtc,
            lastScheduledUtc,
            TimeSpan.FromSeconds(5),
            isInputEnabled: true
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRunPeriodicThumbnailFailureSync_入力無効または間隔内ならFalseを返す()
    {
        DateTime lastScheduledUtc = new(2026, 3, 14, 14, 0, 0, DateTimeKind.Utc);

        bool disabled = MainWindow.ShouldRunPeriodicThumbnailFailureSync(
            lastScheduledUtc.AddSeconds(30),
            lastScheduledUtc,
            TimeSpan.FromSeconds(5),
            isInputEnabled: false
        );
        bool tooEarly = MainWindow.ShouldRunPeriodicThumbnailFailureSync(
            lastScheduledUtc.AddSeconds(4),
            lastScheduledUtc,
            TimeSpan.FromSeconds(5),
            isInputEnabled: true
        );

        Assert.That(disabled, Is.False);
        Assert.That(tooEarly, Is.False);
    }

    [Test]
    public void ResolveThumbnailProgressUiTickBehavior_進捗タブ非表示でもrescued同期は継続する()
    {
        MainWindow.ThumbnailProgressUiTickBehavior result =
            MainWindow.ResolveThumbnailProgressUiTickBehavior(
                isThumbnailProgressTabVisibleOrSelected: false
            );

        Assert.That(result.ShouldRefreshUi, Is.False);
        Assert.That(result.ShouldQueueFailureSync, Is.True);
    }

    [Test]
    public void ResolveThumbnailProgressUiTickBehavior_進捗タブ表示中はUI更新とrescued同期を両方行う()
    {
        MainWindow.ThumbnailProgressUiTickBehavior result =
            MainWindow.ResolveThumbnailProgressUiTickBehavior(
                isThumbnailProgressTabVisibleOrSelected: true
            );

        Assert.That(result.ShouldRefreshUi, Is.True);
        Assert.That(result.ShouldQueueFailureSync, Is.True);
    }

    [Test]
    public void ShouldDeferThumbnailRescueWorkerLaunch_通常救済はbusy中なら待機する()
    {
        bool result = MainWindow.ShouldDeferThumbnailRescueWorkerLaunch(
            requiresIdle: true,
            priority: ThumbnailQueuePriority.Normal,
            activeCount: 3
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldDeferThumbnailRescueWorkerLaunch_優先救済はbusy中でも待機しない()
    {
        bool result = MainWindow.ShouldDeferThumbnailRescueWorkerLaunch(
            requiresIdle: true,
            priority: ThumbnailQueuePriority.Preferred,
            activeCount: 3
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldDeferThumbnailRescueWorkerLaunch_requiresIdleFalseなら通常でも待機しない()
    {
        bool result = MainWindow.ShouldDeferThumbnailRescueWorkerLaunch(
            requiresIdle: false,
            priority: ThumbnailQueuePriority.Normal,
            activeCount: 3
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void BuildThumbnailProgressRescueLaunchObservationText_一時優先の待機付き要求は優先起動表示になる()
    {
        string result = MainWindow.BuildThumbnailProgressRescueLaunchObservationText(
            ThumbnailQueuePriority.Preferred,
            "2026-03-17T12:01:00.000Z",
            "preferred-bypass",
            requiresIdle: true,
            new DateTime(2026, 3, 17, 12, 0, 0, DateTimeKind.Utc)
        );

        Assert.That(result, Is.EqualTo("優先:一時 / 開始:優先起動"));
    }

    [Test]
    public void BuildThumbnailProgressRescueLaunchObservationText_通常待機要求はアイドル待ち表示になる()
    {
        string result = MainWindow.BuildThumbnailProgressRescueLaunchObservationText(
            ThumbnailQueuePriority.Normal,
            "",
            "wait-idle",
            requiresIdle: true,
            new DateTime(2026, 3, 17, 12, 0, 0, DateTimeKind.Utc)
        );

        Assert.That(result, Is.EqualTo("開始:アイドル待ち"));
    }

    [Test]
    public void BuildThumbnailProgressRescueLaunchObservationText_固定優先の即時要求は固定と即時を表示する()
    {
        string result = MainWindow.BuildThumbnailProgressRescueLaunchObservationText(
            ThumbnailQueuePriority.Preferred,
            "",
            "immediate",
            requiresIdle: false,
            new DateTime(2026, 3, 17, 12, 0, 0, DateTimeKind.Utc)
        );

        Assert.That(result, Is.EqualTo("優先:固定 / 開始:即時"));
    }

    [Test]
    public void TryApplyThumbnailPathToMovieRecord_対応tabだけ該当プロパティを書き換える()
    {
        MovieRecords movie = new();

        bool gridUpdated = MainWindow.TryApplyThumbnailPathToMovieRecord(
            movie,
            2,
            @"E:\thumb\grid.#hash.jpg"
        );
        bool unsupportedUpdated = MainWindow.TryApplyThumbnailPathToMovieRecord(
            movie,
            88,
            @"E:\thumb\unsupported.#hash.jpg"
        );

        Assert.That(gridUpdated, Is.True);
        Assert.That(movie.ThumbPathGrid, Is.EqualTo(@"E:\thumb\grid.#hash.jpg"));
        Assert.That(unsupportedUpdated, Is.False);
    }

    [Test]
    public void TryApplyThumbnailPathToMovieRecord_詳細tab99はThumbDetailを書き換える()
    {
        MovieRecords movie = new();

        bool updated = MainWindow.TryApplyThumbnailPathToMovieRecord(
            movie,
            99,
            @"E:\thumb\detail.#hash.jpg"
        );

        Assert.That(updated, Is.True);
        Assert.That(movie.ThumbDetail, Is.EqualTo(@"E:\thumb\detail.#hash.jpg"));
    }

    [Test]
    public void ResolveLane_Phase4でもサイズ分類だけでNormalとSlowを分ける()
    {
        QueueObj normalSizedJob = new()
        {
            MovieFullPath = @"E:\movies\normal.mp4",
            MovieSizeBytes = 100 * 1024 * 1024,
        };
        QueueObj slowSizedJob = new()
        {
            MovieFullPath = @"E:\movies\slow.mp4",
            MovieSizeBytes = 2L * 1024 * 1024 * 1024 * 1024,
        };

        object normalLane = InvokeResolveLane(normalSizedJob);
        object slowLane = InvokeResolveLane(slowSizedJob);

        Assert.That(normalLane.ToString(), Is.EqualTo("Normal"));
        Assert.That(slowLane.ToString(), Is.EqualTo("Slow"));
    }

    [Test]
    public void ResolvePreferredThumbnailTabIndex_救済タブ表示中は対象タブを優先する()
    {
        int? result = MainWindow.ResolvePreferredThumbnailTabIndex(currentTabIndex: 5, actionTabIndex: 2);

        Assert.That(result, Is.EqualTo(2));
    }

    [Test]
    public void ResolveThumbnailActionTabIndex_救済タブは対象タブへ正規化する()
    {
        int explicitTarget = MainWindow.ResolveThumbnailActionTabIndex(currentTabIndex: 5, rescueTargetTabIndex: 4);
        int fallbackTarget = MainWindow.ResolveThumbnailActionTabIndex(currentTabIndex: 5, rescueTargetTabIndex: -1);

        Assert.That(explicitTarget, Is.EqualTo(4));
        Assert.That(fallbackTarget, Is.EqualTo(2));
    }

    [Test]
    public void ResolveThumbnailActionTabIndex_重複動画タブはGridへ正規化する()
    {
        int result = MainWindow.ResolveThumbnailActionTabIndex(currentTabIndex: 6, rescueTargetTabIndex: -1);

        Assert.That(result, Is.EqualTo(2));
    }

    [Test]
    public void ResolveManualThumbnailCaptureFailureMessage_対象サムネ未作成は案内文へ変換する()
    {
        Exception ex = CreateThumbnailCreateFailureException("manual target thumbnail does not exist");

        string result = MainWindow.ResolveManualThumbnailCaptureFailureMessage(ex);

        Assert.That(result, Does.Contain("先に通常のサムネイルを作成してください"));
    }

    [Test]
    public void ResolveManualThumbnailCaptureFailureMessage_既存メタ欠落は再作成案内へ変換する()
    {
        Exception ex = CreateThumbnailCreateFailureException("manual source thumbnail metadata is missing");

        string result = MainWindow.ResolveManualThumbnailCaptureFailureMessage(ex);

        Assert.That(result, Does.Contain("通常サムネイルを再作成してからやり直してください"));
    }

    [Test]
    public void ResolveManualThumbnailCaptureFailureMessage_TimeoutExceptionは専用文言へ変換する()
    {
        string result = MainWindow.ResolveManualThumbnailCaptureFailureMessage(new TimeoutException("timeout"));

        Assert.That(result, Does.Contain("時間内に完了しませんでした"));
    }

    [Test]
    public void ResolveMediaDurationMaximumMilliseconds_TimeSpanありならその値を返す()
    {
        double result = MainWindow.ResolveMediaDurationMaximumMilliseconds(
            new Duration(TimeSpan.FromSeconds(12)),
            345d
        );

        Assert.That(result, Is.EqualTo(12_000d));
    }

    [Test]
    public void ResolveMediaDurationMaximumMilliseconds_Automaticなら既存最大値を維持する()
    {
        double result = MainWindow.ResolveMediaDurationMaximumMilliseconds(
            Duration.Automatic,
            345d
        );

        Assert.That(result, Is.EqualTo(345d));
    }

    [Test]
    public void ResolveManualPlayerViewportSize_縦動画は高さ優先で画面内へ収める()
    {
        System.Windows.Size result = MainWindow.ResolveManualPlayerViewportSize(
            availableWidth: 1200d,
            availableHeight: 700d,
            naturalVideoWidth: 1080d,
            naturalVideoHeight: 1920d
        );

        Assert.That(result.Width, Is.EqualTo(393d).Within(1d));
        Assert.That(result.Height, Is.EqualTo(700d).Within(1d));
    }

    [Test]
    public void ResolveManualPlayerViewportSize_横動画は既定幅900を上限にする()
    {
        System.Windows.Size result = MainWindow.ResolveManualPlayerViewportSize(
            availableWidth: 1400d,
            availableHeight: 900d,
            naturalVideoWidth: 1920d,
            naturalVideoHeight: 1080d
        );

        Assert.That(result.Width, Is.EqualTo(900d).Within(1d));
        Assert.That(result.Height, Is.EqualTo(506d).Within(1d));
    }

    [Test]
    public void BuildPreferredRescueMoviePathKeysSnapshot_空白と重複を除いて順序を維持する()
    {
        IReadOnlyList<string> result = MainWindow.BuildPreferredRescueMoviePathKeysSnapshot(
            [
                @"E:\movies\alpha.mp4",
                "",
                @"E:\movies\ALPHA.mp4",
                @"E:\movies\beta.mp4",
            ]
        );

        Assert.That(
            result,
            Is.EqualTo(
                new[]
                {
                    QueueDbPathResolver.CreateMoviePathKey(@"E:\movies\alpha.mp4"),
                    QueueDbPathResolver.CreateMoviePathKey(@"E:\movies\beta.mp4"),
                }
            )
        );
    }

    [Test]
    public void ResolveThumbnailRescueLaneName_設定変更直後でも即時値を使う()
    {
        int backupSlowLaneMinGb = IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb;
        long movieSizeBytes = 2L * 1024L * 1024L * 1024L;

        try
        {
            IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb = 100;
            string normalLane = MainWindow.ResolveThumbnailRescueLaneName(movieSizeBytes);

            IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb = 1;
            string slowLane = MainWindow.ResolveThumbnailRescueLaneName(movieSizeBytes);

            Assert.That(normalLane, Is.EqualTo("normal"));
            Assert.That(slowLane, Is.EqualTo("slow"));
        }
        finally
        {
            IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb = backupSlowLaneMinGb;
        }
    }

    private static object InvokeResolveLane(QueueObj queueObj)
    {
        Type classifierType = typeof(QueueDbService).Assembly.GetType(
            "IndigoMovieManager.Thumbnail.ThumbnailLaneClassifier",
            throwOnError: true
        )!;
        MethodInfo method = classifierType.GetMethod(
            "ResolveLane",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(QueueObj)],
            modifiers: null
        )!;

        Assert.That(method, Is.Not.Null);
        return method.Invoke(null, [queueObj])!;
    }

    private static MainWindow CreateMainWindowForRescueReservationTests()
    {
        MainWindow window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        SetPrivateField(window, "_missingThumbnailRescueSync", new object());
        SetPrivateField(
            window,
            "_missingThumbnailRescueLastRunUtcByScope",
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
        );
        return window;
    }

    private static bool InvokeTryReserveMissingThumbnailRescueWindow(
        MainWindow window,
        string scopeKey,
        DateTime nowUtc,
        out TimeSpan nextIn
    )
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "TryReserveMissingThumbnailRescueWindow",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);
        object[] args = [scopeKey, nowUtc, TimeSpan.Zero];
        bool result = (bool)method.Invoke(window, args)!;
        nextIn = (TimeSpan)args[2];
        return result;
    }

    private static void InvokeVoid(MainWindow window, string methodName, params object[] args)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null, methodName);
        method.Invoke(window, args);
    }

    private static void SetPrivateField(MainWindow window, string fieldName, object value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(window, value);
    }

    private static Exception CreateThumbnailCreateFailureException(string failureReason)
    {
        Type exceptionType = typeof(MainWindow).GetNestedType(
            "ThumbnailCreateFailureException",
            BindingFlags.NonPublic
        )!;
        ConstructorInfo ctor = exceptionType.GetConstructor([typeof(string)])!;
        object exception = ctor.Invoke(["thumbnail create failed"]);
        PropertyInfo property = exceptionType.GetProperty("FailureReason")!;
        property.SetValue(exception, failureReason);
        return (Exception)exception;
    }
}
