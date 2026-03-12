using IndigoMovieManager.ModelViews;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueuePipeline;
using System.Collections.Specialized;
using System.Reflection;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class ThumbnailProgressRuntimeTests
{
    [Test]
    public void RecordEnqueue_最新10件だけ保持する()
    {
        ThumbnailProgressRuntime runtime = new();

        for (int i = 1; i <= 12; i++)
        {
            runtime.RecordEnqueue(
                new QueueObj
                {
                    MovieFullPath = $@"C:\videos\movie{i}.mp4",
                    Tabindex = 0,
                }
            );
        }

        ThumbnailProgressRuntimeSnapshot snapshot = runtime.CreateSnapshot();
        Assert.That(snapshot.EnqueueLogs.Count, Is.EqualTo(10));
        Assert.That(snapshot.EnqueueLogs.First(), Is.EqualTo("movie3.mp4"));
        Assert.That(snapshot.EnqueueLogs.Last(), Is.EqualTo("movie12.mp4"));
    }

    [Test]
    public void MarkJobStarted_長いファイル名を拡張子付き省略で保持する()
    {
        ThumbnailProgressRuntime runtime = new();
        QueueObj queueObj = new()
        {
            MovieFullPath = @"C:\videos\abcdefghijklmnopqrs.mp4",
            Tabindex = 2,
        };

        runtime.MarkJobStarted(queueObj);
        ThumbnailProgressRuntimeSnapshot started = runtime.CreateSnapshot();

        Assert.That(started.ActiveWorkers.Count, Is.EqualTo(1));
        Assert.That(started.ActiveWorkers[0].DisplayMovieName, Is.EqualTo("abcdefghijklmnopq...mp4"));

        runtime.MarkThumbnailSaved(queueObj, @"C:\thumb\img1.jpg");
        ThumbnailProgressRuntimeSnapshot saved = runtime.CreateSnapshot();
        Assert.That(saved.ActiveWorkers[0].PreviewImagePath, Is.EqualTo(@"C:\thumb\img1.jpg"));

        runtime.MarkJobCompleted(queueObj);
        ThumbnailProgressRuntimeSnapshot completed = runtime.CreateSnapshot();
        Assert.That(completed.ActiveWorkers.Count, Is.EqualTo(1));
        Assert.That(completed.ActiveWorkers[0].IsActive, Is.False);
    }

    [Test]
    public void MarkJobStarted_サイズ別に優先低速スレッドへ割り当てる()
    {
        ThumbnailProgressRuntime runtime = new();
        QueueObj smallJob = new()
        {
            MovieFullPath = @"C:\videos\small.mp4",
            Tabindex = 0,
            MovieSizeBytes = 120L * 1024L * 1024L,
        };
        QueueObj largeJob = new()
        {
            MovieFullPath = @"C:\videos\large.mp4",
            Tabindex = 0,
            MovieSizeBytes = 600L * 1024L * 1024L * 1024L,
        };

        runtime.MarkJobStarted(smallJob);
        runtime.MarkJobStarted(largeJob);

        ThumbnailProgressRuntimeSnapshot snapshot = runtime.CreateSnapshot();
        ThumbnailProgressWorkerSnapshot smallWorker =
            snapshot.ActiveWorkers.Single(x => x.DisplayMovieName == "small.mp4");
        ThumbnailProgressWorkerSnapshot largeWorker =
            snapshot.ActiveWorkers.Single(x => x.DisplayMovieName == "large.mp4");

        Assert.That(smallWorker.WorkerId, Is.EqualTo(1));
        Assert.That(smallWorker.WorkerLabel, Is.EqualTo("優先Thread"));
        Assert.That(largeWorker.WorkerId, Is.EqualTo(2));
        Assert.That(largeWorker.WorkerLabel, Is.EqualTo("低速Thread"));
    }

    [Test]
    public void MarkJobStarted_救済要求は救済スレッドへ割り当てる()
    {
        ThumbnailProgressRuntime runtime = new();
        QueueObj rescueJob = new()
        {
            MovieFullPath = @"C:\videos\repair.mp4",
            Tabindex = 0,
            MovieSizeBytes = 10L * 1024L * 1024L,
            IsRescueRequest = true,
        };

        runtime.MarkJobStarted(rescueJob);

        ThumbnailProgressRuntimeSnapshot snapshot = runtime.CreateSnapshot();
        ThumbnailProgressWorkerSnapshot rescueWorker = snapshot.ActiveWorkers.Single();

        Assert.That(rescueWorker.WorkerId, Is.EqualTo(3));
        Assert.That(rescueWorker.WorkerLabel, Is.EqualTo("救済Thread"));
    }

    [Test]
    public void MarkJobStarted_優先レーン上限30MB設定では40MB動画を通常レーンへ流す()
    {
        int backupPriorityLaneMaxMb = IndigoMovieManager.Properties.Settings.Default.ThumbnailPriorityLaneMaxMb;
        int backupSlowLaneMinGb = IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb;

        try
        {
            // UIで30MBを選んだ時の分類結果を、そのまま進捗Runtimeでも再現させる。
            IndigoMovieManager.Properties.Settings.Default.ThumbnailPriorityLaneMaxMb = 30;
            IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb = 100;
            ResetThumbnailLaneClassifierCache();

            ThumbnailProgressRuntime runtime = new();
            QueueObj mediumJob = new()
            {
                MovieFullPath = @"C:\videos\medium.mp4",
                Tabindex = 0,
                MovieSizeBytes = 40L * 1024L * 1024L,
            };

            runtime.MarkJobStarted(mediumJob);

            ThumbnailProgressRuntimeSnapshot snapshot = runtime.CreateSnapshot();
            ThumbnailProgressWorkerSnapshot worker = snapshot.ActiveWorkers.Single();

            Assert.That(worker.WorkerId, Is.Not.EqualTo(1));
            Assert.That(worker.WorkerLabel, Is.Not.EqualTo("優先Thread"));
        }
        finally
        {
            IndigoMovieManager.Properties.Settings.Default.ThumbnailPriorityLaneMaxMb =
                backupPriorityLaneMaxMb;
            IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb = backupSlowLaneMinGb;
            ResetThumbnailLaneClassifierCache();
        }
    }

    [Test]
    public void UpdateSessionProgress_合計は完了数未満にならない()
    {
        ThumbnailProgressRuntime runtime = new();
        runtime.UpdateSessionProgress(
            completedCount: 8,
            totalCount: 3,
            currentParallel: 2,
            configuredParallel: 4
        );

        ThumbnailProgressRuntimeSnapshot snapshot = runtime.CreateSnapshot();
        Assert.That(snapshot.SessionCompletedCount, Is.EqualTo(8));
        Assert.That(snapshot.SessionTotalCount, Is.EqualTo(8));
        Assert.That(snapshot.CurrentParallelism, Is.EqualTo(2));
        Assert.That(snapshot.ConfiguredParallelism, Is.EqualTo(4));
    }

    [Test]
    public void CreateSnapshot_無変更時は同一インスタンスを再利用する()
    {
        ThumbnailProgressRuntime runtime = new();

        ThumbnailProgressRuntimeSnapshot first = runtime.CreateSnapshot();
        ThumbnailProgressRuntimeSnapshot second = runtime.CreateSnapshot();
        Assert.That(ReferenceEquals(first, second), Is.True);
        Assert.That(first.Version, Is.EqualTo(second.Version));

        runtime.RecordEnqueue(
            new QueueObj
            {
                MovieFullPath = @"C:\videos\movie-a.mp4",
                Tabindex = 0,
            }
        );
        ThumbnailProgressRuntimeSnapshot changed = runtime.CreateSnapshot();
        Assert.That(ReferenceEquals(first, changed), Is.False);
        Assert.That(changed.Version, Is.GreaterThan(first.Version));

        ThumbnailProgressRuntimeSnapshot changedSecond = runtime.CreateSnapshot();
        Assert.That(ReferenceEquals(changed, changedSecond), Is.True);
    }

    [Test]
    public void UpdateSessionProgress_同値更新ではVersionを増やさない()
    {
        ThumbnailProgressRuntime runtime = new();

        runtime.UpdateSessionProgress(3, 10, 2, 6);
        ThumbnailProgressRuntimeSnapshot first = runtime.CreateSnapshot();

        runtime.UpdateSessionProgress(3, 10, 2, 6);
        ThumbnailProgressRuntimeSnapshot second = runtime.CreateSnapshot();

        Assert.That(second.Version, Is.EqualTo(first.Version));
        Assert.That(ReferenceEquals(first, second), Is.True);
    }

    [Test]
    public void ViewStateApply_GPUHDD未取得時はNA表示になる()
    {
        ThumbnailProgressViewState viewState = new();
        ThumbnailProgressRuntimeSnapshot runtimeSnapshot = new()
        {
            SessionCompletedCount = 5,
            SessionTotalCount = 20,
            ConfiguredParallelism = 6,
            ActiveWorkers =
            [
                new ThumbnailProgressWorkerSnapshot
                {
                    WorkerId = 1,
                    WorkerLabel = "Thread 1",
                    DisplayMovieName = "movieA.mp4",
                    PreviewImagePath = @"C:\thumb\a.jpg",
                },
            ],
            EnqueueLogs = ["movieA.mp4", "movieB.mp4"],
        };

        viewState.Apply(
            runtimeSnapshot,
            dbPendingCount: 3,
            dbTotalCount: 100,
            logicalCoreCount: 16,
            cpuPercent: 27.5,
            gpuPercent: null,
            hddPercent: null
        );

        Assert.That(viewState.CreatedQueueText, Is.EqualTo("5 / 20"));
        Assert.That(viewState.DbPendingText, Is.EqualTo("3 / 100"));
        Assert.That(viewState.ThreadText, Is.EqualTo("1 / 6 / 16"));
        Assert.That(viewState.CpuMeterText, Is.EqualTo("27.5%"));
        Assert.That(viewState.GpuMeterText, Is.EqualTo("N/A"));
        Assert.That(viewState.HddMeterText, Is.EqualTo("N/A"));
        Assert.That(viewState.QueueLogs.Count, Is.EqualTo(2));
        Assert.That(viewState.WorkerPanels.Count, Is.EqualTo(6));
        Assert.That(viewState.WorkerPanels[0].WorkerLabel, Is.EqualTo("優先Thread"));
        Assert.That(viewState.WorkerPanels[1].WorkerLabel, Is.EqualTo("低速Thread"));
        Assert.That(viewState.WorkerPanels[0].MovieName, Is.EqualTo("movieA.mp4"));
        Assert.That(viewState.WorkerPanels[5].StatusText, Is.EqualTo("待機"));
    }

    [Test]
    public void ViewStateApply_固定スロット上で同一WorkerIdは既存パネルを再利用する()
    {
        ThumbnailProgressViewState viewState = new();
        ThumbnailProgressRuntimeSnapshot firstSnapshot = new()
        {
            ConfiguredParallelism = 8,
            ActiveWorkers =
            [
                new ThumbnailProgressWorkerSnapshot
                {
                    WorkerId = 7,
                    WorkerLabel = "Thread 7",
                    DisplayMovieName = "movieA.mp4",
                    PreviewImagePath = @"C:\thumb\first.jpg",
                },
            ],
        };

        viewState.Apply(firstSnapshot, 0, 0, 0, 0, null, null);
        ThumbnailProgressWorkerPanelViewState firstPanel = viewState.WorkerPanels[6];

        ThumbnailProgressRuntimeSnapshot secondSnapshot = new()
        {
            ConfiguredParallelism = 8,
            ActiveWorkers =
            [
                new ThumbnailProgressWorkerSnapshot
                {
                    WorkerId = 7,
                    WorkerLabel = "Thread 7",
                    DisplayMovieName = "movieB.mp4",
                    PreviewImagePath = @"C:\thumb\second.jpg",
                },
            ],
        };

        viewState.Apply(secondSnapshot, 0, 0, 0, 0, null, null);

        Assert.That(viewState.WorkerPanels.Count, Is.EqualTo(8));
        Assert.That(ReferenceEquals(firstPanel, viewState.WorkerPanels[6]), Is.True);
        Assert.That(viewState.WorkerPanels[6].MovieName, Is.EqualTo("movieB.mp4"));
        Assert.That(
            viewState.WorkerPanels[6].PreviewImagePath,
            Is.EqualTo(@"C:\thumb\second.jpg")
        );
    }

    [Test]
    public void ViewStateApply_スナップショットに無いスロットは待機へ戻す()
    {
        ThumbnailProgressViewState viewState = new();

        ThumbnailProgressRuntimeSnapshot firstSnapshot = new()
        {
            ConfiguredParallelism = 3,
            ActiveWorkers =
            [
                new ThumbnailProgressWorkerSnapshot
                {
                    WorkerId = 2,
                    WorkerLabel = "Thread 2",
                    DisplayMovieName = "movieA.mp4",
                    PreviewImagePath = @"C:\thumb\a.jpg",
                    PreviewCacheKey = "k1",
                    PreviewRevision = 11,
                    IsActive = true,
                },
            ],
        };

        viewState.Apply(firstSnapshot, 0, 0, 8, 0, null, null);
        Assert.That(viewState.WorkerPanels[1].StatusText, Is.EqualTo("処理中"));

        ThumbnailProgressRuntimeSnapshot secondSnapshot = new()
        {
            ConfiguredParallelism = 3,
            ActiveWorkers = [],
        };

        viewState.Apply(secondSnapshot, 0, 0, 8, 0, null, null);

        Assert.That(viewState.WorkerPanels.Count, Is.EqualTo(3));
        Assert.That(viewState.WorkerPanels[1].StatusText, Is.EqualTo("待機"));
        Assert.That(viewState.WorkerPanels[1].MovieName, Is.EqualTo(""));
        Assert.That(viewState.WorkerPanels[1].PreviewImagePath, Is.EqualTo(""));
        Assert.That(viewState.WorkerPanels[1].PreviewCacheKey, Is.EqualTo(""));
        Assert.That(viewState.WorkerPanels[1].PreviewRevision, Is.EqualTo(0));
    }

    [Test]
    public void ViewStateApply_同一QueueLogs再適用時はCollectionChangedを発火しない()
    {
        ThumbnailProgressViewState viewState = new();
        int changedCount = 0;
        viewState.QueueLogs.CollectionChanged += (_, _) => changedCount++;

        ThumbnailProgressRuntimeSnapshot snapshot = new()
        {
            ConfiguredParallelism = 1,
            EnqueueLogs = ["movieA.mp4", "movieB.mp4"],
        };

        viewState.Apply(snapshot, 0, 0, 8, 0, null, null);
        Assert.That(changedCount, Is.GreaterThan(0));

        changedCount = 0;
        viewState.Apply(snapshot, 0, 0, 8, 0, null, null);

        Assert.That(changedCount, Is.EqualTo(0));
    }

    [Test]
    public void ViewStateApply_固定スロットは削除せず番号順を維持する()
    {
        ThumbnailProgressViewState viewState = new();
        ThumbnailProgressRuntimeSnapshot snapshot = new()
        {
            ConfiguredParallelism = 4,
            ActiveWorkers =
            [
                new ThumbnailProgressWorkerSnapshot
                {
                    WorkerId = 4,
                    WorkerLabel = "Thread 4",
                    DisplayMovieName = "movieDone.mp4",
                    PreviewImagePath = @"C:\thumb\done.jpg",
                    IsActive = false,
                },
            ],
        };

        viewState.Apply(snapshot, 0, 0, 8, 0, null, null);

        Assert.That(viewState.WorkerPanels.Count, Is.EqualTo(4));
        Assert.That(viewState.WorkerPanels[0].WorkerId, Is.EqualTo(1));
        Assert.That(viewState.WorkerPanels[1].WorkerId, Is.EqualTo(2));
        Assert.That(viewState.WorkerPanels[2].WorkerId, Is.EqualTo(3));
        Assert.That(viewState.WorkerPanels[3].WorkerId, Is.EqualTo(4));
        Assert.That(viewState.WorkerPanels[0].StatusText, Is.EqualTo("待機"));
        Assert.That(viewState.WorkerPanels[3].StatusText, Is.EqualTo("完了"));
        Assert.That(viewState.ThreadText, Is.EqualTo("0 / 4 / 8"));
    }

    [Test]
    public void MarkJobCompleted_完了パネルは次ジョブで再利用し画像を保持する()
    {
        ThumbnailProgressRuntime runtime = new();
        QueueObj firstJob = new()
        {
            MovieFullPath = @"C:\videos\first.mp4",
            Tabindex = 0,
        };
        QueueObj secondJob = new()
        {
            MovieFullPath = @"C:\videos\second.mp4",
            Tabindex = 0,
        };

        runtime.MarkJobStarted(firstJob);
        runtime.MarkThumbnailSaved(firstJob, @"C:\thumb\first.jpg");
        runtime.MarkJobCompleted(firstJob);

        ThumbnailProgressRuntimeSnapshot firstCompleted = runtime.CreateSnapshot();
        long reusedWorkerId = firstCompleted.ActiveWorkers[0].WorkerId;

        runtime.MarkJobStarted(secondJob);
        ThumbnailProgressRuntimeSnapshot secondStarted = runtime.CreateSnapshot();

        Assert.That(secondStarted.ActiveWorkers.Count, Is.EqualTo(1));
        Assert.That(secondStarted.ActiveWorkers[0].WorkerId, Is.EqualTo(reusedWorkerId));
        Assert.That(secondStarted.ActiveWorkers[0].IsActive, Is.True);
        Assert.That(
            secondStarted.ActiveWorkers[0].PreviewImagePath,
            Is.EqualTo(@"C:\thumb\first.jpg")
        );

        runtime.MarkThumbnailSaved(secondJob, @"C:\thumb\second.jpg");
        ThumbnailProgressRuntimeSnapshot secondSaved = runtime.CreateSnapshot();
        Assert.That(
            secondSaved.ActiveWorkers[0].PreviewImagePath,
            Is.EqualTo(@"C:\thumb\second.jpg")
        );
    }

    [Test]
    public void MarkThumbnailSaved_プレビューキーとリビジョンを保持する()
    {
        ThumbnailProgressRuntime runtime = new();
        QueueObj queueObj = new()
        {
            MovieFullPath = @"C:\videos\preview.mp4",
            Tabindex = 4,
        };

        runtime.MarkJobStarted(queueObj);
        runtime.MarkThumbnailSaved(
            queueObj,
            @"C:\thumb\preview.jpg",
            previewCacheKey: "preview-key",
            previewRevision: 42
        );

        ThumbnailProgressRuntimeSnapshot snapshot = runtime.CreateSnapshot();
        Assert.That(snapshot.ActiveWorkers.Count, Is.EqualTo(1));
        Assert.That(snapshot.ActiveWorkers[0].PreviewCacheKey, Is.EqualTo("preview-key"));
        Assert.That(snapshot.ActiveWorkers[0].PreviewRevision, Is.EqualTo(42));
    }

    [Test]
    public void MarkJobStarted_同一動画の連続開始は再代入しない()
    {
        ThumbnailProgressRuntime runtime = new();
        QueueObj queueObj = new()
        {
            MovieFullPath = @"C:\videos\same.mp4",
            Tabindex = 0,
        };

        runtime.MarkJobStarted(queueObj);
        ThumbnailProgressRuntimeSnapshot first = runtime.CreateSnapshot();

        runtime.MarkJobStarted(queueObj);
        ThumbnailProgressRuntimeSnapshot second = runtime.CreateSnapshot();

        Assert.That(ReferenceEquals(first, second), Is.True);
        Assert.That(second.ActiveWorkers.Count, Is.EqualTo(1));
        Assert.That(second.ActiveWorkers[0].DisplayMovieName, Is.EqualTo("same.mp4"));
    }

    [Test]
    public void MarkThumbnailSaved_同一動画の連続完了は再代入しない()
    {
        ThumbnailProgressRuntime runtime = new();
        QueueObj queueObj = new()
        {
            MovieFullPath = @"C:\videos\same.mp4",
            Tabindex = 0,
        };

        runtime.MarkJobStarted(queueObj);
        runtime.MarkThumbnailSaved(
            queueObj,
            @"C:\thumb\first.jpg",
            previewCacheKey: "same-key",
            previewRevision: 1
        );
        ThumbnailProgressRuntimeSnapshot first = runtime.CreateSnapshot();

        runtime.MarkThumbnailSaved(
            queueObj,
            @"C:\thumb\second.jpg",
            previewCacheKey: "same-key",
            previewRevision: 2
        );
        ThumbnailProgressRuntimeSnapshot second = runtime.CreateSnapshot();

        Assert.That(ReferenceEquals(first, second), Is.True);
        Assert.That(second.ActiveWorkers.Count, Is.EqualTo(1));
        Assert.That(second.ActiveWorkers[0].PreviewImagePath, Is.EqualTo(@"C:\thumb\first.jpg"));
        Assert.That(second.ActiveWorkers[0].PreviewRevision, Is.EqualTo(1));
    }

    // 設定変更テストでは分類器キャッシュを落とし、現在値を即時読ませる。
    private static void ResetThumbnailLaneClassifierCache()
    {
        Type classifierType =
            Type.GetType(
                "IndigoMovieManager.Thumbnail.ThumbnailLaneClassifier, IndigoMovieManager.Thumbnail.Queue",
                throwOnError: true
            ) ?? throw new InvalidOperationException("ThumbnailLaneClassifier type not found.");
        BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;

        classifierType.GetField("lastSettingsReadUtcTicks", flags)?.SetValue(null, 0L);
        classifierType.GetField("cachedPriorityLaneMaxMb", flags)?.SetValue(null, 300);
        classifierType.GetField("cachedSlowLaneMinGb", flags)?.SetValue(null, 3);
    }
}
