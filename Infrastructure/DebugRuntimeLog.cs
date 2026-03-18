using System.Diagnostics;
using System.IO;

namespace IndigoMovieManager
{
    // Debug実行時だけ、処理の開始/終了をローカルログへ残す。
    internal static class DebugRuntimeLog
    {
        private static readonly object LogLock = new();
        private const long MaxLogFileBytes = 20 * 1024 * 1024;

        [Conditional("DEBUG")]
        internal static void Write(string category, string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}";
            Debug.WriteLine(line);

            try
            {
                // VS出力だけで追いにくいケースに備え、同じ内容をファイルにも追記する。
                string logDir = AppLocalDataPaths.LogsPath;
                Directory.CreateDirectory(logDir);
                string logPath = IndigoMovieManager.Thumbnail.LogFileTimeWindowSeparator.PrepareForWrite(
                    Path.Combine(logDir, "debug-runtime.log"),
                    MaxLogFileBytes
                );

                lock (LogLock)
                {
                    // 上限超過時は同日でも退避して、次の追記を継続できるようにする。
                    File.AppendAllText(logPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // ログ失敗で本体処理を止めない。
            }
        }

        [Conditional("DEBUG")]
        internal static void TaskStart(string taskName, string detail = "")
        {
            Write("task-start", $"{taskName} {detail}".Trim());
        }

        [Conditional("DEBUG")]
        internal static void TaskEnd(string taskName, string detail = "")
        {
            Write("task-end", $"{taskName} {detail}".Trim());
        }
    }
}
