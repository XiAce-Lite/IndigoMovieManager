using IndigoMovieManager.Watcher;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class FileIndexIncrementalSyncPolicyTests
{
    [Test]
    public void ShouldIncludeItem_EqualTimestampはFalseを返す()
    {
        DateTime baselineUtc = new(2026, 3, 18, 5, 34, 0, DateTimeKind.Utc);

        bool result = FileIndexIncrementalSyncPolicy.ShouldIncludeItem(
            baselineUtc,
            baselineUtc
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldIncludeItem_NewerTimestampはTrueを返す()
    {
        DateTime baselineUtc = new(2026, 3, 18, 5, 34, 0, DateTimeKind.Utc);
        DateTime newerUtc = baselineUtc.AddMilliseconds(1);

        bool result = FileIndexIncrementalSyncPolicy.ShouldIncludeItem(newerUtc, baselineUtc);

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldAdvanceCursor_Equalは進めずNewerだけ進める()
    {
        DateTime baselineUtc = new(2026, 3, 18, 5, 34, 0, DateTimeKind.Utc);

        bool equalResult = FileIndexIncrementalSyncPolicy.ShouldAdvanceCursor(
            baselineUtc,
            baselineUtc
        );
        bool newerResult = FileIndexIncrementalSyncPolicy.ShouldAdvanceCursor(
            baselineUtc.AddMilliseconds(1),
            baselineUtc
        );

        Assert.That(equalResult, Is.False);
        Assert.That(newerResult, Is.True);
    }
}
