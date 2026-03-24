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
        private bool _isVisible;
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

            Post(() => RenderOverlayOnOverlayThread(showWindow: false));
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
            if (forceShow)
            {
                _isVisible = true;
            }

            RenderOverlayOnOverlayThread(showWindow: forceShow);
        }

        private void HideOnOverlayThread()
        {
            if (_hwndSource == null)
            {
                return;
            }

            _isVisible = false;
            _ = ShowWindow(_hwndSource.Handle, ShowWindowCommand.SW_HIDE);
        }

        // alpha と位置反映を UpdateLayeredWindow に一本化し、表示順依存を減らす。
        private void RenderOverlayOnOverlayThread(bool showWindow)
        {
            if (_hwndSource == null || !_isVisible)
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

            nint screenDc = GetDC(nint.Zero);
            if (screenDc == 0)
            {
                return;
            }

            nint memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == 0)
            {
                _ = ReleaseDC(nint.Zero, screenDc);
                return;
            }

            nint bitmap = CreateCompatibleBitmap(screenDc, OverlayWidth, OverlayHeight);
            if (bitmap == 0)
            {
                _ = DeleteDC(memoryDc);
                _ = ReleaseDC(nint.Zero, screenDc);
                return;
            }

            nint oldBitmap = SelectObject(memoryDc, bitmap);
            try
            {
                NativeRect clientRect = new()
                {
                    Left = 0,
                    Top = 0,
                    Right = OverlayWidth,
                    Bottom = OverlayHeight,
                };
                nint backgroundBrush = CreateSolidBrush(ToColorRef(0, 0, 0));
                try
                {
                    _ = FillRect(memoryDc, ref clientRect, backgroundBrush);
                }
                finally
                {
                    _ = DeleteObject(backgroundBrush);
                }

                _ = SetBkMode(memoryDc, 1);
                _ = SetTextColor(memoryDc, ResolveAccentColorRef(_currentLevel));
                if (_messageFont != nint.Zero)
                {
                    _ = SelectObject(memoryDc, _messageFont);
                }

                NativeRect textRect = new()
                {
                    Left = 18,
                    Top = 0,
                    Right = Math.Max(18, clientRect.Right - 18),
                    Bottom = clientRect.Bottom,
                };
                _ = DrawTextW(
                    memoryDc,
                    _currentMessage ?? "",
                    -1,
                    ref textRect,
                    DrawTextFormat.DT_SINGLELINE
                        | DrawTextFormat.DT_VCENTER
                        | DrawTextFormat.DT_CENTER
                        | DrawTextFormat.DT_END_ELLIPSIS
                );

                NativePoint sourcePoint = new() { X = 0, Y = 0 };
                NativePoint targetPoint = new() { X = x, Y = y };
                NativeSize size = new() { Width = OverlayWidth, Height = OverlayHeight };
                BlendFunction blend = new()
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = OverlayAlpha,
                    AlphaFormat = 0,
                };

                _ = UpdateLayeredWindow(
                    _hwndSource.Handle,
                    screenDc,
                    ref targetPoint,
                    ref size,
                    memoryDc,
                    ref sourcePoint,
                    0,
                    ref blend,
                    UpdateLayeredWindowFlags.UlwAlpha
                );

                if (showWindow)
                {
                    _ = ShowWindow(_hwndSource.Handle, ShowWindowCommand.SW_SHOWNOACTIVATE);
                }
            }
            finally
            {
                if (oldBitmap != nint.Zero)
                {
                    _ = SelectObject(memoryDc, oldBitmap);
                }

                _ = DeleteObject(bitmap);
                _ = DeleteDC(memoryDc);
                _ = ReleaseDC(nint.Zero, screenDc);
            }
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

        private enum ShowWindowCommand
        {
            SW_HIDE = 0,
            SW_SHOWNOACTIVATE = 4,
        }

        [Flags]
        private enum UpdateLayeredWindowFlags : uint
        {
            UlwAlpha = 0x00000002,
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
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(nint hWnd, ShowWindowCommand nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UpdateLayeredWindow(
            nint hwnd,
            nint hdcDst,
            ref NativePoint pptDst,
            ref NativeSize psize,
            nint hdcSrc,
            ref NativePoint pptSrc,
            uint crKey,
            ref BlendFunction pblend,
            UpdateLayeredWindowFlags dwFlags
        );

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint GetDC(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ReleaseDC(nint hWnd, nint hDC);

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

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern nint CreateCompatibleDC(nint hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(nint hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern nint CreateCompatibleBitmap(nint hdc, int cx, int cy);

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
