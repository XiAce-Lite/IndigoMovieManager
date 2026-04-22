using System.Runtime.InteropServices;

namespace IndigoMovieManager
{
    // NativeOverlayHost が使用する Win32 P/Invoke 宣言をビジネスロジックから分離して管理する。
    internal sealed partial class NativeOverlayHost
    {
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
            GwlHwndParent = -8,
            GwlExStyle = -20,
        }

        private enum WindowMessage : uint
        {
            WM_CLOSE = 0x0010,
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private enum MonitorDpiType : uint
        {
            MdtEffectiveDpi = 0,
        }

        [Flags]
        private enum DrawTextFormat : uint
        {
            DT_CENTER = 0x00000001,
            DT_WORDBREAK = 0x00000010,
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

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int CbSize;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint MonitorFromRect(ref NativeRect lprc, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("shcore.dll", SetLastError = true)]
        private static extern int GetDpiForMonitor(
            IntPtr hMonitor,
            MonitorDpiType dpiType,
            out uint dpiX,
            out uint dpiY
        );

        // CreateWindowEx による直接ウィンドウ生成に必要な宣言群。
        // HwndSource は WS_EX_LAYERED を勝手に剥ぎ取るため、こちらで直接生成する。

        private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int CbSize;
            public uint Style;
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public WndProcDelegate LpfnWndProc;
            public int CbClsExtra;
            public int CbWndExtra;
            public nint HInstance;
            public nint HIcon;
            public nint HCursor;
            public nint HbrBackground;
            public string LpszMenuName;
            public string LpszClassName;
            public nint HIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterClassW(string lpClassName, nint hInstance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowExW(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            nint hWndParent,
            nint hMenu,
            nint hInstance,
            nint lpParam
        );

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint GetModuleHandle(string lpModuleName);
    }
}
