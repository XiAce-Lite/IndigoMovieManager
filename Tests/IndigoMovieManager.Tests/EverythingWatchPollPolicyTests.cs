using System.Reflection;
using System.Runtime.CompilerServices;
using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class EverythingWatchPollPolicyTests
{
    [Test]
    public void ResolveEverythingWatchPollDelayFromState_混雑時はbusy間隔を返す()
    {
        MainWindow window = CreateWindow();
        SetStartupFeedPartialActive(window, isActive: false);

        int delayMs = (int)InvokePrivateInstance(
            window,
            "ResolveEverythingWatchPollDelayFromState",
            200
        );

        Assert.That(delayMs, Is.EqualTo(15000));
    }

    [Test]
    public void ResolveEverythingWatchPollDelayFromState_中負荷時はmedium間隔を返す()
    {
        MainWindow window = CreateWindow();
        SetStartupFeedPartialActive(window, isActive: false);

        int delayMs = (int)InvokePrivateInstance(
            window,
            "ResolveEverythingWatchPollDelayFromState",
            50
        );

        Assert.That(delayMs, Is.EqualTo(6000));
    }

    [Test]
    public void ResolveEverythingWatchPollDelayFromState_静かな周期が続くとcalm間隔を返す()
    {
        MainWindow window = CreateWindow();
        SetStartupFeedPartialActive(window, isActive: false);
        SetPrivateField(window, "_consecutiveCalmEverythingPollCount", 3);

        int delayMs = (int)InvokePrivateInstance(
            window,
            "ResolveEverythingWatchPollDelayFromState",
            0
        );

        Assert.That(delayMs, Is.EqualTo(9000));
    }

    [Test]
    public void RecordEverythingWatchPollResult_startup中はcalm周期を増やさない()
    {
        MainWindow window = CreateWindow();
        SetStartupFeedPartialActive(window, isActive: true);
        SetPrivateField(window, "_consecutiveCalmEverythingPollCount", 2);

        InvokePrivateInstance(window, "RecordEverythingWatchPollResult", 0);

        Assert.That(GetPrivateField<int>(window, "_lastEverythingPollUpdateCount"), Is.EqualTo(0));
        Assert.That(GetPrivateField<int>(window, "_consecutiveCalmEverythingPollCount"), Is.EqualTo(0));
    }

    [Test]
    public void RecordEverythingWatchPollResult_低更新ならcalm周期を増やす()
    {
        MainWindow window = CreateWindow();
        SetStartupFeedPartialActive(window, isActive: false);
        SetPrivateField(window, "_consecutiveCalmEverythingPollCount", 1);

        InvokePrivateInstance(window, "RecordEverythingWatchPollResult", 1);

        Assert.That(GetPrivateField<int>(window, "_lastEverythingPollUpdateCount"), Is.EqualTo(1));
        Assert.That(GetPrivateField<int>(window, "_consecutiveCalmEverythingPollCount"), Is.EqualTo(2));
    }

    [Test]
    public void RecordEverythingWatchPollResult_更新が多い時はcalm周期をリセットする()
    {
        MainWindow window = CreateWindow();
        SetStartupFeedPartialActive(window, isActive: false);
        SetPrivateField(window, "_consecutiveCalmEverythingPollCount", 4);

        InvokePrivateInstance(window, "RecordEverythingWatchPollResult", 3);

        Assert.That(GetPrivateField<int>(window, "_lastEverythingPollUpdateCount"), Is.EqualTo(3));
        Assert.That(GetPrivateField<int>(window, "_consecutiveCalmEverythingPollCount"), Is.EqualTo(0));
    }

    [Test]
    public void ShouldRunEverythingWatchPoll_起動中は止める()
    {
        string dbPath = System.IO.Path.GetTempFileName();
        try
        {
            bool result = MainWindow.ShouldRunEverythingWatchPollPolicy(
                isStartupFeedPartialActive: true,
                isIntegrationConfigured: true,
                canUseAvailability: true,
                keepPollingForFallback: false,
                dbPath: dbPath,
                watchFolders: [@"E:\Movies"],
                pathExists: _ => true,
                isEverythingEligiblePath: _ => true
            );

            Assert.That(result, Is.False);
        }
        finally
        {
            System.IO.File.Delete(dbPath);
        }
    }

    [Test]
    public void ShouldRunEverythingWatchPoll_eligibleなwatchがあれば動かす()
    {
        string dbPath = System.IO.Path.GetTempFileName();
        try
        {
            bool result = MainWindow.ShouldRunEverythingWatchPollPolicy(
                isStartupFeedPartialActive: false,
                isIntegrationConfigured: true,
                canUseAvailability: false,
                keepPollingForFallback: true,
                dbPath: dbPath,
                watchFolders: [@"E:\Movies", @"F:\Other"],
                pathExists: _ => true,
                isEverythingEligiblePath: watchFolder =>
                    watchFolder.EndsWith(@"E:\Movies", StringComparison.OrdinalIgnoreCase)
            );

            Assert.That(result, Is.True);
        }
        finally
        {
            System.IO.File.Delete(dbPath);
        }
    }

    [Test]
    public void ShouldRunEverythingWatchPoll_非eligibleしかないと止める()
    {
        string dbPath = System.IO.Path.GetTempFileName();
        try
        {
            bool result = MainWindow.ShouldRunEverythingWatchPollPolicy(
                isStartupFeedPartialActive: false,
                isIntegrationConfigured: true,
                canUseAvailability: true,
                keepPollingForFallback: false,
                dbPath: dbPath,
                watchFolders: [@"E:\Movies", @"F:\Other"],
                pathExists: _ => true,
                isEverythingEligiblePath: _ => false
            );

            Assert.That(result, Is.False);
        }
        finally
        {
            System.IO.File.Delete(dbPath);
        }
    }

    private static MainWindow CreateWindow()
    {
        return (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
    }

    private static void SetStartupFeedPartialActive(MainWindow window, bool isActive)
    {
        SetPrivateField(window, "_startupFeedIsPartialActive", isActive);
        SetPrivateField(window, "_startupFeedLoadedAllPages", !isActive);
    }

    private static object? InvokePrivateInstance(MainWindow window, string methodName, params object[] args)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null, methodName);
        return method.Invoke(window, args);
    }

    private static void SetPrivateField(MainWindow window, string fieldName, object value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(window, value);
    }

    private static T GetPrivateField<T>(MainWindow window, string fieldName)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(field, Is.Not.Null, fieldName);
        return (T)field.GetValue(window)!;
    }
}
