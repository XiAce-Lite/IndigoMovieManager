using System.IO;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class StartupUiHangActivitySourceTests
{
    [Test]
    public void BeginStartupDbOpenでStartupActivityをrequestへ積む()
    {
        string source = File.ReadAllText(GetMainWindowStartupSourcePath());

        StringAssert.Contains(
            "StartupFeedRequest request = new(",
            source
        );
        StringAssert.Contains(
            "UiHangActivityKind.Startup,",
            source
        );
    }

    [Test]
    public void RunStartupDbOpenAsyncでrequest由来のactivityを追跡する()
    {
        string source = File.ReadAllText(GetMainWindowStartupSourcePath());

        StringAssert.Contains(
            "using IDisposable uiHangScope = TrackUiHangActivity(request.ActivityKind);",
            source
        );
    }

    private static string GetMainWindowStartupSourcePath()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "Views", "Main", "MainWindow.Startup.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        Assert.Fail("MainWindow.Startup.cs の位置を repo root から解決できませんでした。");
        return string.Empty;
    }
}
