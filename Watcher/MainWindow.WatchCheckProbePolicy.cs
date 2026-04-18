namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 1件トレース対象を判定する。動画ID部分で一致させることで、長いパス全体の表記ゆれに強くする。
        private static bool IsWatchCheckProbeTargetMovie(string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            return movieFullPath.Contains(
                WatchCheckProbeMovieIdentity,
                StringComparison.OrdinalIgnoreCase
            );
        }
    }
}
