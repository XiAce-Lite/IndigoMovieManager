using System.IO;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class StartupUiHangActivitySourceTests
{
    [Test]
    public void BeginStartupDbOpenでStartupActivityをrequestへ積む()
    {
        string source = File.ReadAllText(GetMainWindowStartupSourcePath());

        Assert.That(source, Does.Contain("StartupFeedRequest request = new("));
        Assert.That(source, Does.Contain("UiHangActivityKind.Startup,"));
    }

    [Test]
    public void RunStartupDbOpenAsyncでrequest由来のactivityを追跡する()
    {
        string source = File.ReadAllText(GetMainWindowStartupSourcePath());

        Assert.That(
            source,
            Does.Contain("using IDisposable uiHangScope = TrackUiHangActivity(request.ActivityKind);")
        );
    }

    private static string GetMainWindowStartupSourcePath([CallerFilePath] string testSourcePath = "")
    {
        string? resolved = TryFindMainWindowStartupSourcePath(TestContext.CurrentContext.TestDirectory);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        resolved = TryFindMainWindowStartupSourcePath(Path.GetDirectoryName(testSourcePath) ?? "");
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        Assert.Fail("MainWindow.Startup.cs の位置を repo root から解決できませんでした。");
        return string.Empty;
    }

    private static string? TryFindMainWindowStartupSourcePath(string baseDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectoryPath))
        {
            return null;
        }

        DirectoryInfo? current = new(baseDirectoryPath);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "Views", "Main", "MainWindow.Startup.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
