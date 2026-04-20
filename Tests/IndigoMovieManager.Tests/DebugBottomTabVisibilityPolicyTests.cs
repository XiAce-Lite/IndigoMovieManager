using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DebugBottomTabVisibilityPolicyTests
{
    [Test]
    public void Debugビルドで非ReleaseならDebugタブを表示する()
    {
        bool actual = MainWindow.ShouldShowDebugTabCore(
            isDebugBuild: true,
            isReleaseBuild: false,
            isDebuggerAttached: false
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void Release構成でもdebugger接続中ならDebugタブを表示する()
    {
        bool actual = MainWindow.ShouldShowDebugTabCore(
            isDebugBuild: false,
            isReleaseBuild: true,
            isDebuggerAttached: true
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void Release構成でdebugger未接続ならDebugタブを表示しない()
    {
        bool actual = MainWindow.ShouldShowDebugTabCore(
            isDebugBuild: false,
            isReleaseBuild: true,
            isDebuggerAttached: false
        );

        Assert.That(actual, Is.False);
    }
}
