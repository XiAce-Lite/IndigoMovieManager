using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailQueueTabGatePolicyTests
{
    [Test]
    public void ShouldAcceptThumbnailQueueRequest_現在タブと一致する通常タブは通す()
    {
        QueueObj queueObj = new()
        {
            MovieFullPath = @"E:\movies\movie.mp4",
            Tabindex = 2,
        };

        bool result = MainWindow.ShouldAcceptThumbnailQueueRequest(
            queueObj,
            currentTabIndex: 2
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldAcceptThumbnailQueueRequest_現在タブと不一致の通常タブは弾く()
    {
        QueueObj queueObj = new()
        {
            MovieFullPath = @"E:\movies\movie.mp4",
            Tabindex = 2,
        };

        bool result = MainWindow.ShouldAcceptThumbnailQueueRequest(
            queueObj,
            currentTabIndex: 5
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldAcceptThumbnailQueueRequest_救済タブからの明示再試行だけはbypassで通す()
    {
        QueueObj queueObj = new()
        {
            MovieFullPath = @"E:\movies\movie.mp4",
            Tabindex = 2,
        };

        bool result = MainWindow.ShouldAcceptThumbnailQueueRequest(
            queueObj,
            currentTabIndex: 5,
            bypassTabGate: true
        );

        Assert.That(result, Is.True);
    }
}
