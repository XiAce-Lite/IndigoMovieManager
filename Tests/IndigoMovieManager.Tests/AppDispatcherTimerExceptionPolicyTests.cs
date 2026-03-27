using System.ComponentModel;
using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class AppDispatcherTimerExceptionPolicyTests
{
    [Test]
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_SetWin32Timer経路ならTrueを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new Win32Exception(8),
            """
            at System.Windows.Threading.Dispatcher.SetWin32Timer(Int32 dueTimeInTicks)
            at System.Windows.Threading.DispatcherTimer.Start()
            """
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_Rendering経路ならTrueを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new Win32Exception(8),
            """
            at System.Windows.Threading.DispatcherTimer.Start()
            at System.Windows.Media.MediaContext.CommitChannelAfterNextVSync()
            """
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_対象外Win32ExceptionならFalseを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new Win32Exception(5),
            "at IndigoMovieManager.SomeOtherComponent.Run()"
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_SetWin32Timer単独だけではFalse()
    {
        // SetWin32Timer マーカー単体だけでは握る根拠が弱いので true にしない契約を固定する。
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new Win32Exception(8),
            """
            at System.Windows.Threading.Dispatcher.SetWin32Timer(Int32 dueTimeInTicks)
            """
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void ShouldSuppressKnownDispatcherTimerWin32Exception_Win32Exception以外はFalseを返す()
    {
        bool actual = App.ShouldSuppressKnownDispatcherTimerWin32Exception(
            new InvalidOperationException("x"),
            """
            at System.Windows.Threading.Dispatcher.SetWin32Timer(Int32 dueTimeInTicks)
            """
        );

        Assert.That(actual, Is.False);
    }
}
