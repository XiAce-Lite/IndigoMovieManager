using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugRuntimeLogTests
{
    [SetUp]
    public void SetUp()
    {
        DebugRuntimeLog.ResetThrottleStateForTests();
        RestoreLogSwitches(
            watch: true,
            queue: true,
            thumbnail: true,
            ui: true,
            skin: true,
            debug: true,
            database: true,
            other: true
        );
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

    [Test]
    public void ShouldWriteForCurrentProcess_キューログをOFFにするとqueue系は抑止する()
    {
        DateTime baseUtc = new(2026, 4, 11, 0, 0, 0, DateTimeKind.Utc);
        IndigoMovieManager.Properties.Settings.Default.DebugLogQueueEnabled = false;

        bool actual = DebugRuntimeLog.ShouldWriteForCurrentProcess(
            "queue-db",
            "channel write failed: path='E:\\a.mp4'",
            baseUtc
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldWriteForCurrentProcess_WatcherだけOFFでもThumbnail系は流れる()
    {
        DateTime baseUtc = new(2026, 4, 11, 0, 0, 0, DateTimeKind.Utc);
        IndigoMovieManager.Properties.Settings.Default.DebugLogWatchEnabled = false;

        bool watch = DebugRuntimeLog.ShouldWriteForCurrentProcess(
            "watch-check",
            "watch ui suppression begin: reason=test",
            baseUtc
        );
        bool thumbnail = DebugRuntimeLog.ShouldWriteForCurrentProcess(
            "thumbnail-progress",
            "ui update succeeded",
            baseUtc
        );

        Assert.That(watch, Is.False);
        Assert.That(thumbnail, Is.True);
    }

    private static void RestoreLogSwitches(
        bool watch,
        bool queue,
        bool thumbnail,
        bool ui,
        bool skin,
        bool debug,
        bool database,
        bool other
    )
    {
        IndigoMovieManager.Properties.Settings.Default.DebugLogWatchEnabled = watch;
        IndigoMovieManager.Properties.Settings.Default.DebugLogQueueEnabled = queue;
        IndigoMovieManager.Properties.Settings.Default.DebugLogThumbnailEnabled = thumbnail;
        IndigoMovieManager.Properties.Settings.Default.DebugLogUiEnabled = ui;
        IndigoMovieManager.Properties.Settings.Default.DebugLogSkinEnabled = skin;
        IndigoMovieManager.Properties.Settings.Default.DebugLogDebugToolEnabled = debug;
        IndigoMovieManager.Properties.Settings.Default.DebugLogDatabaseEnabled = database;
        IndigoMovieManager.Properties.Settings.Default.DebugLogOtherEnabled = other;
    }
}
