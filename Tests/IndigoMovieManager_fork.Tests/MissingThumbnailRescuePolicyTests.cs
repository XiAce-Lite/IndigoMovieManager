using System.Reflection;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;

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
    public void ShouldUseThumbnailNormalLaneTimeout_手動または明示無効時だけFalseを返す()
    {
        QueueObj normalQueueObj = new()
        {
            MovieFullPath = @"E:\movies\normal.mp4",
            Tabindex = 0,
        };

        Assert.That(
            MainWindow.ShouldUseThumbnailNormalLaneTimeout(normalQueueObj, isManual: false),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldUseThumbnailNormalLaneTimeout(
                normalQueueObj,
                isManual: false,
                disableNormalLaneTimeout: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldUseThumbnailNormalLaneTimeout(normalQueueObj, isManual: true),
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
    public void CanTryThumbnailIndexRepair_救済レーンは対象拡張子なら失敗理由に依存せずprobe可能()
    {
        bool supported = MainWindow.CanTryThumbnailIndexRepair(@"E:\movies\broken.wmv");
        bool unsupported = MainWindow.CanTryThumbnailIndexRepair(@"E:\movies\broken.flv");

        Assert.That(supported, Is.True);
        Assert.That(unsupported, Is.False);
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

    [Test]
    public void CanReflectRescuedThumbnailRecord_出力存在と対応tabならTrueを返す()
    {
        string tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"imm-rescued-sync-{Guid.NewGuid():N}.jpg"
        );

        try
        {
            File.WriteAllText(tempFilePath, "rescued");
            bool result = MainWindow.CanReflectRescuedThumbnailRecord(
                new ThumbnailFailureRecord
                {
                    TabIndex = 2,
                    OutputThumbPath = tempFilePath,
                }
            );

            Assert.That(result, Is.True);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    [Test]
    public void CanReflectRescuedThumbnailRecord_未対応tabや出力欠損ならFalseを返す()
    {
        string tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"imm-rescued-sync-invalid-{Guid.NewGuid():N}.jpg"
        );

        try
        {
            File.WriteAllText(tempFilePath, "rescued");

            bool invalidTab = MainWindow.CanReflectRescuedThumbnailRecord(
                new ThumbnailFailureRecord
                {
                    TabIndex = 88,
                    OutputThumbPath = tempFilePath,
                }
            );
            bool missingFile = MainWindow.CanReflectRescuedThumbnailRecord(
                new ThumbnailFailureRecord
                {
                    TabIndex = 2,
                    OutputThumbPath = tempFilePath + ".missing",
                }
            );

            Assert.That(invalidTab, Is.False);
            Assert.That(missingFile, Is.False);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    [Test]
    public void TryApplyThumbnailPathToMovieRecord_対応tabだけ該当プロパティを書き換える()
    {
        MovieRecords movie = new();

        bool gridUpdated = MainWindow.TryApplyThumbnailPathToMovieRecord(
            movie,
            2,
            @"E:\thumb\grid.#hash.jpg"
        );
        bool unsupportedUpdated = MainWindow.TryApplyThumbnailPathToMovieRecord(
            movie,
            88,
            @"E:\thumb\unsupported.#hash.jpg"
        );

        Assert.That(gridUpdated, Is.True);
        Assert.That(movie.ThumbPathGrid, Is.EqualTo(@"E:\thumb\grid.#hash.jpg"));
        Assert.That(unsupportedUpdated, Is.False);
    }

    [Test]
    public void TryApplyThumbnailPathToMovieRecord_詳細tab99はThumbDetailを書き換える()
    {
        MovieRecords movie = new();

        bool updated = MainWindow.TryApplyThumbnailPathToMovieRecord(
            movie,
            99,
            @"E:\thumb\detail.#hash.jpg"
        );

        Assert.That(updated, Is.True);
        Assert.That(movie.ThumbDetail, Is.EqualTo(@"E:\thumb\detail.#hash.jpg"));
    }

    [Test]
    public void ResolveLane_Phase4でもサイズ分類だけでNormalとSlowを分ける()
    {
        QueueObj normalSizedJob = new()
        {
            MovieFullPath = @"E:\movies\normal.mp4",
            MovieSizeBytes = 100 * 1024 * 1024,
        };
        QueueObj slowSizedJob = new()
        {
            MovieFullPath = @"E:\movies\slow.mp4",
            MovieSizeBytes = 2L * 1024 * 1024 * 1024 * 1024,
        };

        object normalLane = InvokeResolveLane(normalSizedJob);
        object slowLane = InvokeResolveLane(slowSizedJob);

        Assert.That(normalLane.ToString(), Is.EqualTo("Normal"));
        Assert.That(slowLane.ToString(), Is.EqualTo("Slow"));
    }

    private static object InvokeResolveLane(QueueObj queueObj)
    {
        Type classifierType = typeof(QueueDbService).Assembly.GetType(
            "IndigoMovieManager.Thumbnail.ThumbnailLaneClassifier",
            throwOnError: true
        )!;
        MethodInfo method = classifierType.GetMethod(
            "ResolveLane",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(QueueObj)],
            modifiers: null
        )!;

        Assert.That(method, Is.Not.Null);
        return method.Invoke(null, [queueObj])!;
    }
}
