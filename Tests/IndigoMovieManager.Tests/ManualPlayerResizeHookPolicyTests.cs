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
        Assert.That(
            source,
            Does.Contain("Math.Abs(Properties.Settings.Default.PlayerVolume - resolvedVolume) > 0.0001d")
        );
        Assert.That(source, Does.Contain("QueuePlayerVolumeSettingSave();"));
    }

    [Test]
    public void PlayerVolume_保存値が0へリセットされた時だけ起動時に50へ戻す()
    {
        string playerSource = GetMainWindowPlayerSourceText();
        string windowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");

        Assert.That(playerSource, Does.Contain("private const double DefaultPlayerVolume = 0.5d;"));
        Assert.That(playerSource, Does.Contain("private static double ResolveSavedPlayerVolumeSetting(double volume)"));
        Assert.That(playerSource, Does.Contain("return resolvedVolume <= 0d ? DefaultPlayerVolume : resolvedVolume;"));
        Assert.That(playerSource, Does.Not.Contain("resolvedVolume >= 1d"));
        Assert.That(
            windowSource,
            Does.Contain("ResolveSavedPlayerVolumeSetting(Properties.Settings.Default.PlayerVolume)")
        );
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

    [Test]
    public void PlayerThumbnailClick_選択同期でスクロール位置を動かさない()
    {
        string selectionSource = GetMainWindowSelectionSourceText();

        Assert.That(selectionSource, Does.Contain("SelectPlayerThumbnailRecordWithoutScroll(label, record);"));
        Assert.That(selectionSource, Does.Contain("syncPlayerSelection: false"));
        Assert.That(selectionSource, Does.Contain("return;"));
        Assert.That(
            selectionSource,
            Does.Contain("private void SelectPlayerThumbnailRecordWithoutScroll(Label label, MovieRecords record)")
        );
        Assert.That(selectionSource, Does.Contain("_suppressPlayerThumbnailSelectionChanged = true;"));
        Assert.That(selectionSource, Does.Contain("SyncPlayerThumbnailSelectionAcrossViews(sourceList, record);"));
        Assert.That(selectionSource, Does.Contain("ShowExtensionDetail(record);"));
        Assert.That(selectionSource, Does.Contain("ShowTagEditor(record);"));

        string upperTabPlayerSource = GetUpperTabPlayerSourceText();

        Assert.That(upperTabPlayerSource, Does.Contain("bool syncPlayerSelection = true"));
        Assert.That(upperTabPlayerSource, Does.Contain("if (syncPlayerSelection)"));
    }

    [Test]
    public void PlayerThumbnailViewMode_同一モード再選択では再スクロールしない()
    {
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();

        Assert.That(
            upperTabPlayerSource,
            Does.Contain("if (_isPlayerThumbnailCompactViewEnabled == enabled)")
        );
        Assert.That(upperTabPlayerSource, Does.Contain("return;"));
        Assert.That(upperTabPlayerSource, Does.Contain("GetUpperTabPlayerList()?.ScrollIntoView(selectedMovie);"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("RequestUpperTabVisibleRangeRefresh(immediate: true, reason: \"player-view-mode\");")
        );
    }

    [Test]
    public void PlayerThumbnailSelectionSync_同一選択では再スクロールとvisible更新を積まない()
    {
        // プレイヤータブ内の同一動画再生では、選択同期だけで重い visible refresh を再投入しない。
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();

        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private bool SelectUpperTabPlayerMovieRecord(MovieRecords record)")
        );
        Assert.That(upperTabPlayerSource, Does.Contain("bool selectionChanged = false;"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("if (ReferenceEquals(list.SelectedItem, record))")
        );
        Assert.That(upperTabPlayerSource, Does.Contain("return selectionChanged;"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("selectionChanged = SelectUpperTabPlayerMovieRecord(record);")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("if (selectionChanged)")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("RequestUpperTabVisibleRangeRefresh(immediate: true, reason: \"player-view-mode\");")
        );
    }

    [Test]
    public void OpenMovieInPlayerTabAsync_Player操作中はuser_priority_scopeで囲む()
    {
        // Player 再生開始中は watch/poll を後ろへ逃がし、早期 return でも必ず解除する。
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();

        Assert.That(upperTabPlayerSource, Does.Contain("BeginUserPriorityWork(\"player\");"));
        Assert.That(upperTabPlayerSource, Does.Contain("try"));
        Assert.That(upperTabPlayerSource, Does.Contain("finally"));
        Assert.That(upperTabPlayerSource, Does.Contain("EndUserPriorityWork(\"player\");"));
    }

    private static string GetMainWindowPlayerSourceText()
    {
        return GetRepoText("Views", "Main", "MainWindow.Player.cs");
    }

    private static string GetMainWindowSelectionSourceText()
    {
        return GetRepoText("Views", "Main", "MainWindow.Selection.cs");
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
