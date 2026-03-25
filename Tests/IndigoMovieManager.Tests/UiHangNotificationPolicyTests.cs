using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class UiHangNotificationPolicyTests
{
    [Test]
    public void IsUiHangDangerStateCore_pending閾値超過ならハンドル未確定でもTrueを返す()
    {
        bool actual = MainWindow.IsUiHangDangerStateCore(
            new UiHangHeartbeatSample(5000, true),
            5000,
            0,
            _ => false
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void IsUiHangDangerStateCore_pending閾値未満ならHungWindow判定へ委譲する()
    {
        bool resolverCalled = false;

        bool actual = MainWindow.IsUiHangDangerStateCore(
            new UiHangHeartbeatSample(1000, true),
            5000,
            (nint)123,
            _ =>
            {
                resolverCalled = true;
                return true;
            }
        );

        Assert.That(resolverCalled, Is.True);
        Assert.That(actual, Is.True);
    }

    [Test]
    public void IsUiHangDangerStateCore_ハンドル未確定ならHungWindow判定せずFalseを返す()
    {
        bool resolverCalled = false;

        bool actual = MainWindow.IsUiHangDangerStateCore(
            new UiHangHeartbeatSample(1000, false),
            5000,
            0,
            _ =>
            {
                resolverCalled = true;
                return true;
            }
        );

        Assert.That(resolverCalled, Is.False);
        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldDisplayUiHangNotificationCore_criticalは最小化中でもTrueを返す()
    {
        bool actual = MainWindow.ShouldDisplayUiHangNotificationCore(
            UiHangNotificationLevel.Critical,
            isMinimized: true,
            (nint)0,
            () => 0
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldDisplayUiHangNotificationCore_最小化中はcritical以外を抑止する()
    {
        bool actual = MainWindow.ShouldDisplayUiHangNotificationCore(
            UiHangNotificationLevel.Warning,
            isMinimized: true,
            (nint)123,
            () => (nint)123
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldDisplayUiHangNotificationCore_前面ウインドウ一致時だけTrueを返す()
    {
        bool actual = MainWindow.ShouldDisplayUiHangNotificationCore(
            UiHangNotificationLevel.Warning,
            isMinimized: false,
            (nint)123,
            () => (nint)123
        );

        Assert.That(actual, Is.True);
    }
}
