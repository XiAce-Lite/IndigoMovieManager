namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// エンジン内部ログの橋渡し。
    /// </summary>
    internal static class ThumbnailRuntimeLog
    {
        private static readonly object Sync = new();
        private static IThumbnailLogger logger = NoOpThumbnailLogger.Instance;

        public static void SetLogger(IThumbnailLogger nextLogger)
        {
            lock (Sync)
            {
                logger = nextLogger ?? NoOpThumbnailLogger.Instance;
            }
        }

        public static void Write(string category, string message)
        {
            IThumbnailLogger snapshot;
            lock (Sync)
            {
                snapshot = logger;
            }

            snapshot.LogDebug(category, message);
        }
    }
}
