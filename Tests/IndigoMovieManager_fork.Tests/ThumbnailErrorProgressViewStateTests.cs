using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailErrorProgressViewStateTests
{
    [Test]
    public void Apply_表示行と実マーカーと救済管理を分けて集計する()
    {
        ThumbnailErrorProgressViewState state = new();

        state.Apply(
            new[]
            {
                new ThumbnailErrorRecordViewModel
                {
                    MarkerCount = 1,
                    ProgressSummaryKey = "unqueued",
                },
                new ThumbnailErrorRecordViewModel
                {
                    MarkerCount = 0,
                    ProgressSummaryKey = "pending",
                },
                new ThumbnailErrorRecordViewModel
                {
                    MarkerCount = 2,
                    ProgressSummaryKey = "attention",
                },
            }
        );

        Assert.That(state.VisibleCountText, Is.EqualTo("3"));
        Assert.That(state.MarkerCountText, Is.EqualTo("3"));
        Assert.That(state.ManagedCountText, Is.EqualTo("2"));
        Assert.That(state.UnqueuedCountText, Is.EqualTo("1"));
        Assert.That(state.PendingCountText, Is.EqualTo("1"));
        Assert.That(state.AttentionCountText, Is.EqualTo("1"));
    }
}
