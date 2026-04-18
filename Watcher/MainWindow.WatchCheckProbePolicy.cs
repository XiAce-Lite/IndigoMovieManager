namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 重い1件の原因切り分け用。対象動画IDを含むパスは常に詳細トレースする。
        private const string WatchCheckProbeMovieIdentity = "MH922SNIgTs_gggggggggg.mkv";
        // 対象外でも、1件処理が閾値を超えたら詳細トレースする。
        private const long WatchCheckProbeSlowThresholdMs = 120;

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
