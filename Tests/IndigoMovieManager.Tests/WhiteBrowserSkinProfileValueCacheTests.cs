using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinProfileValueCacheTests
{
    [SetUp]
    public void SetUp()
    {
        WhiteBrowserSkinProfileValueCache.ClearForTesting();
    }

    [Test]
    public void Pending値はApiから見えるがRestoreからは見えない()
    {
        WhiteBrowserSkinProfileValueCache.RecordPending(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            "DefaultList"
        );

        bool apiHit = WhiteBrowserSkinProfileValueCache.TryGetApiVisibleValue(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            out string apiValue
        );
        bool restoreHit = WhiteBrowserSkinProfileValueCache.TryGetPersistedValue(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            out string restoreValue
        );

        Assert.Multiple(() =>
        {
            Assert.That(apiHit, Is.True);
            Assert.That(apiValue, Is.EqualTo("DefaultList"));
            Assert.That(restoreHit, Is.False);
            Assert.That(restoreValue, Is.Empty);
        });
    }

    [Test]
    public void Persist成功後はRestoreからも見える()
    {
        WhiteBrowserSkinProfileValueCache.RecordPending(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            "DefaultList"
        );
        WhiteBrowserSkinProfileValueCache.RecordPersisted(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            "DefaultList"
        );

        bool restoreHit = WhiteBrowserSkinProfileValueCache.TryGetPersistedValue(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            out string restoreValue
        );

        Assert.That(restoreHit, Is.True);
        Assert.That(restoreValue, Is.EqualTo("DefaultList"));
    }

    [Test]
    public void Fault後はCacheを使わない()
    {
        WhiteBrowserSkinProfileValueCache.RecordPending(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "grid.columns",
            "4"
        );
        WhiteBrowserSkinProfileValueCache.RecordFault(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "grid.columns"
        );

        bool apiHit = WhiteBrowserSkinProfileValueCache.TryGetApiVisibleValue(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "grid.columns",
            out _
        );
        bool restoreHit = WhiteBrowserSkinProfileValueCache.TryGetPersistedValue(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "grid.columns",
            out _
        );

        Assert.That(apiHit, Is.False);
        Assert.That(restoreHit, Is.False);
    }
}
