using System.Reflection;
using System.Runtime.CompilerServices;
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

    [Test]
    public void TryResolveWatchUpdateCountForPoll_watch時だけ更新量を返す()
    {
        (bool resolved, int updateCount) = InvokeTryResolveWatchUpdateCountForPoll(
            "Watch",
            hasFolderUpdate: true,
            enqueuedCount: 2,
            changedMovieCount: 5
        );

        Assert.That(resolved, Is.True);
        Assert.That(updateCount, Is.EqualTo(5));
    }

    [Test]
    public void TryResolveWatchUpdateCountForPoll_manual時は解決しない()
    {
        (bool resolved, int updateCount) = InvokeTryResolveWatchUpdateCountForPoll(
            "Manual",
            hasFolderUpdate: true,
            enqueuedCount: 2,
            changedMovieCount: 5
        );

        Assert.That(resolved, Is.False);
        Assert.That(updateCount, Is.EqualTo(0));
    }

    [Test]
    public void RecordWatchUpdateCountForPollIfNeeded_watch時だけlast_updatesを更新する()
    {
        MainWindow window = CreateWindow();

        InvokeRecordWatchUpdateCountForPollIfNeeded(
            window,
            "Watch",
            hasFolderUpdate: true,
            enqueuedCount: 2,
            changedMovieCount: 5
        );
        Assert.That(GetPrivateField<int>(window, "_lastEverythingPollUpdateCount"), Is.EqualTo(5));

        SetPrivateField(window, "_lastEverythingPollUpdateCount", 77);
        InvokeRecordWatchUpdateCountForPollIfNeeded(
            window,
            "Manual",
            hasFolderUpdate: true,
            enqueuedCount: 9,
            changedMovieCount: 9
        );
        Assert.That(GetPrivateField<int>(window, "_lastEverythingPollUpdateCount"), Is.EqualTo(77));
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

    private static (bool Resolved, int UpdateCount) InvokeTryResolveWatchUpdateCountForPoll(
        string modeName,
        bool hasFolderUpdate,
        int enqueuedCount,
        int changedMovieCount
    )
    {
        Type checkModeType = typeof(MainWindow).GetNestedType(
            "CheckMode",
            BindingFlags.NonPublic
        )!;
        Assert.That(checkModeType, Is.Not.Null);

        MethodInfo method = typeof(MainWindow).GetMethod(
            "TryResolveWatchUpdateCountForPoll",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);

        object mode = Enum.Parse(checkModeType, modeName);
        object[] args =
        [
            mode,
            hasFolderUpdate,
            enqueuedCount,
            changedMovieCount,
            0,
        ];
        bool resolved = (bool)method.Invoke(null, args)!;
        return (resolved, (int)args[4]);
    }

    private static void InvokeRecordWatchUpdateCountForPollIfNeeded(
        MainWindow window,
        string modeName,
        bool hasFolderUpdate,
        int enqueuedCount,
        int changedMovieCount
    )
    {
        Type checkModeType = typeof(MainWindow).GetNestedType(
            "CheckMode",
            BindingFlags.NonPublic
        )!;
        Assert.That(checkModeType, Is.Not.Null);

        MethodInfo method = typeof(MainWindow).GetMethod(
            "RecordWatchUpdateCountForPollIfNeeded",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);

        object mode = Enum.Parse(checkModeType, modeName);
        method.Invoke(window, [mode, hasFolderUpdate, enqueuedCount, changedMovieCount]);
    }

    private static MainWindow CreateWindow()
    {
        return (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        )!;
        Assert.That(field, Is.Not.Null);
        return (T)field.GetValue(target)!;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        )!;
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }
}
