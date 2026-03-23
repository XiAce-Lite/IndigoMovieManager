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
        private readonly Action afterLaunchReserved;
        private readonly Action beforeProcessStatePublish;
        private ThumbnailFailureDbService cachedFailureDbService;
        private string cachedFailureDbMainDbFullPath = "";
        private Process currentProcess;
        private string currentSessionDirectory = "";
        private DateTime lastLaunchUtc = DateTime.MinValue;
        private bool launchInProgress;
        private bool disposed;

        internal ThumbnailRescueWorkerLauncher(
            ThumbnailRescueWorkerLaunchSettings launchSettings,
            Action afterLaunchReserved = null,
            Action beforeProcessStatePublish = null
        )
        {
            this.launchSettings =
                launchSettings ?? throw new ArgumentNullException(nameof(launchSettings));
            this.afterLaunchReserved = afterLaunchReserved;
            this.beforeProcessStatePublish = beforeProcessStatePublish;
        }

        // pending_rescue が残っている時だけ worker を 1 本起動する。
        // 出力先は本exe側で解決した絶対パスを渡し、session配下へ逸れないようにする。
        public bool TryStartIfNeeded(
            string mainDbFullPath,
            string dbName,
            string thumbFolder,
            long requestedFailureId = 0,
            Action<string> log = null
        )
        {
            if (string.IsNullOrWhiteSpace(mainDbFullPath) || !File.Exists(mainDbFullPath))
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

            DateTime nowUtc = DateTime.UtcNow;
            string sessionDirectory = "";
            Process process = null;
            Process staleProcessToDispose = null;
            bool published = false;

            try
            {
                lock (syncRoot)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    ThumbnailFailureDbService failureDbService = GetOrCreateFailureDbService(
                        mainDbFullPath
                    );
                    int recoveredStaleCount =
                        failureDbService.RecoverExpiredProcessingToPendingRescue(DateTime.UtcNow);
                    if (recoveredStaleCount > 0)
                    {
                        log?.Invoke(
                            $"rescue worker stale lease recovered: count={recoveredStaleCount}"
                        );
                    }

                    if (!failureDbService.HasPendingRescueWork(DateTime.UtcNow))
                    {
                        return false;
                    }

                    if (launchInProgress)
                    {
                        return false;
                    }

                    if (IsCurrentProcessRunningNoDisposeLocked(out staleProcessToDispose))
                    {
                        return false;
                    }

                    if (lastLaunchUtc != DateTime.MinValue && nowUtc - lastLaunchUtc < LaunchDebounce)
                    {
                        return false;
                    }

                    // 起動判定だけ先に予約し、重いコピーと Process.Start は lock 外へ逃がす。
                    launchInProgress = true;
                }

                // Exited コールバックと同じ lock を取り合わないよう、Dispose は lock 外へ逃がす。
                TryDisposeProcess(staleProcessToDispose);
                afterLaunchReserved?.Invoke();

                CleanupOldSessions(log);

                string resolvedThumbFolder = ResolveThumbFolderForWorker(
                    mainDbFullPath,
                    dbName,
                    thumbFolder,
                    launchSettings.HostBaseDirectory
                );
                string generationDirectory = BuildGenerationDirectory(workerExePath);
                sessionDirectory = Path.Combine(
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
                        launchSettings.FailureDbDirectoryPath,
                        requestedFailureId
                    ),
                    WorkingDirectory = sessionDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) =>
                    ForwardWorkerPipeLine("stdout", e.Data, log);
                process.ErrorDataReceived += (_, e) =>
                    ForwardWorkerPipeLine("stderr", e.Data, log);
                process.Exited += (_, _) => HandleWorkerExited(process, sessionDirectory, log);
                if (!process.Start())
                {
                    return false;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                beforeProcessStatePublish?.Invoke();

                lock (syncRoot)
                {
                    launchInProgress = false;

                    if (disposed || process.HasExited)
                    {
                        return false;
                    }

                    currentProcess = process;
                    currentSessionDirectory = sessionDirectory;
                    lastLaunchUtc = nowUtc;
                    published = true;
                }

                log?.Invoke($"rescue worker launched: pid={process.Id} session='{sessionDirectory}'");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"rescue worker launch failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (!published)
                {
                    lock (syncRoot)
                    {
                        launchInProgress = false;
                    }

                    TryDisposeProcess(process);
                    TryDeleteDirectoryQuietly(sessionDirectory);
                }
            }
        }

        // 手動インデックス再構築だけは FailureDb を見ず、指定動画1本へ direct 実行モードで起動する。
        public bool TryStartDirectIndexRepair(
            string movieFullPath,
            Action<string> log = null
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath) || !File.Exists(movieFullPath))
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
                log?.Invoke("direct index repair launch skipped: source worker not found.");
                return false;
            }

            DateTime nowUtc = DateTime.UtcNow;
            string sessionDirectory = "";
            Process process = null;
            Process staleProcessToDispose = null;
            bool published = false;

            try
            {
                lock (syncRoot)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    if (launchInProgress)
                    {
                        return false;
                    }

                    if (IsCurrentProcessRunningNoDisposeLocked(out staleProcessToDispose))
                    {
                        return false;
                    }

                    if (lastLaunchUtc != DateTime.MinValue && nowUtc - lastLaunchUtc < LaunchDebounce)
                    {
                        return false;
                    }

                    launchInProgress = true;
                }

                TryDisposeProcess(staleProcessToDispose);
                afterLaunchReserved?.Invoke();

                CleanupOldSessions(log);

                string generationDirectory = BuildGenerationDirectory(workerExePath);
                sessionDirectory = Path.Combine(
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
                    Arguments = BuildDirectIndexRepairWorkerArguments(
                        movieFullPath,
                        launchSettings.LogDirectoryPath
                    ),
                    WorkingDirectory = sessionDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) =>
                    ForwardWorkerPipeLine("stdout", e.Data, log);
                process.ErrorDataReceived += (_, e) =>
                    ForwardWorkerPipeLine("stderr", e.Data, log);
                process.Exited += (_, _) => HandleWorkerExited(process, sessionDirectory, log);
                if (!process.Start())
                {
                    return false;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                beforeProcessStatePublish?.Invoke();

                lock (syncRoot)
                {
                    launchInProgress = false;

                    if (disposed || process.HasExited)
                    {
                        return false;
                    }

                    currentProcess = process;
                    currentSessionDirectory = sessionDirectory;
                    lastLaunchUtc = nowUtc;
                    published = true;
                }

                log?.Invoke(
                    $"direct index repair worker launched: pid={process.Id} session='{sessionDirectory}' movie='{movieFullPath}'"
                );
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"direct index repair launch failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (!published)
                {
                    lock (syncRoot)
                    {
                        launchInProgress = false;
                    }

                    TryDisposeProcess(process);
                    TryDeleteDirectoryQuietly(sessionDirectory);
                }
            }
        }

        // manual pool の空き判定に使うため、起動中か予約中かだけを安全に返す。
        public bool IsBusy()
        {
            Process staleProcessToDispose = null;
            bool isBusy;

            lock (syncRoot)
            {
                if (disposed)
                {
                    return false;
                }

                isBusy = launchInProgress || IsCurrentProcessRunningNoDisposeLocked(out staleProcessToDispose);
            }

            TryDisposeProcess(staleProcessToDispose);
            return isBusy;
        }

        public void Dispose()
        {
            Process processToDispose = null;

            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                launchInProgress = false;
                processToDispose = currentProcess;
                currentProcess = null;
                currentSessionDirectory = "";
                cachedFailureDbService = null;
                cachedFailureDbMainDbFullPath = "";
            }

            TryDisposeProcess(processToDispose);
        }

        // DB切替時は旧DB用workerを明示停止し、新DBの救済と混線しないようにする。
        public bool TryStopRunningWorker(Action<string> log = null)
        {
            Process processToStop = null;
            string sessionDirectory = "";
            bool shouldClearState = false;
            bool stopped = false;

            lock (syncRoot)
            {
                if (disposed)
                {
                    return false;
                }

                processToStop = currentProcess;
                sessionDirectory = currentSessionDirectory;
                launchInProgress = true;
            }

            try
            {
                if (processToStop == null)
                {
                    shouldClearState = true;
                    return false;
                }

                int pid = -1;
                try
                {
                    pid = processToStop.Id;
                }
                catch
                {
                    // 既に終了している場合は pid 取得失敗を許容する。
                }

                stopped = TryTerminateProcess(processToStop, waitMilliseconds: 2000);
                int terminatedToolCount = TryTerminateSessionToolProcesses(sessionDirectory, log);
                if (stopped)
                {
                    log?.Invoke(
                        $"rescue worker killed: pid={pid} session='{sessionDirectory}'"
                    );
                }
                else if (terminatedToolCount > 0)
                {
                    log?.Invoke(
                        $"rescue worker session cleanup only: pid={pid} session='{sessionDirectory}' tools={terminatedToolCount}"
                    );
                }

                shouldClearState =
                    stopped || terminatedToolCount > 0 || !IsProcessRunning(processToStop);
                return stopped || terminatedToolCount > 0;
            }
            finally
            {
                lock (syncRoot)
                {
                    launchInProgress = false;

                    if (shouldClearState && ReferenceEquals(currentProcess, processToStop))
                    {
                        currentProcess = null;
                        currentSessionDirectory = "";
                    }
                }

                if (shouldClearState)
                {
                    TryDisposeProcess(processToStop);
                    TryDeleteDirectoryQuietly(sessionDirectory);
                }
            }
        }

        // currentProcess の終了判定だけを行い、Dispose は lock 外へ逃がす。
        private bool IsCurrentProcessRunningNoDisposeLocked(out Process staleProcessToDispose)
        {
            staleProcessToDispose = null;
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

            staleProcessToDispose = currentProcess;
            currentProcess = null;
            currentSessionDirectory = "";
            return false;
        }

        private void HandleWorkerExited(Process process, string sessionDirectory, Action<string> log)
        {
            Process processToDispose = null;

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
                    processToDispose = currentProcess;
                    currentProcess = null;
                    currentSessionDirectory = "";
                }
            }

            TryDisposeProcess(processToDispose);
            _ = TryTerminateSessionToolProcesses(sessionDirectory, log);
            TryDeleteDirectoryQuietly(sessionDirectory);
        }

        private static void TryDisposeProcess(Process process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                process.Dispose();
            }
            catch
            {
                // プロセスdispose失敗は無視する。
            }
        }

        // Killと待機を1か所に寄せ、停止判定を呼び出し側で使い回せるようにする。
        internal static bool TryTerminateProcess(Process process, int waitMilliseconds)
        {
            if (process == null)
            {
                return false;
            }

            try
            {
                if (process.HasExited)
                {
                    return false;
                }

                process.Kill(entireProcessTree: true);
                if (waitMilliseconds > 0)
                {
                    _ = process.WaitForExit(waitMilliseconds);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // worker の session 配下から起動した ffmpeg / ffprobe だけを拾い、孤児化した子プロセスを PID kill する。
        internal static int TryTerminateSessionToolProcesses(
            string sessionDirectory,
            Action<string> log = null
        )
        {
            string normalizedSessionDirectory = NormalizePath(sessionDirectory);
            if (string.IsNullOrWhiteSpace(normalizedSessionDirectory))
            {
                return 0;
            }

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return 0;
            }

            int terminatedCount = 0;
            for (int i = 0; i < processes.Length; i++)
            {
                using Process process = processes[i];
                if (
                    !TryResolveSessionToolExecutablePath(
                        process,
                        normalizedSessionDirectory,
                        out string executablePath
                    )
                )
                {
                    continue;
                }

                int pid = -1;
                try
                {
                    pid = process.Id;
                }
                catch
                {
                    // pid 取得に失敗しても kill 自体は試す。
                }

                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    _ = process.WaitForExit(2000);
                    terminatedCount++;
                    log?.Invoke(
                        $"rescue worker session tool killed: pid={pid} path='{executablePath}' session='{normalizedSessionDirectory}'"
                    );
                }
                catch
                {
                    // 取得競合や終了競合は best effort で握る。
                }
            }

            return terminatedCount;
        }

        private static bool IsProcessRunning(Process process)
        {
            if (process == null)
            {
                return false;
            }

            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveSessionToolExecutablePath(
            Process process,
            string normalizedSessionDirectory,
            out string executablePath
        )
        {
            executablePath = "";
            if (process == null || string.IsNullOrWhiteSpace(normalizedSessionDirectory))
            {
                return false;
            }

            string processName = "";
            try
            {
                processName = process.ProcessName ?? "";
            }
            catch
            {
                return false;
            }

            if (
                !string.Equals(processName, "ffmpeg", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(processName, "ffprobe", StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            try
            {
                executablePath = process.MainModule?.FileName ?? "";
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            return IsPathUnderDirectory(executablePath, normalizedSessionDirectory);
        }

        private static bool IsPathUnderDirectory(string filePath, string directoryPath)
        {
            string normalizedFilePath = NormalizePath(filePath);
            string normalizedDirectoryPath = NormalizePath(directoryPath);
            if (
                string.IsNullOrWhiteSpace(normalizedFilePath)
                || string.IsNullOrWhiteSpace(normalizedDirectoryPath)
            )
            {
                return false;
            }

            if (
                string.Equals(
                    normalizedFilePath,
                    normalizedDirectoryPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            return normalizedFilePath.StartsWith(
                normalizedDirectoryPath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
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
            string failureDbDirectoryPath,
            long requestedFailureId = 0
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

            if (requestedFailureId > 0)
            {
                args.Add("--failure-id");
                args.Add(requestedFailureId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return string.Join(" ", args);
        }

        internal static string BuildDirectIndexRepairWorkerArguments(
            string movieFullPath,
            string logDirectoryPath
        )
        {
            List<string> args =
            [
                "--direct-index-repair",
                "--movie",
                $"\"{movieFullPath}\"",
            ];

            if (!string.IsNullOrWhiteSpace(logDirectoryPath))
            {
                args.Add("--log-dir");
                args.Add($"\"{logDirectoryPath}\"");
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
