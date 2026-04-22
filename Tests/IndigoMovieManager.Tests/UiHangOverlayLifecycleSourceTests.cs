using System.IO;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UiHangOverlayLifecycleSourceTests
{
    [Test]
    public void MainWindowはSourceInitializedとStart時にowner更新を呼ぶ()
    {
        string mainWindowSource = GetSourceText(
            new[] { "Views", "Main", "MainWindow.xaml.cs" }
        );
        string uiHangSource = GetSourceText(
            new[] { "Views", "Main", "MainWindow.UiHangNotification.cs" }
        );

        Assert.That(mainWindowSource, Does.Contain("UpdateUiHangNotificationOwnerWindow();"));
        Assert.That(uiHangSource, Does.Contain("private void UpdateUiHangNotificationOwnerWindow()"));
        Assert.That(
            uiHangSource,
            Does.Contain("_uiHangNotificationCoordinator.UpdateOwnerWindowHandle(windowHandle);")
        );
    }

    [Test]
    public void NativeOverlayHostはowner付き生成と停止時即hideを持つ()
    {
        string source = GetSourceText(new[] { "Views", "Main", "NativeOverlayHost.cs" });

        Assert.That(source, Does.Contain("_ownerWindowHandle"));
        Assert.That(source, Does.Contain("internal void UpdateOwnerWindowHandle(nint ownerWindowHandle)"));
        Assert.That(source, Does.Contain("_ownerWindowHandle,"));
        Assert.That(source, Does.Contain("ForceHideNativeOverlayImmediately(overlayHwnd);"));
        Assert.That(source, Does.Contain("RequestOverlayClose(overlayHwnd);"));
    }

    [Test]
    public void NativeOverlayHostNativeMethodsはowner変更とclose要求を持つ()
    {
        string source = GetSourceText(
            new[] { "Views", "Main", "NativeOverlayHost.NativeMethods.cs" }
        );

        Assert.That(source, Does.Contain("GwlHwndParent = -8"));
        Assert.That(source, Does.Contain("WM_CLOSE = 0x0010"));
        Assert.That(source, Does.Contain("private static extern bool PostMessage("));
    }

    private static string GetSourceText(
        string[] relativeSegments,
        [CallerFilePath] string testSourcePath = ""
    )
    {
        string? repoRootFromSource = ResolveRepoRootFromCallerSource(testSourcePath);
        if (!string.IsNullOrEmpty(repoRootFromSource))
        {
            string[] sourceSegments = new string[relativeSegments.Length + 1];
            sourceSegments[0] = repoRootFromSource;
            Array.Copy(relativeSegments, 0, sourceSegments, 1, relativeSegments.Length);
            string sourceCandidate = Path.Combine(sourceSegments);
            if (File.Exists(sourceCandidate))
            {
                return File.ReadAllText(sourceCandidate);
            }
        }

        string[] cwdSegments = new string[relativeSegments.Length + 1];
        cwdSegments[0] = Directory.GetCurrentDirectory();
        Array.Copy(relativeSegments, 0, cwdSegments, 1, relativeSegments.Length);
        string cwdCandidate = Path.Combine(cwdSegments);
        if (File.Exists(cwdCandidate))
        {
            return File.ReadAllText(cwdCandidate);
        }

        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string[] segments = new string[relativeSegments.Length + 1];
            segments[0] = current.FullName;
            Array.Copy(relativeSegments, 0, segments, 1, relativeSegments.Length);
            string candidate = Path.Combine(segments);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail($"source file を repo root から解決できませんでした: {string.Join(Path.DirectorySeparatorChar, relativeSegments)}");
        return string.Empty;
    }

    private static string? ResolveRepoRootFromCallerSource(string testSourcePath)
    {
        if (string.IsNullOrWhiteSpace(testSourcePath))
        {
            return null;
        }

        DirectoryInfo? current = new(Path.GetDirectoryName(testSourcePath));
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "IndigoMovieManager.sln");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
