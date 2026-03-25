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
        private const uint DibRgb = 0;
        private const uint BiRgb = 0;
        private const string OverlayLogCategory = "ui-overlay";
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
                Log("overlay thread start");
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
                    Log("overlay thread stop requested");
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
                Log("overlay thread join wait");
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
            Log("overlay thread created");
            DrainPendingActions();

            if (_stopRequested)
            {
                currentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }

            Dispatcher.Run();
            DestroyOverlayOnCurrentThread();
            Log("overlay thread destroyed");

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
            Log($"overlay created. hwnd={_hwndSource?.Handle}");
            _ = EnsureLayeredWindowStyles(_hwndSource.Handle);
            _ = TryApplyLayeredWindowAlpha(_hwndSource.Handle, OverlayAlpha);
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
            Log(
                $"overlay update request level={level} force_show={forceShow} message='{_currentMessage}'"
            );
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
            Log("overlay hide request");
            _ = ShowWindow(_hwndSource.Handle, ShowWindowCommand.SW_HIDE);
        }

        // 位置更新後にレイヤードウィンドウを毎回再描画し、alpha を確実に反映する。
        private void RenderOverlayOnOverlayThread(bool showWindow)
        {
            if (_hwndSource == null || !_isVisible)
            {
                Log("overlay render skipped: hidden");
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
                    | SetWindowPosFlags.SWP_SHOWWINDOW
            );

            bool rendered = TryRenderByUpdateLayeredWindow(_hwndSource.Handle, x, y);
            if (!rendered)
            {
                Log($"overlay render failed: hwnd={_hwndSource.Handle}, x={x}, y={y}");
            }
            else
            {
                Log($"overlay rendered: hwnd={_hwndSource.Handle}, x={x}, y={y}");
            }

            if (showWindow)
            {
                Log($"overlay show: hwnd={_hwndSource.Handle}");
                _ = ShowWindow(_hwndSource.Handle, ShowWindowCommand.SW_SHOWNOACTIVATE);
            }
        }

        private bool TryRenderByUpdateLayeredWindow(nint hwnd, int x, int y)
        {
            if (hwnd == nint.Zero)
            {
                return false;
            }

            nint screenDc = GetDC(nint.Zero);
            if (screenDc == nint.Zero || hwnd == nint.Zero)
            {
                Log($"overlay render failed: screenDc={screenDc} hwnd={hwnd}");
                return false;
            }

            try
            {
                nint memoryDc = CreateCompatibleDC(screenDc);
                if (memoryDc == nint.Zero)
                {
                    Log("overlay render failed: CreateCompatibleDC");
                    return false;
                }

                nint bitmap = CreatePerPixelBitmap(screenDc, OverlayWidth, OverlayHeight, out nint bitsPtr);
                if (bitmap == nint.Zero)
                {
                    _ = DeleteDC(memoryDc);
                    Log($"overlay render failed: CreatePerPixelBitmap bits={bitsPtr}");
                    return false;
                }

                nint oldBitmap = SelectObject(memoryDc, bitmap);
                try
                {
                    RenderContentOnHdc(memoryDc, OverlayWidth, OverlayHeight);

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

                    bool updated = UpdateLayeredWindow(
                        hwnd,
                        nint.Zero,
                        ref targetPoint,
                        ref size,
                        memoryDc,
                        ref sourcePoint,
                        0,
                        ref blend,
                        UpdateLayeredWindowFlags.UlwAlpha
                    );
                    if (!updated)
                    {
                        int error = Marshal.GetLastWin32Error();
                        Log($"UpdateLayeredWindow failed: hwnd={hwnd}, error={error}, x={x}, y={y}");

                        bool updatedByScreenDc = UpdateLayeredWindow(
                            hwnd,
                            screenDc,
                            ref targetPoint,
                            ref size,
                            memoryDc,
                            ref sourcePoint,
                            0,
                            ref blend,
                            UpdateLayeredWindowFlags.UlwAlpha
                        );
                        if (updatedByScreenDc)
                        {
                            Log(
                                $"UpdateLayeredWindow retry ok with hdcDst=screen: hwnd={hwnd}, x={x}, y={y}"
                            );
                            return true;
                        }

                        int retryError = Marshal.GetLastWin32Error();
                        Log(
                            $"UpdateLayeredWindow retry failed: hwnd={hwnd}, error={retryError}, x={x}, y={y}"
                        );
                        _ = TryApplyLayeredWindowAlpha(hwnd, OverlayAlpha);
                        return false;
                    }

                    Log($"UpdateLayeredWindow ok: hwnd={hwnd}, x={x}, y={y}");
                    return true;
                }
                finally
                {
                    if (oldBitmap != nint.Zero)
                    {
                        _ = SelectObject(memoryDc, oldBitmap);
                    }

                    _ = DeleteObject(bitmap);
                    _ = DeleteDC(memoryDc);
                }
            }
            finally
            {
                _ = ReleaseDC(nint.Zero, screenDc);
            }
        }

        private void RenderContentOnHdc(nint hdc, int width, int height)
        {
            NativeRect clientRect = new()
            {
                Left = 0,
                Top = 0,
                Right = width,
                Bottom = height,
            };
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

        private bool EnsureLayeredWindowStyles(nint hwnd)
        {
            if (hwnd == 0)
            {
                return false;
            }

            nint currentStyle = GetWindowLongPtr(hwnd, WindowLongIndex.GwlExStyle);
            if (currentStyle == nint.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 0)
                {
                    Log($"GetWindowLongPtr failed: hwnd={hwnd}, error={error}");
                    return false;
                }
            }

            Log($"overlay exstyle before: hwnd={hwnd}, style=0x{currentStyle:X}");

            nint overlayStyles = currentStyle
                | (nint)WindowExStyles.WS_EX_LAYERED
                | (nint)WindowExStyles.WS_EX_TOPMOST
                | (nint)WindowExStyles.WS_EX_TOOLWINDOW
                | (nint)WindowExStyles.WS_EX_NOACTIVATE
                | (nint)WindowExStyles.WS_EX_TRANSPARENT;

            nint setStyleResult = SetWindowLongPtr(hwnd, WindowLongIndex.GwlExStyle, overlayStyles);
            int setStyleError = Marshal.GetLastWin32Error();
            if (setStyleResult == nint.Zero && setStyleError != 0)
            {
                Log($"SetWindowLongPtr failed: hwnd={hwnd}, error={setStyleError}");
                return false;
            }

            nint updatedStyle = GetWindowLongPtr(hwnd, WindowLongIndex.GwlExStyle);
            Log(
                $"overlay exstyle after: hwnd={hwnd}, style=0x{updatedStyle:X}, setStyleResult=0x{setStyleResult:X}"
            );

            bool setPosResult = SetWindowPos(
                hwnd,
                nint.Zero,
                0,
                0,
                0,
                0,
                SetWindowPosFlags.SWP_NOMOVE
                    | SetWindowPosFlags.SWP_NOSIZE
                    | SetWindowPosFlags.SWP_NOACTIVATE
                    | SetWindowPosFlags.SWP_FRAMECHANGED
            );
            if (!setPosResult)
            {
                int error = Marshal.GetLastWin32Error();
                Log($"SetWindowPos failed: hwnd={hwnd}, error={error}");
                return false;
            }

            return true;
        }

        private static nint CreatePerPixelBitmap(
            nint hdc,
            int width,
            int height,
            out nint bitsPointer
        )
        {
            bitsPointer = nint.Zero;

            BitmapInfo bitmapInfo = new();
            bitmapInfo.Header.Size = Marshal.SizeOf<BitmapInfoHeader>();
            bitmapInfo.Header.Width = width;
            bitmapInfo.Header.Height = height;
            bitmapInfo.Header.Planes = 1;
            bitmapInfo.Header.BitCount = 32;
            bitmapInfo.Header.Compression = BiRgb;

            nint dib = CreateDIBSection(
                hdc,
                ref bitmapInfo,
                DibRgb,
                out bitsPointer,
                nint.Zero,
                0
            );

            if (dib == nint.Zero)
            {
                return nint.Zero;
            }

            return dib;
        }

        private bool TryApplyLayeredWindowAlpha(nint hwnd, byte alpha)
        {
            if (hwnd == nint.Zero)
            {
                return false;
            }

            bool alphaApplied = SetLayeredWindowAttributes(
                hwnd,
                0,
                alpha,
                LayeredWindowAttributeFlags.LwaAlpha
            );
            if (!alphaApplied)
            {
                int error = Marshal.GetLastWin32Error();
                Log($"SetLayeredWindowAttributes failed: hwnd={hwnd}, error={error}, alpha={alpha}");
                return false;
            }

            Log($"SetLayeredWindowAttributes ok: hwnd={hwnd}, alpha={alpha}");
            return true;
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

        private static void Log(string message)
        {
            DebugRuntimeLog.Write(OverlayLogCategory, $"NativeOverlayHost: {message}");
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
        private enum SetWindowPosFlags : uint
        {
            SWP_NOACTIVATE = 0x0010,
            SWP_NOMOVE = 0x0002,
            SWP_NOSIZE = 0x0001,
            SWP_NOZORDER = 0x0004,
            SWP_SHOWWINDOW = 0x0040,
            SWP_FRAMECHANGED = 0x0020,
        }

        private enum WindowLongIndex
        {
            GwlExStyle = -20,
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

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader Header;
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
        private static extern nint GetDC(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ReleaseDC(nint hWnd, nint hDC);

        [Flags]
        private enum UpdateLayeredWindowFlags : uint
        {
            UlwAlpha = 0x00000002,
        }

        [Flags]
        private enum LayeredWindowAttributeFlags : uint
        {
            LwaColorKey = 0x00000001,
            LwaAlpha = 0x00000002,
        }

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
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(
            nint hwnd,
            uint crKey,
            byte bAlpha,
            LayeredWindowAttributeFlags dwFlags
        );

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern nint GetWindowLongPtr(nint hWnd, WindowLongIndex nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtr(nint hWnd, WindowLongIndex nIndex, nint dwNewLong);

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
        private static extern nint CreateDIBSection(
            nint hdc,
            ref BitmapInfo pbmi,
            uint usage,
            out nint ppvBits,
            nint hSection,
            uint offset
        );

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
