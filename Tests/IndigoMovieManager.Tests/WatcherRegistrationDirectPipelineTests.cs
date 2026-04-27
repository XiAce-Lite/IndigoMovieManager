using IndigoMovieManager;
using IndigoMovieManager.Data;
using IndigoMovieManager.DB;
using IndigoMovieManager.ViewModels;
using System.Data.SQLite;
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
    public async Task QueueWatchEventAsync_Created待機中でもrunnerは後続イベントを塞がない()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string createdMoviePath = Path.Combine(tempRoot, "created.mp4");
        await File.WriteAllBytesAsync(createdMoviePath, [0x1]);
        FileStream? createdMovieLock = null;

        try
        {
            createdMovieLock = new FileStream(
                createdMoviePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            SemaphoreSlim checkFolderRunLock = GetPrivateField<SemaphoreSlim>(
                window,
                "_checkFolderRunLock"
            );
            await checkFolderRunLock.WaitAsync();

            TaskCompletionSource<string> queueRequested = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
            {
                queueRequested.TrySetResult($"{mode}:{trigger}");
            };

            MethodInfo queueMethod = GetQueueWatchEventAsyncRequestMethod();
            Task createdQueueTask = (Task)queueMethod.Invoke(
                window,
                [CreateCreatedWatchEventRequest(createdMoviePath), "watch-created"]
            )!;
            await createdQueueTask.WaitAsync(TimeSpan.FromSeconds(2));

            Task renamedQueueTask = (Task)queueMethod.Invoke(
                window,
                [
                    CreateRenamedWatchEventRequest(
                        Path.Combine(tempRoot, "after.mp4"),
                        Path.Combine(tempRoot, "before.mp4")
                    ),
                    "watch-renamed",
                ]
            )!;
            await renamedQueueTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.That(queueRequested.Task.IsCompleted, Is.False);

            createdMovieLock.Dispose();
            createdMovieLock = null;

            string queuedRequest = await queueRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(queuedRequest, Is.EqualTo($"Watch:created:{createdMoviePath}"));

            checkFolderRunLock.Release();
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
    public async Task QueueWatchEventAsync_Created連続投入はready待ちを直列化して後続を先行解除まで待たせる()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string firstCreatedMoviePath = Path.Combine(tempRoot, "first-created.mp4");
        string secondCreatedMoviePath = Path.Combine(tempRoot, "second-created.mp4");
        await File.WriteAllBytesAsync(firstCreatedMoviePath, [0x1]);
        await File.WriteAllBytesAsync(secondCreatedMoviePath, [0x1]);
        FileStream? firstCreatedMovieLock = null;

        try
        {
            // 先行 created だけを lock して、後続 created の先走り有無を観測する。
            firstCreatedMovieLock = new FileStream(
                firstCreatedMoviePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            List<string> queuedRequests = [];
            object queuedRequestSync = new();
            window.QueueCheckFolderAsyncForTesting = (mode, trigger) =>
            {
                lock (queuedRequestSync)
                {
                    queuedRequests.Add($"{mode}:{trigger}");
                }

                return Task.CompletedTask;
            };

            MethodInfo queueMethod = GetQueueWatchEventAsyncRequestMethod();
            Task firstCreatedQueueTask = (Task)queueMethod.Invoke(
                window,
                [CreateCreatedWatchEventRequest(firstCreatedMoviePath), "watch-created-first"]
            )!;
            Task secondCreatedQueueTask = (Task)queueMethod.Invoke(
                window,
                [CreateCreatedWatchEventRequest(secondCreatedMoviePath), "watch-created-second"]
            )!;
            await firstCreatedQueueTask.WaitAsync(TimeSpan.FromSeconds(2));
            await secondCreatedQueueTask.WaitAsync(TimeSpan.FromSeconds(2));

            // source 契約の 1 秒 retry を跨いでも後続 created が走らないことを確認する。
            await Task.Delay(TimeSpan.FromMilliseconds(1250));
            int queuedCountBeforeUnlock;
            lock (queuedRequestSync)
            {
                queuedCountBeforeUnlock = queuedRequests.Count;
            }
            Assert.That(queuedCountBeforeUnlock, Is.EqualTo(0));

            firstCreatedMovieLock.Dispose();
            firstCreatedMovieLock = null;

            await WaitUntilAsync(
                () =>
                {
                    lock (queuedRequestSync)
                    {
                        return queuedRequests.Count >= 2;
                    }
                },
                TimeSpan.FromSeconds(5),
                "created 連続投入の queue 要求が2件そろいませんでした。"
            );

            List<string> queuedRequestSnapshot;
            lock (queuedRequestSync)
            {
                queuedRequestSnapshot = [.. queuedRequests];
            }

            Assert.That(
                queuedRequestSnapshot,
                Is.EqualTo(
                    [
                        $"Watch:created:{firstCreatedMoviePath}",
                        $"Watch:created:{secondCreatedMoviePath}",
                    ]
                )
            );
        }
        finally
        {
            firstCreatedMovieLock?.Dispose();
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
    public async Task QueueWatchEventAsync_Created_ready待ち中でもRenamedは先行処理できる()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string createdMoviePath = Path.Combine(tempRoot, "created.mp4");
        await File.WriteAllBytesAsync(createdMoviePath, [0x1]);
        FileStream? createdMovieLock = null;

        try
        {
            // created の ready 判定を待機させ、queue runner が後続イベントを先に流せるか観測する。
            createdMovieLock = new FileStream(
                createdMoviePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            MainWindowViewModel mainVm = GetPrivateField<MainWindowViewModel>(window, "MainVM");
            const string oldMoviePath = @"E:\Movies\before.mp4";
            const string newMoviePath = @"E:\Movies\after.mp4";
            MovieRecords renamedMovie = new()
            {
                Movie_Id = 1,
                Movie_Path = oldMoviePath,
                Movie_Name = "before",
            };
            mainVm.MovieRecs = [renamedMovie];

            SemaphoreSlim checkFolderRunLock = new(0, 1);
            SetPrivateField(window, "_checkFolderRunLock", checkFolderRunLock);

            TaskCompletionSource<string> queueRequested = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
            {
                queueRequested.TrySetResult($"{mode}:{trigger}");
            };

            MethodInfo queueMethod = GetQueueWatchEventAsyncRequestMethod();
            object createdRequest = CreateCreatedWatchEventRequest(createdMoviePath);
            object renamedRequest = CreateRenamedWatchEventRequest(newMoviePath, oldMoviePath);

            Task createdQueueTask = (Task)queueMethod.Invoke(
                window,
                [createdRequest, "watch-created"]
            )!;
            Task renamedQueueTask = (Task)queueMethod.Invoke(
                window,
                [renamedRequest, "watch-renamed"]
            )!;

            await WaitUntilAsync(
                () =>
                    string.Equals(
                        renamedMovie.Movie_Path,
                        newMoviePath,
                        StringComparison.OrdinalIgnoreCase
                    ),
                TimeSpan.FromSeconds(5),
                "created ready待ち中に rename の更新が進みませんでした。"
            );

            Assert.That(createdQueueTask.IsCompleted, Is.True);
            Assert.That(renamedQueueTask.IsCompleted, Is.True);
            Assert.That(queueRequested.Task.IsCompleted, Is.False);
            Assert.That(GetPrivateField<bool>(window, "_hasPendingCheckFolderRequest"), Is.False);

            createdMovieLock.Dispose();
            createdMovieLock = null;

            string queuedRequest = await queueRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(
                () => GetPrivateField<bool>(window, "_hasPendingCheckFolderRequest"),
                TimeSpan.FromSeconds(5),
                "_hasPendingCheckFolderRequest が true になりませんでした。"
            );

            Assert.That(queuedRequest, Is.EqualTo($"Watch:created:{createdMoviePath}"));

            checkFolderRunLock.Release();
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
    public async Task QueueWatchEventAsync_Created_detached処理はdrain待機で完了を観測できる()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string createdMoviePath = Path.Combine(tempRoot, "created.mp4");
        await File.WriteAllBytesAsync(createdMoviePath, [0x1]);
        FileStream? createdMovieLock = null;

        try
        {
            // Created ready 待ちを意図的に遅延させ、queue本体とdetached taskを分離して観測する。
            createdMovieLock = new FileStream(
                createdMoviePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            TaskCompletionSource<string> queueRequested = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            window.QueueCheckFolderAsyncForTesting = (mode, trigger) =>
            {
                queueRequested.TrySetResult($"{mode}:{trigger}");
                return Task.CompletedTask;
            };

            MethodInfo queueMethod = GetQueueWatchEventAsyncRequestMethod();
            Task queueRunnerTask = (Task)queueMethod.Invoke(
                window,
                [CreateCreatedWatchEventRequest(createdMoviePath), "watch-created"]
            )!;
            await queueRunnerTask.WaitAsync(TimeSpan.FromSeconds(2));

            Task detachedCreatedTask = GetPrivateField<Task>(
                window,
                "_watchCreatedEventProcessingTask"
            );
            Assert.That(detachedCreatedTask.IsCompleted, Is.False);
            Assert.That(queueRequested.Task.IsCompleted, Is.False);

            createdMovieLock.Dispose();
            createdMovieLock = null;

            string queuedRequest = await queueRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await detachedCreatedTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(queuedRequest, Is.EqualTo($"Watch:created:{createdMoviePath}"));
                Assert.That(detachedCreatedTask.IsCompleted, Is.True);
                Assert.That(
                    GetPrivateField<Task>(window, "_watchCreatedEventProcessingTask").IsCompleted,
                    Is.True
                );
            });
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
    public async Task QueueWatchEventAsync_Created_UI抑制中はdetached処理がdrain後に残留しない()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string createdMoviePath = Path.Combine(tempRoot, "created.mp4");
        await File.WriteAllBytesAsync(createdMoviePath, [0x1]);

        try
        {
            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            SetPrivateField(window, "_watchUiSuppressionCount", 1);
            List<string> queuedRequests = [];
            window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
            {
                queuedRequests.Add($"{mode}:{trigger}");
            };

            MethodInfo queueMethod = GetQueueWatchEventAsyncRequestMethod();
            Task queueRunnerTask = (Task)queueMethod.Invoke(
                window,
                [CreateCreatedWatchEventRequest(createdMoviePath), "watch-created-suppressed"]
            )!;
            await queueRunnerTask.WaitAsync(TimeSpan.FromSeconds(2));

            Task detachedCreatedTask = GetPrivateField<Task>(
                window,
                "_watchCreatedEventProcessingTask"
            );
            await detachedCreatedTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(detachedCreatedTask.IsCompleted, Is.True);
                Assert.That(queuedRequests, Is.Empty);
                Assert.That(GetPrivateField<bool>(window, "_watchWorkDeferredWhileSuppressed"), Is.True);
            });
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
    public async Task ProcessWatchEventAsync_Renamed_未登録pathはWatchScanへ再合流する()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string newMoviePath = Path.Combine(tempRoot, "after.mp4");
        await File.WriteAllBytesAsync(newMoviePath, [0x1]);

        try
        {
            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            MainWindowViewModel mainVm = GetPrivateField<MainWindowViewModel>(window, "MainVM");
            mainVm.MovieRecs = [];
            List<string> queuedRequests = [];
            window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
            {
                queuedRequests.Add($"{mode}:{trigger}");
            };

            MethodInfo method = GetProcessWatchEventAsyncRequestMethod();
            object request = CreateRenamedWatchEventRequest(
                newMoviePath,
                Path.Combine(tempRoot, "before.mp4")
            );
            Task task = (Task)method.Invoke(window, [request])!;
            await task;

            Assert.That(
                queuedRequests,
                Is.EqualTo([$"Watch:renamed-untracked:{newMoviePath}"])
            );
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
    public async Task ProcessWatchEventAsync_CreatedからRenamedの最終整合をDBサムネqueueで観測できる()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string thumbnailRoot = Path.Combine(tempRoot, "thumbnail");
        Directory.CreateDirectory(thumbnailRoot);
        string oldMoviePath = Path.Combine(tempRoot, "before.mp4");
        string newMoviePath = Path.Combine(tempRoot, "after.mp4");
        string sourceThumbnailPath = Path.Combine(thumbnailRoot, "before.jpg");
        string dbPath = CreateTempMainDbForRename(oldMoviePath);
        await File.WriteAllBytesAsync(oldMoviePath, [0x1]);
        await File.WriteAllBytesAsync(sourceThumbnailPath, [0x1]);

        try
        {
            MainWindow window = CreateMainWindow(dbPath, currentTabIndex: 2);
            MainWindowViewModel mainVm = GetPrivateField<MainWindowViewModel>(window, "MainVM");
            mainVm.DbInfo.DBName = "main";
            mainVm.DbInfo.ThumbFolder = thumbnailRoot;
            mainVm.DbInfo.BookmarkFolder = Path.Combine(tempRoot, "bookmark-missing");
            mainVm.MovieRecs =
            [
                new MovieRecords
                {
                    Movie_Id = 1,
                    Movie_Path = oldMoviePath,
                    Movie_Name = "before",
                    ThumbPathSmall = sourceThumbnailPath,
                },
            ];

            // uninitialized MainWindow でも rename の DB 反映を実経路で流せるよう、mutation facade だけ補う。
            SetPrivateField(window, "_mainDbMovieMutationFacade", new MainDbMovieMutationFacade());

            List<string> queuedRequests = [];
            window.QueueCheckFolderAsyncForTesting = (mode, trigger) =>
            {
                queuedRequests.Add($"{mode}:{trigger}");
                return Task.CompletedTask;
            };

            MethodInfo method = GetProcessWatchEventAsyncRequestMethod();
            Task createdTask = (Task)method.Invoke(window, [CreateCreatedWatchEventRequest(oldMoviePath)])!;
            await createdTask;
            Task renamedTask = (Task)method.Invoke(
                window,
                [CreateRenamedWatchEventRequest(newMoviePath, oldMoviePath)]
            )!;
            await renamedTask;

            MovieRecords renamedMovie = mainVm.MovieRecs.Single();
            string expectedThumbnailPath = Path.Combine(thumbnailRoot, "after.jpg");
            (string MoviePath, string MovieName) persisted = ReadMoviePathAndNameFromDb(dbPath, 1);

            Assert.Multiple(() =>
            {
                Assert.That(queuedRequests, Is.EqualTo([($"Watch:created:{oldMoviePath}")]));
                Assert.That(renamedMovie.Movie_Path, Is.EqualTo(newMoviePath));
                Assert.That(renamedMovie.Movie_Name, Is.EqualTo("after"));
                Assert.That(persisted.MoviePath, Is.EqualTo(newMoviePath));
                Assert.That(persisted.MovieName, Is.EqualTo("after"));
                Assert.That(File.Exists(sourceThumbnailPath), Is.False);
                Assert.That(File.Exists(expectedThumbnailPath), Is.True);
                Assert.That(renamedMovie.ThumbPathSmall, Is.EqualTo(expectedThumbnailPath));
            });
        }
        finally
        {
            TryDeleteFile(dbPath);
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
            SemaphoreSlim eventRunLock = GetPrivateField<SemaphoreSlim>(
                window,
                "_watchEventRunLock"
            );
            await eventRunLock.WaitAsync();
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
            Assert.That(processingTask.IsCompleted, Is.False);
            eventRunLock.Release();
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

    [Test]
    public async Task QueueWatchEventAsync_Shutdown開始後は新規イベントを受け付けない()
    {
        MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
        int queueCheckRequestedCount = 0;
        window.QueueCheckFolderAsyncRequestedForTesting = (_, _) => queueCheckRequestedCount++;

        MethodInfo beginShutdownMethod = typeof(MainWindow).GetMethod(
            "BeginWatchEventQueueShutdownForClosing",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        beginShutdownMethod.Invoke(window, null);

        MethodInfo queueMethod = GetQueueWatchEventAsyncRequestMethod();
        object createdRequest = CreateCreatedWatchEventRequest(@"E:\Movies\shutdown-created.mp4");
        Task queueTask = (Task)queueMethod.Invoke(window, [createdRequest, "watch-created"])!;
        await queueTask.WaitAsync(TimeSpan.FromSeconds(2));

        object watchEventRequests = GetPrivateField<object>(window, "_watchEventRequests");
        int pendingRequestCount = (int)(
            watchEventRequests.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(watchEventRequests) ?? -1
        );

        Assert.That(queueCheckRequestedCount, Is.EqualTo(0));
        Assert.That(pendingRequestCount, Is.EqualTo(0));
        Assert.That(
            GetPrivateField<Task>(window, "_watchEventProcessingTask"),
            Is.SameAs(Task.CompletedTask)
        );
    }

    [Test]
    public async Task ProcessCreatedWatchEventAsync_Shutdown開始後はready後の再走査を積まない()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string createdMoviePath = Path.Combine(tempRoot, "shutdown-created.mp4");
        await File.WriteAllBytesAsync(createdMoviePath, [0x1]);

        try
        {
            MainWindow window = CreateMainWindow(@"D:\Db\main.wb", currentTabIndex: 2);
            int queueCheckRequestedCount = 0;
            window.QueueCheckFolderAsyncRequestedForTesting = (_, _) => queueCheckRequestedCount++;

            MethodInfo beginShutdownMethod = typeof(MainWindow).GetMethod(
                "BeginWatchEventQueueShutdownForClosing",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            beginShutdownMethod.Invoke(window, null);

            MethodInfo method = GetProcessCreatedWatchEventAsyncMethod();
            Task task = (Task)method.Invoke(window, [createdMoviePath])!;
            await task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.That(queueCheckRequestedCount, Is.EqualTo(0));
            Assert.That(GetPrivateField<bool>(window, "_hasPendingCheckFolderRequest"), Is.False);
        }
        finally
        {
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

    private static MethodInfo GetQueueWatchEventAsyncRequestMethod()
    {
        Type requestType = GetWatchEventRequestType();
        MethodInfo method = typeof(MainWindow).GetMethod(
            "QueueWatchEventAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [requestType, typeof(string)],
            modifiers: null
        )!;
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo GetProcessCreatedWatchEventAsyncMethod()
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "ProcessCreatedWatchEventAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
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
        SetPrivateField(window, "_watchCreatedEventProcessingTask", Task.CompletedTask);
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

    private static object CreateRenamedWatchEventRequest(string fullPath, string oldFullPath)
    {
        Type watchEventKindType = typeof(MainWindow).GetNestedType(
            "WatchEventKind",
            BindingFlags.NonPublic
        )!;
        Assert.That(watchEventKindType, Is.Not.Null);

        object renamedKind = Enum.Parse(watchEventKindType, "Renamed");
        Type requestType = GetWatchEventRequestType();
        ConstructorInfo constructor = requestType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [watchEventKindType, typeof(string), typeof(string)],
            modifiers: null
        )!;
        Assert.That(constructor, Is.Not.Null);
        return constructor.Invoke([renamedKind, fullPath, oldFullPath]);
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

    private static string CreateTempMainDbForRename(string oldMoviePath)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-watch-rename-{Guid.NewGuid():N}.wb");
        SQLiteConnection.CreateFile(dbPath);

        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE movie (
    movie_id INTEGER PRIMARY KEY,
    tag TEXT NOT NULL,
    score INTEGER NOT NULL,
    view_count INTEGER NOT NULL,
    last_date TEXT NOT NULL,
    movie_path TEXT NOT NULL,
    movie_name TEXT NOT NULL,
    movie_length INTEGER NOT NULL,
    kana TEXT NOT NULL,
    roma TEXT NOT NULL
);

INSERT INTO movie (
    movie_id,
    tag,
    score,
    view_count,
    last_date,
    movie_path,
    movie_name,
    movie_length,
    kana,
    roma
)
VALUES (
    1,
    '',
    0,
    0,
    '2026-03-20 00:00:00',
    @old_movie_path,
    'before',
    0,
    '',
    ''
);";
        _ = command.Parameters.AddWithValue("@old_movie_path", oldMoviePath);
        command.ExecuteNonQuery();
        return dbPath;
    }

    private static (string MoviePath, string MovieName) ReadMoviePathAndNameFromDb(
        string dbPath,
        long movieId
    )
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT movie_path, movie_name FROM movie WHERE movie_id = @movie_id;";
        _ = command.Parameters.AddWithValue("@movie_id", movieId);
        using SQLiteDataReader reader = command.ExecuteReader();
        Assert.That(reader.Read(), Is.True);
        return (reader["movie_path"]?.ToString() ?? "", reader["movie_name"]?.ToString() ?? "");
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
            // 一時DBの掃除失敗は、テスト本体の判定を優先する。
        }
    }
}
