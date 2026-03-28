namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// エンジンログを既存の DebugRuntimeLog へ流す。
    /// </summary>
    internal sealed class AppThumbnailLogger : IThumbnailLogger
    {
        public void LogDebug(string category, string message)
        {
            DebugRuntimeLog.Write(category, message);
        }

        public void LogInfo(string category, string message)
        {
            DebugRuntimeLog.Write(category, $"[info] {message}");
        }

        public void LogWarning(string category, string message)
        {
            DebugRuntimeLog.Write(category, $"[warn] {message}");
        }

        public void LogError(string category, string message)
        {
            DebugRuntimeLog.Write(category, $"[error] {message}");
        }
    }
}
