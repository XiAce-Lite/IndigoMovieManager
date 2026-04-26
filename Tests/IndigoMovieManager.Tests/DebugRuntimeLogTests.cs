using IndigoMovieManager;
using System.Threading.Tasks;

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

    [Test]
    public void BuildLineForTesting_連番付きの既定形式を返す()
    {
        DateTime localNow = new(2026, 4, 16, 12, 34, 56, 789, DateTimeKind.Local);

        string actual = DebugRuntimeLog.BuildLineForTesting(
            localNow,
            "skin-catalog",
            "catalog cache hit",
            12
        );

        Assert.That(
            actual,
            Is.EqualTo("2026-04-16 12:34:56.789 #000012 [skin-catalog] catalog cache hit")
        );
    }

    [Test]
    public void BuildLineForTesting_改行とタブを潰して1行形式を維持する()
    {
        DateTime localNow = new(2026, 4, 16, 12, 34, 56, 789, DateTimeKind.Local);

        string actual = DebugRuntimeLog.BuildLineForTesting(
            localNow,
            " skin-catalog\r\n",
            "\tcache miss\r\nroot='C:\\skin'\nreused=1",
            13
        );

        Assert.That(
            actual,
            Is.EqualTo("2026-04-16 12:34:56.789 #000013 [skin-catalog] cache miss root='C:\\skin' reused=1")
        );
    }

    [Test]
    public void BuildLineForTesting_scope付きでも1行形式を維持する()
    {
        DateTime localNow = new(2026, 4, 16, 12, 34, 56, 789, DateTimeKind.Local);

        string actual = DebugRuntimeLog.BuildLineForTesting(
            localNow,
            "skin-catalog",
            "catalog cache hit",
            14,
            "trace=rq0001\r\nbatch=bt0001"
        );

        Assert.That(
            actual,
            Is.EqualTo("2026-04-16 12:34:56.789 #000014 [skin-catalog] trace=rq0001 batch=bt0001 catalog cache hit")
        );
    }

    [Test]
    public async Task BeginScopeForCurrentAsyncFlow_asyncをまたいでscopeを引き継ぐ()
    {
        Assert.That(DebugRuntimeLog.GetAmbientScopeTextForTesting(), Is.Empty);

        using (DebugRuntimeLog.BeginScopeForCurrentAsyncFlow("trace=rq0001"))
        {
            await Task.Yield();
            Assert.That(DebugRuntimeLog.GetAmbientScopeTextForTesting(), Is.EqualTo("trace=rq0001"));

            using (DebugRuntimeLog.BeginScopeForCurrentAsyncFlow("batch=bt0001"))
            {
                Assert.That(
                    DebugRuntimeLog.GetAmbientScopeTextForTesting(),
                    Is.EqualTo("trace=rq0001 batch=bt0001")
                );
            }

            Assert.That(DebugRuntimeLog.GetAmbientScopeTextForTesting(), Is.EqualTo("trace=rq0001"));
        }

        Assert.That(DebugRuntimeLog.GetAmbientScopeTextForTesting(), Is.Empty);
    }

    [Test]
    public async Task BeginScopeForCurrentAsyncFlow_並列タスクでtraceが混線しない()
    {
        static async Task<string> CaptureScopeAsync(string traceText)
        {
            using (DebugRuntimeLog.BeginScopeForCurrentAsyncFlow(traceText))
            {
                await Task.Yield();
                return DebugRuntimeLog.GetAmbientScopeTextForTesting();
            }
        }

        string[] actual = await Task.WhenAll(
            CaptureScopeAsync("trace=rq0001"),
            CaptureScopeAsync("trace=rq0002")
        );

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EquivalentTo(new[] { "trace=rq0001", "trace=rq0002" }));
            Assert.That(DebugRuntimeLog.GetAmbientScopeTextForTesting(), Is.Empty);
        });
    }

    [Test]
    public async Task BeginScopeForCurrentAsyncFlow_catalogとpersistの要約件数を保持できる()
    {
        using (DebugRuntimeLog.BeginScopeForCurrentAsyncFlow("trace=rq0003"))
        {
            DebugRuntimeLog.RecordCatalogCacheHit();
            DebugRuntimeLog.RecordCatalogCacheMiss();
            DebugRuntimeLog.RecordCatalogLoadCore(reusedCount: 2, skippedCount: 1);
            DebugRuntimeLog.RecordCatalogSignatureElapsed(12.34);
            DebugRuntimeLog.RecordCatalogLoadElapsed(4.56);
            DebugRuntimeLog.RecordCatalogSignatureElapsed(1.16);
            DebugRuntimeLog.RecordCatalogLoadElapsed(0.44);
            await Task.Yield();
            DebugRuntimeLog.RecordSkinDbPersistQueued();
            DebugRuntimeLog.RecordSkinDbPersistFallbackApplied();

            Assert.That(
                DebugRuntimeLog.BuildCurrentScopeMetricSummary(),
                Is.EqualTo("catalog_hit=1 catalog_miss=1 persist_enqueued=1 persist_fallback_applied=1 catalog_reused=2 catalog_skipped=1 catalog_signature_ms=13.5 catalog_load_ms=5.0")
            );
        }

        Assert.That(DebugRuntimeLog.BuildCurrentScopeMetricSummary(), Is.Empty);
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
