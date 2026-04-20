namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowFilterSortExecutionPolicyTests
{
    [TestCase(false, false, false, "full-reload")]
    [TestCase(false, true, false, "query-only")]
    [TestCase(true, false, false, "query-only")]
    [TestCase(true, true, false, "query-only")]
    [TestCase(false, false, true, "full-reload")]
    [TestCase(true, true, true, "full-reload")]
    public void ResolveFilterSortExecutionRouteLabel_経路を短い札で返せる(
        bool hasSnapshotData,
        bool startupFeedLoadedAllPages,
        bool isGetNew,
        string expected
    )
    {
        string actual = MainWindow.ResolveFilterSortExecutionRouteLabel(
            hasSnapshotData,
            startupFeedLoadedAllPages,
            isGetNew
        );

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(63, false)]
    [TestCase(64, true)]
    [TestCase(120, true)]
    public void ShouldRunFilterSortOnBackground_件数閾値で実行方式を切り替えられる(
        int sourceCount,
        bool expected
    )
    {
        bool actual = MainWindow.ShouldRunFilterSortOnBackground(sourceCount);
        Assert.That(actual, Is.EqualTo(expected));
    }
}
