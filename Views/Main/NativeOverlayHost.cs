using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    internal sealed partial class NativeOverlayHost : IDisposable
    {
        private const int OverlayWidth = 460;
        private const int OverlayHeight = 48;
        private const int OverlayBottomMargin = 24;
        private const byte OverlayAlpha = 153;
        private const uint DibRgb = 0;
        private const uint BiRgb = 0;
        private const double FallbackWindowOpacity = 0.6;
        private const string OverlayLogCategory = "ui-overlay";
        private const int ReleaseLogThrottleMilliseconds = 800;
        private static readonly nint HwndTopmost = new(-1);
#if !DEBUG
        private static readonly bool IsReleaseConfiguration = true;
#else
        private static readonly bool IsReleaseConfiguration = false;
#endif

        private readonly object _gate = new();
        private readonly Dictionary<string, DateTime> _releaseLogThrottleMap = new();
        private readonly ConcurrentQueue<Action> _pendingActions = new();
        private UiHangOverlayPlacement _placement;
        private Thread _overlayThread;
        private Dispatcher _overlayDispatcher;
        private nint _overlayHwnd;
        private WndProcDelegate _wndProcDelegate;
        private ushort _windowClassAtom;
        private UiHangNotificationLevel _currentLevel = UiHangNotificationLevel.Warning;
        private string _currentMessage = "UI応答低下を検知";
        private nint _messageFont = nint.Zero;
        private Window _fallbackWindow;
        private Grid _fallbackRoot;
        private Border _fallbackContent;
        private TextBlock _fallbackText;
        private bool _useFallbackWindow;
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

        // HwndSource は WS_EX_LAYERED を勝手に剥ぎ取るため、CreateWindowEx で直接ウィンドウを作成する。
        private void CreateOverlayOnCurrentThread()
        {
            _useFallbackWindow = false;

            // WndProc デリゲートを GC から守るためにフィールドに保持する。
            _wndProcDelegate = OverlayWndProc;

            string className = $"UiHangOverlay_{Environment.CurrentManagedThreadId}";
            WNDCLASSEX wc = new()
            {
                CbSize = Marshal.SizeOf<WNDCLASSEX>(),
                LpfnWndProc = _wndProcDelegate,
                HInstance = GetModuleHandle(null),
                LpszClassName = className,
            };
            _windowClassAtom = RegisterClassExW(ref wc);
            if (_windowClassAtom == 0)
            {
                int error = Marshal.GetLastWin32Error();
                Log($"RegisterClassEx failed: error={error}");
                _useFallbackWindow = true;
                EnsureFallbackWindowOnCurrentThread();
                HideOnOverlayThread();
                return;
            }

            uint exStyle = (uint)(
                WindowExStyles.WS_EX_LAYERED
                | WindowExStyles.WS_EX_TOPMOST
                | WindowExStyles.WS_EX_TOOLWINDOW
                | WindowExStyles.WS_EX_NOACTIVATE
                | WindowExStyles.WS_EX_TRANSPARENT
            );

            _overlayHwnd = CreateWindowExW(
                exStyle,
                className,
                "UiHangOverlayBanner",
                (uint)WindowStyles.WS_POPUP,
                0,
                0,
                OverlayWidth,
                OverlayHeight,
                nint.Zero,
                nint.Zero,
                wc.HInstance,
                nint.Zero
            );

            _messageFont = CreateFontW(
                -16, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Yu Gothic UI"
            );

            if (_overlayHwnd == nint.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Log($"CreateWindowEx failed: error={error}");
                _useFallbackWindow = true;
                EnsureFallbackWindowOnCurrentThread();
                HideOnOverlayThread();
                return;
            }

            Log($"overlay created. hwnd={_overlayHwnd}");
            // alpha は UpdateLayeredWindow の BlendFunction.SourceConstantAlpha で適用する。
            // SetLayeredWindowAttributes と UpdateLayeredWindow は排他的なため、
            // 初期化時に SetLayeredWindowAttributes を呼ぶと UpdateLayeredWindow が error=87 で失敗する。

            HideOnOverlayThread();
        }

        // 最小限の WndProc: メッセージを全て既定処理に委譲する。
        private static nint OverlayWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
        {
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // Overlay の透過 API が環境依存で失敗する場合に備えて、透過可能な WPF Window をフォールバックとして準備する。
        private void EnsureFallbackWindowOnCurrentThread()
        {
            if (_fallbackWindow != null)
            {
                return;
            }

            _fallbackRoot = new Grid { Background = Brushes.Transparent };
            _fallbackContent = new Border
            {
                Height = OverlayHeight,
                Width = OverlayWidth,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(16, 0, 16, 0),
                Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                Child = _fallbackText = new TextBlock
                {
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontFamily = new FontFamily("Yu Gothic UI"),
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                },
            };

            _fallbackRoot.Children.Add(_fallbackContent);

            _fallbackWindow = new Window
            {
                Width = OverlayWidth,
                Height = OverlayHeight,
                Content = _fallbackRoot,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Opacity = FallbackWindowOpacity,
                IsHitTestVisible = false,
                ShowActivated = false,
            };
            _fallbackWindow.Show();
            _fallbackWindow.Hide();
        }

        private void DestroyOverlayOnCurrentThread()
        {
            if (_overlayHwnd != nint.Zero)
            {
                _ = DestroyWindow(_overlayHwnd);
                _overlayHwnd = nint.Zero;
            }

            if (_windowClassAtom != 0)
            {
                _ = UnregisterClassW(
                    $"UiHangOverlay_{Environment.CurrentManagedThreadId}",
                    GetModuleHandle(null)
                );
                _windowClassAtom = 0;
            }

            _wndProcDelegate = null;

            if (_fallbackWindow != null)
            {
                _fallbackWindow.Close();
                _fallbackWindow = null;
            }

            _fallbackRoot = null;
            _fallbackContent = null;
            _fallbackText = null;

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
            if (_overlayHwnd == nint.Zero)
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
            _isVisible = false;
            if (_fallbackWindow != null)
            {
                _fallbackWindow.Hide();
            }

            if (_overlayHwnd == nint.Zero)
            {
                return;
            }

            Log("overlay hide request");
            _ = ShowWindow(_overlayHwnd, ShowWindowCommand.SW_HIDE);
        }

        // 位置更新後にレイヤードウィンドウを毎回再描画し、alpha を確実に反映する。
        private void RenderOverlayOnOverlayThread(bool showWindow)
        {
            if (_overlayHwnd == nint.Zero || !_isVisible)
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
            (int resolvedX, int resolvedY, nint monitorHandle) = ResolveAndClampOverlayPosition(x, y);
            x = resolvedX;
            y = resolvedY;

            // fallback モード中は WPF Window で描画し、native 側は触らない。
            if (_useFallbackWindow)
            {
                bool fallbackRendered = RenderByFallbackWindow(x, y, monitorHandle);
                if (showWindow && _fallbackWindow != null)
                {
                    Log($"overlay fallback show: hwnd={_overlayHwnd}, x={x}, y={y}");
                    if (!_fallbackWindow.IsVisible)
                    {
                        _fallbackWindow.Show();
                    }
                }

                if (!fallbackRendered)
                {
                    Log($"overlay fallback render failed: hwnd={_overlayHwnd}, x={x}, y={y}");
                }

                return;
            }

            _ = SetWindowPos(
                _overlayHwnd,
                HwndTopmost,
                x,
                y,
                OverlayWidth,
                OverlayHeight,
                SetWindowPosFlags.SWP_NOACTIVATE
                    | SetWindowPosFlags.SWP_SHOWWINDOW
            );

            bool rendered = TryRenderByUpdateLayeredWindow(_overlayHwnd, x, y);
            if (!rendered)
            {
                Log($"overlay render failed: hwnd={_overlayHwnd}, x={x}, y={y}");
            }
            else
            {
                Log($"overlay rendered: hwnd={_overlayHwnd}, x={x}, y={y}");
            }

            if (showWindow)
            {
                Log($"overlay show: hwnd={_overlayHwnd}");
                _ = ShowWindow(_overlayHwnd, ShowWindowCommand.SW_SHOWNOACTIVATE);
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
                    // UpdateLayeredWindow 失敗時は即座に fallback へ移行し、native 側の再試行はしない。
                    if (!updated)
                    {
                        int error = Marshal.GetLastWin32Error();
                        Log($"UpdateLayeredWindow failed: hwnd={hwnd}, error={error}, x={x}, y={y}");

                        if (!_useFallbackWindow)
                        {
                            _useFallbackWindow = true;
                            EnsureFallbackWindowOnCurrentThread();
                            Log(
                                $"overlay native renderer disabled: switched to fallback window (error={error}): hwnd={hwnd}, x={x}, y={y}"
                            );
                            return RenderByFallbackWindow(x, y, nint.Zero);
                        }

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

        // 呼び出し元でクランプ済みの座標を受け取り、DPI 換算して WPF Window へ反映する。
        private bool RenderByFallbackWindow(int x, int y, nint monitorHint)
        {
            if (!_useFallbackWindow)
            {
                return false;
            }

            EnsureFallbackWindowOnCurrentThread();
            if (_fallbackWindow == null || _fallbackText == null || _fallbackContent == null)
            {
                return false;
            }

            // 座標クランプは呼び出し元（RenderOverlayOnOverlayThread）で実施済み。
            // monitorHint をそのまま DPI 取得に使う。
            double scaleX = GetMonitorScale(monitorHint, true);
            double scaleY = GetMonitorScale(monitorHint, false);
            double leftDip = scaleX > 0 ? x / scaleX : x;
            double topDip = scaleY > 0 ? y / scaleY : y;
            double widthDip = scaleX > 0 ? OverlayWidth / scaleX : OverlayWidth;
            double heightDip = scaleY > 0 ? OverlayHeight / scaleY : OverlayHeight;

            var accentColor = ResolveAccentColor(_currentLevel);
            _fallbackContent.Background = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            _fallbackText.Foreground = new SolidColorBrush(accentColor);
            _fallbackText.Text = _currentMessage ?? "";

            _fallbackWindow.Width = Math.Max(1, widthDip);
            _fallbackWindow.Height = Math.Max(1, heightDip);
            _fallbackWindow.Left = leftDip;
            _fallbackWindow.Top = topDip;
            _fallbackWindow.Opacity = FallbackWindowOpacity;
            _fallbackWindow.Topmost = true;
            _fallbackWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            if (!_fallbackWindow.IsVisible)
            {
                _fallbackWindow.Show();
            }

            return true;
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

        private static Color ResolveAccentColor(UiHangNotificationLevel level)
        {
            uint colorRef = ResolveAccentColorRef(level);
            return Color.FromRgb(
                (byte)(colorRef & 0xFF),
                (byte)((colorRef >> 8) & 0xFF),
                (byte)((colorRef >> 16) & 0xFF)
            );
        }

        // 表示座標を現在のモニタ作業領域内へ補正し、モニタ情報を返す。
        private (int X, int Y, nint MonitorHandle) ResolveAndClampOverlayPosition(int x, int y)
        {
            NativeRect targetRect = new()
            {
                Left = x,
                Top = y,
                Right = x + OverlayWidth,
                Bottom = y + OverlayHeight,
            };

            nint monitor = MonitorFromRect(ref targetRect, MONITOR_DEFAULTTONEAREST);
            if (monitor == nint.Zero)
            {
                return (x, y, nint.Zero);
            }

            MONITORINFO monitorInfo = new()
            {
                CbSize = Marshal.SizeOf<MONITORINFO>(),
            };

            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return (x, y, monitor);
            }

            int clampedX = Math.Clamp(x, monitorInfo.Work.Left, monitorInfo.Work.Right - OverlayWidth);
            int clampedY = Math.Clamp(y, monitorInfo.Work.Top, monitorInfo.Work.Bottom - OverlayHeight);
            return (clampedX, clampedY, monitor);
        }

        private double GetMonitorScale(nint monitor, bool horizontal)
        {
            if (monitor == nint.Zero)
            {
                return 1.0;
            }

            int result = 0;
            uint dpiX;
            uint dpiY;
            try
            {
                result = GetDpiForMonitor(
                    monitor,
                    MonitorDpiType.MdtEffectiveDpi,
                    out dpiX,
                    out dpiY
                );
            }
            catch (Exception)
            {
                return 1.0;
            }

            if (result != 0)
            {
                return 1.0;
            }

            if (horizontal)
            {
                return Math.Max(1.0, dpiX / 96.0);
            }

            return Math.Max(1.0, dpiY / 96.0);
        }

        private static uint ToColorRef(byte red, byte green, byte blue)
        {
            return (uint)(red | (green << 8) | (blue << 16));
        }

        private static bool IsReleaseVerboseLog(string message)
        {
            if (!IsReleaseConfiguration)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            ReadOnlySpan<char> text = message.AsSpan();

            if (
                text.StartsWith("overlay thread start")
                || text.StartsWith("overlay thread created")
                || text.StartsWith("overlay thread destroyed")
                || text.StartsWith("overlay thread stop requested")
                || text.StartsWith("overlay thread join wait")
                || text.StartsWith("overlay created.")
                || text.StartsWith("overlay hide request")
                || text.StartsWith("overlay fallback show")
                || text.StartsWith("overlay fallback render failed")
                || text.StartsWith("overlay native initialize failed")
                || text.StartsWith("overlay native renderer disabled")
                || text.StartsWith("overlay render failed")
                || text.StartsWith("UpdateLayeredWindow failed")
                || text.StartsWith("UpdateLayeredWindow retry failed")
                || text.StartsWith("SetLayeredWindowAttributes failed")
                || text.StartsWith("RegisterClassEx failed")
                || text.StartsWith("CreateWindowEx failed")
                || text.StartsWith("GetWindowLongPtr failed")
                || text.StartsWith("SetWindowLongPtr failed")
                || text.StartsWith("SetWindowPos failed")
                || text.StartsWith("overlay exstyle before")
                || text.StartsWith("overlay exstyle after")
            )
            {
                return true;
            }

            return false;
        }

        private bool IsReleaseLogThrottled(string prefix)
        {
            if (!IsReleaseConfiguration)
            {
                return false;
            }

            DateTime now = DateTime.UtcNow;
            DateTime lastLogAt;
            if (_releaseLogThrottleMap.TryGetValue(prefix, out lastLogAt))
            {
                if ((now - lastLogAt).TotalMilliseconds < ReleaseLogThrottleMilliseconds)
                {
                    return true;
                }
            }

            _releaseLogThrottleMap[prefix] = now;
            return false;
        }

        private void Log(string message)
        {
            if (!IsReleaseVerboseLog(message))
            {
                return;
            }

            if (
                IsReleaseConfiguration
                && (
                    message.StartsWith("overlay render failed")
                    || message.StartsWith("UpdateLayeredWindow failed")
                    || message.StartsWith("UpdateLayeredWindow retry failed")
                    || message.StartsWith("SetLayeredWindowAttributes failed")
                    || message.StartsWith("overlay fallback render failed")
                )
                && IsReleaseLogThrottled(message.AsSpan().Contains(':') ? message[..message.IndexOf(':')] : message)
            )
            {
                return;
            }

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
    }

    internal readonly record struct UiHangOverlayPlacement(Rect Bounds)
    {
        internal bool IsEmpty => Bounds.Width <= 0 || Bounds.Height <= 0;
    }
}
