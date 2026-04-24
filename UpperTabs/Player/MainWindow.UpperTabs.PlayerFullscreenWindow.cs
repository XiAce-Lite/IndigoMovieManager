using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IndigoMovieManager
{
    internal sealed class PlayerWebViewPlaybackSnapshot
    {
        public double CurrentTime { get; set; }
        public bool Paused { get; set; }
        public double Volume { get; set; }
    }

    internal sealed class PlayerFullscreenHostWindow : Window
    {
        private readonly Grid _playerHost;

        public PlayerFullscreenHostWindow()
        {
            Title = "IndigoMovieManager Player Fullscreen";
            Background = Brushes.Black;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Topmost = true;

            _playerHost = new Grid
            {
                Background = Brushes.Black,
            };
            Content = _playerHost;

            PreviewKeyDown += PlayerFullscreenHostWindow_PreviewKeyDown;
        }

        public event EventHandler EscapeRequested;

        private void PlayerFullscreenHostWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e?.Key != Key.Escape)
            {
                return;
            }

            e.Handled = true;
            EscapeRequested?.Invoke(this, EventArgs.Empty);
        }

        // 既に再生できている WebView2 をそのまま持ち上げ、全画面側で再ロードさせない。
        public void ShowPlayer(WebView2 player)
        {
            ArgumentNullException.ThrowIfNull(player);

            MovePlayerToHost(player);

            if (!IsVisible)
            {
                Show();
            }

            WindowState = WindowState.Maximized;
            Activate();
            Focus();
        }

        // 戻す時も同じ実体をプレイヤータブへ返し、再生状態の揺れを最小化する。
        public void ReturnPlayer(WebView2 player, Panel targetParent, int targetRow)
        {
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(targetParent);

            DetachFromCurrentParent(player);

            if (targetParent is Grid)
            {
                Grid.SetRow(player, targetRow);
            }

            player.Visibility = Visibility.Visible;
            player.HorizontalAlignment = HorizontalAlignment.Stretch;
            player.VerticalAlignment = VerticalAlignment.Stretch;

            if (!targetParent.Children.Contains(player))
            {
                targetParent.Children.Add(player);
            }

            Hide();
        }

        private void MovePlayerToHost(WebView2 player)
        {
            DetachFromCurrentParent(player);

            Grid.SetRow(player, 0);
            player.Visibility = Visibility.Visible;
            player.Width = double.NaN;
            player.Height = double.NaN;
            player.HorizontalAlignment = HorizontalAlignment.Stretch;
            player.VerticalAlignment = VerticalAlignment.Stretch;

            if (!_playerHost.Children.Contains(player))
            {
                _playerHost.Children.Add(player);
            }
        }

        private static void DetachFromCurrentParent(WebView2 player)
        {
            switch (player.Parent)
            {
                case Panel panel:
                    panel.Children.Remove(player);
                    break;
                case Decorator decorator:
                    decorator.Child = null;
                    break;
                case ContentControl contentControl when ReferenceEquals(contentControl.Content, player):
                    contentControl.Content = null;
                    break;
            }
        }
    }

    public partial class MainWindow
    {
        private const int PlayerWebViewRowIndex = 1;

        private PlayerFullscreenHostWindow _playerFullscreenWindow;
        private bool _isDetachedPlayerFullscreenActive;
        private bool _playerFullscreenTemporarilyEnabledUiDebugLog;
        private bool _playerFullscreenRestoreUiDebugLogEnabled;

        private PlayerFullscreenHostWindow GetOrCreatePlayerFullscreenWindow()
        {
            if (_playerFullscreenWindow != null)
            {
                return _playerFullscreenWindow;
            }

            _playerFullscreenWindow = new PlayerFullscreenHostWindow
            {
                Owner = this,
            };
            _playerFullscreenWindow.EscapeRequested += PlayerFullscreenWindow_EscapeRequested;
            return _playerFullscreenWindow;
        }

        private void PlayerFullscreenWindow_EscapeRequested(object sender, EventArgs e)
        {
            _ = CloseMainWindowPlayerFullscreenAsync();
        }

        private async Task OpenMainWindowPlayerFullscreenAsync()
        {
            if (
                string.IsNullOrWhiteSpace(_currentWebViewPlayerPath)
                || uxWebVideoPlayer?.CoreWebView2 == null
            )
            {
                return;
            }

            EnsurePlayerFullscreenDebugLoggingEnabled();
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"player fullscreen open requested: path='{_currentWebViewPlayerPath}'"
            );

            PlayerWebViewPlaybackSnapshot snapshot =
                await CapturePlayerWebViewPlaybackSnapshotAsync(uxWebVideoPlayer);

            try
            {
                DebugRuntimeLog.Write("ui-tempo", "player fullscreen snapshot captured");

                PlayerFullscreenHostWindow window = GetOrCreatePlayerFullscreenWindow();
                window.ShowPlayer(uxWebVideoPlayer);
                DebugRuntimeLog.Write("ui-tempo", "player fullscreen player moved");

                _isDetachedPlayerFullscreenActive = true;
                await ApplyPlayerWebViewPlaybackSnapshotAsync(uxWebVideoPlayer, snapshot);
                await SetDetachedWindowDomFullscreenAsync(enable: true);
                DebugRuntimeLog.Write("ui-tempo", "player fullscreen playback synced");
                DebugRuntimeLog.Write("ui-tempo", "player fullscreen window shown");
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"player fullscreen open failed: {ex.GetType().Name} {ex.Message}"
                );
                MessageBox.Show(
                    $"全画面表示の開始に失敗しました。{Environment.NewLine}{ex.Message}",
                    "プレイヤー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                await RestorePlayerTabPlaybackAfterFullscreenFailureAsync(snapshot);
            }
        }

        private async Task RestorePlayerTabPlaybackAfterFullscreenFailureAsync(
            PlayerWebViewPlaybackSnapshot snapshot
        )
        {
            ReturnDetachedWebViewPlayerToPlayerTab();

            if (_isWebViewPlayerActive && uxWebVideoPlayer?.CoreWebView2 != null)
            {
                await ApplyPlayerWebViewPlaybackSnapshotAsync(uxWebVideoPlayer, snapshot);
                UpdatePlayerPositionUi(TimeSpan.FromSeconds(snapshot.CurrentTime));
                IsPlaying = !snapshot.Paused;
            }

            RestorePlayerFullscreenDebugLoggingIfNeeded();
        }

        private void EnsurePlayerFullscreenDebugLoggingEnabled()
        {
            if (_playerFullscreenTemporarilyEnabledUiDebugLog)
            {
                return;
            }

            _playerFullscreenRestoreUiDebugLogEnabled = Properties.Settings.Default.DebugLogUiEnabled;
            if (_playerFullscreenRestoreUiDebugLogEnabled)
            {
                return;
            }

            Properties.Settings.Default.DebugLogUiEnabled = true;
            Properties.Settings.Default.Save();
            _playerFullscreenTemporarilyEnabledUiDebugLog = true;
        }

        private void RestorePlayerFullscreenDebugLoggingIfNeeded()
        {
            if (!_playerFullscreenTemporarilyEnabledUiDebugLog)
            {
                return;
            }

            Properties.Settings.Default.DebugLogUiEnabled = _playerFullscreenRestoreUiDebugLogEnabled;
            Properties.Settings.Default.Save();
            _playerFullscreenTemporarilyEnabledUiDebugLog = false;
        }

        private async Task<PlayerWebViewPlaybackSnapshot> CapturePlayerWebViewPlaybackSnapshotAsync(
            WebView2 webView
        )
        {
            if (webView?.CoreWebView2 == null)
            {
                return new PlayerWebViewPlaybackSnapshot
                {
                    Volume = ClampPlayerVolumeSetting(uxVolumeSlider?.Value ?? 0.5d),
                };
            }

            try
            {
                string rawJson = await webView.ExecuteScriptAsync(
                    """
                    (() => {
                      const player = document.querySelector('video');
                      if (!player) {
                        return null;
                      }

                      return {
                        currentTime: player.currentTime || 0,
                        paused: !!player.paused,
                        volume: Number.isFinite(player.volume) ? player.volume : 0.5
                      };
                    })();
                    """
                );

                PlayerWebViewPlaybackSnapshot snapshot =
                    ParsePlayerWebViewPlaybackSnapshot(rawJson);
                snapshot.Volume = ClampPlayerVolumeSetting(snapshot.Volume);
                return snapshot;
            }
            catch
            {
                return new PlayerWebViewPlaybackSnapshot
                {
                    Volume = ClampPlayerVolumeSetting(uxVolumeSlider?.Value ?? 0.5d),
                };
            }
        }

        private static PlayerWebViewPlaybackSnapshot ParsePlayerWebViewPlaybackSnapshot(
            string rawJson
        )
        {
            if (
                string.IsNullOrWhiteSpace(rawJson)
                || string.Equals(rawJson, "null", StringComparison.Ordinal)
            )
            {
                return new PlayerWebViewPlaybackSnapshot();
            }

            try
            {
                return JsonSerializer.Deserialize<PlayerWebViewPlaybackSnapshot>(rawJson)
                    ?? new PlayerWebViewPlaybackSnapshot();
            }
            catch
            {
                return new PlayerWebViewPlaybackSnapshot();
            }
        }

        private async Task ApplyPlayerWebViewPlaybackSnapshotAsync(
            WebView2 webView,
            PlayerWebViewPlaybackSnapshot snapshot
        )
        {
            if (webView?.CoreWebView2 == null || snapshot == null)
            {
                return;
            }

            string seconds = snapshot.CurrentTime.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );
            string volume = ClampPlayerVolumeSetting(snapshot.Volume).ToString(
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

                    player.muted = false;
                    player.volume = {{volume}};

                    const playPromise = player.play();
                    if (playPromise) {
                      playPromise.catch(() => {});
                    }

                    if ({{(snapshot.Paused ? "true" : "false")}}) {
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
            await webView.ExecuteScriptAsync(script);
        }

        private bool TryHandleMainWindowPlayerFullscreenShortcut(KeyEventArgs e)
        {
            if (e == null || e.Key != Key.Escape || !_isDetachedPlayerFullscreenActive)
            {
                return false;
            }

            _ = CloseMainWindowPlayerFullscreenAsync();
            e.Handled = true;
            return true;
        }

        private async Task CloseMainWindowPlayerFullscreenAsync()
        {
            if (!_isDetachedPlayerFullscreenActive || _playerFullscreenWindow == null)
            {
                return;
            }

            DebugRuntimeLog.Write("ui-tempo", "player fullscreen close requested");
            PlayerWebViewPlaybackSnapshot snapshot = await CapturePlayerWebViewPlaybackSnapshotAsync(
                uxWebVideoPlayer
            );
            await SetDetachedWindowDomFullscreenAsync(enable: false);

            ReturnDetachedWebViewPlayerToPlayerTab();

            ApplyPlayerVolumeSetting(snapshot.Volume, pushToWebView: false);

            if (_isWebViewPlayerActive && uxWebVideoPlayer?.CoreWebView2 != null)
            {
                await ApplyPlayerWebViewPlaybackSnapshotAsync(uxWebVideoPlayer, snapshot);
                UpdatePlayerPositionUi(TimeSpan.FromSeconds(snapshot.CurrentTime));
                IsPlaying = !snapshot.Paused;
            }

            RestorePlayerFullscreenDebugLoggingIfNeeded();
            Activate();
            uxWebVideoPlayer?.Focus();
        }

        private async Task ForceCloseMainWindowPlayerFullscreenAsync()
        {
            if (!_isDetachedPlayerFullscreenActive)
            {
                return;
            }

            await CloseMainWindowPlayerFullscreenAsync();
        }

        // 戻し忘れを防ぎ、全画面を抜けた瞬間にプレイヤータブの見た目も元へ戻す。
        private void ReturnDetachedWebViewPlayerToPlayerTab()
        {
            if (_playerFullscreenWindow == null || uxWebVideoPlayer == null || PlayerArea == null)
            {
                _isDetachedPlayerFullscreenActive = false;
                return;
            }

            if (!ReferenceEquals(uxWebVideoPlayer.Parent, PlayerArea))
            {
                _playerFullscreenWindow.ReturnPlayer(
                    uxWebVideoPlayer,
                    PlayerArea,
                    PlayerWebViewRowIndex
                );
                DebugRuntimeLog.Write("ui-tempo", "player fullscreen player returned");
            }
            else
            {
                _playerFullscreenWindow.Hide();
            }

            _isDetachedPlayerFullscreenActive = false;
            ShowPlayerSurface();
            UpdateManualPlayerViewport();
        }
    }
}
