using System.IO;
using System.Text.Json;

namespace IndigoMovieManager.Thumbnail
{
    // app host が worker の所在と補助依存を解決し、launcher には具体値だけを渡す。
    internal static class ThumbnailRescueWorkerLaunchSettingsFactory
    {
        private const string RescueWorkerExeName = "IndigoMovieManager.Thumbnail.RescueWorker.exe";
        internal const string PublishedArtifactMarkerFileName = "rescue-worker-artifact.json";
        internal const string PublishedArtifactSyncMetadataFileName =
            "rescue-worker-sync-source.json";
        internal const string WorkerPathOverrideEnvName = "IMM_THUMB_RESCUE_WORKER_EXE_PATH";
        internal const string AllowProjectBuildFallbackEnvName =
            "IMM_THUMB_RESCUE_ALLOW_PROJECT_BUILD_FALLBACK";
        private const string RepoProjectFileName = "IndigoMovieManager.csproj";
        private const string RepoSolutionFileName = "IndigoMovieManager.sln";
        private static readonly string[] PublishedArtifactDirectoryNames =
            ["Release-win-x64", "Debug-win-x64"];
        private static readonly string[] SupplementalDirectoryNames = ["runtimes", "tools"];
        private static readonly string[] SupplementalFileNames =
        [
            "SQLitePCLRaw.batteries_v2.dll",
            "SQLitePCLRaw.core.dll",
            "SQLitePCLRaw.provider.e_sqlite3.dll",
            "System.Data.SQLite.dll",
        ];
        private static readonly string[] PublishedArtifactRequiredRelativePaths =
        [
            "IndigoMovieManager.Thumbnail.RescueWorker.exe",
            "Images\\noFileSmall.jpg",
            "tools\\ffmpeg-shared",
            "SQLitePCLRaw.batteries_v2.dll",
            "SQLitePCLRaw.core.dll",
            "SQLitePCLRaw.provider.e_sqlite3.dll",
            "System.Data.SQLite.dll",
        ];

        public static ThumbnailRescueWorkerLaunchSettings CreateDefault(
            string sessionRootDirectoryPath,
            string logDirectoryPath,
            string failureDbDirectoryPath,
            string hostBaseDirectory
        )
        {
            string workerExecutablePathOverride =
                Environment.GetEnvironmentVariable(WorkerPathOverrideEnvName) ?? "";
            return CreateDefault(
                sessionRootDirectoryPath,
                logDirectoryPath,
                failureDbDirectoryPath,
                hostBaseDirectory,
                workerExecutablePathOverride
            );
        }

        internal static ThumbnailRescueWorkerLaunchSettings CreateDefault(
            string sessionRootDirectoryPath,
            string logDirectoryPath,
            string failureDbDirectoryPath,
            string hostBaseDirectory,
            string workerExecutablePathOverride
        )
        {
            string resolvedWorkerExecutablePath = "";
            string resolvedWorkerExecutablePathOrigin = "";
            string resolvedWorkerExecutablePathDiagnostic = "";
            string resolvedWorkerArtifactLockSummary = "";
            _ = TryResolveWorkerExecutablePath(
                hostBaseDirectory,
                workerExecutablePathOverride,
                out resolvedWorkerExecutablePath,
                out resolvedWorkerExecutablePathOrigin,
                out resolvedWorkerExecutablePathDiagnostic
            );
            if (
                ThumbnailRescueWorkerArtifactLockFile.TryRead(
                    hostBaseDirectory,
                    out ThumbnailRescueWorkerArtifactLockInfo workerArtifactLockInfo,
                    out _
                )
                && workerArtifactLockInfo != null
            )
            {
                resolvedWorkerArtifactLockSummary = workerArtifactLockInfo.BuildSummary();
            }

            return new ThumbnailRescueWorkerLaunchSettings(
                sessionRootDirectoryPath: sessionRootDirectoryPath,
                logDirectoryPath: logDirectoryPath,
                failureDbDirectoryPath: failureDbDirectoryPath,
                hostBaseDirectory: hostBaseDirectory,
                workerExecutablePath: resolvedWorkerExecutablePath,
                workerExecutablePathOrigin: resolvedWorkerExecutablePathOrigin,
                workerExecutablePathDiagnostic: resolvedWorkerExecutablePathDiagnostic,
                workerArtifactLockSummary: resolvedWorkerArtifactLockSummary,
                supplementalDirectoryPaths: ResolveSupplementalDirectoryPaths(
                    hostBaseDirectory,
                    resolvedWorkerExecutablePath
                ),
                supplementalFilePaths: ResolveSupplementalFilePaths(
                    hostBaseDirectory,
                    resolvedWorkerExecutablePath
                ),
                useJobJsonModeForMainRescue: ShouldUseJobJsonModeForMainRescue(
                    resolvedWorkerExecutablePath,
                    resolvedWorkerExecutablePathOrigin
                )
            );
        }

        internal static bool TryResolveWorkerExecutablePath(
            string hostBaseDirectory,
            string workerExecutablePathOverride,
            out string workerExecutablePath
        ) =>
            TryResolveWorkerExecutablePath(
                hostBaseDirectory,
                workerExecutablePathOverride,
                out workerExecutablePath,
                out _
            );

        internal static bool TryResolveWorkerExecutablePath(
            string hostBaseDirectory,
            string workerExecutablePathOverride,
            out string workerExecutablePath,
            out string workerExecutablePathOrigin
        )
        {
            return TryResolveWorkerExecutablePath(
                hostBaseDirectory,
                workerExecutablePathOverride,
                out workerExecutablePath,
                out workerExecutablePathOrigin,
                out _
            );
        }

        internal static bool TryResolveWorkerExecutablePath(
            string hostBaseDirectory,
            string workerExecutablePathOverride,
            out string workerExecutablePath,
            out string workerExecutablePathOrigin,
            out string workerExecutablePathDiagnostic
        )
        {
            workerExecutablePath = "";
            workerExecutablePathOrigin = "";
            workerExecutablePathDiagnostic = "";
            if (
                !ThumbnailRescueWorkerArtifactLockFile.TryRead(
                    hostBaseDirectory,
                    out ThumbnailRescueWorkerArtifactLockInfo workerArtifactLockInfo,
                    out string workerArtifactLockDiagnostic
                )
                && !string.IsNullOrWhiteSpace(workerArtifactLockDiagnostic)
            )
            {
                workerExecutablePathDiagnostic = workerArtifactLockDiagnostic;
                return false;
            }

            string workerExecutablePathDebug = Path.GetFullPath(
                Path.Combine(
                    hostBaseDirectory,
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
            );
            string workerExecutablePathRelease = Path.GetFullPath(
                Path.Combine(
                    hostBaseDirectory,
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
            );
            bool preferProjectBuildOutput = IsDebugHostBaseDirectory(hostBaseDirectory);
            bool allowProjectBuildFallback = ShouldAllowProjectBuildFallback();

            List<string> candidates = [NormalizeFilePath(workerExecutablePathOverride)];
            if (
                TryResolvePublishedWorkerExecutablePath(
                    hostBaseDirectory,
                    out string publishedArtifactPath,
                    out string publishedArtifactDiagnostic
                )
            )
            {
                candidates.Add(publishedArtifactPath);
            }
            else if (!string.IsNullOrWhiteSpace(publishedArtifactDiagnostic))
            {
                workerExecutablePathDiagnostic = publishedArtifactDiagnostic;
            }

            // 既定では artifact / 同梱 worker を優先し、project-build は明示 opt-in 時だけ候補へ戻す。
            if (allowProjectBuildFallback && preferProjectBuildOutput)
            {
                candidates.Add(workerExecutablePathDebug);
                candidates.Add(workerExecutablePathRelease);
            }

            candidates.AddRange(
            [
                Path.Combine(hostBaseDirectory, "rescue-worker", RescueWorkerExeName),
                Path.Combine(hostBaseDirectory, RescueWorkerExeName),
            ]);

            if (allowProjectBuildFallback && !preferProjectBuildOutput)
            {
                candidates.Add(workerExecutablePathDebug);
                candidates.Add(workerExecutablePathRelease);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                {
                    continue;
                }

                if (
                    HasPublishedArtifactMarker(candidate)
                    && !TryValidatePublishedWorkerArtifact(
                        candidate,
                        out string publishedArtifactValidationDiagnostic
                    )
                )
                {
                    if (string.IsNullOrWhiteSpace(workerExecutablePathDiagnostic))
                    {
                        workerExecutablePathDiagnostic = publishedArtifactValidationDiagnostic;
                    }

                    continue;
                }

                if (
                    workerArtifactLockInfo != null
                    && !ThumbnailRescueWorkerArtifactLockFile.TryValidateWorkerExecutablePath(
                        candidate,
                        workerArtifactLockInfo,
                        out string lockValidationDiagnostic
                    )
                )
                {
                    if (string.IsNullOrWhiteSpace(workerExecutablePathDiagnostic))
                    {
                        workerExecutablePathDiagnostic = lockValidationDiagnostic;
                    }

                    continue;
                }

                workerExecutablePath = candidate;
                workerExecutablePathOrigin = ResolveWorkerExecutablePathOrigin(
                    hostBaseDirectory,
                    workerExecutablePathOverride,
                    candidate,
                    workerExecutablePathDebug,
                    workerExecutablePathRelease
                );
                return true;
            }

            if (string.IsNullOrWhiteSpace(workerExecutablePathDiagnostic))
            {
                workerExecutablePathDiagnostic = BuildWorkerExecutablePathDiagnostic(
                    workerExecutablePathOverride,
                    allowProjectBuildFallback
                );
            }
            return false;
        }

        private static bool IsDebugHostBaseDirectory(string hostBaseDirectory)
        {
            string normalizedHostBaseDirectory = NormalizeDirectoryPath(hostBaseDirectory);
            if (string.IsNullOrWhiteSpace(normalizedHostBaseDirectory))
            {
                return false;
            }

            return normalizedHostBaseDirectory.IndexOf(
                    $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0
                || normalizedHostBaseDirectory.EndsWith(
                    $"{Path.DirectorySeparatorChar}Debug",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        internal static IReadOnlyList<string> ResolveSupplementalDirectoryPaths(
            string hostBaseDirectory,
            string workerExecutablePath
        )
        {
            if (IsPublishedWorkerArtifact(workerExecutablePath))
            {
                return [];
            }

            List<string> result = [];
            for (int i = 0; i < SupplementalDirectoryNames.Length; i++)
            {
                string path = Path.Combine(hostBaseDirectory, SupplementalDirectoryNames[i]);
                if (Directory.Exists(path))
                {
                    result.Add(path);
                }
            }

            return result;
        }

        internal static IReadOnlyList<string> ResolveSupplementalFilePaths(
            string hostBaseDirectory,
            string workerExecutablePath
        )
        {
            if (IsPublishedWorkerArtifact(workerExecutablePath))
            {
                return [];
            }

            List<string> result = [];
            for (int i = 0; i < SupplementalFileNames.Length; i++)
            {
                string path = Path.Combine(hostBaseDirectory, SupplementalFileNames[i]);
                if (File.Exists(path))
                {
                    result.Add(path);
                }
            }

            return result;
        }

        internal static bool TryResolvePublishedWorkerExecutablePath(
            string hostBaseDirectory,
            out string workerExecutablePath
        ) =>
            TryResolvePublishedWorkerExecutablePath(
                hostBaseDirectory,
                out workerExecutablePath,
                out _
            );

        internal static bool TryResolvePublishedWorkerExecutablePath(
            string hostBaseDirectory,
            out string workerExecutablePath,
            out string diagnosticMessage
        )
        {
            workerExecutablePath = "";
            diagnosticMessage = "";
            if (!TryResolveRepositoryRootDirectory(hostBaseDirectory, out string repoRootDirectory))
            {
                return false;
            }

            for (int i = 0; i < PublishedArtifactDirectoryNames.Length; i++)
            {
                string candidate = Path.Combine(
                    repoRootDirectory,
                    "artifacts",
                    "rescue-worker",
                    "publish",
                    PublishedArtifactDirectoryNames[i],
                    RescueWorkerExeName
                );
                if (!File.Exists(candidate))
                {
                    continue;
                }

                if (!TryValidatePublishedWorkerArtifact(candidate, out string validationMessage))
                {
                    if (string.IsNullOrWhiteSpace(diagnosticMessage))
                    {
                        diagnosticMessage = validationMessage;
                    }
                    continue;
                }

                workerExecutablePath = candidate;
                return true;
            }

            return false;
        }

        internal static bool IsPublishedWorkerArtifact(string workerExecutablePath)
        {
            return TryValidatePublishedWorkerArtifact(workerExecutablePath, out _);
        }

        internal static bool HasPublishedWorkerSyncMetadata(string workerExecutablePath)
        {
            string normalizedWorkerExecutablePath = NormalizeFilePath(workerExecutablePath);
            if (string.IsNullOrWhiteSpace(normalizedWorkerExecutablePath))
            {
                return false;
            }

            string artifactDirectoryPath =
                Path.GetDirectoryName(normalizedWorkerExecutablePath) ?? "";
            if (string.IsNullOrWhiteSpace(artifactDirectoryPath))
            {
                return false;
            }

            return File.Exists(
                Path.Combine(artifactDirectoryPath, PublishedArtifactSyncMetadataFileName)
            );
        }

        internal static bool TryValidatePublishedWorkerArtifact(
            string workerExecutablePath,
            out string diagnosticMessage
        )
        {
            diagnosticMessage = "";
            if (string.IsNullOrWhiteSpace(workerExecutablePath))
            {
                diagnosticMessage = "published artifact invalid: worker executable path is empty.";
                return false;
            }

            string normalizedWorkerExecutablePath = NormalizeFilePath(workerExecutablePath);
            if (string.IsNullOrWhiteSpace(normalizedWorkerExecutablePath))
            {
                diagnosticMessage = "published artifact invalid: worker executable path could not be normalized.";
                return false;
            }

            string artifactDirectoryPath =
                Path.GetDirectoryName(normalizedWorkerExecutablePath) ?? "";
            if (string.IsNullOrWhiteSpace(artifactDirectoryPath))
            {
                diagnosticMessage = "published artifact invalid: artifact directory path is empty.";
                return false;
            }

            string markerPath = Path.Combine(
                artifactDirectoryPath,
                PublishedArtifactMarkerFileName
            );
            if (!File.Exists(normalizedWorkerExecutablePath) || !File.Exists(markerPath))
            {
                diagnosticMessage = $"published artifact invalid: marker missing '{markerPath}'.";
                return false;
            }

            if (
                !TryReadArtifactCompatibilityVersion(
                    normalizedWorkerExecutablePath,
                    out string compatibilityVersion
                )
                || !string.Equals(
                    compatibilityVersion,
                    RescueWorkerArtifactContract.CompatibilityVersion,
                    StringComparison.Ordinal
                )
            )
            {
                diagnosticMessage =
                    "published artifact invalid: compatibilityVersion mismatch.";
                return false;
            }

            // overlay不要の完成済みartifactだけを優先採用し、不完全な古い成果物へ戻らないようにする。
            if (!HasRequiredPublishedArtifactFiles(artifactDirectoryPath))
            {
                diagnosticMessage = "published artifact invalid: required files are missing.";
                return false;
            }

            if (!HasPublishedArtifactNativeSqlite(artifactDirectoryPath))
            {
                diagnosticMessage = "published artifact invalid: native sqlite is missing.";
                return false;
            }

            return true;
        }

        private static bool HasPublishedArtifactMarker(string workerExecutablePath)
        {
            string normalizedWorkerExecutablePath = NormalizeFilePath(workerExecutablePath);
            if (string.IsNullOrWhiteSpace(normalizedWorkerExecutablePath))
            {
                return false;
            }

            string artifactDirectoryPath =
                Path.GetDirectoryName(normalizedWorkerExecutablePath) ?? "";
            if (string.IsNullOrWhiteSpace(artifactDirectoryPath))
            {
                return false;
            }

            return File.Exists(Path.Combine(artifactDirectoryPath, PublishedArtifactMarkerFileName));
        }

        private static bool TryResolveRepositoryRootDirectory(
            string hostBaseDirectory,
            out string repoRootDirectory
        )
        {
            repoRootDirectory = "";
            string startDirectory = NormalizeDirectoryPath(hostBaseDirectory);
            if (string.IsNullOrWhiteSpace(startDirectory))
            {
                startDirectory = NormalizeDirectoryPath(AppContext.BaseDirectory);
            }

            if (string.IsNullOrWhiteSpace(startDirectory))
            {
                return false;
            }

            DirectoryInfo currentDirectoryInfo = new(startDirectory);
            while (currentDirectoryInfo != null)
            {
                if (
                    File.Exists(Path.Combine(currentDirectoryInfo.FullName, RepoProjectFileName))
                    || File.Exists(Path.Combine(currentDirectoryInfo.FullName, RepoSolutionFileName))
                )
                {
                    repoRootDirectory = currentDirectoryInfo.FullName;
                    return true;
                }

                currentDirectoryInfo = currentDirectoryInfo.Parent;
            }

            return false;
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

        private static string NormalizeDirectoryPath(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(directoryPath.Trim(), AppContext.BaseDirectory);
            }
            catch
            {
                return directoryPath.Trim();
            }
        }

        internal static bool ShouldAllowProjectBuildFallback()
        {
            string rawValue =
                Environment.GetEnvironmentVariable(AllowProjectBuildFallbackEnvName) ?? "";
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            string normalized = rawValue.Trim();
            if (
                string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            return false;
        }

        internal static bool TryReadArtifactCompatibilityVersion(
            string workerExecutablePath,
            out string compatibilityVersion
        )
        {
            compatibilityVersion = "";
            string markerPath = Path.Combine(
                Path.GetDirectoryName(workerExecutablePath) ?? "",
                PublishedArtifactMarkerFileName
            );
            if (string.IsNullOrWhiteSpace(markerPath) || !File.Exists(markerPath))
            {
                return false;
            }

            try
            {
                using FileStream stream = File.OpenRead(markerPath);
                using JsonDocument document = JsonDocument.Parse(stream);
                if (
                    !document.RootElement.TryGetProperty(
                        "compatibilityVersion",
                        out JsonElement property
                    )
                )
                {
                    return false;
                }

                compatibilityVersion = property.GetString() ?? "";
                return !string.IsNullOrWhiteSpace(compatibilityVersion);
            }
            catch
            {
                return false;
            }
        }

        internal static bool ShouldUseJobJsonModeForMainRescue(
            string workerExecutablePath,
            string workerExecutablePathOrigin
        )
        {
            if (string.IsNullOrWhiteSpace(workerExecutablePath))
            {
                return false;
            }

            if (
                TryReadArtifactSupportedEntryModes(
                    workerExecutablePath,
                    out IReadOnlyList<string> supportedEntryModes
                )
            )
            {
                for (int i = 0; i < supportedEntryModes.Count; i++)
                {
                    if (
                        string.Equals(
                            supportedEntryModes[i],
                            ThumbnailRescueWorkerJobJsonClient.SupportedEntryMode,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool TryReadArtifactSupportedEntryModes(
            string workerExecutablePath,
            out IReadOnlyList<string> supportedEntryModes
        )
        {
            List<string> modes = [];
            supportedEntryModes = modes;
            string markerPath = Path.Combine(
                Path.GetDirectoryName(workerExecutablePath) ?? "",
                PublishedArtifactMarkerFileName
            );
            if (string.IsNullOrWhiteSpace(markerPath) || !File.Exists(markerPath))
            {
                return false;
            }

            try
            {
                using FileStream stream = File.OpenRead(markerPath);
                using JsonDocument document = JsonDocument.Parse(stream);
                if (
                    !document.RootElement.TryGetProperty(
                        "supportedEntryModes",
                        out JsonElement property
                    )
                    || property.ValueKind != JsonValueKind.Array
                )
                {
                    return false;
                }

                foreach (JsonElement item in property.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string mode = item.GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(mode))
                    {
                        continue;
                    }

                    modes.Add(mode.Trim());
                }

                return modes.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasRequiredPublishedArtifactFiles(string artifactDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(artifactDirectoryPath))
            {
                return false;
            }

            for (int i = 0; i < PublishedArtifactRequiredRelativePaths.Length; i++)
            {
                string relativePath = PublishedArtifactRequiredRelativePaths[i];
                string fullPath = Path.Combine(artifactDirectoryPath, relativePath);
                if (
                    !File.Exists(fullPath)
                    && !Directory.Exists(fullPath)
                )
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasPublishedArtifactNativeSqlite(string artifactDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(artifactDirectoryPath))
            {
                return false;
            }

            return File.Exists(Path.Combine(artifactDirectoryPath, "e_sqlite3.dll"))
                || File.Exists(
                    Path.Combine(
                        artifactDirectoryPath,
                        "runtimes",
                        "win-x64",
                        "native",
                        "e_sqlite3.dll"
                    )
                );
        }

        internal static string ResolveWorkerExecutablePathOrigin(
            string hostBaseDirectory,
            string workerExecutablePathOverride,
            string workerExecutablePath,
            string workerExecutablePathDebug = "",
            string workerExecutablePathRelease = ""
        )
        {
            string normalizedWorkerExecutablePath = NormalizeFilePath(workerExecutablePath);
            if (string.IsNullOrWhiteSpace(normalizedWorkerExecutablePath))
            {
                return "missing";
            }

            string normalizedOverridePath = NormalizeFilePath(workerExecutablePathOverride);
            if (
                !string.IsNullOrWhiteSpace(normalizedOverridePath)
                && string.Equals(
                    normalizedWorkerExecutablePath,
                    normalizedOverridePath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "override";
            }

            if (IsPublishedWorkerArtifact(normalizedWorkerExecutablePath))
            {
                return HasPublishedWorkerSyncMetadata(normalizedWorkerExecutablePath)
                    ? "artifact-sync"
                    : "artifact";
            }

            string normalizedDebugPath = NormalizeFilePath(workerExecutablePathDebug);
            if (
                !string.IsNullOrWhiteSpace(normalizedDebugPath)
                && string.Equals(
                    normalizedWorkerExecutablePath,
                    normalizedDebugPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "project-build";
            }

            string normalizedReleasePath = NormalizeFilePath(workerExecutablePathRelease);
            if (
                !string.IsNullOrWhiteSpace(normalizedReleasePath)
                && string.Equals(
                    normalizedWorkerExecutablePath,
                    normalizedReleasePath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "project-build";
            }

            string normalizedHostBaseDirectory = NormalizeDirectoryPath(hostBaseDirectory);
            if (!string.IsNullOrWhiteSpace(normalizedHostBaseDirectory))
            {
                string bundledWorkerPath = NormalizeFilePath(
                    Path.Combine(normalizedHostBaseDirectory, "rescue-worker", RescueWorkerExeName)
                );
                if (
                    string.Equals(
                        normalizedWorkerExecutablePath,
                        bundledWorkerPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return "host-rescue-folder";
                }

                string hostBaseWorkerPath = NormalizeFilePath(
                    Path.Combine(normalizedHostBaseDirectory, RescueWorkerExeName)
                );
                if (
                    string.Equals(
                        normalizedWorkerExecutablePath,
                        hostBaseWorkerPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return "host-base";
                }
            }

            return "unknown";
        }

        private static string BuildWorkerExecutablePathDiagnostic(
            string workerExecutablePathOverride,
            bool allowProjectBuildFallback
        )
        {
            string normalizedOverridePath = NormalizeFilePath(workerExecutablePathOverride);
            if (!string.IsNullOrWhiteSpace(normalizedOverridePath))
            {
                return $"worker executable not found: override='{normalizedOverridePath}'.";
            }

            if (!allowProjectBuildFallback)
            {
                return
                    $"worker executable not found: no valid candidate resolved. project-build fallback is disabled by default; set {AllowProjectBuildFallbackEnvName}=1 to opt in.";
            }

            return "worker executable not found: no valid candidate resolved.";
        }
    }
}
