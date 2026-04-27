using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int PlayerTabIndex = 7;
        private const double PlayerTabBottomLayoutWidthThreshold = 1260d;
        private static readonly HashSet<string> WebViewPreferredPlayerExtensions = new(
            System.StringComparer.OrdinalIgnoreCase
        )
        {
            ".mp4",
            ".webm",
            ".m4v",
            ".ogv",
        };
        private bool _suppressPlayerThumbnailSelectionChanged;
        private bool _suppressPlayerTabActivationAutoOpen;
        private bool _isPlayerThumbnailCompactViewEnabled;
        private bool _hasPendingPlayerPlaybackRequest;
        private int _pendingPlayerStartMilliseconds;
        private bool _pendingPlayerPlayImmediately;
        private bool _pendingPlayerMute;
        private bool _pendingPlayerFocusTimeSlider;
        private bool _hasPendingWebViewPlaybackRequest;
        private int _pendingWebViewStartMilliseconds;
        private bool _pendingWebViewPlayImmediately;
        private bool _pendingWebViewMute;
        private bool _isWebViewPlayerActive;
        private bool _isWebViewPlayerBridgeRegistered;
        private bool _isPlayerUserPriorityReleasePending;
        private bool _isHandlingWebViewNativeFullscreenRequest;
        private bool _isSyncingDetachedWindowDomFullscreen;
        private Task<CoreWebView2Environment> _playerWebViewEnvironmentTask;
        private string _currentPlayerMoviePath = "";
        private string _currentWebViewPlayerPath = "";

        private ListView GetUpperTabPlayerList()
        {
            return _isPlayerThumbnailCompactViewEnabled
                ? PlayerThumbnailCompactList
                : PlayerThumbnailList;
        }

        private IEnumerable<ListView> GetAllUpperTabPlayerLists()
        {
            if (PlayerThumbnailList != null)
            {
                yield return PlayerThumbnailList;
            }

            if (PlayerThumbnailCompactList != null)
            {
                yield return PlayerThumbnailCompactList;
            }
        }

        private void SelectFirstUpperTabPlayerItemIfAvailable()
        {
            if (GetUpperTabPlayerList()?.Items.Count > 0 && GetUpperTabPlayerList().SelectedItem == null)
            {
                GetUpperTabPlayerList().SelectedIndex = 0;
            }
        }

        private void SelectUpperTabPlayerAsDefaultView()
        {
            SelectUpperTabByFixedIndex(PlayerTabIndex);
            SelectFirstUpperTabPlayerItemIfAvailable();
        }

        private MovieRecords GetSelectedUpperTabPlayerMovieRecord()
        {
            return GetUpperTabPlayerList()?.SelectedItem as MovieRecords;
        }

        private List<MovieRecords> GetSelectedUpperTabPlayerMovieRecords()
        {
            List<MovieRecords> items = [];
            if (GetUpperTabPlayerList()?.SelectedItems == null)
            {
                return items;
            }

            foreach (MovieRecords item in GetUpperTabPlayerList().SelectedItems)
            {
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private bool SelectUpperTabPlayerMovieRecord(MovieRecords record)
        {
            if (record == null)
            {
                return false;
            }

            bool selectionChanged = false;
            foreach (ListView list in GetAllUpperTabPlayerLists())
            {
                if (ReferenceEquals(list.SelectedItem, record))
                {
                    continue;
                }

                list.SelectedItem = record;
                selectionChanged = true;
            }

            if (selectionChanged)
            {
                // 同じ選択の再同期ではスクロールと可視範囲更新を積み直さない。
                GetUpperTabPlayerList()?.ScrollIntoView(record);
            }

            return selectionChanged;
        }

        // プレイヤータブへ飛ばす時だけ、選択イベントの自動再生を一時停止して狙った動画へ揃える。
        private void SyncUpperTabPlayerSelection(MovieRecords record)
        {
            if (record == null || GetUpperTabPlayerList() == null)
            {
                return;
            }

            _suppressPlayerThumbnailSelectionChanged = true;
            bool selectionChanged = false;
            try
            {
                selectionChanged = SelectUpperTabPlayerMovieRecord(record);
            }
            finally
            {
                _suppressPlayerThumbnailSelectionChanged = false;
            }

            if (selectionChanged)
            {
                RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "player-view-mode");
            }
        }

        // プレイヤータブ選択時は先頭選択を揃えたうえで、左ペインへ再生内容を同期する。
        private async void HandleUpperTabPlayerSelectionChanged(
            Stopwatch selectionStopwatch,
            int tabIndex
        )
        {
            UpdatePlayerTabLayoutMode();

            MovieRecords selectedMovie = RefreshUpperTabExtensionDetailFromCurrentSelection(
                selectFirstItem: true
            );
            if (selectedMovie == null)
            {
                selectionStopwatch.Stop();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"tab change end: tab={tabIndex} selected=none player_items={GetUpperTabPlayerList()?.Items.Count ?? 0} total_ms={selectionStopwatch.ElapsedMilliseconds}"
                );
                ShowPlayerEmptyState();
                return;
            }

            selectionStopwatch.Stop();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"tab change end: tab={tabIndex} selected='{selectedMovie.Movie_Name}' player_items={GetUpperTabPlayerList()?.Items.Count ?? 0} total_ms={selectionStopwatch.ElapsedMilliseconds}"
            );

            if (_suppressPlayerTabActivationAutoOpen)
            {
                return;
            }

            await OpenMovieInPlayerTabAsync(
                selectedMovie,
                0,
                playImmediately: true,
                mute: false,
                focusTimeSlider: false
            );
        }

        private async void PlayerThumbnailList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            List_SelectionChanged(sender, e);
            if (_suppressPlayerThumbnailSelectionChanged || TabPlayer?.IsSelected != true)
            {
                return;
            }

            MovieRecords selectedMovie = GetSelectedUpperTabPlayerMovieRecord();
            if (selectedMovie == null)
            {
                return;
            }

            SyncPlayerThumbnailSelectionAcrossViews(sender as ListView, selectedMovie);

            await OpenMovieInPlayerTabAsync(
                selectedMovie,
                0,
                playImmediately: true,
                mute: false,
                focusTimeSlider: false
            );
        }

        private void PlayerTabLayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePlayerTabLayoutMode();
            UpdateManualPlayerViewport();
        }

        private void PlayerThumbnailSingleColumnButton_Click(object sender, RoutedEventArgs e)
        {
            SetPlayerThumbnailCompactViewMode(false);
        }

        private void PlayerThumbnailCompactGridButton_Click(object sender, RoutedEventArgs e)
        {
            SetPlayerThumbnailCompactViewMode(true);
        }

        // プレイヤータブ上では一覧選択と再生面を同じ動画へ揃え、手動サムネ導線もここへ寄せる。
        private async Task OpenMovieInPlayerTabAsync(
            MovieRecords movie,
            int startMilliseconds,
            bool playImmediately,
            bool mute,
            bool focusTimeSlider,
            bool syncPlayerSelection = true
        )
        {
            if (
                movie == null
                || string.IsNullOrWhiteSpace(movie.Movie_Path)
                || !Path.Exists(movie.Movie_Path)
                || uxVideoPlayer == null
            )
            {
                return;
            }

            ReleasePendingPlayerUserPriorityWork();
            BeginUserPriorityWork("player");
            bool releaseUserPriorityOnExit = true;
            try
            {
                EnsureManualPlayerResizeTrackingHooked();
                UpdatePlayerTabLayoutMode();

                if (!ReferenceEquals(Tabs?.SelectedItem, TabPlayer))
                {
                    _suppressPlayerTabActivationAutoOpen = true;
                    try
                    {
                        if (syncPlayerSelection)
                        {
                            SyncUpperTabPlayerSelection(movie);
                        }

                        SelectUpperTabByFixedIndex(PlayerTabIndex);
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    }
                    finally
                    {
                        _suppressPlayerTabActivationAutoOpen = false;
                    }
                }
                else
                {
                    if (syncPlayerSelection)
                    {
                        SyncUpperTabPlayerSelection(movie);
                    }
                }

                // プレイヤータブの通常再生は WebView2 を正面採用し、
                // 手動サムネ位置合わせだけ従来の MediaElement を残す。
                if (ShouldUseWebViewPlayerForPlayerTab(movie.Movie_Path, focusTimeSlider))
                {
                    releaseUserPriorityOnExit = !await OpenMovieInWebViewPlayerAsync(
                        movie,
                        startMilliseconds,
                        playImmediately,
                        mute
                    );
                    return;
                }

                if (_isWebViewPlayerActive)
                {
                    ResetWebViewPlayerSurface();
                }

                ShowPlayerSurface();
                StopDispatcherTimerSafely(timer, nameof(timer));
                RememberPendingPlayerPlaybackRequest(
                    startMilliseconds,
                    playImmediately,
                    mute,
                    focusTimeSlider
                );

                bool sourceChanged =
                    !string.Equals(
                        _currentPlayerMoviePath,
                        movie.Movie_Path,
                        System.StringComparison.OrdinalIgnoreCase
                    )
                    || uxVideoPlayer.Source == null;
                if (sourceChanged)
                {
                    uxVideoPlayer.Stop();
                    uxVideoPlayer.Source = new System.Uri(movie.Movie_Path);
                    _currentPlayerMoviePath = movie.Movie_Path;
                    MarkPlayerUserPriorityReleasePending();
                    releaseUserPriorityOnExit = false;
                    return;
                }

                await ApplyPendingPlayerPlaybackRequestAsync();
            }
            finally
            {
                if (releaseUserPriorityOnExit)
                {
                    EndUserPriorityWork("player");
                }
            }
        }

        // MediaElement が苦手な形式だけ、Chromium の HTML5 video へ切り替えて再生互換を確保する。
        private async Task<bool> OpenMovieInWebViewPlayerAsync(
            MovieRecords movie,
            int startMilliseconds,
            bool playImmediately,
            bool mute
        )
        {
            if (movie == null || string.IsNullOrWhiteSpace(movie.Movie_Path) || uxWebVideoPlayer == null)
            {
                return false;
            }

            if (!await EnsureWebVideoPlayerReadyAsync())
            {
                return false;
            }

            _isWebViewPlayerActive = true;
            _hasPendingPlayerPlaybackRequest = false;
            _currentPlayerMoviePath = "";
            uxVideoPlayer.Stop();
            uxVideoPlayer.Source = null;
            StopDispatcherTimerSafely(timer, nameof(timer));
            ShowPlayerSurface();
            UpdateManualPlayerViewport();
            RememberPendingWebViewPlaybackRequest(
                startMilliseconds,
                playImmediately,
                mute
            );

            bool sourceChanged =
                !string.Equals(
                    _currentWebViewPlayerPath,
                    movie.Movie_Path,
                    System.StringComparison.OrdinalIgnoreCase
                )
                || uxWebVideoPlayer.Source == null
                || !string.Equals(
                    uxWebVideoPlayer.Source.LocalPath,
                    movie.Movie_Path,
                    System.StringComparison.OrdinalIgnoreCase
                );
            if (sourceChanged)
            {
                uxWebVideoPlayer.Source = new System.Uri(movie.Movie_Path);
                _currentWebViewPlayerPath = movie.Movie_Path;
                MarkPlayerUserPriorityReleasePending();
                return true;
            }

            await ApplyPendingWebViewPlaybackRequestAsync();
            return false;
        }

        private void MarkPlayerUserPriorityReleasePending()
        {
            _isPlayerUserPriorityReleasePending = true;
        }

        private void ReleasePendingPlayerUserPriorityWork()
        {
            if (!_isPlayerUserPriorityReleasePending)
            {
                return;
            }

            _isPlayerUserPriorityReleasePending = false;
            EndUserPriorityWork("player");
        }

        // Source 差し替えと MediaOpened の間でも、再生要求を落とさず持ち運ぶ。
        private void RememberPendingPlayerPlaybackRequest(
            int startMilliseconds,
            bool playImmediately,
            bool mute,
            bool focusTimeSlider
        )
        {
            _hasPendingPlayerPlaybackRequest = true;
            _pendingPlayerStartMilliseconds = startMilliseconds;
            _pendingPlayerPlayImmediately = playImmediately;
            _pendingPlayerMute = mute;
            _pendingPlayerFocusTimeSlider = focusTimeSlider;
        }

        private async Task ApplyPendingPlayerPlaybackRequestAsync()
        {
            bool shouldReleaseUserPriority =
                _hasPendingPlayerPlaybackRequest || _isPlayerUserPriorityReleasePending;
            if (!_hasPendingPlayerPlaybackRequest || uxVideoPlayer == null)
            {
                if (shouldReleaseUserPriority)
                {
                    ReleasePendingPlayerUserPriorityWork();
                }
                return;
            }

            try
            {
                int startMilliseconds = _pendingPlayerStartMilliseconds;
                bool playImmediately = _pendingPlayerPlayImmediately;
                bool mute = _pendingPlayerMute;
                bool focusTimeSlider = _pendingPlayerFocusTimeSlider;
                _hasPendingPlayerPlaybackRequest = false;

                double restoreVolume = uxVolumeSlider?.Value ?? 0.5d;
                uxVideoPlayer.Volume = mute ? 0d : restoreVolume;

                if (startMilliseconds > 0)
                {
                    uxVideoPlayer.Position = System.TimeSpan.FromMilliseconds(startMilliseconds);
                }

                uxVideoPlayer.Play();
                UpdateManualPlayerViewport();
                UpdatePlayerPositionUi(uxVideoPlayer.Position);

                if (playImmediately)
                {
                    IsPlaying = true;
                    TryStartDispatcherTimer(timer, nameof(timer));
                }
                else
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    uxVideoPlayer.Pause();
                    IsPlaying = false;
                    StopDispatcherTimerSafely(timer, nameof(timer));

                    if (!mute)
                    {
                        uxVideoPlayer.Volume = restoreVolume;
                    }
                }

                if (focusTimeSlider)
                {
                    uxTimeSlider.Focus();
                }
            }
            finally
            {
                if (shouldReleaseUserPriority)
                {
                    ReleasePendingPlayerUserPriorityWork();
                }
            }
        }

        private void ShowPlayerSurface()
        {
            SetPlayerVisibilityIfChanged(PlayerArea, Visibility.Visible);
            SetPlayerVisibilityIfChanged(
                PlayerController,
                _isWebViewPlayerActive ? Visibility.Collapsed : Visibility.Visible
            );
            SetPlayerVisibilityIfChanged(
                uxVideoPlayer,
                _isWebViewPlayerActive ? Visibility.Collapsed : Visibility.Visible
            );
            if (uxWebVideoPlayer != null)
            {
                SetPlayerVisibilityIfChanged(
                    uxWebVideoPlayer,
                    _isWebViewPlayerActive ? Visibility.Visible : Visibility.Collapsed
                );
            }
            if (PlayerEmptyState != null)
            {
                SetPlayerVisibilityIfChanged(PlayerEmptyState, Visibility.Collapsed);
            }
        }

        private void ShowPlayerEmptyState()
        {
            if (PlayerEmptyState != null)
            {
                SetPlayerVisibilityIfChanged(PlayerEmptyState, Visibility.Visible);
            }
        }

        private static void SetPlayerVisibilityIfChanged(UIElement element, Visibility visibility)
        {
            if (element == null || element.Visibility == visibility)
            {
                return;
            }

            // 同じ表示状態の再代入を避け、Player 操作後の余計なレイアウト更新を増やさない。
            element.Visibility = visibility;
        }

        // WebView2 は別 HWND として前面に出るため、左ドロワー操作中だけ描画面を退避する。
        private void SetWebViewPlayerHiddenForLeftDrawer(bool hidden)
        {
            if (uxWebVideoPlayer == null || !_isWebViewPlayerActive)
            {
                return;
            }

            if (hidden)
            {
                if (uxWebVideoPlayer.Visibility == Visibility.Visible)
                {
                    uxWebVideoPlayer.Visibility = Visibility.Hidden;
                    DebugRuntimeLog.Write("ui-tempo", "player webview hidden for left drawer");
                }

                return;
            }

            if (
                TabPlayer?.IsSelected == true
                && !_isDetachedPlayerFullscreenActive
                && uxWebVideoPlayer.Visibility != Visibility.Visible
            )
            {
                uxWebVideoPlayer.Visibility = Visibility.Visible;
                DebugRuntimeLog.Write("ui-tempo", "player webview restored after left drawer");
            }
        }

        // タブを離れたら音だけ残さないよう一旦止め、戻った時は同じ動画を再開しやすくする。
        private void PausePlayerTabPlaybackForBackground()
        {
            if (_isDetachedPlayerFullscreenActive)
            {
                return;
            }

            if (PlayerArea?.Visibility != Visibility.Visible)
            {
                return;
            }

            if (_isWebViewPlayerActive)
            {
                _ = PauseWebViewPlayerAsync();
                return;
            }

            if (uxVideoPlayer == null)
            {
                return;
            }

            uxVideoPlayer.Pause();
            IsPlaying = false;
            StopDispatcherTimerSafely(timer, nameof(timer));
        }

        private void UpdatePlayerPositionUi(System.TimeSpan position)
        {
            if (uxTimeSlider != null)
            {
                uxTimeSlider.Value = position.TotalMilliseconds;
            }

            if (uxTime != null)
            {
                uxTime.Text = position.ToString(@"hh\:mm\:ss");
            }
        }

        private bool TryGetPlayerTabViewportSize(out double availableWidth, out double availableHeight)
        {
            availableWidth = 0d;
            availableHeight = 0d;

            if (TabPlayer?.IsSelected != true || PlayerSurfaceHost == null)
            {
                return false;
            }

            availableWidth = System.Math.Max(0d, PlayerSurfaceHost.ActualWidth - 16d);
            availableHeight = System.Math.Max(0d, PlayerSurfaceHost.ActualHeight - 16d);
            return availableWidth > 0d && availableHeight > 0d;
        }

        // 横幅が足りない時は一覧を下へ落とし、プレイヤー面積を優先する。
        private void UpdatePlayerTabLayoutMode()
        {
            if (
                PlayerTabLayoutRoot == null
                || PlayerSurfaceHost == null
                || PlayerThumbnailHost == null
                || PlayerTabMainColumn == null
                || PlayerTabGapColumn == null
                || PlayerTabSideColumn == null
                || PlayerTabTopRow == null
                || PlayerTabGapRow == null
                || PlayerTabBottomRow == null
            )
            {
                return;
            }

            PlayerThumbnailHost.Visibility = Visibility.Visible;
            bool useBottomLayout =
                PlayerTabLayoutRoot.ActualWidth > 0d
                && PlayerTabLayoutRoot.ActualWidth < PlayerTabBottomLayoutWidthThreshold;

            if (useBottomLayout)
            {
                Grid.SetRow(PlayerSurfaceHost, 0);
                Grid.SetColumn(PlayerSurfaceHost, 0);
                Grid.SetColumnSpan(PlayerSurfaceHost, 3);
                Grid.SetRow(PlayerThumbnailHost, 2);
                Grid.SetColumn(PlayerThumbnailHost, 0);
                Grid.SetColumnSpan(PlayerThumbnailHost, 3);

                PlayerTabMainColumn.Width = new GridLength(1d, GridUnitType.Star);
                PlayerTabGapColumn.Width = new GridLength(0d);
                PlayerTabSideColumn.Width = new GridLength(0d);
                PlayerTabTopRow.Height = new GridLength(1d, GridUnitType.Star);
                PlayerTabGapRow.Height = new GridLength(12d);
                PlayerTabBottomRow.Height = new GridLength(320d);
                return;
            }

            Grid.SetRow(PlayerSurfaceHost, 0);
            Grid.SetColumn(PlayerSurfaceHost, 0);
            Grid.SetColumnSpan(PlayerSurfaceHost, 1);
            Grid.SetRow(PlayerThumbnailHost, 0);
            Grid.SetColumn(PlayerThumbnailHost, 2);
            Grid.SetColumnSpan(PlayerThumbnailHost, 1);

            PlayerTabMainColumn.Width = new GridLength(1d, GridUnitType.Star);
            PlayerTabGapColumn.Width = new GridLength(16d);
            PlayerTabSideColumn.Width = new GridLength(280d);
            PlayerTabTopRow.Height = new GridLength(1d, GridUnitType.Star);
            PlayerTabGapRow.Height = new GridLength(0d);
            PlayerTabBottomRow.Height = new GridLength(0d);
        }

        // 右側一覧は 1列詳細と 3列小サムネを切り替え、選択中の動画だけは必ず持ち歩く。
        private void SetPlayerThumbnailCompactViewMode(bool enabled)
        {
            if (_isPlayerThumbnailCompactViewEnabled == enabled)
            {
                return;
            }

            _isPlayerThumbnailCompactViewEnabled = enabled;

            if (PlayerThumbnailList != null)
            {
                PlayerThumbnailList.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            }

            if (PlayerThumbnailCompactList != null)
            {
                PlayerThumbnailCompactList.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PlayerThumbnailSingleColumnButton != null)
            {
                PlayerThumbnailSingleColumnButton.IsChecked = !enabled;
            }

            if (PlayerThumbnailCompactGridButton != null)
            {
                PlayerThumbnailCompactGridButton.IsChecked = enabled;
            }

            MovieRecords selectedMovie = GetSelectedUpperTabPlayerMovieRecord();
            if (selectedMovie == null)
            {
                selectedMovie = PlayerThumbnailList?.SelectedItem as MovieRecords
                    ?? PlayerThumbnailCompactList?.SelectedItem as MovieRecords;
            }

            if (selectedMovie == null)
            {
                SelectFirstUpperTabPlayerItemIfAvailable();
                RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "player-view-mode");
                return;
            }

            _suppressPlayerThumbnailSelectionChanged = true;
            try
            {
                foreach (ListView list in GetAllUpperTabPlayerLists())
                {
                    if (ReferenceEquals(list.SelectedItem, selectedMovie))
                    {
                        continue;
                    }

                    list.SelectedItem = selectedMovie;
                }

                GetUpperTabPlayerList()?.ScrollIntoView(selectedMovie);
            }
            finally
            {
                _suppressPlayerThumbnailSelectionChanged = false;
            }

            RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "player-view-mode");
        }

        private void SyncPlayerThumbnailSelectionAcrossViews(
            ListView sourceList,
            MovieRecords selectedMovie
        )
        {
            if (selectedMovie == null)
            {
                return;
            }

            _suppressPlayerThumbnailSelectionChanged = true;
            try
            {
                foreach (ListView list in GetAllUpperTabPlayerLists())
                {
                    if (ReferenceEquals(list, sourceList))
                    {
                        continue;
                    }

                    if (!ReferenceEquals(list.SelectedItem, selectedMovie))
                    {
                        list.SelectedItem = selectedMovie;
                    }
                }
            }
            finally
            {
                _suppressPlayerThumbnailSelectionChanged = false;
            }
        }

        // プレイヤータブは Grid サムネを借りるので、画像系の扱いだけ Grid 固定IDへ正規化する。
        private static int ResolvePlayerTabGridProxyTabIndex(int tabIndex)
        {
            return tabIndex == PlayerTabIndex ? UpperTabGridFixedIndex : tabIndex;
        }

        private void RememberPendingWebViewPlaybackRequest(
            int startMilliseconds,
            bool playImmediately,
            bool mute
        )
        {
            _hasPendingWebViewPlaybackRequest = true;
            _pendingWebViewStartMilliseconds = startMilliseconds;
            _pendingWebViewPlayImmediately = playImmediately;
            _pendingWebViewMute = mute;
        }

        private async Task ApplyPendingWebViewPlaybackRequestAsync()
        {
            bool shouldReleaseUserPriority =
                _hasPendingWebViewPlaybackRequest || _isPlayerUserPriorityReleasePending;
            if (
                !_hasPendingWebViewPlaybackRequest
                || !_isWebViewPlayerActive
                || uxWebVideoPlayer?.CoreWebView2 == null
            )
            {
                if (shouldReleaseUserPriority)
                {
                    ReleasePendingPlayerUserPriorityWork();
                }
                return;
            }

            try
            {
                int startMilliseconds = _pendingWebViewStartMilliseconds;
                bool playImmediately = _pendingWebViewPlayImmediately;
                bool mute = _pendingWebViewMute;
                _hasPendingWebViewPlaybackRequest = false;

                string seconds = (startMilliseconds / 1000d).ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                );
                string volume = uxVolumeSlider.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                );
                string script = $$"""
                    (() => {
                      const player = document.querySelector('video');
                      if (!player) {
                        return;
                      }

                      const applyPlayback = () => {
                        try {
                          player.currentTime = {{seconds}};
                        } catch {}

                        player.muted = {{(mute ? "true" : "false")}};
                        player.volume = {{volume}};
                        player.dataset.indigoPlayerHostVolumeApplied = '1';

                        const playPromise = player.play();
                        if (playPromise) {
                          playPromise.catch(() => {});
                        }

                        if (!{{(playImmediately ? "true" : "false")}}) {
                          Promise.resolve(playPromise).finally(() => player.pause());
                        }
                      };

                      if (player.readyState >= 1) {
                        applyPlayback();
                        return;
                      }

                      player.addEventListener('loadedmetadata', applyPlayback, { once: true });
                    })();
                    """;
                await uxWebVideoPlayer.ExecuteScriptAsync(script);
                IsPlaying = playImmediately;
                UpdatePlayerPositionUi(System.TimeSpan.FromMilliseconds(startMilliseconds));
            }
            finally
            {
                if (shouldReleaseUserPriority)
                {
                    ReleasePendingPlayerUserPriorityWork();
                }
            }
        }

        private async void UxWebVideoPlayer_NavigationCompleted(
            object sender,
            CoreWebView2NavigationCompletedEventArgs e
        )
        {
            if (!e.IsSuccess || !_isWebViewPlayerActive)
            {
                return;
            }

            await ApplyPendingWebViewPlaybackRequestAsync();
        }

        private void UxWebVideoPlayer_WebMessageReceived(
            object sender,
            CoreWebView2WebMessageReceivedEventArgs e
        )
        {
            const string playerVolumeMessagePrefix = "player-volume:";
            string message = e.TryGetWebMessageAsString();
            if (
                string.IsNullOrWhiteSpace(message)
                || !message.StartsWith(playerVolumeMessagePrefix, System.StringComparison.Ordinal)
            )
            {
                return;
            }

            string volumeText = message[playerVolumeMessagePrefix.Length..];
            if (
                !double.TryParse(
                    volumeText,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double volume
                )
            )
            {
                return;
            }

            SyncPlayerVolumeFromWebView(volume);
        }

        // ネイティブの拡大ボタンは直接差し替えず、fullscreen 要求を横取りして専用Windowへ流す。
        private async void UxWebVideoPlayer_ContainsFullScreenElementChanged(
            object sender,
            object e
        )
        {
            if (uxWebVideoPlayer?.CoreWebView2 == null)
            {
                return;
            }

            bool containsFullscreenElement = uxWebVideoPlayer.CoreWebView2.ContainsFullScreenElement;
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"player native fullscreen changed: contains={containsFullscreenElement} detached={_isDetachedPlayerFullscreenActive} handling={_isHandlingWebViewNativeFullscreenRequest} syncing={_isSyncingDetachedWindowDomFullscreen}"
            );

            if (_isDetachedPlayerFullscreenActive && !containsFullscreenElement)
            {
                if (_isSyncingDetachedWindowDomFullscreen)
                {
                    return;
                }

                await CloseMainWindowPlayerFullscreenAsync();
                return;
            }

            if (
                !containsFullscreenElement
                || _isDetachedPlayerFullscreenActive
                || _isHandlingWebViewNativeFullscreenRequest
                || _isSyncingDetachedWindowDomFullscreen
                || !_isWebViewPlayerActive
                || string.IsNullOrWhiteSpace(_currentWebViewPlayerPath)
            )
            {
                return;
            }

            _isHandlingWebViewNativeFullscreenRequest = true;
            try
            {
                await uxWebVideoPlayer.ExecuteScriptAsync(
                    """
                    (() => {
                      if (!document.fullscreenElement) {
                        return;
                      }

                      const result = document.exitFullscreen();
                      if (result && typeof result.catch === 'function') {
                        result.catch(() => {});
                      }
                    })();
                    """
                );
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                await OpenMainWindowPlayerFullscreenAsync();
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"player native fullscreen handoff failed: {ex.GetType().Name} {ex.Message}"
                );
            }
            finally
            {
                _isHandlingWebViewNativeFullscreenRequest = false;
            }
        }

        // 専用Windowへ移した後も video 要素を fullscreen 状態へ揃え、ネイティブアイコンを戻る表示へ切り替える。
        private async Task SetDetachedWindowDomFullscreenAsync(bool enable)
        {
            if (uxWebVideoPlayer?.CoreWebView2 == null)
            {
                return;
            }

            _isSyncingDetachedWindowDomFullscreen = true;
            try
            {
                string script = enable
                    ? """
                    (() => {
                      const player = document.querySelector('video');
                      if (!player || document.fullscreenElement === player) {
                        return;
                      }

                      const result = player.requestFullscreen?.();
                      if (result && typeof result.catch === 'function') {
                        result.catch(() => {});
                      }
                    })();
                    """
                    : """
                    (() => {
                      if (!document.fullscreenElement) {
                        return;
                      }

                      const result = document.exitFullscreen?.();
                      if (result && typeof result.catch === 'function') {
                        result.catch(() => {});
                      }
                    })();
                    """;

                await uxWebVideoPlayer.ExecuteScriptAsync(script);
                await Task.Delay(120);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"player native fullscreen sync failed: enable={enable} {ex.GetType().Name} {ex.Message}"
                );
            }
            finally
            {
                _isSyncingDetachedWindowDomFullscreen = false;
            }
        }

        private static bool ShouldUseWebViewPlayerForPlayerTab(
            string moviePath,
            bool focusTimeSlider
        )
        {
            if (focusTimeSlider)
            {
                return false;
            }

            string extension = Path.GetExtension(moviePath) ?? "";
            return WebViewPreferredPlayerExtensions.Contains(extension);
        }

        // dotnet 起動時でも Program Files 側へ書こうとしないよう、プレーヤー専用の保存先を固定する。
        private async Task<CoreWebView2Environment> GetOrCreatePlayerWebViewEnvironmentAsync()
        {
            if (_playerWebViewEnvironmentTask == null)
            {
                _playerWebViewEnvironmentTask = CreatePlayerWebViewEnvironmentAsync();
            }

            try
            {
                return await _playerWebViewEnvironmentTask;
            }
            catch
            {
                _playerWebViewEnvironmentTask = null;
                throw;
            }
        }

        private static async Task<CoreWebView2Environment> CreatePlayerWebViewEnvironmentAsync()
        {
            string userDataFolder = Path.Combine(
                AppLocalDataPaths.RootPath,
                "WebView2",
                "Player"
            );
            Directory.CreateDirectory(userDataFolder);
            return await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder
            );
        }

        private async Task<bool> EnsureWebVideoPlayerReadyAsync()
        {
            if (uxWebVideoPlayer == null)
            {
                return false;
            }

            try
            {
                if (uxWebVideoPlayer.CoreWebView2 != null)
                {
                    await EnsureWebVideoPlayerBridgeAsync();
                    return true;
                }

                CoreWebView2Environment environment = await GetOrCreatePlayerWebViewEnvironmentAsync();
                await uxWebVideoPlayer.EnsureCoreWebView2Async(environment);
                await EnsureWebVideoPlayerBridgeAsync();
                return true;
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(
                    "WebM 再生には WebView2 Runtime が必要です。",
                    "プレイヤー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"player webview init denied: {ex.Message}"
                );
                MessageBox.Show(
                    $"プレーヤー用 WebView2 の初期化に失敗しました。{Environment.NewLine}{ex.Message}",
                    "プレイヤー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return false;
            }
        }

        // 動画ページへ共通の見た目と通知橋を注入し、音量変更を次の動画にも流す。
        private async Task EnsureWebVideoPlayerBridgeAsync()
        {
            if (_isWebViewPlayerBridgeRegistered || uxWebVideoPlayer?.CoreWebView2 == null)
            {
                return;
            }

            uxWebVideoPlayer.CoreWebView2.WebMessageReceived += UxWebVideoPlayer_WebMessageReceived;
            uxWebVideoPlayer.CoreWebView2.DownloadStarting += UxWebVideoPlayer_DownloadStarting;
            uxWebVideoPlayer.CoreWebView2.ContainsFullScreenElementChanged +=
                UxWebVideoPlayer_ContainsFullScreenElementChanged;
            await uxWebVideoPlayer.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                """
                (() => {
                  const bindPlayer = () => {
                    const player = document.querySelector('video');
                    if (!player || player.dataset.indigoPlayerBound === '1') {
                      return;
                    }

                    player.dataset.indigoPlayerBound = '1';
                    document.documentElement.style.background = '#000';
                    document.documentElement.style.height = '100%';
                    document.body.style.margin = '0';
                    document.body.style.background = '#000';
                    document.body.style.height = '100%';
                    document.body.style.overflow = 'hidden';
                    player.style.width = '100vw';
                    player.style.height = '100vh';
                    player.style.objectFit = 'contain';
                    player.style.background = '#000';
                    player.controls = true;

                    const notifyVolume = () => {
                      if (player.dataset.indigoPlayerHostVolumeApplied !== '1') {
                        return;
                      }

                      try {
                        chrome.webview.postMessage(`player-volume:${player.volume}`);
                      } catch {}
                    };

                    player.addEventListener('volumechange', notifyVolume);
                  };

                  if (document.readyState === 'loading') {
                    document.addEventListener('DOMContentLoaded', bindPlayer, { once: true });
                  } else {
                    bindPlayer();
                  }

                  new MutationObserver(bindPlayer).observe(document.documentElement, {
                    childList: true,
                    subtree: true
                  });
                })();
                """
            );
            _isWebViewPlayerBridgeRegistered = true;
        }

        // 非対応拡張子でダウンロードへ落ちても、その場で止めてプレイヤータブから外へ出さない。
        private void UxWebVideoPlayer_DownloadStarting(
            object sender,
            CoreWebView2DownloadStartingEventArgs e
        )
        {
            e.Cancel = true;

            string downloadUri = e.DownloadOperation?.Uri ?? "";
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"player webview download canceled: uri='{downloadUri}'"
            );
        }

        private async Task PauseWebViewPlayerAsync()
        {
            if (!_isWebViewPlayerActive || uxWebVideoPlayer?.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                await uxWebVideoPlayer.ExecuteScriptAsync("document.querySelector('video')?.pause();");
            }
            catch
            {
                // 破棄競合中は黙って抜け、タブ切替を止めない。
            }

            IsPlaying = false;
            StopDispatcherTimerSafely(timer, nameof(timer));
        }

        private void ResetWebViewPlayerSurface()
        {
            _hasPendingWebViewPlaybackRequest = false;
            _isWebViewPlayerActive = false;
            _currentWebViewPlayerPath = "";

            if (uxWebVideoPlayer == null)
            {
                return;
            }

            try
            {
                if (uxWebVideoPlayer.CoreWebView2 != null)
                {
                    uxWebVideoPlayer.CoreWebView2.NavigateToString("<html><body style='background:#000'></body></html>");
                }
            }
            catch
            {
                // 破棄や初期化途中の競合では安全側で握りつぶす。
            }

            uxWebVideoPlayer.Visibility = Visibility.Collapsed;
        }
    }
}
