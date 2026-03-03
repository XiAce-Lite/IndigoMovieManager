namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// ログ出力を行わない既定ロガー。
    /// </summary>
    internal sealed class NoOpThumbnailLogger : IThumbnailLogger
    {
        public static NoOpThumbnailLogger Instance { get; } = new();

        private NoOpThumbnailLogger() { }

        public void LogDebug(string category, string message) { }

        public void LogInfo(string category, string message) { }

        public void LogWarning(string category, string message) { }

        public void LogError(string category, string message) { }
    }
}
