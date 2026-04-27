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
    public void PlayerVolume_保存値が0または100へリセットされた時は起動時に50へ戻す()
    {
        string playerSource = GetMainWindowPlayerSourceText();
        string windowSource = GetRepoText("Views", "Main", "MainWindow.xaml.cs");

        Assert.That(playerSource, Does.Contain("private const double DefaultPlayerVolume = 0.5d;"));
        Assert.That(playerSource, Does.Contain("private static double ResolveSavedPlayerVolumeSetting(double volume)"));
        Assert.That(playerSource, Does.Contain("return resolvedVolume <= 0d || resolvedVolume >= 1d"));
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
        Assert.That(mainWindowPlayerSource, Does.Contain("indigoPlayerHostVolumeApplying = '1'"));
        Assert.That(upperTabPlayerSource, Does.Contain("indigoPlayerHostVolumeApplying = '1'"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("player.dataset.indigoPlayerHostVolumeApplied !== '1'")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("player.dataset.indigoPlayerHostVolumeApplying === '1'")
        );
        Assert.That(upperTabPlayerSource, Does.Not.Contain("notifyVolume();"));
    }

    [Test]
    public void WebViewPlayer_動画切り替え時の100パーセント通知は保存しない()
    {
        string playerSource = GetMainWindowPlayerSourceText();

        Assert.That(playerSource, Does.Contain("if (resolvedVolume >= 1d && currentVolume < 0.999d)"));
        Assert.That(playerSource, Does.Contain("player webview default volume ignored"));
        Assert.That(playerSource, Does.Contain("return;"));
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
        Assert.That(
            selectionSource,
            Does.Contain("if (!ReferenceEquals(sourceList.SelectedItem, record))")
        );
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
    public void PlayerSurface_同一表示状態とサイズは再代入しない()
    {
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();

        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private static void SetPlayerVisibilityIfChanged(UIElement element, Visibility visibility)")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("element == null || element.Visibility == visibility")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("SetPlayerVisibilityIfChanged(PlayerArea, Visibility.Visible);")
        );
        Assert.That(
            mainWindowPlayerSource,
            Does.Contain("private static void SetPlayerElementSizeIfChanged(")
        );
        Assert.That(
            mainWindowPlayerSource,
            Does.Contain("ArePlayerLayoutLengthsEqual(element.Width, width)")
        );
        Assert.That(
            mainWindowPlayerSource,
            Does.Contain("ArePlayerLayoutLengthsEqual(element.Height, height)")
        );
        Assert.That(
            mainWindowPlayerSource,
            Does.Contain("double.IsNaN(current) && double.IsNaN(next)")
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
        Assert.That(upperTabPlayerSource, Does.Contain("bool activeListSelectionChanged = false;"));
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("if (activeListSelectionChanged)")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("if (!ReferenceEquals(list.SelectedItem, selectedMovie))")
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
    public void PlayerDispatcherBackgroundWait_同時待機は1本へ畳む()
    {
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();

        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private DispatcherOperation _playerBackgroundYieldOperation;")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("private async Task WaitForPlayerDispatcherBackgroundAsync()")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("pendingOperation = Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("_playerBackgroundYieldOperation = pendingOperation;")
        );
        Assert.That(
            upperTabPlayerSource,
            Does.Contain("await WaitForPlayerDispatcherBackgroundAsync();")
        );
    }

    [Test]
    public void OpenMovieInPlayerTabAsync_Player操作中はuser_priority_scopeで囲む()
    {
        // Player 再生開始中は watch/poll を後ろへ逃がし、早期 return でも必ず解除する。
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string mainWindowPlayerSource = GetMainWindowPlayerSourceText();
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");

        Assert.That(upperTabPlayerSource, Does.Contain("BeginUserPriorityWork(\"player\");"));
        Assert.That(upperTabPlayerSource, Does.Contain("MarkPlayerUserPriorityReleasePending();"));
        Assert.That(upperTabPlayerSource, Does.Contain("ReleasePendingPlayerUserPriorityWork();"));
        Assert.That(upperTabPlayerSource, Does.Contain("if (!e.IsSuccess)"));
        Assert.That(mainWindowXaml, Does.Contain("MediaEnded=\"UxVideoPlayer_MediaEnded\""));
        Assert.That(upperTabPlayerSource, Does.Contain("_hasPendingWebViewPlaybackRequest = false;"));
        Assert.That(mainWindowXaml, Does.Contain("NavigationStarting=\"UxWebVideoPlayer_NavigationStarting\""));
        Assert.That(upperTabPlayerSource, Does.Contain("e.NavigationId != _pendingWebViewNavigationId"));
        Assert.That(mainWindowXaml, Does.Contain("MediaFailed=\"UxVideoPlayer_MediaFailed\""));
        Assert.That(mainWindowPlayerSource, Does.Contain("private void UxVideoPlayer_MediaFailed("));
        Assert.That(mainWindowPlayerSource, Does.Contain("private void UxVideoPlayer_MediaEnded("));
        Assert.That(mainWindowPlayerSource, Does.Contain("IsPlaying = false;"));
        Assert.That(mainWindowPlayerSource, Does.Contain("_hasPendingPlayerPlaybackRequest = false;"));
        Assert.That(upperTabPlayerSource, Does.Contain("try"));
        Assert.That(upperTabPlayerSource, Does.Contain("finally"));
        Assert.That(upperTabPlayerSource, Does.Contain("EndUserPriorityWork(\"player\");"));
    }

    [Test]
    public void ResetWebViewPlayerSurface_WebView停止時もpending_user_priorityを解除する()
    {
        // WebView の NavigationCompleted が後着しても、reset 側で優先区間を確実に畳む。
        string upperTabPlayerSource = GetUpperTabPlayerSourceText();
        string resetMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private void ResetWebViewPlayerSurface()"
        );
        string openMovieMethod = GetMethodBlock(
            upperTabPlayerSource,
            "private async Task OpenMovieInPlayerTabAsync("
        );

        int webViewActiveCheckIndex = openMovieMethod.IndexOf(
            "if (_isWebViewPlayerActive)",
            StringComparison.Ordinal
        );
        int resetCallIndex = openMovieMethod.IndexOf(
            "ResetWebViewPlayerSurface();",
            StringComparison.Ordinal
        );

        Assert.That(webViewActiveCheckIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(resetCallIndex, Is.GreaterThan(webViewActiveCheckIndex));
        Assert.That(resetMethod, Does.Contain("_hasPendingWebViewPlaybackRequest = false;"));
        Assert.That(resetMethod, Does.Contain("_isWebViewPlayerActive = false;"));
        Assert.That(resetMethod, Does.Contain("_pendingWebViewNavigationId = 0;"));
        Assert.That(resetMethod, Does.Contain("ReleasePendingPlayerUserPriorityWork();"));
        Assert.That(
            resetMethod.IndexOf("ReleasePendingPlayerUserPriorityWork();", StringComparison.Ordinal),
            Is.LessThan(resetMethod.IndexOf("if (uxWebVideoPlayer == null)", StringComparison.Ordinal))
        );
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

    private static string GetMethodBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文開始が見つかりません。");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }
        }

        Assert.Fail($"{signature} の本文終了が見つかりません。");
        return string.Empty;
    }
}
