using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;

namespace IndigoMovieManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly object FileNotFoundLogLock = new();

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

                string logPath = Path.Combine(logDir, "firstchance.log");
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

            // 起動時に保存済みテーマモードを適用する。
            ApplyTheme(IndigoMovieManager.Properties.Settings.Default.ThemeMode);
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
            materialTheme.SetBaseTheme(isOriginalTheme ? BaseTheme.Light : BaseTheme.Inherit);
            paletteHelper.SetTheme(materialTheme);

            // 設定画面の文字は、OS連動ダーク時だけ白へ寄せる。
            bool useLightSettingsForeground = isOsSyncDark;
            app.Resources["SettingsForegroundBrush"] = new SolidColorBrush(
                useLightSettingsForeground ? Colors.White : Colors.Black
            );

            // メインヘッダーは背景が暗くなるため、ラベル文字と入力文字を分けて追従させる。
            app.Resources["MainHeaderForegroundBrush"] = new SolidColorBrush(
                isOriginalTheme || isOsSyncDark ? Colors.White : Colors.Black
            );
            app.Resources["MainHeaderInputForegroundBrush"] = new SolidColorBrush(
                isOriginalTheme ? Colors.Black : (isOsSyncDark ? Colors.White : Colors.Black)
            );
            app.Resources["GridTabTitleForegroundBrush"] =
                app.TryFindResource(isOsSyncDark ? "MaterialDesignBodyLight" : "MaterialDesignPaper")
                    as Brush
                ?? new SolidColorBrush(isOsSyncDark ? Colors.LightGray : Colors.White);

            string resourceUri = string.Equals(
                themeMode, "Original", StringComparison.OrdinalIgnoreCase)
                ? "pack://application:,,,/Themes/OriginalColors.xaml"
                : "pack://application:,,,/Themes/OsSyncColors.xaml";

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
    }
}
