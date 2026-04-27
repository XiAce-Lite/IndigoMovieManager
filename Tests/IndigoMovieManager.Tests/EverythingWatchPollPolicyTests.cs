using System.Data;
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
        SetPrivateField(window, "_everythingWatchPollLoopStartedTick64", Environment.TickCount64 - 15000);

        int delayMs = (int)InvokePrivateInstance(
            window,
            "ResolveEverythingWatchPollDelayFromState",
            0
        );

        Assert.That(delayMs, Is.EqualTo(9000));
    }

    [Test]
    public void ResolveEverythingWatchPollDelayFromState_起動直後は静かでも基本間隔を返す()
    {
        MainWindow window = CreateWindow();
        SetStartupFeedPartialActive(window, isActive: false);
        SetPrivateField(window, "_consecutiveCalmEverythingPollCount", 3);
        SetPrivateField(window, "_everythingWatchPollLoopStartedTick64", Environment.TickCount64);

        int delayMs = (int)InvokePrivateInstance(
            window,
            "ResolveEverythingWatchPollDelayFromState",
            0
        );

        Assert.That(delayMs, Is.EqualTo(3000));
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
    public void ResetEverythingWatchPollAdaptiveDelayState_DB切替時にcalm状態を初期化する()
    {
        MainWindow window = CreateWindow();
        SetPrivateField(window, "_lastEverythingPollUpdateCount", 1);
        SetPrivateField(window, "_consecutiveCalmEverythingPollCount", 5);
        SetPrivateField(window, "_lastEverythingPollDelayMs", 9000);
        SetPrivateField(window, "_everythingWatchPollLoopStartedTick64", Environment.TickCount64 - 15000);

        InvokePrivateInstance(window, "ResetEverythingWatchPollAdaptiveDelayState");

        Assert.Multiple(() =>
        {
            Assert.That(GetPrivateField<int>(window, "_lastEverythingPollUpdateCount"), Is.EqualTo(0));
            Assert.That(GetPrivateField<int>(window, "_consecutiveCalmEverythingPollCount"), Is.EqualTo(0));
            Assert.That(GetPrivateField<int>(window, "_lastEverythingPollDelayMs"), Is.EqualTo(3000));
            Assert.That(GetPrivateField<long>(window, "_everythingWatchPollLoopStartedTick64"), Is.GreaterThan(0));
        });
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
    public void ShouldDeferEverythingWatchPollForUserPriority_検索優先中だけTrueを返す()
    {
        Assert.That(
            MainWindow.ShouldDeferEverythingWatchPollForUserPriority(isUserPriorityActive: true),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldDeferEverythingWatchPollForUserPriority(isUserPriorityActive: false),
            Is.False
        );
    }

    [Test]
    public void ShouldProbeEverythingWatchPollQueueLoad_poll延期中はFalseを返す()
    {
        Assert.That(
            MainWindow.ShouldProbeEverythingWatchPollQueueLoad(
                isDeferredByUiSuppression: true,
                isDeferredByUserPriority: false
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldProbeEverythingWatchPollQueueLoad(
                isDeferredByUiSuppression: false,
                isDeferredByUserPriority: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldProbeEverythingWatchPollQueueLoad(
                isDeferredByUiSuppression: false,
                isDeferredByUserPriority: false
            ),
            Is.True
        );
    }

    [Test]
    public void ApplyEverythingWatchPollInteractionDelayPolicy_通常時は遅延を変えない()
    {
        int delayMs = MainWindow.ApplyEverythingWatchPollInteractionDelayPolicy(
            delayMs: 3000,
            isDeferredByUiSuppression: false,
            isDeferredByUserPriority: false,
            isPlayerPlaybackActive: false
        );

        Assert.That(delayMs, Is.EqualTo(3000));
    }

    [Test]
    public void ApplyEverythingWatchPollInteractionDelayPolicy_UI操作中はcalm間隔まで延長する()
    {
        int delayMs = MainWindow.ApplyEverythingWatchPollInteractionDelayPolicy(
            delayMs: 3000,
            isDeferredByUiSuppression: true,
            isDeferredByUserPriority: false,
            isPlayerPlaybackActive: false
        );

        Assert.That(delayMs, Is.EqualTo(9000));
    }

    [Test]
    public void ApplyEverythingWatchPollInteractionDelayPolicy_再生中はcalm間隔まで延長する()
    {
        int delayMs = MainWindow.ApplyEverythingWatchPollInteractionDelayPolicy(
            delayMs: 3000,
            isDeferredByUiSuppression: false,
            isDeferredByUserPriority: false,
            isPlayerPlaybackActive: true
        );

        Assert.That(delayMs, Is.EqualTo(9000));
    }

    [Test]
    public void ApplyEverythingWatchPollInteractionDelayPolicy_既に長い遅延は維持する()
    {
        int delayMs = MainWindow.ApplyEverythingWatchPollInteractionDelayPolicy(
            delayMs: 15000,
            isDeferredByUiSuppression: false,
            isDeferredByUserPriority: true,
            isPlayerPlaybackActive: true
        );

        Assert.That(delayMs, Is.EqualTo(15000));
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

    [Test]
    public void ExtractEverythingPollWatchFolders_空行と空白行を除いて順序維持で返す()
    {
        DataTable watchTable = new();
        watchTable.Columns.Add("dir", typeof(string));
        watchTable.Rows.Add(@"E:\Movies");
        watchTable.Rows.Add("");
        watchTable.Rows.Add("   ");
        watchTable.Rows.Add(@"F:\Anime");

        string[] result = MainWindow.ExtractEverythingPollWatchFolders(watchTable);

        Assert.That(result, Is.EqualTo(new[] { @"E:\Movies", @"F:\Anime" }));
    }

    [Test]
    public void ExtractEverythingPollWatchFolders_重複は順序維持で1件にまとめる()
    {
        DataTable watchTable = new();
        watchTable.Columns.Add("dir", typeof(string));
        watchTable.Rows.Add(@"E:\Movies");
        watchTable.Rows.Add(@"e:\movies");
        watchTable.Rows.Add(@"F:\Anime");
        watchTable.Rows.Add(@"F:\Anime");

        string[] result = MainWindow.ExtractEverythingPollWatchFolders(watchTable);

        Assert.That(result, Is.EqualTo(new[] { @"E:\Movies", @"F:\Anime" }));
    }

    [Test]
    public void ExtractEverythingPollWatchFolders_nullなら空配列を返す()
    {
        string[] result = MainWindow.ExtractEverythingPollWatchFolders(null);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ExtractEverythingEligibleWatchFolders_eligibleだけを順序維持で返す()
    {
        string[] result = MainWindow.ExtractEverythingEligibleWatchFolders(
            [@"E:\Movies", @"F:\Anime", @"G:\Other"],
            watchFolder => watchFolder.StartsWith(@"F:\", StringComparison.OrdinalIgnoreCase)
                || watchFolder.StartsWith(@"G:\", StringComparison.OrdinalIgnoreCase)
        );

        Assert.That(result, Is.EqualTo(new[] { @"F:\Anime", @"G:\Other" }));
    }

    [Test]
    public void ExtractEverythingEligibleWatchFolders_重複eligibleは順序維持で1件にまとめる()
    {
        string[] result = MainWindow.ExtractEverythingEligibleWatchFolders(
            [@"E:\Movies", @"e:\movies", @"F:\Anime", @"f:\anime"],
            _ => true
        );

        Assert.That(result, Is.EqualTo(new[] { @"E:\Movies", @"F:\Anime" }));
    }

    [Test]
    public void ExtractEverythingEligibleWatchFolders_重複候補は判定前に除外する()
    {
        int eligibleCheckCount = 0;

        string[] result = MainWindow.ExtractEverythingEligibleWatchFolders(
            [@"E:\Movies", @"e:\movies", @"F:\Anime"],
            _ =>
            {
                eligibleCheckCount++;
                return true;
            }
        );

        Assert.That(result, Is.EqualTo(new[] { @"E:\Movies", @"F:\Anime" }));
        Assert.That(eligibleCheckCount, Is.EqualTo(2));
    }

    [Test]
    public void ExtractEverythingEligibleWatchFolders_nullなら空配列を返す()
    {
        string[] result = MainWindow.ExtractEverythingEligibleWatchFolders(null, _ => true);

        Assert.That(result, Is.Empty);
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
