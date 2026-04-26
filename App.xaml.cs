using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;
using Vs2013DarkTheme = AvalonDock.Themes.Vs2013DarkTheme;
using Vs2013LightTheme = AvalonDock.Themes.Vs2013LightTheme;

namespace IndigoMovieManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly object FileNotFoundLogLock = new();
        private static bool? LastAppliedOsSyncDarkTheme;
        private const int DwmaUseImmersiveDarkMode = 20;
        private const int DwmaUseImmersiveDarkModeLegacy = 19;
        private const int DwmaCaptionColor = 35;
        private const int DwmaTextColor = 36;
        private const int DwmaColorDefault = unchecked((int)0xFFFFFFFF);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const string DispatcherSetWin32TimerStackMarker =
            "System.Windows.Threading.Dispatcher.SetWin32Timer";
        private const string DispatcherTimerStartStackMarker =
            "System.Windows.Threading.DispatcherTimer.Start";
        private const string MediaContextCommitStackMarker =
            "System.Windows.Media.MediaContext.CommitChannelAfterNextVSync";

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int attributeValue,
            int attributeSize
        );

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags
        );

        public App()
        {
#if DEBUG
            // デバッグ中だけ、FileNotFound の詳細（対象ファイル名/発生箇所）を出す。
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
#endif
        }

#if DEBUG
        private static void OnFirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is not FileNotFoundException ex)
            {
                return;
            }
            if (IsIgnorableFileNotFound(ex))
            {
                return;
            }

            string stack = ex.StackTrace ?? "";
            // ローカライズ探索などのノイズを減らすため、手掛かりがあるものだけ拾う。
            bool hasFileName = !string.IsNullOrWhiteSpace(ex.FileName);
            bool isAppStack = stack.Contains("IndigoMovieManager", StringComparison.Ordinal);
            if (!hasFileName && !isAppStack)
            {
                return;
            }

            Debug.WriteLine(
                $"[FileNotFound] File='{ex.FileName ?? "(unknown)"}' Message='{ex.Message}'"
            );
            Debug.WriteLine(stack);
            WriteFileNotFoundLog(ex.FileName, ex.Message, stack);
        }

        private static bool IsIgnorableFileNotFound(FileNotFoundException ex)
        {
            string fileName = ex.FileName ?? "";
            string message = ex.Message ?? "";

            // XmlSerializer は事前生成DLLを探索してから動的生成へフォールバックする。
            // その探索失敗は通常動作なので、診断ログ対象から外す。
            if (fileName.Contains(".XmlSerializers", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (message.Contains(".XmlSerializers", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static void WriteFileNotFoundLog(string fileName, string message, string stack)
        {
            try
            {
                // VS出力が拾いづらい環境でも見られるよう、ローカルへ追記する。
                string logDir = AppLocalDataPaths.LogsPath;
                Directory.CreateDirectory(logDir);

                string logPath = IndigoMovieManager.Thumbnail.LogFileTimeWindowSeparator.PrepareForWrite(
                    Path.Combine(logDir, "firstchance.log")
                );
                string line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] File='{fileName ?? "(unknown)"}' Message='{message}'{Environment.NewLine}{stack}{Environment.NewLine}";

                lock (FileNotFoundLogLock)
                {
                    File.AppendAllText(logPath, line);
                }
            }
            catch
            {
                // ログ出力失敗で本体動作を止めない。
            }
        }
#endif

        // StartupUri=MainWindow.xaml により、アプリ起動時は MainWindow が最初に開く。
        // グローバル初期化が必要になった場合はここへ追記する。

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 補助 overlay window が一瞬残っても、MainWindow close を終了条件として固定する。
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // WPF 内部の SetWin32Timer 失敗だけは、ログと機能縮退へ逃がして本体クラッシュを防ぐ。
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Queue / FailureDb / 補助ログの保存先は host 側で固定し、Queue project へ app 固有規約を持ち込まない。
            ThumbnailQueueHostPathPolicy.Configure(
                queueDbDirectoryPath: AppLocalDataPaths.QueueDbPath,
                failureDbDirectoryPath: AppLocalDataPaths.FailureDbPath,
                logDirectoryPath: AppLocalDataPaths.LogsPath
            );

            // rescue trace は engine 側へ app 固有パスを持ち込まず、起動時に設定する。
            IndigoMovieManager.Thumbnail.ThumbnailRescueTraceLog.ConfigureLogDirectory(
                AppLocalDataPaths.LogsPath
            );

            // OSテーマ変更時に、OS連動モードだけ即時反映する。
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // 起動時に保存済みテーマモードを適用する。
            ApplyTheme(IndigoMovieManager.Properties.Settings.Default.ThemeMode);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e
        )
        {
            if (!ShouldSuppressKnownDispatcherTimerWin32Exception(e?.Exception))
            {
                return;
            }

            LogKnownDispatcherTimerWin32Exception(e.Exception);
            if (Current?.MainWindow is MainWindow mainWindow)
            {
                mainWindow.HandleDispatcherTimerInfrastructureFault(
                    "dispatcher-unhandled",
                    e.Exception as System.ComponentModel.Win32Exception
                );
            }

            e.Handled = true;
        }

        // WPF 内部の render timer 起点だけを狙い撃ちし、他の Win32Exception は握り潰さない。
        internal static bool ShouldSuppressKnownDispatcherTimerWin32Exception(
            Exception exception,
            string stackTraceOverride = null
        )
        {
            if (exception is not System.ComponentModel.Win32Exception)
            {
                return false;
            }

            string stackTrace = stackTraceOverride ?? exception.StackTrace ?? "";
            if (string.IsNullOrWhiteSpace(stackTrace))
            {
                return string.Equals(
                    exception.TargetSite?.Name,
                    "SetWin32Timer",
                    StringComparison.Ordinal
                );
            }

            bool isSetWin32TimerPath = stackTrace.Contains(
                DispatcherSetWin32TimerStackMarker,
                StringComparison.Ordinal
            );
            bool isDispatcherTimerStartPath = stackTrace.Contains(
                DispatcherTimerStartStackMarker,
                StringComparison.Ordinal
            );
            bool isMediaContextCommitPath = stackTrace.Contains(
                MediaContextCommitStackMarker,
                StringComparison.Ordinal
            );

            // SetWin32Timer という名前だけでは広すぎるため、DispatcherTimer 経路まで見えている時だけ握る。
            return (isSetWin32TimerPath && isDispatcherTimerStartPath)
                || (isDispatcherTimerStartPath && isMediaContextCommitPath);
        }

        private static void LogKnownDispatcherTimerWin32Exception(Exception exception)
        {
            int nativeErrorCode = exception is System.ComponentModel.Win32Exception win32
                ? win32.NativeErrorCode
                : 0;
            int userObjects = TryGetGuiResourceCount(0);
            int gdiObjects = TryGetGuiResourceCount(1);
            string stackHead = ExtractStackHead(exception?.StackTrace);
            DebugRuntimeLog.Write(
                "ui-timer",
                $"suppressed WPF SetWin32Timer failure: native_error={nativeErrorCode} user_objects={userObjects} gdi_objects={gdiObjects} err='{exception?.GetType().Name}: {exception?.Message}' stack='{stackHead}'"
            );
        }

        private static string ExtractStackHead(string stackTrace)
        {
            if (string.IsNullOrWhiteSpace(stackTrace))
            {
                return "";
            }

            string[] lines = stackTrace.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
            );
            return string.Join(" | ", lines.Take(3));
        }

        private static int TryGetGuiResourceCount(int resourceKind)
        {
            try
            {
                using Process process = Process.GetCurrentProcess();
                return GetGuiResources(process.Handle, resourceKind);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// テーマ用 ResourceDictionary を差し替える。
        /// "Original" ならオリジナル色、それ以外は OS 連動。
        /// </summary>
        public static void ApplyTheme(string themeMode)
        {
            var app = Current;
            if (app == null) return;

            // MDIX本体のベーステーマも合わせて切り替え、入力欄の下線や装飾を正しい配色へ戻す。
            var paletteHelper = new PaletteHelper();
            Theme materialTheme = paletteHelper.GetTheme();
            bool isOriginalTheme = string.Equals(
                themeMode,
                "Original",
                StringComparison.OrdinalIgnoreCase
            );
            bool isOsSyncDark = !isOriginalTheme && IsWindowsAppsDarkThemeEnabled();
            bool useDarkTheme = !isOriginalTheme && isOsSyncDark;
            LastAppliedOsSyncDarkTheme = isOriginalTheme ? null : isOsSyncDark;
            materialTheme.SetBaseTheme(useDarkTheme ? BaseTheme.Dark : BaseTheme.Light);
            paletteHelper.SetTheme(materialTheme);

            // 上下タブは AvalonDock と MDIX の両方が噛むため、OS連動時も明示的に合わせる。
            app.Resources["AvalonDockTheme"] =
                isOriginalTheme || !isOsSyncDark ? new Vs2013LightTheme() : new Vs2013DarkTheme();

            // 設定画面の文字は、OS連動ダーク時だけ白へ寄せる。
            bool useLightSettingsForeground = isOsSyncDark;
            app.Resources["SettingsForegroundBrush"] = new SolidColorBrush(
                useLightSettingsForeground ? Colors.White : Colors.Black
            );

            // 左ドロワーは、OS連動では本文色、Original では旧UI相当の indigo を使う。
            app.Resources["LeftDrawerForegroundBrush"] =
                isOriginalTheme
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF303F9F"))
                    : app.TryFindResource("MaterialDesignBody") as Brush
                        ?? new SolidColorBrush(Colors.Black);

            // メインヘッダーは背景が暗くなるため、ラベル文字と入力文字を分けて追従させる。
            app.Resources["MainHeaderForegroundBrush"] = new SolidColorBrush(
                isOriginalTheme || isOsSyncDark ? Colors.White : Colors.Black
            );
            app.Resources["MainHeaderInputForegroundBrush"] = new SolidColorBrush(
                isOriginalTheme ? Colors.Black : (isOsSyncDark ? Colors.White : Colors.Black)
            );
            Brush upperTabPanelForegroundBrush =
                isOriginalTheme
                    ? app.TryFindResource(SystemColors.ControlTextBrushKey) as Brush
                        ?? SystemColors.ControlTextBrush
                    : isOsSyncDark
                        ? new SolidColorBrush(Colors.DarkGray)
                        : app.TryFindResource("MaterialDesignBody") as Brush
                            ?? new SolidColorBrush(Colors.DimGray);
            app.Resources["UpperTabPanelForegroundBrush"] = upperTabPanelForegroundBrush;
            app.Resources["GridTabTitleForegroundBrush"] =
                new SolidColorBrush(Colors.DarkGray);

            string resourceUri = string.Equals(
                themeMode, "Original", StringComparison.OrdinalIgnoreCase)
                ? BuildApplicationResourceUri("Themes/OriginalColors.xaml")
                : BuildApplicationResourceUri("Themes/OsSyncColors.xaml");

            var dict = new ResourceDictionary { Source = new Uri(resourceUri) };

            // 既存のテーマ辞書があれば先に除去する。
            var existing = app.Resources.MergedDictionaries.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.ToString().EndsWith("OriginalColors.xaml", StringComparison.OrdinalIgnoreCase) ||
                 d.Source.ToString().EndsWith("OsSyncColors.xaml", StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                app.Resources.MergedDictionaries.Remove(existing);
            }

            app.Resources.MergedDictionaries.Add(dict);

            // 開いている全ウィンドウのタイトルバーも、テーマ変更に追従させる。
            foreach (Window window in app.Windows)
            {
                ApplyWindowTitleBarTheme(window, isOriginalTheme, isOsSyncDark);
            }
        }

        private static string BuildApplicationResourceUri(string relativePath)
        {
            string assemblyName = typeof(App).Assembly.GetName().Name ?? "IndigoMovieManager";
            string normalizedPath = (relativePath ?? "").TrimStart('/');
            return $"pack://application:,,,/{assemblyName};component/{normalizedPath}";
        }

        // 各ウィンドウの標準タイトルバーへ、現在のテーマ設定を反映する。
        public static void ApplyWindowTitleBarTheme(Window window)
        {
            if (window == null)
            {
                return;
            }

            string themeMode = IndigoMovieManager.Properties.Settings.Default.ThemeMode ?? "";
            bool isOriginalTheme = string.Equals(
                themeMode,
                "Original",
                StringComparison.OrdinalIgnoreCase
            );
            bool isOsSyncDark = !isOriginalTheme && IsWindowsAppsDarkThemeEnabled();
            ApplyWindowTitleBarTheme(window, isOriginalTheme, isOsSyncDark);
        }

        // OS連動ダークは標準ダークバー、Originalは固定indigo、それ以外はOS既定へ戻す。
        private static void ApplyWindowTitleBarTheme(
            Window window,
            bool isOriginalTheme,
            bool isOsSyncDark
        )
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int immersiveDark = !isOriginalTheme && isOsSyncDark ? 1 : 0;
            if (!TrySetWindowAttribute(hwnd, DwmaUseImmersiveDarkMode, immersiveDark))
            {
                _ = TrySetWindowAttribute(hwnd, DwmaUseImmersiveDarkModeLegacy, immersiveDark);
            }

            int captionColor = isOriginalTheme
                ? ToColorRef((Color)ColorConverter.ConvertFromString("#FF303F9F"))
                : DwmaColorDefault;
            int textColor = isOriginalTheme ? ToColorRef(Colors.White) : DwmaColorDefault;
            _ = TrySetWindowAttribute(hwnd, DwmaCaptionColor, captionColor);
            _ = TrySetWindowAttribute(hwnd, DwmaTextColor, textColor);

            // 非クライアント領域の再描画を促して、色変更を即時反映させる。
            _ = SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged
            );
        }

        private static void OnUserPreferenceChanged(
            object sender,
            UserPreferenceChangedEventArgs e
        )
        {
            string themeMode = IndigoMovieManager.Properties.Settings.Default.ThemeMode ?? "";
            if (string.Equals(themeMode, "Original", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (
                e.Category != UserPreferenceCategory.General
                && e.Category != UserPreferenceCategory.Color
                && e.Category != UserPreferenceCategory.VisualStyle
            )
            {
                return;
            }

            bool nextIsDark = IsWindowsAppsDarkThemeEnabled();
            if (LastAppliedOsSyncDarkTheme.HasValue && LastAppliedOsSyncDarkTheme.Value == nextIsDark)
            {
                return;
            }

            Current?.Dispatcher.BeginInvoke(() => ApplyTheme("OsSync"));
        }

        private static bool IsWindowsAppsDarkThemeEnabled()
        {
            try
            {
                object value =
                    Registry.CurrentUser
                        .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                        ?.GetValue("AppsUseLightTheme");

                return value is int intValue && intValue == 0;
            }
            catch
            {
                // OSテーマ判定に失敗した時は、無理に白文字へ倒さない。
                return false;
            }
        }

        // Win32 COLORREF(0x00BBGGRR) に変換して、DWMへそのまま渡す。
        private static int ToColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        // 未対応OSでは E_INVALIDARG になり得るため、失敗は握り潰してフォールバックする。
        private static bool TrySetWindowAttribute(IntPtr hwnd, int attribute, int value)
        {
            int localValue = value;
            return DwmSetWindowAttribute(hwnd, attribute, ref localValue, sizeof(int)) >= 0;
        }

        [DllImport("user32.dll")]
        private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);
    }
}
