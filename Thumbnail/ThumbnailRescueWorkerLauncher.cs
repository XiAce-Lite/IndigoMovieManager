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
        private ThumbnailFailureDbService cachedFailureDbService;
        private string cachedFailureDbMainDbFullPath = "";
        private Process currentProcess;
        private string currentSessionDirectory = "";
        private DateTime lastLaunchUtc = DateTime.MinValue;

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

                if (!TryResolveWorkerSourceDirectory(out string sourceDirectory, out string workerExePath))
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
                        AppContext.BaseDirectory
                    );
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
                        Arguments = BuildWorkerArguments(mainDbFullPath, resolvedThumbFolder),
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

        private static bool TryResolveWorkerSourceDirectory(
            out string sourceDirectory,
            out string workerExePath
        ) =>
            TryResolveWorkerSourceDirectory(
                AppContext.BaseDirectory,
                Environment.GetEnvironmentVariable(RescueWorkerPathEnvName)?.Trim('"') ?? "",
                out sourceDirectory,
                out workerExePath
            );

        internal static bool TryResolveWorkerSourceDirectory(
            string appBaseDirectory,
            string envPathOverride,
            out string sourceDirectory,
            out string workerExePath
        )
        {
            sourceDirectory = "";
            workerExePath = "";

            List<string> candidates =
            [
                envPathOverride,
                Path.Combine(appBaseDirectory, "rescue-worker", RescueWorkerExeName),
                Path.Combine(appBaseDirectory, RescueWorkerExeName),
                Path.GetFullPath(
                    Path.Combine(
                        appBaseDirectory,
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
                        appBaseDirectory,
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
            string candidate = string.IsNullOrWhiteSpace(thumbFolder)
                ? Path.Combine(normalizedAppBaseDirectory, "Thumb", resolvedDbName)
                : thumbFolder.Trim();

            if (Path.IsPathRooted(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            return Path.GetFullPath(candidate, normalizedAppBaseDirectory);
        }

        internal static string BuildWorkerArguments(string mainDbFullPath, string resolvedThumbFolder)
        {
            if (string.IsNullOrWhiteSpace(resolvedThumbFolder))
            {
                return $"--main-db \"{mainDbFullPath}\"";
            }

            return
                $"--main-db \"{mainDbFullPath}\" --thumb-folder \"{resolvedThumbFolder}\"";
        }

        private static string BuildGenerationDirectory(string workerExePath) =>
            BuildGenerationDirectory(AppLocalDataPaths.RescueWorkerSessionsPath, workerExePath);

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

        private static void CleanupOldSessions(Action<string> log) =>
            CleanupOldSessions(AppLocalDataPaths.RescueWorkerSessionsPath, DateTime.UtcNow, log);

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
