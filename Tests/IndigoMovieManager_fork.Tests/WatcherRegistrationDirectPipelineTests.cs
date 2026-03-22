using IndigoMovieManager;
using IndigoMovieManager.DB;
using IndigoMovieManager.ViewModels;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WatcherRegistrationDirectPipelineTests
{
    [Test]
    public async Task ProcessCreatedWatchEventDirectAsync_ready後もenqueue時snapshotを固定でDBとタブを使い続けてbypassTabGateを要求する()
    {
        List<string> calls = [];
        MovieInfo movieInfo = CreateMovieInfo(@"E:\Movies\created.mp4", movieId: 42, hash: "hash-42");
        string currentDbFullPath = @"D:\Db\before-ready.wb";
        int currentTabIndex = 2;

        MainWindow.CreatedWatchEventDirectResult result =
            await MainWindow.ProcessCreatedWatchEventDirectAsync(
                createdFullPath: movieInfo.MoviePath,
                resolveCurrentState: () =>
                {
                    calls.Add($"snapshot:{currentDbFullPath}:{currentTabIndex}");
                    return (currentDbFullPath, currentTabIndex);
                },
                waitForReadyAsync: _ =>
                {
                    calls.Add("ready");
                    currentDbFullPath = @"D:\Db\after-ready.wb";
                    currentTabIndex = 5;
                    return Task.FromResult(true);
                },
                resolveZeroByteState: _ => (false, 0L),
                createErrorMarkerForSkippedMovie: (_, _, _) => calls.Add("error-marker"),
                createMovieInfoAsync: _ =>
                {
                    calls.Add("movie-info");
                    return Task.FromResult(movieInfo);
                },
                insertMovieAsync: (dbFullPath, _) =>
                {
                    calls.Add($"insert:{dbFullPath}");
                    return Task.FromResult(1);
                },
                adjustRegisteredMovieCount: (dbFullPath, insertedCount) =>
                    calls.Add($"adjust:{dbFullPath}:{insertedCount}"),
                appendMovieToViewAsync: (dbFullPath, _) =>
                {
                    calls.Add($"append:{dbFullPath}");
                    return Task.CompletedTask;
                },
                tryEnqueueThumbnailJob: enqueueRequest =>
                {
                    QueueObj queueObj = enqueueRequest.QueueObj;
                    calls.Add(
                        $"enqueue:{queueObj.MovieId}:{queueObj.MovieFullPath}:{queueObj.Tabindex}:{enqueueRequest.BypassTabGate}"
                    );
                    return enqueueRequest.BypassTabGate;
                },
                logWatchMessage: _ => calls.Add("log")
            );

        Assert.That(
            result,
            Is.EqualTo(MainWindow.CreatedWatchEventDirectResult.RegisteredAndEnqueued)
        );
        Assert.That(
            calls,
            Is.EqualTo(
                [
                    "snapshot:D:\\Db\\before-ready.wb:2",
                    "ready",
                    "movie-info",
                    "insert:D:\\Db\\before-ready.wb",
                    "adjust:D:\\Db\\before-ready.wb:1",
                    "append:D:\\Db\\before-ready.wb",
                    "enqueue:42:E:\\Movies\\created.mp4:2:True",
                ]
            )
        );
    }

    [Test]
    public async Task ProcessCreatedWatchEventDirectAsync_DB未確定ならready待ち前にIgnoredで返す()
    {
        bool waitCalled = false;

        MainWindow.CreatedWatchEventDirectResult result =
            await MainWindow.ProcessCreatedWatchEventDirectAsync(
                createdFullPath: @"E:\Movies\created.mp4",
                resolveCurrentState: () => ("", -1),
                waitForReadyAsync: _ =>
                {
                    waitCalled = true;
                    return Task.FromResult(true);
                },
                resolveZeroByteState: _ => (false, 0L),
                createErrorMarkerForSkippedMovie: (_, _, _) => { },
                createMovieInfoAsync: _ => Task.FromResult<MovieInfo>(null),
                insertMovieAsync: (_, _) => Task.FromResult(0),
                adjustRegisteredMovieCount: (_, _) => { },
                appendMovieToViewAsync: (_, _) => Task.CompletedTask,
                tryEnqueueThumbnailJob: _ => false,
                logWatchMessage: _ => { }
            );

        Assert.That(result, Is.EqualTo(MainWindow.CreatedWatchEventDirectResult.Ignored));
        Assert.That(waitCalled, Is.False);
    }

    [Test]
    public async Task ProcessCreatedWatchEventDirectAsync_上側タブ外でもQueueCheckFolderへ流さずdirect登録まで進む()
    {
        List<string> calls = [];
        MovieInfo movieInfo = CreateMovieInfo(@"E:\Movies\created.mp4", movieId: 7, hash: "hash-7");

        MainWindow.CreatedWatchEventDirectResult result =
            await MainWindow.ProcessCreatedWatchEventDirectAsync(
                createdFullPath: movieInfo.MoviePath,
                resolveCurrentState: () => (@"D:\Db\main.wb", 5),
                waitForReadyAsync: _ => Task.FromResult(true),
                resolveZeroByteState: _ => (false, 0L),
                createErrorMarkerForSkippedMovie: (_, _, _) => calls.Add("error-marker"),
                createMovieInfoAsync: _ => Task.FromResult(movieInfo),
                insertMovieAsync: (_, _) =>
                {
                    calls.Add("insert");
                    return Task.FromResult(1);
                },
                adjustRegisteredMovieCount: (_, insertedCount) =>
                    calls.Add($"adjust:{insertedCount}"),
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
                logWatchMessage: _ => calls.Add("log")
            );

        Assert.That(
            result,
            Is.EqualTo(MainWindow.CreatedWatchEventDirectResult.RegisteredWithoutEnqueue)
        );
        Assert.That(calls, Is.EqualTo(["insert", "adjust:1", "append"]));
    }

    [Test]
    public async Task ProcessCreatedWatchEventDirectAsync_enqueue不受理はresultへ反映する()
    {
        MovieInfo movieInfo = CreateMovieInfo(@"E:\Movies\created.mp4", movieId: 9, hash: "hash-9");

        MainWindow.CreatedWatchEventDirectResult result =
            await MainWindow.ProcessCreatedWatchEventDirectAsync(
                createdFullPath: movieInfo.MoviePath,
                resolveCurrentState: () => (@"D:\Db\main.wb", 2),
                waitForReadyAsync: _ => Task.FromResult(true),
                resolveZeroByteState: _ => (false, 0L),
                createErrorMarkerForSkippedMovie: (_, _, _) => { },
                createMovieInfoAsync: _ => Task.FromResult(movieInfo),
                insertMovieAsync: (_, _) => Task.FromResult(1),
                adjustRegisteredMovieCount: (_, _) => { },
                appendMovieToViewAsync: (_, _) => Task.CompletedTask,
                tryEnqueueThumbnailJob: _ => false,
                logWatchMessage: _ => { }
            );

        Assert.That(
            result,
            Is.EqualTo(MainWindow.CreatedWatchEventDirectResult.RegisteredButEnqueueRejected)
        );
    }

    [Test]
    public async Task ProcessCreatedWatchEventAsync_入口からruntimeAdapterへbypassTabGateを渡しQueueCheckFolderAsyncへ流さない()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string createdMoviePath = Path.Combine(tempRoot, "created.mp4");
        await File.WriteAllBytesAsync(createdMoviePath, [0x1]);

        try
        {
            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 5);
            MovieInfo movieInfo = CreateMovieInfo(createdMoviePath, movieId: 18, hash: "hash-18");
            List<string> calls = [];
            List<string> queuedRequests = [];
            QueueObj? actualQueueObj = null;
            bool actualBypassTabGate = false;
            object request = CreateCreatedWatchEventRequest(
                createdMoviePath,
                snapshotDbFullPath: @"D:\Db\main.wb",
                snapshotTabIndex: 2,
                requestScopeStamp: 0
            );
            window.CreatedWatchEventRuntimeTestHooksForTesting =
                new MainWindow.CreatedWatchEventRuntimeTestHooks
                {
                    WaitForReadyAsync = _ =>
                    {
                        calls.Add("ready");
                        return Task.FromResult(true);
                    },
                    ResolveZeroByteState = _ =>
                    {
                        calls.Add("zero-byte");
                        return (false, 1L);
                    },
                    CreateErrorMarkerForSkippedMovie = (_, _, _) => calls.Add("error-marker"),
                    CreateMovieInfoAsync = _ =>
                    {
                        calls.Add("movie-info");
                        return Task.FromResult(movieInfo);
                    },
                    InsertMovieAsync = (dbFullPath, movie) =>
                    {
                        calls.Add($"insert:{dbFullPath}:{movie.MoviePath}");
                        return Task.FromResult(1);
                    },
                    AdjustRegisteredMovieCount = (dbFullPath, insertedCount) =>
                        calls.Add($"adjust:{dbFullPath}:{insertedCount}"),
                    AppendMovieToViewAsync = (dbFullPath, moviePath) =>
                    {
                        calls.Add($"append:{dbFullPath}:{moviePath}");
                        return Task.CompletedTask;
                    },
                    TryEnqueueThumbnailJob = enqueueRequest =>
                    {
                        QueueObj queueObj = enqueueRequest.QueueObj;
                        actualQueueObj = queueObj;
                        actualBypassTabGate = enqueueRequest.BypassTabGate;
                        calls.Add(
                            $"enqueue:{queueObj.MovieId}:{queueObj.MovieFullPath}:{queueObj.Tabindex}:{enqueueRequest.BypassTabGate}"
                        );
                        return true;
                    },
                    LogWatchMessage = message => calls.Add($"log:{message}"),
                };
            window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
            {
                queuedRequests.Add($"{mode}:{trigger}");
            };

            MethodInfo method = GetProcessCreatedWatchEventAsyncRequestMethod();

            Task task = (Task)method.Invoke(window, [request])!;
            await task;

            Assert.That(
                calls,
                Is.EqualTo(
                    [
                        "ready",
                        "zero-byte",
                        "movie-info",
                        $"insert:D:\\Db\\main.wb:{createdMoviePath}",
                        "adjust:D:\\Db\\main.wb:1",
                        $"append:D:\\Db\\main.wb:{createdMoviePath}",
                        $"enqueue:18:{createdMoviePath}:2:True",
                    ]
                )
            );
            Assert.That(queuedRequests, Is.Empty);
            Assert.That(actualQueueObj, Is.Not.Null);
            Assert.That(actualQueueObj!.MovieId, Is.EqualTo(18));
            Assert.That(actualQueueObj.MovieFullPath, Is.EqualTo(createdMoviePath));
            Assert.That(actualQueueObj.Tabindex, Is.EqualTo(2));
            Assert.That(actualBypassTabGate, Is.True);
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
    public void ProcessRenamedWatchEventDirect_RenameThumbへ即渡す()
    {
        string actualNewPath = "";
        string actualOldPath = "";

        MainWindow.ProcessRenamedWatchEventDirect(
            @"E:\Movies\new-name.mp4",
            @"E:\Movies\old-name.mp4",
            (newFullPath, oldFullPath) =>
            {
                actualNewPath = newFullPath;
                actualOldPath = oldFullPath;
            }
        );

        Assert.That(actualNewPath, Is.EqualTo(@"E:\Movies\new-name.mp4"));
        Assert.That(actualOldPath, Is.EqualTo(@"E:\Movies\old-name.mp4"));
    }

    private static MainWindow CreateMainWindow(string dbFullPath, int currentTabIndex)
    {
        MainWindow window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        MainWindowViewModel mainVm =
            (MainWindowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainWindowViewModel));
        mainVm.DbInfo = new DBInfo
        {
            DBFullPath = dbFullPath,
            CurrentTabIndex = currentTabIndex,
        };

        SetPrivateField(window, "MainVM", mainVm);
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        return window;
    }

    private static MovieInfo CreateMovieInfo(string moviePath, long movieId, string hash)
    {
        MovieInfo movieInfo = (MovieInfo)RuntimeHelpers.GetUninitializedObject(typeof(MovieInfo));
        movieInfo.MovieId = movieId;
        movieInfo.MoviePath = moviePath;
        movieInfo.Hash = hash;
        return movieInfo;
    }

    // private な request/enum を明示解決し、string overload へ落ちない形で本番入口を叩く。
    private static MethodInfo GetProcessCreatedWatchEventAsyncRequestMethod()
    {
        Type requestType = GetWatchEventRequestType();
        MethodInfo method = typeof(MainWindow).GetMethod(
            "ProcessCreatedWatchEventAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [requestType],
            modifiers: null
        )!;
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static object CreateCreatedWatchEventRequest(
        string fullPath,
        string snapshotDbFullPath,
        int snapshotTabIndex,
        long requestScopeStamp
    )
    {
        Type watchEventKindType = typeof(MainWindow).GetNestedType(
            "WatchEventKind",
            BindingFlags.NonPublic
        )!;
        Assert.That(watchEventKindType, Is.Not.Null);

        object createdKind = Enum.Parse(watchEventKindType, "Created");
        Type requestType = GetWatchEventRequestType();
        ConstructorInfo constructor = requestType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                watchEventKindType,
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(int),
                typeof(long),
            ],
            modifiers: null
        )!;
        Assert.That(constructor, Is.Not.Null);
        return constructor.Invoke(
            [createdKind, fullPath, "", snapshotDbFullPath, snapshotTabIndex, requestScopeStamp]
        );
    }

    private static Type GetWatchEventRequestType()
    {
        Type requestType = typeof(MainWindow).GetNestedType(
            "WatchEventRequest",
            BindingFlags.NonPublic
        )!;
        Assert.That(requestType, Is.Not.Null);
        return requestType;
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
}
