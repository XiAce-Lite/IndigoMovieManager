using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailFailureSyncUiTests
{
    [Test]
    public void ApplyThumbnailPathWithForcedRebind_同一パスなら空経由で再通知する()
    {
        MovieRecords item = new()
        {
            ThumbPathGrid = @"C:\thumb\grid.#hash.jpg",
        };
        int changedCount = 0;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MovieRecords.ThumbPathGrid))
            {
                changedCount++;
            }
        };

        bool applied = MainWindow.TryApplyThumbnailPathToMovieRecord(
            item,
            2,
            @"C:\thumb\grid.#hash.jpg"
        );

        Assert.That(applied, Is.True);
        Assert.That(item.ThumbPathGrid, Is.EqualTo(@"C:\thumb\grid.#hash.jpg"));
        Assert.That(changedCount, Is.EqualTo(2));
    }

    [Test]
    public void ApplyThumbnailPathWithForcedRebind_別パスなら通常の一回通知で更新する()
    {
        MovieRecords item = new()
        {
            ThumbPathGrid = @"C:\thumb\old.#hash.jpg",
        };
        int changedCount = 0;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MovieRecords.ThumbPathGrid))
            {
                changedCount++;
            }
        };

        bool applied = MainWindow.TryApplyThumbnailPathToMovieRecord(
            item,
            2,
            @"C:\thumb\new.#hash.jpg"
        );

        Assert.That(applied, Is.True);
        Assert.That(item.ThumbPathGrid, Is.EqualTo(@"C:\thumb\new.#hash.jpg"));
        Assert.That(changedCount, Is.EqualTo(1));
    }
}
