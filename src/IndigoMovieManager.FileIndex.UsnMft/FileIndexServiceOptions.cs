using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IndigoMovieManager.FileIndex.UsnMft
{
    public enum FileIndexBackendMode
    {
        Auto = 0,
        AdminUsnMft = 1,
        StandardFileSystem = 2,
    }

    public sealed class FileIndexServiceOptions
    {
        public FileIndexBackendMode BackendMode { get; set; } = FileIndexBackendMode.Auto;

        public IReadOnlyList<string> StandardUserRoots { get; set; } = GetDefaultStandardRoots();

        public IReadOnlyList<string> StandardUserExcludePaths { get; set; } = GetDefaultStandardExcludePaths();

        private static IReadOnlyList<string> GetDefaultStandardRoots()
        {
            var roots = DriveInfo
                .GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => d.RootDirectory.FullName)
                .ToArray();

            if (roots.Length > 0)
            {
                return roots;
            }

            return new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        }

        private static IReadOnlyList<string> GetDefaultStandardExcludePaths()
        {
            return new[]
            {
                ".git",
                "node_modules",
            };
        }
    }
}
