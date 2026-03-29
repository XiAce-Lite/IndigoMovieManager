using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogTests
{
    [SetUp]
    public void SetUp()
    {
        DebugRuntimeLog.ResetThrottleStateForTests();
    }

    [Test]
    public void ShouldWriteForCurrentProcess_既存DB補修ログは短時間連打を抑止する()
    {
        DateTime baseUtc = new(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc);

        bool first = DebugRuntimeLog.ShouldWriteForCurrentProcess(
            "watch-check",
            "repair view by existing-db-movie: tab=2, movie='E:\\a.mp4'",
            baseUtc
        );
        bool second = DebugRuntimeLog.ShouldWriteForCurrentProcess(
            "watch-check",
            "repair view by existing-db-movie: tab=2, movie='E:\\b.mp4'",
            baseUtc.AddMilliseconds(400)
        );
        bool third = DebugRuntimeLog.ShouldWriteForCurrentProcess(
            "watch-check",
            "repair view by existing-db-movie: tab=2, movie='E:\\c.mp4'",
            baseUtc.AddMilliseconds(1600)
        );

        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
        Assert.That(third, Is.True);
    }

    [Test]
    public void ShouldWriteForCurrentProcess_補修ログと再描画ログは別バケットで扱う()
    {
        DateTime baseUtc = new(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc);

        bool repair = DebugRuntimeLog.ShouldWriteForCurrentProcess(
            "watch-check",
            "repair view by existing-db-movie: tab=2, movie='E:\\a.mp4'",
            baseUtc
        );
        bool refresh = DebugRuntimeLog.ShouldWriteForCurrentProcess(
            "watch-check",
            "refresh filtered-view by existing-db-movie: tab=2, movie='E:\\a.mp4'",
            baseUtc.AddMilliseconds(200)
        );

        Assert.That(repair, Is.True);
        Assert.That(refresh, Is.True);
    }
}
