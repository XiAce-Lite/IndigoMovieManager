namespace IndigoMovieManager.FileIndex.UsnMft
{
    public sealed class IndexProgress
    {
        public IndexProgress(int totalCount, string currentPath)
        {
            TotalCount = totalCount;
            CurrentPath = currentPath;
        }

        public int TotalCount { get; }

        public string CurrentPath { get; }
    }
}
