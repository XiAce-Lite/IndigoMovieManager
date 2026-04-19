using System.Reflection;
using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchUpdateSignalPolicyTests
{
    [Test]
    public void ComputeWatchUpdateCountForPoll_enqueue件数が大きければそれを返す()
    {
        int result = InvokeComputeWatchUpdateCountForPoll(
            hasFolderUpdate: true,
            enqueuedCount: 5,
            changedMovieCount: 2
        );

        Assert.That(result, Is.EqualTo(5));
    }

    [Test]
    public void ComputeWatchUpdateCountForPoll_changed_movie件数が大きければそれを返す()
    {
        int result = InvokeComputeWatchUpdateCountForPoll(
            hasFolderUpdate: true,
            enqueuedCount: 1,
            changedMovieCount: 4
        );

        Assert.That(result, Is.EqualTo(4));
    }

    [Test]
    public void ComputeWatchUpdateCountForPoll_更新ありで件数ゼロなら最低1を返す()
    {
        int result = InvokeComputeWatchUpdateCountForPoll(
            hasFolderUpdate: true,
            enqueuedCount: 0,
            changedMovieCount: 0
        );

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void ComputeWatchUpdateCountForPoll_更新なしで件数ゼロなら0を返す()
    {
        int result = InvokeComputeWatchUpdateCountForPoll(
            hasFolderUpdate: false,
            enqueuedCount: 0,
            changedMovieCount: 0
        );

        Assert.That(result, Is.EqualTo(0));
    }

    private static int InvokeComputeWatchUpdateCountForPoll(
        bool hasFolderUpdate,
        int enqueuedCount,
        int changedMovieCount
    )
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "ComputeWatchUpdateCountForPoll",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);
        return (int)method.Invoke(null, [hasFolderUpdate, enqueuedCount, changedMovieCount])!;
    }
}
