using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail
{
    // 外部救済workerをセッション専用フォルダへコピーして起動する。
    internal sealed class ThumbnailRescueWorkerLauncher : IDisposable
    {
        private const string RescueWorkerExeName = "IndigoMovieManager.Thumbnail.RescueWorker.exe";
        private const string RescueWorkerPathEnvName = "IMM_THUMB_RESCUE_WORKER_EXE_PATH";
        private static readonly TimeSpan LaunchDebounce = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan SessionRetention = TimeSpan.FromDays(7);
        private readonly object syncRoot = new();
        private Process currentProcess;
        private string currentSessionDirectory = "";
        private DateTime lastLaunchUtc = DateTime.MinValue;

        // pending_rescue が残っている時だけ worker を 1 本起動する。
        public bool TryStartIfNeeded(string mainDbFullPath, Action<string> log = null)
        {
            if (string.IsNullOrWhiteSpace(mainDbFullPath) || !File.Exists(mainDbFullPath))
            {
                return false;
            }

            ThumbnailFailureDbService failureDbService = new(mainDbFullPath);
            if (!failureDbService.HasPendingRescueWork(DateTime.UtcNow))
            {
                return false;
            }

            lock (syncRoot)
            {
                if (IsCurrentProcessRunning())
                {
                    return false;
                }

                DateTime nowUtc = DateTime.UtcNow;
                if (lastLaunchUtc != DateTime.MinValue && nowUtc - lastLaunchUtc < LaunchDebounce)
                {
                    return false;
                }

                if (!TryResolveWorkerSourceDirectory(out string sourceDirectory, out string workerExePath))
                {
                    log?.Invoke("rescue worker launch skipped: source worker not found.");
                    return false;
                }

                try
                {
                    CleanupOldSessions(log);

                    string generationDirectory = BuildGenerationDirectory(workerExePath);
                    string sessionDirectory = Path.Combine(
                        generationDirectory,
                        $"session_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"
                    );
                    CopyDirectoryRecursive(sourceDirectory, sessionDirectory);

                    string sessionExePath = Path.Combine(sessionDirectory, RescueWorkerExeName);
                    ProcessStartInfo startInfo = new()
                    {
                        FileName = sessionExePath,
                        Arguments = $"--main-db \"{mainDbFullPath}\"",
                        WorkingDirectory = sessionDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
                    process.Exited += (_, _) => HandleWorkerExited(process, sessionDirectory, log);
                    if (!process.Start())
                    {
                        TryDeleteDirectoryQuietly(sessionDirectory);
                        return false;
                    }

                    currentProcess = process;
                    currentSessionDirectory = sessionDirectory;
                    lastLaunchUtc = nowUtc;
                    log?.Invoke(
                        $"rescue worker launched: pid={process.Id} session='{sessionDirectory}'"
                    );
                    return true;
                }
                catch (Exception ex)
                {
                    log?.Invoke($"rescue worker launch failed: {ex.Message}");
                    return false;
                }
            }
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (currentProcess == null)
                {
                    return;
                }

                try
                {
                    currentProcess.Dispose();
                }
                catch
                {
                    // プロセスdispose失敗は終了処理より優先しない。
                }

                currentProcess = null;
                currentSessionDirectory = "";
            }
        }

        private bool IsCurrentProcessRunning()
        {
            if (currentProcess == null)
            {
                return false;
            }

            try
            {
                if (!currentProcess.HasExited)
                {
                    return true;
                }
            }
            catch
            {
                // 状態取得失敗時は再起動可能とみなす。
            }

            TryDisposeCurrentProcess();
            return false;
        }

        private void HandleWorkerExited(Process process, string sessionDirectory, Action<string> log)
        {
            try
            {
                log?.Invoke(
                    $"rescue worker exited: pid={process?.Id} code={process?.ExitCode} session='{sessionDirectory}'"
                );
            }
            catch
            {
                // Exited時の観測失敗は握る。
            }

            lock (syncRoot)
            {
                if (ReferenceEquals(currentProcess, process))
                {
                    TryDisposeCurrentProcess();
                    currentSessionDirectory = "";
                }
            }

            TryDeleteDirectoryQuietly(sessionDirectory);
        }

        private void TryDisposeCurrentProcess()
        {
            if (currentProcess == null)
            {
                return;
            }

            try
            {
                currentProcess.Dispose();
            }
            catch
            {
                // プロセスdispose失敗は無視する。
            }

            currentProcess = null;
        }

        private static bool TryResolveWorkerSourceDirectory(
            out string sourceDirectory,
            out string workerExePath
        )
        {
            sourceDirectory = "";
            workerExePath = "";

            string envPath = Environment.GetEnvironmentVariable(RescueWorkerPathEnvName)?.Trim('"') ?? "";
            List<string> candidates =
            [
                envPath,
                Path.Combine(AppContext.BaseDirectory, "rescue-worker", RescueWorkerExeName),
                Path.Combine(AppContext.BaseDirectory, RescueWorkerExeName),
                Path.GetFullPath(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "..",
                        "..",
                        "..",
                        "..",
                        "src",
                        "IndigoMovieManager.Thumbnail.RescueWorker",
                        "bin",
                        "x64",
                        "Debug",
                        "net8.0-windows",
                        RescueWorkerExeName
                    )
                ),
                Path.GetFullPath(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "..",
                        "..",
                        "..",
                        "..",
                        "src",
                        "IndigoMovieManager.Thumbnail.RescueWorker",
                        "bin",
                        "x64",
                        "Release",
                        "net8.0-windows",
                        RescueWorkerExeName
                    )
                ),
            ];

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                {
                    continue;
                }

                sourceDirectory = Path.GetDirectoryName(candidate) ?? "";
                workerExePath = candidate;
                return !string.IsNullOrWhiteSpace(sourceDirectory);
            }

            return false;
        }

        private static string BuildGenerationDirectory(string workerExePath)
        {
            string root = AppLocalDataPaths.RescueWorkerSessionsPath;
            Directory.CreateDirectory(root);

            string version = FileVersionInfo.GetVersionInfo(workerExePath).FileVersion ?? "0.0.0.0";
            FileInfo fileInfo = new(workerExePath);
            string signatureSource =
                $"{version}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}|{workerExePath}";
            string signature = BuildShortHash(signatureSource);
            string generationName = $"worker_v{version}_{signature}";
            string generationDirectory = Path.Combine(root, generationName);
            Directory.CreateDirectory(generationDirectory);
            return generationDirectory;
        }

        // worker出力一式をそのままセッションへ複製し、DLLロックの影響を隔離する。
        private static void CopyDirectoryRecursive(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (string directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
            }

            foreach (string filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDirectory, filePath);
                string destinationPath = Path.Combine(destinationDirectory, relative);
                string parent = Path.GetDirectoryName(destinationPath) ?? "";
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(filePath, destinationPath, overwrite: true);
            }
        }

        private static string BuildShortHash(string source)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(source ?? "");
            byte[] hash = SHA256.HashData(bytes);
            StringBuilder builder = new();
            for (int i = 0; i < 4; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }
            return builder.ToString();
        }

        private static void CleanupOldSessions(Action<string> log)
        {
            string root = AppLocalDataPaths.RescueWorkerSessionsPath;
            if (!Directory.Exists(root))
            {
                return;
            }

            DirectoryInfo rootInfo = new(root);
            DirectoryInfo[] generationDirectories = rootInfo
                .GetDirectories()
                .OrderByDescending(x => x.CreationTimeUtc)
                .ToArray();

            for (int i = 0; i < generationDirectories.Length; i++)
            {
                DirectoryInfo generationDirectory = generationDirectories[i];
                bool keepGeneration = i < 3;
                if (!keepGeneration)
                {
                    TryDeleteDirectoryQuietly(generationDirectory.FullName);
                    continue;
                }

                foreach (DirectoryInfo sessionDirectory in generationDirectory.GetDirectories())
                {
                    if (DateTime.UtcNow - sessionDirectory.CreationTimeUtc <= SessionRetention)
                    {
                        continue;
                    }

                    TryDeleteDirectoryQuietly(sessionDirectory.FullName);
                }
            }

            log?.Invoke("rescue worker session cleanup completed.");
        }

        private static void TryDeleteDirectoryQuietly(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            try
            {
                Directory.Delete(directoryPath, recursive: true);
            }
            catch
            {
                // セッション掃除失敗は次回cleanupへ回す。
            }
        }
    }
}
