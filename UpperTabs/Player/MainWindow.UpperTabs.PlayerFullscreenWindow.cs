using System;
using System.ComponentModel;
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
    public partial class MainWindow
    {
        private const string DetachedFullscreenCloseMessage = "detached-player-close";
        private const string DetachedFullscreenVolumeMessagePrefix = "detached-player-volume:";
        private Window _detachedPlayerFullscreenWindow;
        private WebView2 _detachedPlayerFullscreenWebView;
        private bool _isDetachedPlayerFullscreenClosing;
        private bool _isDetachedPlayerFullscreenBridgeRegistered;
        private bool _hasPendingDetachedPlayerFullscreenPlayback;
        private PlayerWebViewPlaybackSnapshot _pendingDetachedPlayerFullscreenSnapshot;

        private sealed class PlayerWebViewPlaybackSnapshot
        {
            public double CurrentTime { get; set; }
            public bool Paused { get; set; }
            public double Volume { get; set; }
        }

        // タブの外へ専用ウィンドウを出し、動画だけに集中できる全画面表示へ切り替える。
        private async Task OpenDetachedPlayerFullscreenWindowAsync()
        {
            if (string.IsNullOrWhiteSpace(_currentWebViewPlayerPath) || uxWebVideoPlayer == null)
            {
                return;
            }

            PlayerWebViewPlaybackSnapshot snapshot = await CapturePlayerWebViewPlaybackSnapshotAsync(
                uxWebVideoPlayer
            );
            await PauseWebViewPlayerAsync();

            EnsureDetachedPlayerFullscreenWindowCreated();
            if (!await EnsureDetachedPlayerFullscreenWebViewReadyAsync())
            {
                return;
            }

            _pendingDetachedPlayerFullscreenSnapshot = snapshot;
            _hasPendingDetachedPlayerFullscreenPlayback = true;

            if (!_detachedPlayerFullscreenWindow.IsVisible)
            {
                _detachedPlayerFullscreenWindow.Show();
            }

            bool sourceChanged =
                _detachedPlayerFullscreenWebView.Source == null
                || !string.Equals(
                    _detachedPlayerFullscreenWebView.Source.LocalPath,
                    _currentWebViewPlayerPath,
                    StringComparison.OrdinalIgnoreCase
                );
            if (sourceChanged)
            {
                _detachedPlayerFullscreenWebView.Source = new Uri(_currentWebViewPlayerPath);
            }
            else
            {
                await ApplyPendingDetachedPlayerFullscreenPlaybackAsync();
            }

            _detachedPlayerFullscreenWindow.Activate();
            _detachedPlayerFullscreenWebView.Focus();
        }

        // 専用ウィンドウは余計な装飾を持たず、動画だけを置く黒いキャンバスにする。
        private void EnsureDetachedPlayerFullscreenWindowCreated()
        {
            if (_detachedPlayerFullscreenWindow != null && _detachedPlayerFullscreenWebView != null)
            {
                return;
            }

            Grid hostRoot = new()
            {
                Background = Brushes.Black,
            };

            WebView2 fullscreenWebView = new()
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            fullscreenWebView.NavigationCompleted += DetachedPlayerFullscreenWebView_NavigationCompleted;
            hostRoot.Children.Add(fullscreenWebView);

            Window fullscreenWindow = new()
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowState = WindowState.Maximized,
                Topmost = true,
                Background = Brushes.Black,
                Content = hostRoot,
                ShowInTaskbar = false,
            };
            fullscreenWindow.PreviewKeyDown += DetachedPlayerFullscreenWindow_PreviewKeyDown;
            fullscreenWindow.Closing += DetachedPlayerFullscreenWindow_Closing;
            fullscreenWindow.Closed += DetachedPlayerFullscreenWindow_Closed;

            _detachedPlayerFullscreenWindow = fullscreenWindow;
            _detachedPlayerFullscreenWebView = fullscreenWebView;
        }

        // 全画面側も本体と同じ見た目・音量連携を持たせ、ESC で抜けられるようにする。
        private async Task<bool> EnsureDetachedPlayerFullscreenWebViewReadyAsync()
        {
            if (_detachedPlayerFullscreenWebView == null)
            {
                return false;
            }

            try
            {
                if (_detachedPlayerFullscreenWebView.CoreWebView2 == null)
                {
                    await _detachedPlayerFullscreenWebView.EnsureCoreWebView2Async();
                }

                if (_isDetachedPlayerFullscreenBridgeRegistered)
                {
                    return true;
                }

                _detachedPlayerFullscreenWebView.CoreWebView2.WebMessageReceived +=
                    DetachedPlayerFullscreenWebView_WebMessageReceived;
                await _detachedPlayerFullscreenWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    """
                    (() => {
                      const bindPlayer = () => {
                        const player = document.querySelector('video');
                        if (!player || player.dataset.indigoDetachedPlayerBound === '1') {
                          return;
                        }

                        player.dataset.indigoDetachedPlayerBound = '1';
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
                          try {
                            chrome.webview.postMessage(`detached-player-volume:${player.volume}`);
                          } catch {}
                        };

                        player.addEventListener('volumechange', notifyVolume);
                        player.addEventListener('dblclick', () => {
                          try {
                            chrome.webview.postMessage('detached-player-close');
                          } catch {}
                        });

                        notifyVolume();
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
                _isDetachedPlayerFullscreenBridgeRegistered = true;
                return true;
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(
                    "全画面プレーヤーには WebView2 Runtime が必要です。",
                    "プレイヤー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return false;
            }
        }

        private async void DetachedPlayerFullscreenWebView_NavigationCompleted(
            object sender,
            CoreWebView2NavigationCompletedEventArgs e
        )
        {
            if (!e.IsSuccess)
            {
                return;
            }

            await ApplyPendingDetachedPlayerFullscreenPlaybackAsync();
        }

        // 開いた瞬間に元の再生位置へ寄せ、再生中か停止中かも引き継ぐ。
        private async Task ApplyPendingDetachedPlayerFullscreenPlaybackAsync()
        {
            if (
                !_hasPendingDetachedPlayerFullscreenPlayback
                || _detachedPlayerFullscreenWebView?.CoreWebView2 == null
                || _pendingDetachedPlayerFullscreenSnapshot == null
            )
            {
                return;
            }

            PlayerWebViewPlaybackSnapshot snapshot = _pendingDetachedPlayerFullscreenSnapshot;
            _hasPendingDetachedPlayerFullscreenPlayback = false;
            _pendingDetachedPlayerFullscreenSnapshot = null;
            await ApplyPlayerWebViewPlaybackSnapshotAsync(
                _detachedPlayerFullscreenWebView,
                snapshot
            );
        }

        // WebView2 から返る JSON を C# 側の軽い再生状態へ戻す。
        private static PlayerWebViewPlaybackSnapshot ParsePlayerWebViewPlaybackSnapshot(
            string rawJson
        )
        {
            if (string.IsNullOrWhiteSpace(rawJson) || string.Equals(rawJson, "null", StringComparison.Ordinal))
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

        // いま見えている位置と再生/停止状態を取っておけば、全画面の出入りで体験が切れにくい。
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
                PlayerWebViewPlaybackSnapshot snapshot = ParsePlayerWebViewPlaybackSnapshot(rawJson);
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

        // 同じ動画を別の WebView2 へ載せても、位置と音量と再生状態がそろうようにする。
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

        private async void DetachedPlayerFullscreenWindow_Closing(
            object sender,
            CancelEventArgs e
        )
        {
            if (_isDetachedPlayerFullscreenClosing)
            {
                return;
            }

            e.Cancel = true;
            await CloseDetachedPlayerFullscreenWindowAsync();
        }

        private async void DetachedPlayerFullscreenWindow_PreviewKeyDown(
            object sender,
            KeyEventArgs e
        )
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            e.Handled = true;
            await CloseDetachedPlayerFullscreenWindowAsync();
        }

        private void DetachedPlayerFullscreenWindow_Closed(object sender, EventArgs e)
        {
            if (_detachedPlayerFullscreenWebView != null)
            {
                _detachedPlayerFullscreenWebView.NavigationCompleted -=
                    DetachedPlayerFullscreenWebView_NavigationCompleted;
            }

            _detachedPlayerFullscreenWebView = null;
            _detachedPlayerFullscreenWindow = null;
            _isDetachedPlayerFullscreenBridgeRegistered = false;
            Activate();
        }

        private async void DetachedPlayerFullscreenWebView_WebMessageReceived(
            object sender,
            CoreWebView2WebMessageReceivedEventArgs e
        )
        {
            string message = e.TryGetWebMessageAsString();
            if (string.Equals(message, DetachedFullscreenCloseMessage, StringComparison.Ordinal))
            {
                await CloseDetachedPlayerFullscreenWindowAsync();
                return;
            }

            if (
                string.IsNullOrWhiteSpace(message)
                || !message.StartsWith(DetachedFullscreenVolumeMessagePrefix, StringComparison.Ordinal)
            )
            {
                return;
            }

            string volumeText = message[DetachedFullscreenVolumeMessagePrefix.Length..];
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

        // 専用全画面を閉じる時は、最後に見ていた位置を本体プレーヤーへ戻す。
        private async Task CloseDetachedPlayerFullscreenWindowAsync()
        {
            if (
                _isDetachedPlayerFullscreenClosing
                || _detachedPlayerFullscreenWindow == null
                || _detachedPlayerFullscreenWebView == null
            )
            {
                return;
            }

            _isDetachedPlayerFullscreenClosing = true;
            try
            {
                PlayerWebViewPlaybackSnapshot snapshot = await CapturePlayerWebViewPlaybackSnapshotAsync(
                    _detachedPlayerFullscreenWebView
                );
                ApplyPlayerVolumeSetting(snapshot.Volume, pushToWebView: _isWebViewPlayerActive);

                if (_isWebViewPlayerActive && uxWebVideoPlayer?.CoreWebView2 != null)
                {
                    await ApplyPlayerWebViewPlaybackSnapshotAsync(uxWebVideoPlayer, snapshot);
                    UpdatePlayerPositionUi(TimeSpan.FromSeconds(snapshot.CurrentTime));
                    IsPlaying = !snapshot.Paused;
                }

                _detachedPlayerFullscreenWindow.Close();
            }
            finally
            {
                _isDetachedPlayerFullscreenClosing = false;
            }
        }
    }
}
