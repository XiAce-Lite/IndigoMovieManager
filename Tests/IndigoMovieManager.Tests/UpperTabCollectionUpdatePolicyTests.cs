using IndigoMovieManager.UpperTabs.Common;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabCollectionUpdatePolicyTests
{
    [Test]
    public void ListタブのDiff更新はRefresh不要()
    {
        bool shouldRefresh = UpperTabCollectionUpdatePolicy.ShouldRefreshAfterCollectionApply(
            tabIndex: 3,
            updateMode: FilteredMovieRecsUpdateMode.Diff
        );

        Assert.That(shouldRefresh, Is.False);
    }

    [Test]
    public void ListタブのMove更新はRefresh不要()
    {
        bool shouldRefresh = UpperTabCollectionUpdatePolicy.ShouldRefreshAfterCollectionApply(
            tabIndex: 3,
            updateMode: FilteredMovieRecsUpdateMode.Move
        );

        Assert.That(shouldRefresh, Is.False);
    }

    [Test]
    public void ListタブでもReset更新はRefreshを維持する()
    {
        bool shouldRefresh = UpperTabCollectionUpdatePolicy.ShouldRefreshAfterCollectionApply(
            tabIndex: 3,
            updateMode: FilteredMovieRecsUpdateMode.Reset
        );

        Assert.That(shouldRefresh, Is.True);
    }

    [Test]
    public void List以外はDiff更新でもRefreshを維持する()
    {
        bool shouldRefresh = UpperTabCollectionUpdatePolicy.ShouldRefreshAfterCollectionApply(
            tabIndex: 2,
            updateMode: FilteredMovieRecsUpdateMode.Diff
        );

        Assert.That(shouldRefresh, Is.True);
    }

    [Test]
    public void プレーヤータブのDiff更新はRefresh不要()
    {
        bool shouldRefresh = UpperTabCollectionUpdatePolicy.ShouldRefreshAfterCollectionApply(
            tabIndex: 7,
            updateMode: FilteredMovieRecsUpdateMode.Diff
        );

        Assert.That(shouldRefresh, Is.False);
    }

    [Test]
    public void プレーヤータブのMove更新はRefresh不要()
    {
        bool shouldRefresh = UpperTabCollectionUpdatePolicy.ShouldRefreshAfterCollectionApply(
            tabIndex: 7,
            updateMode: FilteredMovieRecsUpdateMode.Move
        );

        Assert.That(shouldRefresh, Is.False);
    }

    [Test]
    public void プレーヤータブのFilter更新はDiffを選ぶ()
    {
        FilteredMovieRecsUpdateMode updateMode =
            UpperTabCollectionUpdatePolicy.ResolveUpdateMode(tabIndex: 7, isSortOnly: false);

        Assert.That(updateMode, Is.EqualTo(FilteredMovieRecsUpdateMode.Diff));
    }

    [Test]
    public void プレーヤータブのSort更新はMoveを選ぶ()
    {
        FilteredMovieRecsUpdateMode updateMode =
            UpperTabCollectionUpdatePolicy.ResolveUpdateMode(tabIndex: 7, isSortOnly: true);

        Assert.That(updateMode, Is.EqualTo(FilteredMovieRecsUpdateMode.Move));
    }
}
