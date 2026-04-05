using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailCreateEntryCoordinatorTests
{
    [Test]
    public void Args入口_nullはArgumentNullException()
    {
        var coordinator = new ThumbnailCreateEntryCoordinator((_, _) =>
            Task.FromResult(ThumbnailCreateResultFactory.CreateSuccess(@"C:\thumb.jpg", 10))
        );

        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await coordinator.CreateAsync(null!)
        );
    }

    [Test]
    public void Args入口_RequestがnullならArgumentException()
    {
        var coordinator = new ThumbnailCreateEntryCoordinator((_, _) =>
            Task.FromResult(ThumbnailCreateResultFactory.CreateSuccess(@"C:\thumb.jpg", 10))
        );

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await coordinator.CreateAsync(new ThumbnailCreateArgs())
        );

        Assert.That(ex!.ParamName, Is.EqualTo("args"));
    }

    [Test]
    public void Args入口_MovieFullPathが空ならArgumentException()
    {
        var coordinator = new ThumbnailCreateEntryCoordinator((_, _) =>
            Task.FromResult(ThumbnailCreateResultFactory.CreateSuccess(@"C:\thumb.jpg", 10))
        );

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await coordinator.CreateAsync(
                new ThumbnailCreateArgs
                {
                    Request = new ThumbnailRequest { MovieFullPath = "" },
                    DbName = "db",
                    ThumbFolder = @"C:\thumbs",
                }
            )
        );

        Assert.That(ex!.ParamName, Is.EqualTo("args"));
    }

    [Test]
    public async Task Args入口_Request変更は呼び出し元Requestへ反映される()
    {
        ThumbnailCreateWorkflowRequest? capturedRequest = null;
        CancellationToken capturedToken = default;
        var coordinator = new ThumbnailCreateEntryCoordinator(
            (request, cts) =>
            {
                capturedRequest = request;
                capturedToken = cts;
                request.Request.Hash = "mutated";
                request.Request.MovieSizeBytes = 12345;
                return Task.FromResult(
                    ThumbnailCreateResultFactory.CreateSuccess(@"C:\thumb.jpg", 10)
                );
            }
        );

        ThumbnailRequest request = new()
        {
            MovieFullPath = @"C:\movie.mp4",
            Hash = "before",
            MovieSizeBytes = 1,
        };
        using var cts = new CancellationTokenSource();

        ThumbnailCreateResult result = await coordinator.CreateAsync(
            new ThumbnailCreateArgs
            {
                Request = request,
                DbName = "db1",
                ThumbFolder = @"C:\thumbs",
                IsResizeThumb = true,
                IsManual = true,
                SourceMovieFullPathOverride = @"C:\override.mp4",
                InitialEngineHint = "autogen",
                TraceId = "trace-queue",
            },
            cts.Token
        );

        ThumbnailCreateWorkflowRequest actual = capturedRequest!;
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(capturedRequest, Is.Not.Null);
            Assert.That(actual.DbName, Is.EqualTo("db1"));
            Assert.That(actual.ThumbFolder, Is.EqualTo(@"C:\thumbs"));
            Assert.That(actual.IsResizeThumb, Is.True);
            Assert.That(actual.IsManual, Is.True);
            Assert.That(actual.SourceMovieFullPathOverride, Is.EqualTo(@"C:\override.mp4"));
            Assert.That(actual.InitialEngineHint, Is.EqualTo("autogen"));
            Assert.That(actual.TraceId, Is.EqualTo("trace-queue"));
            Assert.That(capturedToken, Is.EqualTo(cts.Token));
            Assert.That(request.Hash, Is.EqualTo("mutated"));
            Assert.That(request.MovieSizeBytes, Is.EqualTo(12345));
        });
    }

    [Test]
    public async Task Args入口_ThumbnailRequestをWorkflowRequestへ詰める()
    {
        ThumbnailCreateWorkflowRequest? capturedRequest = null;
        var thumbInfo = new ThumbInfo();
        var coordinator = new ThumbnailCreateEntryCoordinator(
            (request, _) =>
            {
                capturedRequest = request;
                return Task.FromResult(
                    ThumbnailCreateResultFactory.CreateFailed(@"C:\thumb.jpg", 20, "failed")
                );
            }
        );
        ThumbnailRequest request = new() { MovieFullPath = @"C:\movie.mp4" };

        ThumbnailCreateResult result = await coordinator.CreateAsync(
            new ThumbnailCreateArgs
            {
                Request = request,
                DbName = "db2",
                ThumbFolder = @"C:\thumbs2",
                IsResizeThumb = false,
                IsManual = false,
                SourceMovieFullPathOverride = @"C:\src.mp4",
                InitialEngineHint = "ffmpeg1pass",
                TraceId = "trace-request",
                ThumbInfoOverride = thumbInfo,
            }
        );

        ThumbnailCreateWorkflowRequest actual = capturedRequest!;
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(capturedRequest, Is.Not.Null);
            Assert.That(actual.Request, Is.SameAs(request));
            Assert.That(actual.DbName, Is.EqualTo("db2"));
            Assert.That(actual.ThumbFolder, Is.EqualTo(@"C:\thumbs2"));
            Assert.That(actual.IsResizeThumb, Is.False);
            Assert.That(actual.IsManual, Is.False);
            Assert.That(actual.SourceMovieFullPathOverride, Is.EqualTo(@"C:\src.mp4"));
            Assert.That(actual.InitialEngineHint, Is.EqualTo("ffmpeg1pass"));
            Assert.That(actual.TraceId, Is.EqualTo("trace-request"));
            Assert.That(actual.ThumbInfoOverride, Is.SameAs(thumbInfo));
        });
    }

    [Test]
    public void CompatibilityHelper_legacyQueueObjをRequestへ変換できる()
    {
        QueueObj queueObj = new()
        {
            MovieFullPath = @"C:\legacy.mp4",
            Hash = "before-args",
            MovieSizeBytes = 1,
        };
        var thumbInfo = new ThumbInfo();

        ThumbnailCreateArgs args = ThumbnailCreateArgsCompatibility.FromLegacyQueueObj(
            queueObj,
            dbName: "db-args",
            thumbFolder: @"C:\thumbs-args",
            isResizeThumb: true,
            isManual: true,
            sourceMovieFullPathOverride: @"C:\override-args.mp4",
            initialEngineHint: "autogen",
            traceId: "trace-priority",
            thumbInfoOverride: thumbInfo
        );

        Assert.Multiple(() =>
        {
            Assert.That(args.Request, Is.Not.Null);
            Assert.That(args.Request.MovieFullPath, Is.EqualTo(@"C:\legacy.mp4"));
            Assert.That(args.Request.Hash, Is.EqualTo("before-args"));
            Assert.That(args.DbName, Is.EqualTo("db-args"));
            Assert.That(args.ThumbFolder, Is.EqualTo(@"C:\thumbs-args"));
            Assert.That(args.IsResizeThumb, Is.True);
            Assert.That(args.IsManual, Is.True);
            Assert.That(args.SourceMovieFullPathOverride, Is.EqualTo(@"C:\override-args.mp4"));
            Assert.That(args.InitialEngineHint, Is.EqualTo("autogen"));
            Assert.That(args.TraceId, Is.EqualTo("trace-priority"));
            Assert.That(args.ThumbInfoOverride, Is.SameAs(thumbInfo));
        });
    }

    [Test]
    public void CompatibilityHelper_Request変更をlegacyQueueObjへ戻せる()
    {
        QueueObj queueObj = new()
        {
            MovieFullPath = @"C:\legacy.mp4",
            Hash = "before-args",
            MovieSizeBytes = 1,
        };
        ThumbnailCreateArgs args = ThumbnailCreateArgsCompatibility.FromLegacyQueueObj(
            queueObj,
            dbName: "db-args",
            thumbFolder: @"C:\thumbs-args",
            isResizeThumb: false,
            isManual: false
        );

        args.Request.Hash = "after-args";
        args.Request.MovieSizeBytes = 54321;

        ThumbnailCreateArgsCompatibility.ApplyBackToLegacyQueueObj(args, queueObj);

        Assert.Multiple(() =>
        {
            Assert.That(queueObj.Hash, Is.EqualTo("after-args"));
            Assert.That(queueObj.MovieSizeBytes, Is.EqualTo(54321));
        });
    }
}
