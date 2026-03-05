using System;

namespace IndigoMovieManager.FileIndex.UsnMft
{
    public sealed class SearchResultItem
    {
        public SearchResultItem(string name, string fullPath, long size, DateTime lastWriteTimeUtc, bool isDirectory)
        {
            Name = name;
            FullPath = fullPath;
            Size = size;
            LastWriteTimeUtc = lastWriteTimeUtc;
            IsDirectory = isDirectory;
            NameLower = (name ?? string.Empty).ToLowerInvariant();
        }

        public string Name { get; }

        public string FullPath { get; }

        public long Size { get; }

        public DateTime LastWriteTimeUtc { get; }

        public bool IsDirectory { get; }

        public string NameLower { get; }
    }
}
