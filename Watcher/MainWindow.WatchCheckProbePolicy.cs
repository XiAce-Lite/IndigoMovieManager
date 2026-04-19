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

        // 1件処理が重かった時だけ詳細ログを残し、watch の詰まり位置を後から追いやすくする。
        private void WriteWatchCheckProbeIfNeeded(
            WatchFolderScanMovieResult probeResult,
            string movieFullPath,
            int snapshotTabIndex
        )
        {
            if (probeResult == null)
            {
                return;
            }

            bool isTarget = IsWatchCheckProbeTargetMovie(movieFullPath);
            if (!isTarget && probeResult.TotalElapsedMs < WatchCheckProbeSlowThresholdMs)
            {
                return;
            }

            DebugRuntimeLog.Write(
                "watch-check-probe",
                $"tab={snapshotTabIndex} outcome={probeResult.Outcome} total_ms={probeResult.TotalElapsedMs} "
                    + $"db_lookup_ms={probeResult.DbLookupElapsedMs} thumb_exists_ms={probeResult.ThumbExistsElapsedMs} "
                    + $"movieinfo_ms={probeResult.MovieInfoElapsedMs} flush_wait_ms={probeResult.FlushWaitElapsedMs} path='{movieFullPath}'"
            );
        }

        // 1件処理後の集計反映と probe 出力を同じ場所へ寄せ、走査ループの見通しを保つ。
        private void ApplyWatchProcessResultWithProbe(
            WatchFolderScanMovieResult processResult,
            string movieFullPath,
            int snapshotTabIndex,
            ref long dbLookupTotalMs,
            ref long movieInfoTotalMs,
            ref long dbInsertTotalMs,
            ref long uiReflectTotalMs,
            ref long enqueueFlushTotalMs,
            ref int addedByFolderCount,
            ref int enqueuedCount,
            ref bool folderCheckFlag,
            ref List<WatchChangedMovie> changedMoviesForUiReload
        )
        {
            ApplyWatchScannedMovieProcessResult(
                processResult,
                ref dbLookupTotalMs,
                ref movieInfoTotalMs,
                ref dbInsertTotalMs,
                ref uiReflectTotalMs,
                ref enqueueFlushTotalMs,
                ref addedByFolderCount,
                ref enqueuedCount,
                ref folderCheckFlag,
                ref changedMoviesForUiReload
            );
            WriteWatchCheckProbeIfNeeded(processResult, movieFullPath, snapshotTabIndex);
        }
    }
}
