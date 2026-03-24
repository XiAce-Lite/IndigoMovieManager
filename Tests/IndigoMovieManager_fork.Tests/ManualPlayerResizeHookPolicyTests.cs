using System.IO;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ManualPlayerResizeHookPolicyTests
{
    [Test]
    public void EnsureManualPlayerResizeTrackingHooked_未登録時だけフックする()
    {
        // 変更後の実装方針は、二重登録を防ぎつつ1回だけ SizeChanged を拾うこと。
        string source = GetMainWindowPlayerSourceText();

        Assert.That(source, Does.Contain("private void EnsureManualPlayerResizeTrackingHooked()"));
        Assert.That(source, Does.Contain("if (_isManualPlayerResizeTrackingHooked)"));
        Assert.That(source, Does.Contain("SizeChanged += ManualPlayerHost_SizeChanged;"));
    }

    [Test]
    public void ManualPlayerHost_SizeChanged_表示中のみviewport更新する()
    {
        // 再生オーバーレイが隠れている時は不要な再計算を避ける契約を保持する。
        string source = GetMainWindowPlayerSourceText();

        Assert.That(
            source,
            Does.Contain("if (PlayerArea?.Visibility != Visibility.Visible)")
        );
        Assert.That(source, Does.Contain("UpdateManualPlayerViewport();"));
    }

    private static string GetMainWindowPlayerSourceText()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "Views", "Main", "MainWindow.Player.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail("MainWindow.Player.cs の位置を repo root から解決できませんでした。");
        return string.Empty;
    }
}
