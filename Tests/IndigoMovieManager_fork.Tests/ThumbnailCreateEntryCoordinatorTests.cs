using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailCreateEntryCoordinatorTests
{
    [Test]
    public async Task QueueObj入口_Request変更をlegacyFacadeへ戻せる()
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
            queueObj,
            "db1",
            @"C:\thumbs",
            true,
            isManual: true,
            cts: cts.Token,
            sourceMovieFullPathOverride: @"C:\override.mp4",
            initialEngineHint: "autogen"
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
            Assert.That(capturedToken, Is.EqualTo(cts.Token));
            Assert.That(queueObj.Hash, Is.EqualTo("mutated"));
            Assert.That(queueObj.MovieSizeBytes, Is.EqualTo(12345));
        });
    }

    [Test]
    public async Task ThumbnailRequest入口_WorkflowRequestへ必要情報を詰める()
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
            request,
            "db2",
            @"C:\thumbs2",
            false,
            isManual: false,
            sourceMovieFullPathOverride: @"C:\src.mp4",
            initialEngineHint: "ffmpeg1pass",
            thumbInfoOverride: thumbInfo
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
            Assert.That(actual.ThumbInfoOverride, Is.SameAs(thumbInfo));
            Assert.That(queueObj.Hash, Is.EqualTo("after-args"));
            Assert.That(queueObj.MovieSizeBytes, Is.EqualTo(54321));
        });
    }
}
