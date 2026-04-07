using IndigoMovieManager.Watcher;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchPathFilterTests
{
    [Test]
    public void ShouldExcludeFromWatchScan_ゴミ箱配下はTrueを返す()
    {
        bool result = WatchPathFilter.ShouldExcludeFromWatchScan(
            @"D:\$RECYCLE.BIN\S-1-5-21-1001\$IEU487G.mp4"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldExcludeFromWatchScan_通常の動画パスはFalseを返す()
    {
        bool result = WatchPathFilter.ShouldExcludeFromWatchScan(
            @"D:\Movies\Anime\sample.mp4"
        );

        Assert.That(result, Is.False);
    }
}
