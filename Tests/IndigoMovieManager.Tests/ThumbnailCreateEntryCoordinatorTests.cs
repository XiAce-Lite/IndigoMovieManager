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
    public void Args入口_QueueObjとRequestが両方nullならArgumentException()
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
    public async Task Args入口_QueueObj変更をlegacyFacadeへ戻せる()
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

        QueueObj queueObj = new()
        {
            MovieFullPath = @"C:\movie.mp4",
            Hash = "before",
            MovieSizeBytes = 1,
        };
        using var cts = new CancellationTokenSource();

        ThumbnailCreateResult result = await coordinator.CreateAsync(
            new ThumbnailCreateArgs
            {
                QueueObj = queueObj,
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
            Assert.That(queueObj.Hash, Is.EqualTo("mutated"));
            Assert.That(queueObj.MovieSizeBytes, Is.EqualTo(12345));
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
    public async Task Args入口_Request優先でlegacyFacadeへも戻せる()
    {
        ThumbnailCreateWorkflowRequest? capturedRequest = null;
        var coordinator = new ThumbnailCreateEntryCoordinator(
            (request, _) =>
            {
                capturedRequest = request;
                request.Request.Hash = "after-args";
                request.Request.MovieSizeBytes = 54321;
                return Task.FromResult(
                    ThumbnailCreateResultFactory.CreateSuccess(@"C:\thumb-args.jpg", 30)
                );
            }
        );
        QueueObj queueObj = new()
        {
            MovieFullPath = @"C:\legacy.mp4",
            Hash = "before-args",
            MovieSizeBytes = 1,
        };
        ThumbnailRequest request = new() { MovieFullPath = @"C:\request.mp4" };
        var thumbInfo = new ThumbInfo();

        ThumbnailCreateResult result = await coordinator.CreateAsync(
            new ThumbnailCreateArgs
            {
                QueueObj = queueObj,
                Request = request,
                DbName = "db-args",
                ThumbFolder = @"C:\thumbs-args",
                IsResizeThumb = true,
                IsManual = true,
                SourceMovieFullPathOverride = @"C:\override-args.mp4",
                InitialEngineHint = "autogen",
                TraceId = "trace-priority",
                ThumbInfoOverride = thumbInfo,
            }
        );

        ThumbnailCreateWorkflowRequest actual = capturedRequest!;
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(actual.Request, Is.SameAs(request));
            Assert.That(actual.DbName, Is.EqualTo("db-args"));
            Assert.That(actual.ThumbFolder, Is.EqualTo(@"C:\thumbs-args"));
            Assert.That(actual.IsResizeThumb, Is.True);
            Assert.That(actual.IsManual, Is.True);
            Assert.That(actual.SourceMovieFullPathOverride, Is.EqualTo(@"C:\override-args.mp4"));
            Assert.That(actual.InitialEngineHint, Is.EqualTo("autogen"));
            Assert.That(actual.TraceId, Is.EqualTo("trace-priority"));
            Assert.That(actual.ThumbInfoOverride, Is.SameAs(thumbInfo));
            Assert.That(queueObj.Hash, Is.EqualTo("after-args"));
            Assert.That(queueObj.MovieSizeBytes, Is.EqualTo(54321));
        });
    }
}
