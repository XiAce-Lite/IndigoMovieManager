namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabViewportRefreshTests
{
    [Test]
    public void 抑止期限内ならfollowup_scroll_refreshを止める()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        long suppressUntilUtcTicks = nowUtcTicks + TimeSpan.FromMilliseconds(50).Ticks;

        bool actual = IndigoMovieManager.MainWindow.ShouldSuppressUpperTabFollowupScrollRefresh(
            nowUtcTicks,
            suppressUntilUtcTicks
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void 抑止期限切れならfollowup_scroll_refreshを止めない()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        long suppressUntilUtcTicks = nowUtcTicks - 1;

        bool actual = IndigoMovieManager.MainWindow.ShouldSuppressUpperTabFollowupScrollRefresh(
            nowUtcTicks,
            suppressUntilUtcTicks
        );

        Assert.That(actual, Is.False);
    }
}
