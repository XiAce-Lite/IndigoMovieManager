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
        private static readonly TimeSpan LaunchDebounce = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan SessionRetention = TimeSpan.FromDays(7);
        private readonly object syncRoot = new();
        private readonly ThumbnailRescueWorkerLaunchSettings launchSettings;
        private ThumbnailFailureDbService cachedFailureDbService;
        private string cachedFailureDbMainDbFullPath = "";
        private Process currentProcess;
        private string currentSessionDirectory = "";
        private DateTime lastLaunchUtc = DateTime.MinValue;

        internal ThumbnailRescueWorkerLauncher(ThumbnailRescueWorkerLaunchSettings launchSettings)
        {
            this.launchSettings =
                launchSettings ?? throw new ArgumentNullException(nameof(launchSettings));
        }

        // pending_rescue が残っている時だけ worker を 1 本起動する。
        // 出力先は本exe側で解決した絶対パスを渡し、session配下へ逸れないようにする。
        public bool TryStartIfNeeded(
            string mainDbFullPath,
            string dbName,
            string thumbFolder,
            Action<string> log = null
        )
        {
            if (string.IsNullOrWhiteSpace(mainDbFullPath) || !File.Exists(mainDbFullPath))
            {
                return false;
            }

            lock (syncRoot)
            {
                ThumbnailFailureDbService failureDbService = GetOrCreateFailureDbService(mainDbFullPath);
                int recoveredStaleCount = failureDbService.RecoverExpiredProcessingToPendingRescue(
                    DateTime.UtcNow
                );
                if (recoveredStaleCount > 0)
                {
                    log?.Invoke($"rescue worker stale lease recovered: count={recoveredStaleCount}");
                }

                if (!failureDbService.HasPendingRescueWork(DateTime.UtcNow))
                {
                    return false;
                }

                if (IsCurrentProcessRunning())
                {
                    return false;
                }

                DateTime nowUtc = DateTime.UtcNow;
                if (lastLaunchUtc != DateTime.MinValue && nowUtc - lastLaunchUtc < LaunchDebounce)
                {
                    return false;
                }

                string workerExePath = launchSettings.WorkerExecutablePath;
                string sourceDirectory = Path.GetDirectoryName(workerExePath) ?? "";
                if (
                    string.IsNullOrWhiteSpace(workerExePath)
                    || !File.Exists(workerExePath)
                    || string.IsNullOrWhiteSpace(sourceDirectory)
                )
                {
                    log?.Invoke("rescue worker launch skipped: source worker not found.");
                    return false;
                }

                try
                {
                    CleanupOldSessions(log);

                    string resolvedThumbFolder = ResolveThumbFolderForWorker(
                        mainDbFullPath,
                        dbName,
                        thumbFolder,
                        launchSettings.HostBaseDirectory
                    );
                    string generationDirectory = BuildGenerationDirectory(workerExePath);
                    string sessionDirectory = Path.Combine(
                        generationDirectory,
                        $"session_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"
                    );
                    CopyDirectoryRecursive(sourceDirectory, sessionDirectory);
                    OverlaySupplementalDependencies(
                        launchSettings.SupplementalDirectoryPaths,
                        launchSettings.SupplementalFilePaths,
                        sessionDirectory,
                        log
                    );

                    string sessionExePath = Path.Combine(sessionDirectory, RescueWorkerExeName);
                    ProcessStartInfo startInfo = new()
                    {
                        FileName = sessionExePath,
                        Arguments = BuildWorkerArguments(
                            mainDbFullPath,
                            resolvedThumbFolder,
                            launchSettings.LogDirectoryPath,
                            launchSettings.FailureDbDirectoryPath
                        ),
                        WorkingDirectory = sessionDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    };

                    Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
                    process.OutputDataReceived += (_, e) =>
                        ForwardWorkerPipeLine("stdout", e.Data, log);
                    process.ErrorDataReceived += (_, e) =>
                        ForwardWorkerPipeLine("stderr", e.Data, log);
                    process.Exited += (_, _) => HandleWorkerExited(process, sessionDirectory, log);
                    if (!process.Start())
                    {
                        TryDeleteDirectoryQuietly(sessionDirectory);
                        return false;
                    }

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

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
                cachedFailureDbService = null;
                cachedFailureDbMainDbFullPath = "";
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

        // MainDBを切り替えた時だけ service を差し替え、通常は初期化済みインスタンスを使い回す。
        private ThumbnailFailureDbService GetOrCreateFailureDbService(string mainDbFullPath)
        {
            if (
                cachedFailureDbService != null
                && string.Equals(
                    cachedFailureDbMainDbFullPath,
                    mainDbFullPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return cachedFailureDbService;
            }

            cachedFailureDbService = new ThumbnailFailureDbService(mainDbFullPath);
            cachedFailureDbMainDbFullPath = mainDbFullPath;
            return cachedFailureDbService;
        }

        // worker へ渡す出力先は、main app と同じ基準で絶対パス化しておく。
        internal static string ResolveThumbFolderForWorker(
            string mainDbFullPath,
            string dbName,
            string thumbFolder,
            string appBaseDirectory
        )
        {
            string normalizedAppBaseDirectory = string.IsNullOrWhiteSpace(appBaseDirectory)
                ? AppContext.BaseDirectory
                : appBaseDirectory;
            string resolvedDbName = string.IsNullOrWhiteSpace(dbName)
                ? Path.GetFileNameWithoutExtension(mainDbFullPath) ?? ""
                : dbName.Trim();
            string candidate = ThumbRootResolver.ResolveRuntimeThumbRoot(
                mainDbFullPath,
                resolvedDbName,
                thumbFolder,
                normalizedAppBaseDirectory
            );

            if (Path.IsPathRooted(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            return Path.GetFullPath(candidate, normalizedAppBaseDirectory);
        }

        internal static string BuildWorkerArguments(
            string mainDbFullPath,
            string resolvedThumbFolder,
            string logDirectoryPath,
            string failureDbDirectoryPath
        )
        {
            List<string> args =
            [
                "--main-db",
                $"\"{mainDbFullPath}\"",
            ];

            if (!string.IsNullOrWhiteSpace(resolvedThumbFolder))
            {
                args.Add("--thumb-folder");
                args.Add($"\"{resolvedThumbFolder}\"");
            }

            if (!string.IsNullOrWhiteSpace(logDirectoryPath))
            {
                args.Add("--log-dir");
                args.Add($"\"{logDirectoryPath}\"");
            }

            if (!string.IsNullOrWhiteSpace(failureDbDirectoryPath))
            {
                args.Add("--failure-db-dir");
                args.Add($"\"{failureDbDirectoryPath}\"");
            }

            return string.Join(" ", args);
        }

        private string BuildGenerationDirectory(string workerExePath) =>
            BuildGenerationDirectory(launchSettings.SessionRootDirectoryPath, workerExePath);

        internal static string BuildGenerationDirectory(string rootPath, string workerExePath)
        {
            Directory.CreateDirectory(rootPath);

            string version = FileVersionInfo.GetVersionInfo(workerExePath).FileVersion ?? "0.0.0.0";
            FileInfo fileInfo = new(workerExePath);
            string signatureSource =
                $"{version}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}|{workerExePath}";
            string signature = BuildShortHash(signatureSource);
            string generationName = $"worker_v{version}_{signature}";
            string generationDirectory = Path.Combine(rootPath, generationName);
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

        // host が指定した補助依存だけを overlay し、launcher 本体は「何を同梱するか」を決めない。
        internal static void OverlaySupplementalDependencies(
            IReadOnlyList<string> supplementalDirectoryPaths,
            IReadOnlyList<string> supplementalFilePaths,
            string destinationDirectory,
            Action<string> log = null
        )
        {
            if (
                string.IsNullOrWhiteSpace(destinationDirectory)
                || !Directory.Exists(destinationDirectory)
            )
            {
                return;
            }

            for (int i = 0; i < (supplementalDirectoryPaths?.Count ?? 0); i++)
            {
                string directoryPath = supplementalDirectoryPaths[i];
                if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                {
                    continue;
                }

                string destinationPath = Path.Combine(
                    destinationDirectory,
                    Path.GetFileName(directoryPath)
                );
                CopyDirectoryRecursive(directoryPath, destinationPath);
                log?.Invoke($"rescue worker overlay dir: '{directoryPath}'");
            }

            for (int i = 0; i < (supplementalFilePaths?.Count ?? 0); i++)
            {
                string filePath = supplementalFilePaths[i];
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    continue;
                }

                string destinationPath = Path.Combine(
                    destinationDirectory,
                    Path.GetFileName(filePath)
                );
                File.Copy(filePath, destinationPath, overwrite: true);
                log?.Invoke($"rescue worker overlay file: '{filePath}'");
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

        private static void ForwardWorkerPipeLine(
            string streamName,
            string line,
            Action<string> log
        )
        {
            string message = FormatWorkerPipeLogLine(streamName, line);
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            log?.Invoke(message);
        }

        internal static string FormatWorkerPipeLogLine(string streamName, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return "";
            }

            string normalizedStream = string.Equals(
                streamName,
                "stderr",
                StringComparison.OrdinalIgnoreCase
            )
                ? "stderr"
                : "stdout";
            return $"rescue worker {normalizedStream}: {line.Trim()}";
        }

        private void CleanupOldSessions(Action<string> log) =>
            CleanupOldSessions(launchSettings.SessionRootDirectoryPath, DateTime.UtcNow, log);

        internal static void CleanupOldSessions(
            string rootPath,
            DateTime utcNow,
            Action<string> log = null
        )
        {
            if (!Directory.Exists(rootPath))
            {
                return;
            }

            DirectoryInfo rootInfo = new(rootPath);
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
                    if (utcNow - sessionDirectory.CreationTimeUtc <= SessionRetention)
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
