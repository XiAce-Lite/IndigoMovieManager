namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル関連ログの抽象。
    /// </summary>
    public interface IThumbnailLogger
    {
        void LogDebug(string category, string message);

        void LogInfo(string category, string message);

        void LogWarning(string category, string message);

        void LogError(string category, string message);
    }
}
