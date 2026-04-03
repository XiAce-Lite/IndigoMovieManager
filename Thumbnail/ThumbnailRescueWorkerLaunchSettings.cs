using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    // launcher が握る host 設定を1つにまとめ、引数追加のたびに ctor が太らないようにする。
    internal sealed class ThumbnailRescueWorkerLaunchSettings
    {
        public ThumbnailRescueWorkerLaunchSettings(
            string sessionRootDirectoryPath,
            string logDirectoryPath,
            string failureDbDirectoryPath,
            string hostBaseDirectory,
            string workerExecutablePath,
            string workerExecutablePathOrigin = "",
            string workerExecutablePathDiagnostic = "",
            string workerArtifactLockSummary = "",
            IReadOnlyList<string> supplementalDirectoryPaths = null,
            IReadOnlyList<string> supplementalFilePaths = null,
            bool useJobJsonModeForMainRescue = false
        )
        {
            HostBaseDirectory = NormalizeDirectoryPath(hostBaseDirectory, AppContext.BaseDirectory);
            SessionRootDirectoryPath = NormalizeDirectoryPath(
                sessionRootDirectoryPath,
                Path.Combine(HostBaseDirectory, "rescue-worker-sessions")
            );
            LogDirectoryPath = NormalizeDirectoryPath(
                logDirectoryPath,
                Path.Combine(HostBaseDirectory, "logs")
            );
            FailureDbDirectoryPath = NormalizeDirectoryPath(
                failureDbDirectoryPath,
                Path.Combine(HostBaseDirectory, "FailureDb")
            );
            WorkerExecutablePath = NormalizeFilePath(workerExecutablePath);
            WorkerExecutablePathOrigin = NormalizeOrigin(workerExecutablePathOrigin);
            WorkerExecutablePathDiagnostic = NormalizeDiagnostic(workerExecutablePathDiagnostic);
            WorkerArtifactLockSummary = NormalizeDiagnostic(workerArtifactLockSummary);
            SupplementalDirectoryPaths = NormalizePathList(
                supplementalDirectoryPaths,
                HostBaseDirectory
            );
            SupplementalFilePaths = NormalizeFilePathList(supplementalFilePaths);
            UseJobJsonModeForMainRescue = useJobJsonModeForMainRescue;
        }

        public string SessionRootDirectoryPath { get; }

        public string LogDirectoryPath { get; }

        public string FailureDbDirectoryPath { get; }

        public string HostBaseDirectory { get; }

        public string WorkerExecutablePath { get; }

        public string WorkerExecutablePathOrigin { get; }

        public string WorkerExecutablePathDiagnostic { get; }

        public string WorkerArtifactLockSummary { get; }

        public IReadOnlyList<string> SupplementalDirectoryPaths { get; }

        public IReadOnlyList<string> SupplementalFilePaths { get; }

        public bool UseJobJsonModeForMainRescue { get; }

        private static string NormalizeDirectoryPath(string directoryPath, string fallbackPath)
        {
            string candidate = string.IsNullOrWhiteSpace(directoryPath)
                ? fallbackPath
                : directoryPath.Trim();

            try
            {
                return Path.GetFullPath(candidate, AppContext.BaseDirectory);
            }
            catch
            {
                return candidate;
            }
        }

        private static string NormalizeFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return "";
            }

            string trimmed = filePath.Trim();
            if (
                trimmed.Length >= 2
                && trimmed.StartsWith('"')
                && trimmed.EndsWith('"')
            )
            {
                trimmed = trimmed[1..^1].Trim();
            }

            try
            {
                return Path.GetFullPath(trimmed, AppContext.BaseDirectory);
            }
            catch
            {
                return trimmed;
            }
        }

        private static IReadOnlyList<string> NormalizePathList(
            IReadOnlyList<string> paths,
            string fallbackBaseDirectory
        )
        {
            List<string> normalized = [];
            if (paths == null)
            {
                return normalized;
            }

            for (int i = 0; i < paths.Count; i++)
            {
                string path = NormalizeDirectoryPath(paths[i], fallbackBaseDirectory);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                normalized.Add(path);
            }

            return normalized;
        }

        private static IReadOnlyList<string> NormalizeFilePathList(IReadOnlyList<string> paths)
        {
            List<string> normalized = [];
            if (paths == null)
            {
                return normalized;
            }

            for (int i = 0; i < paths.Count; i++)
            {
                string path = NormalizeFilePath(paths[i]);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                normalized.Add(path);
            }

            return normalized;
        }

        private static string NormalizeOrigin(string origin) =>
            string.IsNullOrWhiteSpace(origin) ? "unknown" : origin.Trim();

        private static string NormalizeDiagnostic(string diagnostic) =>
            string.IsNullOrWhiteSpace(diagnostic) ? "" : diagnostic.Trim();
    }
}
