using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;

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
    }
}
