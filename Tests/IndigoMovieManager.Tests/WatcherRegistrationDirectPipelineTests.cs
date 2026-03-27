using IndigoMovieManager;
using IndigoMovieManager.DB;
using IndigoMovieManager.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatcherRegistrationDirectPipelineTests
{
    [Test]
    public async Task ProcessWatchEventAsync_Created_ready後はQueueCheckFolderAsyncへ再合流する()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string createdMoviePath = Path.Combine(tempRoot, "created.mp4");
        await File.WriteAllBytesAsync(createdMoviePath, [0x1]);
        FileStream? createdMovieLock = null;
        TimeSpan retryWindow = TimeSpan.FromSeconds(1);
        TimeSpan observeAcrossRetryWindow = retryWindow + TimeSpan.FromMilliseconds(250);

        try
        {
            // read 不可の実ファイルを保持し、source 契約の 1 秒 retry を跨ぐまで ready 待ちへ留める。
            createdMovieLock = new FileStream(
                createdMoviePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            SemaphoreSlim checkFolderRunLock = new(0, 1);
            SetPrivateField(window, "_checkFolderRunLock", checkFolderRunLock);

            Stopwatch stopwatch = Stopwatch.StartNew();
            TaskCompletionSource<(string Request, TimeSpan ObservedAt)> queueRequested = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            object request = CreateCreatedWatchEventRequest(createdMoviePath);

            // hook は観測だけに留め、以降の pending 更新と run lock 待機まで本処理を流す。
            window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
            {
                queueRequested.TrySetResult(($"{mode}:{trigger}", stopwatch.Elapsed));
            };

            MethodInfo method = GetProcessWatchEventAsyncRequestMethod();
            Task task = (Task)method.Invoke(window, [request])!;

            await Task.Delay(observeAcrossRetryWindow);
            Assert.That(queueRequested.Task.IsCompleted, Is.False);
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(GetPrivateField<bool>(window, "_hasPendingCheckFolderRequest"), Is.False);

            TimeSpan fileUnlockedAt = stopwatch.Elapsed;
            createdMovieLock.Dispose();
            createdMovieLock = null;

            (string queuedRequest, TimeSpan queuedAt) = await queueRequested.Task.WaitAsync(
                TimeSpan.FromSeconds(5)
            );
            await WaitUntilAsync(
                () => GetPrivateField<bool>(window, "_hasPendingCheckFolderRequest"),
                TimeSpan.FromSeconds(5),
                "_hasPendingCheckFolderRequest が true になりませんでした。"
            );

            Assert.That(fileUnlockedAt, Is.GreaterThanOrEqualTo(retryWindow));
            Assert.That(queuedAt, Is.GreaterThan(fileUnlockedAt));
            Assert.That(
                queuedRequest,
                Is.EqualTo($"Watch:created:{createdMoviePath}")
            );
            Assert.That(task.IsCompleted, Is.False);

            checkFolderRunLock.Release();
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            createdMovieLock?.Dispose();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public async Task ProcessWatchEventAsync_Created_zeroByteはQueueCheckFolderAsyncへ流さない()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string createdMoviePath = Path.Combine(tempRoot, "created.mp4");
        await File.WriteAllBytesAsync(createdMoviePath, []);

        try
        {
            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 5);
            List<string> queuedRequests = [];
            object request = CreateCreatedWatchEventRequest(createdMoviePath);
            window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
            {
                queuedRequests.Add($"{mode}:{trigger}");
            };

            MethodInfo method = GetProcessWatchEventAsyncRequestMethod();
            Task task = (Task)method.Invoke(window, [request])!;
            await task;

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
    public async Task ProcessWatchEventAsync_Created_UI抑制中はdeferredフラグだけ立てる()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string createdMoviePath = Path.Combine(tempRoot, "created.mp4");
        await File.WriteAllBytesAsync(createdMoviePath, [0x1]);

        try
        {
            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            List<string> queuedRequests = [];
            object request = CreateCreatedWatchEventRequest(createdMoviePath);
            window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
            {
                queuedRequests.Add($"{mode}:{trigger}");
            };
            SetPrivateField(window, "_watchUiSuppressionCount", 1);

            MethodInfo method = GetProcessWatchEventAsyncRequestMethod();
            Task task = (Task)method.Invoke(window, [request])!;
            await task;

            Assert.That(queuedRequests, Is.Empty);
            Assert.That(GetPrivateField<bool>(window, "_watchWorkDeferredWhileSuppressed"), Is.True);
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

    [Test]
    public void ProcessRenamedWatchEventDirect_guard付きRenameThumbへ引数をそのまま流す()
    {
        List<string> calls = [];
        MainWindow.ProcessRenamedWatchEventDirect(
            @"E:\Movies\new-name.mp4",
            @"E:\Movies\old-name.mp4",
            (newFullPath, oldFullPath, canStartRenameBridge, logWatchMessage) =>
            {
                bool canStart = canStartRenameBridge?.Invoke() ?? false;
                logWatchMessage?.Invoke("from-rename");
                calls.Add($"rename:{newFullPath}:{oldFullPath}:{canStart}");
            },
            () =>
            {
                calls.Add("guard");
                return true;
            },
            message => calls.Add($"log:{message}")
        );

        Assert.That(
            calls,
            Is.EqualTo(
                [
                    "guard",
                    "log:from-rename",
                    "rename:E:\\Movies\\new-name.mp4:E:\\Movies\\old-name.mp4:True",
                ]
            )
        );
    }

    [Test]
    public void ProcessRenamedWatchEventDirect_callback後にguard付きRenameThumbへ流す()
    {
        List<string> calls = [];
        MainWindow.ProcessRenamedWatchEventDirect(
            @"E:\Movies\new-name.mp4",
            @"E:\Movies\old-name.mp4",
            (newFullPath, oldFullPath) =>
                calls.Add($"callback:{newFullPath}:{oldFullPath}"),
            (newFullPath, oldFullPath, canStartRenameBridge, logWatchMessage) =>
            {
                bool canStart = canStartRenameBridge?.Invoke() ?? true;
                logWatchMessage?.Invoke("from-rename");
                calls.Add($"rename:{newFullPath}:{oldFullPath}:{canStart}");
            },
            () =>
            {
                calls.Add("guard");
                return false;
            },
            message => calls.Add($"log:{message}")
        );
        Assert.That(
            calls,
            Is.EqualTo(
                [
                    "callback:E:\\Movies\\new-name.mp4:E:\\Movies\\old-name.mp4",
                    "guard",
                    "log:from-rename",
                    "rename:E:\\Movies\\new-name.mp4:E:\\Movies\\old-name.mp4:False",
                ]
            )
        );
    }

    [Test]
    public async Task FileChanged_対象拡張のCreatedはqueue処理まで到達して抑制へ退避する()
    {
        string originalCheckExt = IndigoMovieManager.Properties.Settings.Default.CheckExt;
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string createdMoviePath = Path.Combine(tempRoot, "created.MP4");
        await File.WriteAllBytesAsync(createdMoviePath, [0x1]);

        try
        {
            IndigoMovieManager.Properties.Settings.Default.CheckExt = "*.mp4,*.mkv";
            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            InitializeWatchEventQueue(window);
            SetPrivateField(window, "_watchUiSuppressionSync", new object());
            SetPrivateField(window, "_watchUiSuppressionCount", 1);

            window.QueueCheckFolderAsyncRequestedForTesting = (_, _) => { };
            MethodInfo fileChanged = typeof(MainWindow).GetMethod(
                "FileChanged",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            fileChanged.Invoke(window, [null, new FileSystemEventArgs(WatcherChangeTypes.Created, tempRoot, "created.MP4")]);

            Task processingTask = GetPrivateField<Task>(window, "_watchEventProcessingTask");
            await processingTask;

            Assert.That(GetPrivateField<bool>(window, "_watchWorkDeferredWhileSuppressed"), Is.True);
        }
        finally
        {
            IndigoMovieManager.Properties.Settings.Default.CheckExt = originalCheckExt;
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void FileChanged_対象外拡張はqueue処理を起動しない()
    {
        string originalCheckExt = IndigoMovieManager.Properties.Settings.Default.CheckExt;
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            IndigoMovieManager.Properties.Settings.Default.CheckExt = "*.mp4,*.mkv";
            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            InitializeWatchEventQueue(window);
            SetPrivateField(window, "_watchEventProcessingTask", Task.CompletedTask);

            MethodInfo fileChanged = typeof(MainWindow).GetMethod(
                "FileChanged",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            fileChanged.Invoke(window, [null, new FileSystemEventArgs(WatcherChangeTypes.Created, tempRoot, "other.txt")]);

            Assert.That(GetPrivateField<Task>(window, "_watchEventProcessingTask"), Is.SameAs(Task.CompletedTask));
        }
        finally
        {
            IndigoMovieManager.Properties.Settings.Default.CheckExt = originalCheckExt;
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public async Task FileRenamed_対象拡張はqueue処理を起動する()
    {
        string originalCheckExt = IndigoMovieManager.Properties.Settings.Default.CheckExt;
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            IndigoMovieManager.Properties.Settings.Default.CheckExt = "*.mp4,*.mkv";
            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            InitializeWatchEventQueue(window);
            Task beforeTask = GetPrivateField<Task>(window, "_watchEventProcessingTask");

            MethodInfo fileRenamed = typeof(MainWindow).GetMethod(
                "FileRenamed",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            fileRenamed.Invoke(
                window,
                [
                    null,
                    new RenamedEventArgs(
                        WatcherChangeTypes.Renamed,
                        tempRoot,
                        "after.MP4",
                        "before.MKV"
                    ),
                ]
            );

            Task processingTask = GetPrivateField<Task>(window, "_watchEventProcessingTask");
            Assert.That(processingTask, Is.Not.SameAs(beforeTask));
            await processingTask;
        }
        finally
        {
            IndigoMovieManager.Properties.Settings.Default.CheckExt = originalCheckExt;
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
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
        SetPrivateField(window, "_checkFolderRequestSync", new object());
        SetPrivateField(window, "_checkFolderRunLock", new SemaphoreSlim(1, 1));
        InitializeWatchEventQueue(window);
        return window;
    }

    private static MethodInfo GetProcessWatchEventAsyncRequestMethod()
    {
        Type requestType = GetWatchEventRequestType();
        MethodInfo method = typeof(MainWindow).GetMethod(
            "ProcessWatchEventAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [requestType],
            modifiers: null
        )!;
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static void InitializeWatchEventQueue(MainWindow window)
    {
        SetPrivateField(window, "_watchEventRunLock", new SemaphoreSlim(1, 1));
        SetPrivateField(window, "_watchEventRequestSync", new object());
        Type requestType = GetWatchEventRequestType();
        Type requestQueueType = typeof(Queue<>).MakeGenericType(requestType);
        SetPrivateField(window, "_watchEventRequests", Activator.CreateInstance(requestQueueType)!);
        SetPrivateField(window, "_watchEventProcessingTask", Task.CompletedTask);
    }

    private static object CreateCreatedWatchEventRequest(string fullPath)
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
            types: [watchEventKindType, typeof(string), typeof(string)],
            modifiers: null
        )!;
        Assert.That(constructor, Is.Not.Null);
        return constructor.Invoke([createdKind, fullPath, ""]);
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

    private static T GetPrivateField<T>(MainWindow window, string fieldName)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        )!;
        Assert.That(field, Is.Not.Null, fieldName);
        return (T)field.GetValue(window)!;
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string failureMessage
    )
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail(failureMessage);
    }
}
