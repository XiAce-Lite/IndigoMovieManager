using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class QueueObjContractTests
{
    [Test]
    public void 既定値_後方互換の初期値を維持する()
    {
        QueueObj queueObj = new();

        Assert.Multiple(() =>
        {
            Assert.That(queueObj.Tabindex, Is.EqualTo(0));
            Assert.That(queueObj.MovieId, Is.EqualTo(0L));
            Assert.That(queueObj.MovieFullPath, Is.Null);
            Assert.That(queueObj.Hash, Is.EqualTo(""));
            Assert.That(queueObj.MovieSizeBytes, Is.EqualTo(0L));
            Assert.That(queueObj.ThumbPanelPos, Is.Null);
            Assert.That(queueObj.ThumbTimePos, Is.Null);
            Assert.That(queueObj.Priority, Is.EqualTo(ThumbnailQueuePriority.Normal));
        });
    }

    [Test]
    public void Hash_null代入時は空文字へ正規化する()
    {
        QueueObj queueObj = new();

        queueObj.Hash = null!;

        Assert.That(queueObj.Hash, Is.EqualTo(""));
    }

    [Test]
    public void Priority_未知値代入時はNormalへ丸める()
    {
        QueueObj queueObj = new();

        queueObj.Priority = (ThumbnailQueuePriority)99;

        Assert.Multiple(() =>
        {
            Assert.That(queueObj.Priority, Is.EqualTo(ThumbnailQueuePriority.Normal));
            Assert.That(ThumbnailQueuePriorityHelper.IsPreferred(queueObj.Priority), Is.False);
        });
    }

    [Test]
    public void Priority_Preferred代入時は優先扱いを維持する()
    {
        QueueObj queueObj = new();

        queueObj.Priority = ThumbnailQueuePriority.Preferred;

        Assert.Multiple(() =>
        {
            Assert.That(queueObj.Priority, Is.EqualTo(ThumbnailQueuePriority.Preferred));
            Assert.That(ThumbnailQueuePriorityHelper.IsPreferred(queueObj.Priority), Is.True);
        });
    }

    [Test]
    public void ToThumbnailRequest_legacy値を新契約へ写せる()
    {
        QueueObj queueObj = new()
        {
            Tabindex = 3,
            MovieId = 42,
            MovieFullPath = @"C:\test\movie.mp4",
            Hash = "abc123",
            MovieSizeBytes = 987654321,
            ThumbPanelPos = 5,
            ThumbTimePos = 120,
            Priority = ThumbnailQueuePriority.Preferred,
        };

        ThumbnailRequest request = queueObj.ToThumbnailRequest();
        queueObj.Hash = "changed";

        Assert.Multiple(() =>
        {
            Assert.That(request.TabIndex, Is.EqualTo(3));
            Assert.That(request.MovieId, Is.EqualTo(42L));
            Assert.That(request.MovieFullPath, Is.EqualTo(@"C:\test\movie.mp4"));
            Assert.That(request.Hash, Is.EqualTo("abc123"));
            Assert.That(request.MovieSizeBytes, Is.EqualTo(987654321L));
            Assert.That(request.ThumbPanelPosition, Is.EqualTo(5));
            Assert.That(request.ThumbTimePosition, Is.EqualTo(120));
            Assert.That(request.Priority, Is.EqualTo(ThumbnailQueuePriority.Preferred));
        });
    }

    [Test]
    public void FromThumbnailRequest_legacyFacadeへ戻せる()
    {
        ThumbnailRequest request = new()
        {
            TabIndex = 7,
            MovieId = 99,
            MovieFullPath = @"C:\test\manual.mp4",
            Hash = "xyz789",
            MovieSizeBytes = 2468,
            ThumbPanelPosition = 2,
            ThumbTimePosition = 33,
            Priority = ThumbnailQueuePriority.Preferred,
        };

        QueueObj queueObj = QueueObj.FromThumbnailRequest(request);

        Assert.Multiple(() =>
        {
            Assert.That(queueObj.Tabindex, Is.EqualTo(7));
            Assert.That(queueObj.MovieId, Is.EqualTo(99L));
            Assert.That(queueObj.MovieFullPath, Is.EqualTo(@"C:\test\manual.mp4"));
            Assert.That(queueObj.Hash, Is.EqualTo("xyz789"));
            Assert.That(queueObj.MovieSizeBytes, Is.EqualTo(2468L));
            Assert.That(queueObj.ThumbPanelPos, Is.EqualTo(2));
            Assert.That(queueObj.ThumbTimePos, Is.EqualTo(33));
            Assert.That(queueObj.Priority, Is.EqualTo(ThumbnailQueuePriority.Preferred));
        });
    }

    [Test]
    public void ThumbnailJobContext_Request指定時もlegacyQueueObj互換を返す()
    {
        ThumbnailRequest request = new()
        {
            TabIndex = 8,
            MovieId = 123,
            MovieFullPath = @"C:\test\context.mp4",
            Hash = "ctx",
            MovieSizeBytes = 555,
            ThumbPanelPosition = 6,
            ThumbTimePosition = 44,
            Priority = ThumbnailQueuePriority.Preferred,
        };

        ThumbnailJobContext context = new() { Request = request };

        Assert.Multiple(() =>
        {
            Assert.That(context.Request.TabIndex, Is.EqualTo(8));
            Assert.That(context.QueueObj.Tabindex, Is.EqualTo(8));
            Assert.That(context.QueueObj.MovieId, Is.EqualTo(123L));
            Assert.That(context.QueueObj.MovieFullPath, Is.EqualTo(@"C:\test\context.mp4"));
            Assert.That(context.QueueObj.Hash, Is.EqualTo("ctx"));
            Assert.That(context.QueueObj.MovieSizeBytes, Is.EqualTo(555L));
            Assert.That(context.QueueObj.ThumbPanelPos, Is.EqualTo(6));
            Assert.That(context.QueueObj.ThumbTimePos, Is.EqualTo(44));
            Assert.That(context.QueueObj.Priority, Is.EqualTo(ThumbnailQueuePriority.Preferred));
        });
    }
}
