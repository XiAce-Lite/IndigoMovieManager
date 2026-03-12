using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class MissingThumbnailRescuePolicyTests
{
    [Test]
    public void ShouldSkipMissingThumbnailRescueForBusyQueue_Watch高負荷時は抑止する()
    {
        bool result = MainWindow.ShouldSkipMissingThumbnailRescueForBusyQueue(
            isManualRequest: false,
            activeCount: 14,
            busyThreshold: 14
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldSkipMissingThumbnailRescueForBusyQueue_Manual高負荷時でも抑止しない()
    {
        bool result = MainWindow.ShouldSkipMissingThumbnailRescueForBusyQueue(
            isManualRequest: true,
            activeCount: 14,
            busyThreshold: 14
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldUseThumbnailNormalLaneTimeout_通常キューだけTrueを返す()
    {
        QueueObj normalQueueObj = new()
        {
            MovieFullPath = @"E:\movies\normal.mp4",
            Tabindex = 0,
        };
        QueueObj rescueQueueObj = new()
        {
            MovieFullPath = @"E:\movies\rescue.mp4",
            Tabindex = 0,
            IsRescueRequest = true,
        };

        Assert.That(MainWindow.ShouldUseThumbnailNormalLaneTimeout(normalQueueObj, isManual: false), Is.True);
        Assert.That(MainWindow.ShouldUseThumbnailNormalLaneTimeout(rescueQueueObj, isManual: false), Is.False);
        Assert.That(MainWindow.ShouldUseThumbnailNormalLaneTimeout(normalQueueObj, isManual: true), Is.False);
    }

    [Test]
    public void ShouldPromoteThumbnailFailureToRescueLane_通常失敗だけTrueを返す()
    {
        QueueObj normalQueueObj = new()
        {
            MovieFullPath = @"E:\movies\normal.mp4",
            Tabindex = 0,
        };
        QueueObj rescueQueueObj = new()
        {
            MovieFullPath = @"E:\movies\rescue.mp4",
            Tabindex = 0,
            IsRescueRequest = true,
        };

        Assert.That(
            MainWindow.ShouldPromoteThumbnailFailureToRescueLane(normalQueueObj, isManual: false),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldPromoteThumbnailFailureToRescueLane(rescueQueueObj, isManual: false),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldPromoteThumbnailFailureToRescueLane(normalQueueObj, isManual: true),
            Is.False
        );
    }

    [Test]
    public void ShouldTryThumbnailIndexRepair_対象拡張子かつ失敗文言でTrueを返す()
    {
        bool result = MainWindow.ShouldTryThumbnailIndexRepair(
            @"E:\movies\broken.wmv",
            "No frames decoded"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldTryThumbnailIndexRepair_対象外拡張子や通常失敗ではFalseを返す()
    {
        bool wrongExtension = MainWindow.ShouldTryThumbnailIndexRepair(
            @"E:\movies\normal.flv",
            "No frames decoded"
        );
        bool wrongReason = MainWindow.ShouldTryThumbnailIndexRepair(
            @"E:\movies\normal.mp4",
            "manual target thumbnail does not exist"
        );

        Assert.That(wrongExtension, Is.False);
        Assert.That(wrongReason, Is.False);
    }

    [Test]
    public void ShouldTryThumbnailIndexRepair_大文字小文字混在でも条件一致ならTrueを返す()
    {
        bool result = MainWindow.ShouldTryThumbnailIndexRepair(
            @"E:\movies\BROKEN.WMV",
            "AVFormat_Open_Input Failed"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsThumbnailErrorPlaceholderPath_組み込みerror画像だけTrueを返す()
    {
        Assert.That(MainWindow.IsThumbnailErrorPlaceholderPath(@"C:\app\Images\errorGrid.jpg"), Is.True);
        Assert.That(MainWindow.IsThumbnailErrorPlaceholderPath(@"C:\app\Images\ERRORBIG.JPG"), Is.True);
        Assert.That(MainWindow.IsThumbnailErrorPlaceholderPath(@"C:\videos\my_error_movie.jpg"), Is.False);
        Assert.That(MainWindow.IsThumbnailErrorPlaceholderPath(@"C:\thumb\movie.#ERROR.jpg"), Is.False);
        Assert.That(MainWindow.IsThumbnailErrorPlaceholderPath(""), Is.False);
    }
}
