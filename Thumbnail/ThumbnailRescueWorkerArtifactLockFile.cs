using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IndigoMovieManager.Thumbnail
{
    // main repo が「どの worker artifact を消費するか」を固定する lock 情報。
    internal sealed class ThumbnailRescueWorkerArtifactLockInfo
    {
        public ThumbnailRescueWorkerArtifactLockInfo(
            string lockFilePath,
            int schemaVersion,
            string artifactType,
            string sourceType,
            string version,
            string assetFileName,
            string compatibilityVersion,
            string workerExecutableSha256
        )
        {
            LockFilePath = NormalizePath(lockFilePath);
            SchemaVersion = schemaVersion;
            ArtifactType = NormalizeValue(artifactType);
            SourceType = NormalizeValue(sourceType);
            Version = NormalizeValue(version);
            AssetFileName = NormalizeValue(assetFileName);
            CompatibilityVersion = NormalizeValue(compatibilityVersion);
            WorkerExecutableSha256 = NormalizeHash(workerExecutableSha256);
        }

        public string LockFilePath { get; }

        public int SchemaVersion { get; }

        public string ArtifactType { get; }

        public string SourceType { get; }

        public string Version { get; }

        public string AssetFileName { get; }

        public string CompatibilityVersion { get; }

        public string WorkerExecutableSha256 { get; }

        public string BuildSummary() =>
            $"source={SourceType} version={Version} asset='{AssetFileName}'";

        private static string NormalizeValue(string value) =>
            string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

        private static string NormalizeHash(string hash) =>
            string.IsNullOrWhiteSpace(hash)
                ? ""
                : hash.Trim().Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path.Trim(), AppContext.BaseDirectory);
            }
            catch
            {
                return path.Trim();
            }
        }
    }

    // app package と launcher の間で、worker artifact の pin 情報を読む専用 helper。
    internal static class ThumbnailRescueWorkerArtifactLockFile
    {
        internal const string LockFileName = "rescue-worker.lock.json";

        public static bool TryRead(
            string hostBaseDirectory,
            out ThumbnailRescueWorkerArtifactLockInfo lockInfo,
            out string diagnosticMessage
        )
        {
            lockInfo = null;
            diagnosticMessage = "";

            string lockFilePath = ResolveLockFilePath(hostBaseDirectory);
            if (string.IsNullOrWhiteSpace(lockFilePath) || !File.Exists(lockFilePath))
            {
                return false;
            }

            try
            {
                using FileStream stream = File.OpenRead(lockFilePath);
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;

                if (
                    !root.TryGetProperty("schemaVersion", out JsonElement schemaVersionElement)
                    || schemaVersionElement.ValueKind != JsonValueKind.Number
                    || !schemaVersionElement.TryGetInt32(out int schemaVersion)
                    || schemaVersion < 1
                )
                {
                    diagnosticMessage =
                        "worker artifact lock invalid: schemaVersion is missing or invalid.";
                    return false;
                }

                if (
                    !root.TryGetProperty("workerArtifact", out JsonElement workerArtifactElement)
                    || workerArtifactElement.ValueKind != JsonValueKind.Object
                )
                {
                    diagnosticMessage =
                        "worker artifact lock invalid: workerArtifact section is missing.";
                    return false;
                }

                string sourceType = ReadRequiredString(
                    workerArtifactElement,
                    "sourceType",
                    ref diagnosticMessage
                );
                if (!string.IsNullOrWhiteSpace(diagnosticMessage))
                {
                    return false;
                }

                string artifactType = ReadRequiredString(
                    workerArtifactElement,
                    "artifactType",
                    ref diagnosticMessage
                );
                if (!string.IsNullOrWhiteSpace(diagnosticMessage))
                {
                    return false;
                }

                string version = ReadRequiredString(
                    workerArtifactElement,
                    "version",
                    ref diagnosticMessage
                );
                if (!string.IsNullOrWhiteSpace(diagnosticMessage))
                {
                    return false;
                }

                string assetFileName = ReadRequiredString(
                    workerArtifactElement,
                    "assetFileName",
                    ref diagnosticMessage
                );
                if (!string.IsNullOrWhiteSpace(diagnosticMessage))
                {
                    return false;
                }

                string compatibilityVersion = ReadRequiredString(
                    workerArtifactElement,
                    "compatibilityVersion",
                    ref diagnosticMessage
                );
                if (!string.IsNullOrWhiteSpace(diagnosticMessage))
                {
                    return false;
                }

                string workerExecutableSha256 = ReadRequiredString(
                    workerArtifactElement,
                    "workerExecutableSha256",
                    ref diagnosticMessage
                );
                if (!string.IsNullOrWhiteSpace(diagnosticMessage))
                {
                    return false;
                }

                lockInfo = new ThumbnailRescueWorkerArtifactLockInfo(
                    lockFilePath,
                    schemaVersion,
                    artifactType,
                    sourceType,
                    version,
                    assetFileName,
                    compatibilityVersion,
                    workerExecutableSha256
                );
                return true;
            }
            catch (Exception ex)
            {
                diagnosticMessage = $"worker artifact lock invalid: {ex.Message}";
                return false;
            }
        }

        internal static bool TryValidateWorkerExecutablePath(
            string workerExecutablePath,
            ThumbnailRescueWorkerArtifactLockInfo lockInfo,
            out string diagnosticMessage
        )
        {
            diagnosticMessage = "";
            if (lockInfo == null)
            {
                return true;
            }

            string normalizedWorkerExecutablePath = NormalizePath(workerExecutablePath);
            if (string.IsNullOrWhiteSpace(normalizedWorkerExecutablePath) || !File.Exists(normalizedWorkerExecutablePath))
            {
                diagnosticMessage = "worker artifact lock mismatch: worker executable is missing.";
                return false;
            }

            string markerPath = Path.Combine(
                Path.GetDirectoryName(normalizedWorkerExecutablePath) ?? "",
                ThumbnailRescueWorkerLaunchSettingsFactory.PublishedArtifactMarkerFileName
            );
            if (!File.Exists(markerPath))
            {
                diagnosticMessage =
                    "worker artifact lock mismatch: artifact marker is missing near worker executable.";
                return false;
            }

            if (!TryReadArtifactMarkerMetadata(markerPath, out string actualArtifactType, out string actualCompatibilityVersion))
            {
                diagnosticMessage = "worker artifact lock mismatch: artifact marker could not be read.";
                return false;
            }

            if (
                !string.Equals(
                    actualArtifactType,
                    lockInfo.ArtifactType,
                    StringComparison.Ordinal
                )
            )
            {
                diagnosticMessage =
                    $"worker artifact lock mismatch: artifactType expected='{lockInfo.ArtifactType}' actual='{actualArtifactType}'.";
                return false;
            }

            if (
                !string.Equals(
                    actualCompatibilityVersion,
                    lockInfo.CompatibilityVersion,
                    StringComparison.Ordinal
                )
            )
            {
                diagnosticMessage =
                    $"worker artifact lock mismatch: compatibilityVersion expected='{lockInfo.CompatibilityVersion}' actual='{actualCompatibilityVersion}'.";
                return false;
            }

            string actualSha256 = ComputeFileSha256(normalizedWorkerExecutablePath);
            if (
                !string.Equals(
                    actualSha256,
                    lockInfo.WorkerExecutableSha256,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                diagnosticMessage =
                    $"worker artifact lock mismatch: sha256 expected='{lockInfo.WorkerExecutableSha256}' actual='{actualSha256}'.";
                return false;
            }

            return true;
        }

        internal static string ResolveLockFilePath(string hostBaseDirectory)
        {
            string normalizedHostBaseDirectory = NormalizePath(hostBaseDirectory);
            if (string.IsNullOrWhiteSpace(normalizedHostBaseDirectory))
            {
                normalizedHostBaseDirectory = NormalizePath(AppContext.BaseDirectory);
            }

            if (string.IsNullOrWhiteSpace(normalizedHostBaseDirectory))
            {
                return "";
            }

            return Path.Combine(normalizedHostBaseDirectory, LockFileName);
        }

        internal static string ComputeFileSha256(string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            byte[] hashBytes = SHA256.HashData(stream);
            StringBuilder builder = new();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                builder.Append(hashBytes[i].ToString("X2"));
            }

            return builder.ToString();
        }

        private static string ReadRequiredString(
            JsonElement element,
            string propertyName,
            ref string diagnosticMessage
        )
        {
            if (
                !element.TryGetProperty(propertyName, out JsonElement propertyElement)
                || propertyElement.ValueKind != JsonValueKind.String
            )
            {
                diagnosticMessage =
                    $"worker artifact lock invalid: {propertyName} is missing.";
                return "";
            }

            string value = propertyElement.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(value))
            {
                diagnosticMessage =
                    $"worker artifact lock invalid: {propertyName} is empty.";
                return "";
            }

            return value.Trim();
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path.Trim(), AppContext.BaseDirectory);
            }
            catch
            {
                return path.Trim();
            }
        }

        private static bool TryReadArtifactMarkerMetadata(
            string markerPath,
            out string artifactType,
            out string compatibilityVersion
        )
        {
            artifactType = "";
            compatibilityVersion = "";

            try
            {
                using FileStream stream = File.OpenRead(markerPath);
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;
                if (
                    !root.TryGetProperty("artifactType", out JsonElement artifactTypeElement)
                    || artifactTypeElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(artifactTypeElement.GetString())
                )
                {
                    return false;
                }

                if (
                    !root.TryGetProperty("compatibilityVersion", out JsonElement compatibilityVersionElement)
                    || compatibilityVersionElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(compatibilityVersionElement.GetString())
                )
                {
                    return false;
                }

                artifactType = artifactTypeElement.GetString() ?? "";
                compatibilityVersion = compatibilityVersionElement.GetString() ?? "";
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
