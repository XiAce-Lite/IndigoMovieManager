using System.Windows;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ManualPlayerResizeHookPolicyTests
{
    [Test]
    public void manualPlayerResizeHookは未登録時だけ追加する()
    {
        Assert.That(MainWindow.ShouldAttachManualPlayerResizeHook(false), Is.True);
        Assert.That(MainWindow.ShouldAttachManualPlayerResizeHook(true), Is.False);
    }

    [Test]
    public void manualPlayerResize時は表示中だけviewport更新へ流す()
    {
        Assert.That(
            MainWindow.ShouldUpdateManualPlayerViewportOnResize(Visibility.Visible),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldUpdateManualPlayerViewportOnResize(Visibility.Collapsed),
            Is.False
        );
    }
}
