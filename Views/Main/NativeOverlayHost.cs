using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    internal sealed class NativeOverlayHost : IDisposable
    {
        private const int OverlayWidth = 460;
        private const int OverlayHeight = 48;
        private const int OverlayBottomMargin = 24;
        private const byte OverlayAlpha = 153;
        private static readonly nint HwndTopmost = new(-1);

        private readonly object _gate = new();
        private readonly ConcurrentQueue<Action> _pendingActions = new();
        private UiHangOverlayPlacement _placement;
        private Thread _overlayThread;
        private Dispatcher _overlayDispatcher;
        private HwndSource _hwndSource;
        private UiHangNotificationLevel _currentLevel = UiHangNotificationLevel.Warning;
        private string _currentMessage = "UI応答低下を検知";
        private nint _messageFont = nint.Zero;
        private bool _isStarted;
        private bool _stopRequested;
        private bool _disposed;

        internal void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_isStarted)
                {
                    return;
                }

                _isStarted = true;
                _stopRequested = false;
                _overlayThread = new Thread(OverlayThreadMain)
                {
                    IsBackground = true,
                    Name = "UiHangOverlayThread",
                };
                _overlayThread.SetApartmentState(ApartmentState.STA);
                _overlayThread.Start();
            }
        }

        internal void Stop()
        {
            Thread threadToJoin;
            Dispatcher dispatcherToStop;

            lock (_gate)
            {
                if (!_isStarted)
                {
                    return;
                }

                _isStarted = false;
                _stopRequested = true;
                _pendingActions.Clear();
                threadToJoin = _overlayThread;
                dispatcherToStop = _overlayDispatcher;
            }

            if (dispatcherToStop != null)
            {
                try
                {
                    _ = dispatcherToStop.BeginInvoke(
                        DispatcherPriority.Send,
                        new Action(() =>
                        {
                            HideOnOverlayThread();
                            dispatcherToStop.BeginInvokeShutdown(DispatcherPriority.Send);
                        })
                    );
                }
                catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
                {
                    // 終了競合だけなので何もしない。
                }
            }

            if (threadToJoin != null && threadToJoin != Thread.CurrentThread)
            {
                threadToJoin.Join(250);
            }
        }

        internal void Show(UiHangNotificationLevel level, string message)
        {
            Post(() => ShowOrUpdateOnOverlayThread(level, message, forceShow: true));
        }

        internal void Update(UiHangNotificationLevel level, string message)
        {
            Post(() => ShowOrUpdateOnOverlayThread(level, message, forceShow: false));
        }

        internal void Hide()
        {
            Post(HideOnOverlayThread);
        }

        internal void UpdatePlacement(UiHangOverlayPlacement placement)
        {
            lock (_gate)
            {
                _placement = placement;
            }

            Post(() => UpdatePlacementOnOverlayThread(forceShow: false));
        }

        private void OverlayThreadMain()
        {
            Dispatcher currentDispatcher = Dispatcher.CurrentDispatcher;

            lock (_gate)
            {
                _overlayDispatcher = currentDispatcher;
            }

            CreateOverlayOnCurrentThread();
            DrainPendingActions();

            if (_stopRequested)
            {
                currentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }

            Dispatcher.Run();
            DestroyOverlayOnCurrentThread();

            lock (_gate)
            {
                _overlayDispatcher = null;
                _overlayThread = null;
            }
        }

        private void Post(Action action)
        {
            Dispatcher dispatcher;

            lock (_gate)
            {
                if (!_isStarted || _stopRequested)
                {
                    return;
                }

                dispatcher = _overlayDispatcher;
                if (dispatcher == null)
                {
                    _pendingActions.Enqueue(action);
                    return;
                }
            }

            try
            {
                _ = dispatcher.BeginInvoke(DispatcherPriority.Background, action);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
            {
                // 終了競合だけなので何もしない。
            }
        }

        private void DrainPendingActions()
        {
            while (_pendingActions.TryDequeue(out Action action))
            {
                action();
            }
        }

        // 表示は別スレッドの HWND に閉じ込め、MainWindow 側からは状態だけ送る。
        private void CreateOverlayOnCurrentThread()
        {
            HwndSourceParameters parameters = new("UiHangOverlayBanner")
            {
                Width = OverlayWidth,
                Height = OverlayHeight,
                WindowStyle = unchecked((int)WindowStyles.WS_POPUP),
                ExtendedWindowStyle = unchecked(
                    (int)(
                        WindowExStyles.WS_EX_LAYERED
                        | WindowExStyles.WS_EX_TOPMOST
                        | WindowExStyles.WS_EX_TOOLWINDOW
                        | WindowExStyles.WS_EX_NOACTIVATE
                        | WindowExStyles.WS_EX_TRANSPARENT
                    )
                ),
            };
            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(OverlayWndProc);
            _messageFont = CreateFontW(
                -16,
                0,
                0,
                0,
                600,
                0,
                0,
                0,
                1,
                0,
                0,
                5,
                0,
                "Yu Gothic UI"
            );
            EnsureLayeredWindowAlpha(_hwndSource.Handle);
            HideOnOverlayThread();
        }

        private void DestroyOverlayOnCurrentThread()
        {
            _hwndSource?.Dispose();
            _hwndSource = null;
            if (_messageFont != nint.Zero)
            {
                _ = DeleteObject(_messageFont);
                _messageFont = nint.Zero;
            }
        }

        private void ShowOrUpdateOnOverlayThread(
            UiHangNotificationLevel level,
            string message,
            bool forceShow
        )
        {
            if (_hwndSource == null)
            {
                return;
            }

            _currentLevel = level;
            _currentMessage = message ?? "";
            EnsureLayeredWindowAlpha(_hwndSource.Handle);
            _ = InvalidateRect(_hwndSource.Handle, nint.Zero, false);
            UpdatePlacementOnOverlayThread(forceShow);

            if (forceShow)
            {
                _ = ShowWindow(_hwndSource.Handle, ShowWindowCommand.SW_SHOWNOACTIVATE);
            }
        }

        private void HideOnOverlayThread()
        {
            if (_hwndSource == null)
            {
                return;
            }

            _ = ShowWindow(_hwndSource.Handle, ShowWindowCommand.SW_HIDE);
        }

        private nint OverlayWndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x000F: // WM_PAINT
                    PaintOverlay(hwnd);
                    handled = true;
                    return nint.Zero;
                case 0x0014: // WM_ERASEBKGND
                    handled = true;
                    return new nint(1);
            }

            return nint.Zero;
        }

        // WPF Visual を介さず WM_PAINT で直接描画し、専用スレッドの MediaContext を持たないようにする。
        private void PaintOverlay(nint hwnd)
        {
            if (hwnd == 0)
            {
                return;
            }

            PaintStruct paintStruct;
            nint hdc = BeginPaint(hwnd, out paintStruct);
            if (hdc == 0)
            {
                return;
            }

            try
            {
                NativeRect clientRect;
                _ = GetClientRect(hwnd, out clientRect);
                nint backgroundBrush = CreateSolidBrush(ToColorRef(0, 0, 0));
                try
                {
                    _ = FillRect(hdc, ref clientRect, backgroundBrush);
                }
                finally
                {
                    _ = DeleteObject(backgroundBrush);
                }

                _ = SetBkMode(hdc, 1);
                _ = SetTextColor(hdc, ResolveAccentColorRef(_currentLevel));
                if (_messageFont != nint.Zero)
                {
                    _ = SelectObject(hdc, _messageFont);
                }

                NativeRect textRect = new()
                {
                    Left = 18,
                    Top = 0,
                    Right = Math.Max(18, clientRect.Right - 18),
                    Bottom = clientRect.Bottom,
                };
                _ = DrawTextW(
                    hdc,
                    _currentMessage ?? "",
                    -1,
                    ref textRect,
                    DrawTextFormat.DT_SINGLELINE
                        | DrawTextFormat.DT_VCENTER
                        | DrawTextFormat.DT_CENTER
                        | DrawTextFormat.DT_END_ELLIPSIS
                );
            }
            finally
            {
                _ = EndPaint(hwnd, ref paintStruct);
            }
        }

        // MainWindow のヘッダー帯へ寄せ、画面右上固定ではなく対象ウインドウに追従させる。
        private void UpdatePlacementOnOverlayThread(bool forceShow)
        {
            if (_hwndSource == null)
            {
                return;
            }

            UiHangOverlayPlacement placement;
            lock (_gate)
            {
                placement = _placement;
            }

            Rect bounds = placement.IsEmpty ? SystemParameters.WorkArea : placement.Bounds;
            int x = (int)Math.Round(bounds.Left + Math.Max(0, (bounds.Width - OverlayWidth) / 2));
            int y = (int)Math.Round(
                bounds.Top + Math.Max(0, bounds.Height - OverlayHeight - OverlayBottomMargin)
            );

            _ = SetWindowPos(
                _hwndSource.Handle,
                HwndTopmost,
                x,
                y,
                OverlayWidth,
                OverlayHeight,
                SetWindowPosFlags.SWP_NOACTIVATE
                    | (forceShow ? SetWindowPosFlags.SWP_SHOWWINDOW : 0)
            );
        }

        private static uint ResolveAccentColorRef(UiHangNotificationLevel level)
        {
            return level switch
            {
                UiHangNotificationLevel.Caution => ToColorRef(255, 230, 0),
                UiHangNotificationLevel.Warning => ToColorRef(255, 18, 18),
                UiHangNotificationLevel.Critical => ToColorRef(255, 196, 196),
                _ => ToColorRef(236, 236, 236),
            };
        }

        private static uint ToColorRef(byte red, byte green, byte blue)
        {
            return (uint)(red | (green << 8) | (blue << 16));
        }

        // HwndSource 側で拡張スタイルが揺れても、表示前に layered alpha を必ず再適用する。
        private static void EnsureLayeredWindowAlpha(nint hwnd)
        {
            if (hwnd == 0)
            {
                return;
            }

            nint exStyle = GetWindowLongPtr(hwnd, WindowLongIndex.GwlExStyle);
            nint layeredStyle = new(unchecked((nint)WindowExStyles.WS_EX_LAYERED));
            if ((exStyle.ToInt64() & layeredStyle.ToInt64()) == 0)
            {
                _ = SetWindowLongPtr(hwnd, WindowLongIndex.GwlExStyle, new nint(exStyle.ToInt64() | layeredStyle.ToInt64()));
            }

            _ = SetLayeredWindowAttributes(hwnd, 0, OverlayAlpha, LayeredWindowFlags.LWA_ALPHA);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NativeOverlayHost));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;
        }

        [Flags]
        private enum WindowStyles : uint
        {
            WS_POPUP = 0x80000000,
        }

        [Flags]
        private enum WindowExStyles : uint
        {
            WS_EX_LAYERED = 0x00080000,
            WS_EX_TOPMOST = 0x00000008,
            WS_EX_TRANSPARENT = 0x00000020,
            WS_EX_TOOLWINDOW = 0x00000080,
            WS_EX_NOACTIVATE = 0x08000000,
        }

        [Flags]
        private enum SetWindowPosFlags : uint
        {
            SWP_NOACTIVATE = 0x0010,
            SWP_SHOWWINDOW = 0x0040,
        }

        private enum ShowWindowCommand
        {
            SW_HIDE = 0,
            SW_SHOWNOACTIVATE = 4,
        }

        private enum WindowLongIndex
        {
            GwlExStyle = -20,
        }

        [Flags]
        private enum LayeredWindowFlags : uint
        {
            LWA_ALPHA = 0x00000002,
        }

        [Flags]
        private enum DrawTextFormat : uint
        {
            DT_CENTER = 0x00000001,
            DT_VCENTER = 0x00000004,
            DT_SINGLELINE = 0x00000020,
            DT_END_ELLIPSIS = 0x00008000,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PaintStruct
        {
            public nint Hdc;
            [MarshalAs(UnmanagedType.Bool)]
            public bool FErase;
            public NativeRect RcPaint;
            [MarshalAs(UnmanagedType.Bool)]
            public bool FRestore;
            [MarshalAs(UnmanagedType.Bool)]
            public bool FIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(nint hWnd, ShowWindowCommand nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            nint hWnd,
            nint hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            SetWindowPosFlags uFlags
        );

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InvalidateRect(
            nint hWnd,
            nint lpRect,
            [MarshalAs(UnmanagedType.Bool)] bool bErase
        );

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(
            nint hwnd,
            uint crKey,
            byte bAlpha,
            LayeredWindowFlags dwFlags
        );

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern nint GetWindowLongPtr(nint hWnd, WindowLongIndex nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtr(nint hWnd, WindowLongIndex nIndex, nint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint BeginPaint(nint hWnd, out PaintStruct lpPaint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EndPaint(nint hWnd, ref PaintStruct lpPaint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(nint hWnd, out NativeRect lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DrawTextW(
            nint hdc,
            string lpchText,
            int cchText,
            ref NativeRect lprc,
            DrawTextFormat format
        );

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int SetBkMode(nint hdc, int mode);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern uint SetTextColor(nint hdc, uint color);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int FillRect(nint hDC, ref NativeRect lprc, nint hbr);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern nint CreateSolidBrush(uint color);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateFontW(
            int cHeight,
            int cWidth,
            int cEscapement,
            int cOrientation,
            int cWeight,
            uint bItalic,
            uint bUnderline,
            uint bStrikeOut,
            uint iCharSet,
            uint iOutPrecision,
            uint iClipPrecision,
            uint iQuality,
            uint iPitchAndFamily,
            string pszFaceName
        );

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern nint SelectObject(nint hdc, nint h);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(nint ho);
    }

    internal readonly record struct UiHangOverlayPlacement(Rect Bounds)
    {
        internal bool IsEmpty => Bounds.Width <= 0 || Bounds.Height <= 0;
    }
}
