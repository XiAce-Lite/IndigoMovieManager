using System.Collections.Generic;
using System.IO;

namespace IndigoMovieManager
{
    internal static class WatchFolderDropRegistrationPolicy
    {
        internal static bool CanAccept(IEnumerable<string> droppedPaths)
        {
            if (droppedPaths == null)
            {
                return false;
            }

            foreach (string droppedPath in droppedPaths)
            {
                if (!string.IsNullOrWhiteSpace(NormalizeDirectoryPath(droppedPath)))
                {
                    return true;
                }
            }

            return false;
        }

        internal static WatchFolderDropResult Build(
            IEnumerable<string> droppedPaths,
            IEnumerable<string> existingDirectories
        )
        {
            HashSet<string> existingLookup = BuildDirectoryLookup(existingDirectories);
            HashSet<string> addedLookup = new(StringComparer.OrdinalIgnoreCase);
            List<string> directoriesToAdd = [];
            int duplicateCount = 0;
            int invalidCount = 0;

            if (droppedPaths != null)
            {
                foreach (string droppedPath in droppedPaths)
                {
                    string normalizedDirectoryPath = NormalizeDirectoryPath(droppedPath);
                    if (string.IsNullOrWhiteSpace(normalizedDirectoryPath))
                    {
                        invalidCount++;
                        continue;
                    }

                    if (
                        existingLookup.Contains(normalizedDirectoryPath)
                        || !addedLookup.Add(normalizedDirectoryPath)
                    )
                    {
                        duplicateCount++;
                        continue;
                    }

                    directoriesToAdd.Add(normalizedDirectoryPath);
                    existingLookup.Add(normalizedDirectoryPath);
                }
            }

            return new WatchFolderDropResult(directoriesToAdd, duplicateCount, invalidCount);
        }

        private static HashSet<string> BuildDirectoryLookup(IEnumerable<string> directoryPaths)
        {
            HashSet<string> lookup = new(StringComparer.OrdinalIgnoreCase);
            if (directoryPaths == null)
            {
                return lookup;
            }

            foreach (string directoryPath in directoryPaths)
            {
                string normalizedDirectoryPath = NormalizeExistingDirectoryPath(directoryPath);
                if (!string.IsNullOrWhiteSpace(normalizedDirectoryPath))
                {
                    lookup.Add(normalizedDirectoryPath);
                }
            }

            return lookup;
        }

        private static string NormalizeDirectoryPath(string directoryPath)
        {
            string normalizedDirectoryPath = NormalizeExistingDirectoryPath(directoryPath);
            return !string.IsNullOrWhiteSpace(normalizedDirectoryPath)
                && Directory.Exists(normalizedDirectoryPath)
                ? normalizedDirectoryPath
                : "";
        }

        private static string NormalizeExistingDirectoryPath(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return "";
            }

            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath.Trim()));
            }
            catch
            {
                return "";
            }
        }
    }

    internal sealed class WatchFolderDropResult
    {
        internal WatchFolderDropResult(
            IReadOnlyList<string> directoriesToAdd,
            int duplicateCount,
            int invalidCount
        )
        {
            DirectoriesToAdd = directoriesToAdd ?? [];
            DuplicateCount = duplicateCount;
            InvalidCount = invalidCount;
        }

        internal IReadOnlyList<string> DirectoriesToAdd { get; }
        internal int DuplicateCount { get; }
        internal int InvalidCount { get; }
    }
}
