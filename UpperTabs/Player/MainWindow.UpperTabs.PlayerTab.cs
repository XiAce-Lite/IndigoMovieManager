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
        private bool _suppressPlayerThumbnailSelectionChanged;
        private bool _suppressPlayerTabActivationAutoOpen;
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
        private string _currentPlayerMoviePath = "";
        private string _currentWebViewPlayerPath = "";

        private ListView GetUpperTabPlayerList()
        {
            return PlayerThumbnailList;
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

        private void SelectUpperTabPlayerMovieRecord(MovieRecords record)
        {
            if (record == null || GetUpperTabPlayerList() == null)
            {
                return;
            }

            GetUpperTabPlayerList().SelectedItem = record;
            GetUpperTabPlayerList().ScrollIntoView(record);
        }

        // プレイヤータブへ飛ばす時だけ、選択イベントの自動再生を一時停止して狙った動画へ揃える。
        private void SyncUpperTabPlayerSelection(MovieRecords record)
        {
            if (record == null || GetUpperTabPlayerList() == null)
            {
                return;
            }

            _suppressPlayerThumbnailSelectionChanged = true;
            try
            {
                SelectUpperTabPlayerMovieRecord(record);
            }
            finally
            {
                _suppressPlayerThumbnailSelectionChanged = false;
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

        // プレイヤータブ上では一覧選択と再生面を同じ動画へ揃え、手動サムネ導線もここへ寄せる。
        private async Task OpenMovieInPlayerTabAsync(
            MovieRecords movie,
            int startMilliseconds,
            bool playImmediately,
            bool mute,
            bool focusTimeSlider
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

            EnsureManualPlayerResizeTrackingHooked();
            UpdatePlayerTabLayoutMode();

            if (!ReferenceEquals(Tabs?.SelectedItem, TabPlayer))
            {
                _suppressPlayerTabActivationAutoOpen = true;
                try
                {
                    SyncUpperTabPlayerSelection(movie);
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
                SyncUpperTabPlayerSelection(movie);
            }

            if (IsWebViewPreferredPlayerPath(movie.Movie_Path))
            {
                await OpenMovieInWebViewPlayerAsync(
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
                return;
            }

            await ApplyPendingPlayerPlaybackRequestAsync();
        }

        // MediaElement が苦手な形式だけ、Chromium の HTML5 video へ切り替えて再生互換を確保する。
        private async Task OpenMovieInWebViewPlayerAsync(
            MovieRecords movie,
            int startMilliseconds,
            bool playImmediately,
            bool mute
        )
        {
            if (movie == null || string.IsNullOrWhiteSpace(movie.Movie_Path) || uxWebVideoPlayer == null)
            {
                return;
            }

            if (!await EnsureWebVideoPlayerReadyAsync())
            {
                return;
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
                return;
            }

            await ApplyPendingWebViewPlaybackRequestAsync();
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
            if (!_hasPendingPlayerPlaybackRequest || uxVideoPlayer == null)
            {
                return;
            }

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

        private void ShowPlayerSurface()
        {
            PlayerArea.Visibility = Visibility.Visible;
            PlayerController.Visibility = _isWebViewPlayerActive
                ? Visibility.Collapsed
                : Visibility.Visible;
            uxVideoPlayer.Visibility = _isWebViewPlayerActive
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (uxWebVideoPlayer != null)
            {
                uxWebVideoPlayer.Visibility = _isWebViewPlayerActive
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            if (PlayerEmptyState != null)
            {
                PlayerEmptyState.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowPlayerEmptyState()
        {
            if (PlayerEmptyState != null)
            {
                PlayerEmptyState.Visibility = Visibility.Visible;
            }
        }

        // タブを離れたら音だけ残さないよう一旦止め、戻った時は同じ動画を再開しやすくする。
        private void PausePlayerTabPlaybackForBackground()
        {
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
            if (
                !_hasPendingWebViewPlaybackRequest
                || !_isWebViewPlayerActive
                || uxWebVideoPlayer?.CoreWebView2 == null
            )
            {
                return;
            }

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

        private static bool IsWebViewPreferredPlayerPath(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return false;
            }

            return string.Equals(
                Path.GetExtension(moviePath),
                ".webm",
                System.StringComparison.OrdinalIgnoreCase
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
                    return true;
                }

                // 既に既定環境で初期化済みの可能性があるため、追加の環境指定はせず既定経路へ揃える。
                await uxWebVideoPlayer.EnsureCoreWebView2Async();
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
