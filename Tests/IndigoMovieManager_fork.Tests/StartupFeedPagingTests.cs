namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class StartupFeedPagingTests
{
    [Test]
    public void 起動partialで末尾近傍なら追加読込を要求する()
    {
        bool shouldAppend = IndigoMovieManager.MainWindow.ShouldRequestStartupAppend(
            isStartupFeedPartialActive: true,
            hasMorePages: true,
            appendInFlight: false,
            loadedCount: 200,
            hasVisibleItems: true,
            lastNearVisibleIndex: 160,
            nearEndThreshold: 72
        );

        Assert.That(shouldAppend, Is.True);
    }

    [Test]
    public void まだ末尾から遠いなら追加読込しない()
    {
        bool shouldAppend = IndigoMovieManager.MainWindow.ShouldRequestStartupAppend(
            isStartupFeedPartialActive: true,
            hasMorePages: true,
            appendInFlight: false,
            loadedCount: 200,
            hasVisibleItems: true,
            lastNearVisibleIndex: 80,
            nearEndThreshold: 72
        );

        Assert.That(shouldAppend, Is.False);
    }

    [Test]
    public void 追加読込中は重複要求しない()
    {
        bool shouldAppend = IndigoMovieManager.MainWindow.ShouldRequestStartupAppend(
            isStartupFeedPartialActive: true,
            hasMorePages: true,
            appendInFlight: true,
            loadedCount: 200,
            hasVisibleItems: true,
            lastNearVisibleIndex: 199,
            nearEndThreshold: 72
        );

        Assert.That(shouldAppend, Is.False);
    }

    [Test]
    public void partial終了後は追加読込しない()
    {
        bool shouldAppend = IndigoMovieManager.MainWindow.ShouldRequestStartupAppend(
            isStartupFeedPartialActive: false,
            hasMorePages: true,
            appendInFlight: false,
            loadedCount: 200,
            hasVisibleItems: true,
            lastNearVisibleIndex: 199,
            nearEndThreshold: 72
        );

        Assert.That(shouldAppend, Is.False);
    }

    [Test]
    public void ページ送り直後はstartupAppendを短時間だけ寝かせる()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        bool isSuppressed = IndigoMovieManager.MainWindow.TryGetStartupAppendRetryDelayMs(
            nowUtcTicks,
            nowUtcTicks + TimeSpan.FromMilliseconds(80).Ticks,
            out int retryDelayMs
        );

        Assert.That(isSuppressed, Is.True);
        Assert.That(retryDelayMs, Is.InRange(1, 80));
    }

    [Test]
    public void 抑制期限を過ぎたstartupAppendはそのまま流す()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        bool isSuppressed = IndigoMovieManager.MainWindow.TryGetStartupAppendRetryDelayMs(
            nowUtcTicks,
            nowUtcTicks - TimeSpan.FromMilliseconds(1).Ticks,
            out int retryDelayMs
        );

        Assert.That(isSuppressed, Is.False);
        Assert.That(retryDelayMs, Is.Zero);
    }
}
