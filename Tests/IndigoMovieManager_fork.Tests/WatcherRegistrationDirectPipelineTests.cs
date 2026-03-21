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
    public async Task ProcessCreatedWatchEventDirectAsync_ready後はsnapshotでDBとタブを評価してdirect登録からUI反映とサムネ投入まで進む()
    {
        List<string> calls = [];
        MovieInfo movieInfo = CreateMovieInfo(@"E:\Movies\created.mp4", movieId: 42, hash: "hash-42");
        string currentDbFullPath = @"D:\Db\before-ready.wb";
        int currentTabIndex = 5;

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
                    currentTabIndex = 2;
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
                tryEnqueueThumbnailJob: queueObj =>
                {
                    calls.Add(
                        $"enqueue:{queueObj.MovieId}:{queueObj.MovieFullPath}:{queueObj.Tabindex}"
                    );
                    return true;
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
                    "snapshot:D:\\Db\\before-ready.wb:5",
                    "ready",
                    "snapshot:D:\\Db\\after-ready.wb:2",
                    "movie-info",
                    "insert:D:\\Db\\after-ready.wb",
                    "adjust:D:\\Db\\after-ready.wb:1",
                    "append:D:\\Db\\after-ready.wb",
                    "enqueue:42:E:\\Movies\\created.mp4:2",
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
    public async Task ProcessCreatedWatchEventAsync_入口でも通常Createdはdirect_helperへ渡しQueueCheckFolderAsyncへ流さない()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string createdMoviePath = Path.Combine(tempRoot, "created.mp4");
        await File.WriteAllBytesAsync(createdMoviePath, [0x1]);

        try
        {
            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 5);
            List<string> directRequests = [];
            List<string> queuedRequests = [];
            window.ProcessCreatedWatchEventDirectAsyncOverrideForTesting = path =>
            {
                directRequests.Add(path);
                return Task.FromResult(
                    MainWindow.CreatedWatchEventDirectResult.RegisteredWithoutEnqueue
                );
            };
            window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
            {
                queuedRequests.Add($"{mode}:{trigger}");
            };

            MethodInfo method = typeof(MainWindow).GetMethod(
                "ProcessCreatedWatchEventAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            Assert.That(method, Is.Not.Null);

            Task task = (Task)method.Invoke(window, [createdMoviePath])!;
            await task;

            Assert.That(directRequests, Is.EqualTo([createdMoviePath]));
            Assert.That(queuedRequests, Is.Empty);
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
