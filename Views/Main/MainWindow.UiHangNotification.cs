namespace IndigoMovieManager
{
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Interop;

    public partial class MainWindow
    {
        private const int UiHangDangerPendingThresholdMs = 5000;

        private readonly UiHangActivityTracker _uiHangActivityTracker;
        private readonly UiHangNotificationCoordinator _uiHangNotificationCoordinator;
        private readonly object _uiHangWindowStateGate = new();
        private nint _uiHangWindowHandle;
        private bool _uiHangWindowIsMinimized;

        private void StartUiHangNotificationSupport()
        {
            UpdateUiHangWindowStateSnapshot();
            UpdateUiHangNotificationPlacement();
            _uiHangNotificationCoordinator.Start();
            UpdateUiHangNotificationVisibilityPolicy();
        }

        private void StopUiHangNotificationSupport()
        {
            _uiHangNotificationCoordinator.Stop();
            ResetUiHangWindowStateSnapshot();
        }

        private IDisposable TrackUiHangActivity(UiHangActivityKind activityKind)
        {
            return _uiHangActivityTracker.Begin(activityKind);
        }

        private void UpdateUiHangNotificationPlacement()
        {
            _uiHangNotificationCoordinator.UpdatePlacement(ResolveUiHangOverlayPlacement());
        }

        private void UpdateUiHangNotificationVisibilityPolicy()
        {
            _uiHangNotificationCoordinator.ReevaluateVisibility();
        }

        private void ShowUiHangShutdownStatus(string message)
        {
            _uiHangNotificationCoordinator.ShowExplicitStatus(
                UiHangNotificationLevel.Warning,
                message,
                allowBackground: false
            );
        }

        private void HideUiHangShutdownStatus()
        {
            _uiHangNotificationCoordinator.HideExplicitStatus();
        }

        private void ShowUiHangDbSwitchStatus(string message)
        {
            _uiHangNotificationCoordinator.ShowExplicitStatus(
                UiHangNotificationLevel.Warning,
                message,
                allowBackground: false
            );
        }

        private void HideUiHangDbSwitchStatus()
        {
            _uiHangNotificationCoordinator.HideExplicitStatus();
        }

        // UI スレッドで取った HWND と最小化状態だけを保持し、背景監視側は WPF オブジェクトへ触れない。
        private void UpdateUiHangWindowStateSnapshot()
        {
            nint windowHandle;

            try
            {
                windowHandle = new WindowInteropHelper(this).Handle;
            }
            catch
            {
                windowHandle = 0;
            }

            lock (_uiHangWindowStateGate)
            {
                _uiHangWindowHandle = windowHandle;
                _uiHangWindowIsMinimized = WindowState == WindowState.Minimized;
            }
        }

        private void ResetUiHangWindowStateSnapshot()
        {
            lock (_uiHangWindowStateGate)
            {
                _uiHangWindowHandle = 0;
                _uiHangWindowIsMinimized = false;
            }
        }

        private (nint WindowHandle, bool IsMinimized) GetUiHangWindowStateSnapshot()
        {
            lock (_uiHangWindowStateGate)
            {
                return (_uiHangWindowHandle, _uiHangWindowIsMinimized);
            }
        }

        // ヘッダー帯のスクリーン座標を取り、通知を対象ウインドウの上部へ追従させる。
        private UiHangOverlayPlacement ResolveUiHangOverlayPlacement()
        {
            try
            {
                nint windowHandle = new WindowInteropHelper(this).Handle;
                if (windowHandle != 0 && GetWindowRect(windowHandle, out NativeRect nativeRect))
                {
                    return new UiHangOverlayPlacement(
                        new Rect(
                            nativeRect.Left,
                            nativeRect.Top,
                            Math.Max(0, nativeRect.Right - nativeRect.Left),
                            Math.Max(0, nativeRect.Bottom - nativeRect.Top)
                        )
                    );
                }
            }
            catch
            {
                // 初期化途中は window 座標へフォールバックする。
            }

            double fallbackWidth = ActualWidth > 0 ? ActualWidth : Width;
            double fallbackHeight = ActualHeight > 0 ? ActualHeight : Height;
            return new UiHangOverlayPlacement(new Rect(Left, Top, fallbackWidth, fallbackHeight));
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsHungAppWindow(nint hWnd);

        // heartbeat 未復帰が長く続くか、OS が hung window と見た時だけ危険扱いへ上げる。
        private bool IsUiHangDangerState(UiHangHeartbeatSample sample)
        {
            (nint windowHandle, _) = GetUiHangWindowStateSnapshot();

            try
            {
                return IsUiHangDangerStateCore(
                    sample,
                    UiHangDangerPendingThresholdMs,
                    windowHandle,
                    IsHungAppWindow
                );
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsUiHangDangerStateCore(
            UiHangHeartbeatSample sample,
            int pendingThresholdMs,
            nint windowHandle,
            Func<nint, bool> hungWindowResolver
        )
        {
            if (sample.IsPending && sample.DelayMs >= pendingThresholdMs)
            {
                return true;
            }

            if (windowHandle == 0 || hungWindowResolver == null)
            {
                return false;
            }

            return hungWindowResolver(windowHandle);
        }

        // 通常通知は本体が前面の時だけ出し、危険扱いの critical だけは背後でも出す。
        private bool ShouldDisplayUiHangNotification(UiHangNotificationLevel level)
        {
            (nint windowHandle, bool isMinimized) = GetUiHangWindowStateSnapshot();
            return ShouldDisplayUiHangNotificationCore(
                level,
                isMinimized,
                windowHandle,
                GetForegroundWindow
            );
        }

        internal static bool ShouldDisplayUiHangNotificationCore(
            UiHangNotificationLevel level,
            bool isMinimized,
            nint windowHandle,
            Func<nint> foregroundWindowResolver
        )
        {
            if (level == UiHangNotificationLevel.Critical)
            {
                return true;
            }

            if (isMinimized || windowHandle == 0 || foregroundWindowResolver == null)
            {
                return false;
            }

            return foregroundWindowResolver() == windowHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
