using System.IO;

namespace IndigoMovieManager.Tests;

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

    [Test]
    public void ApplyPlayerVolumeSetting_保存はdebounce経由で畳む()
    {
        string source = GetMainWindowPlayerSourceText();

        Assert.That(source, Does.Contain("private DispatcherTimer _playerVolumeSaveDebounceTimer;"));
        Assert.That(source, Does.Contain("private void QueuePlayerVolumeSettingSave()"));
        Assert.That(source, Does.Contain("private void PlayerVolumeSaveDebounceTimer_Tick("));
        Assert.That(source, Does.Contain("QueuePlayerVolumeSettingSave();"));
    }

    [Test]
    public void WebViewPlayer_ホスト音量適用前の既定音量通知を抑止する()
    {
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string fullscreenWindowSource = GetUpperTabPlayerFullscreenWindowSourceText();

        Assert.That(mainWindowPlayerSource, Does.Contain("indigoPlayerHostVolumeApplied = '1'"));
        Assert.That(upperTabPlayerSource, Does.Contain("indigoPlayerHostVolumeApplied = '1'"));
        Assert.That(fullscreenWindowSource, Does.Contain("indigoPlayerHostVolumeApplied = '1'"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("player.dataset.indigoPlayerHostVolumeApplied !== '1'")
        );
        Assert.That(upperTabPlayerSource, Does.Not.Contain("notifyVolume();"));
    }

    private static string GetMainWindowPlayerSourceText()
    {
        return GetRepoText("Views", "Main", "MainWindow.Player.cs");
    }

    private static string GetUpperTabPlayerSourceText()
    {
        return GetRepoText("UpperTabs", "Player", "MainWindow.UpperTabs.PlayerTab.cs");
    }

    private static string GetUpperTabPlayerFullscreenWindowSourceText()
    {
        return GetRepoText("UpperTabs", "Player", "MainWindow.UpperTabs.PlayerFullscreenWindow.cs");
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string candidate = Path.Combine([current.FullName, .. relativePathParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置を repo root から解決できませんでした。");
        return string.Empty;
    }
}
