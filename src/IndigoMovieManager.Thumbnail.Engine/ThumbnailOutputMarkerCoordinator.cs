namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 自動生成前の出力掃除と、成功/失敗時の #ERROR marker 更新をまとめる。
    /// </summary>
    internal static class ThumbnailOutputMarkerCoordinator
    {
        public static void ResetExistingOutputBeforeAutomaticAttempt(string saveThumbFileName)
        {
            if (string.IsNullOrWhiteSpace(saveThumbFileName) || !Path.Exists(saveThumbFileName))
            {
                return;
            }

            DeleteFileQuietly(saveThumbFileName);
        }

        public static void ApplyFailureMarker(
            string outPath,
            string movieFullPath,
            Action<string> log
        )
        {
            try
            {
                string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                    outPath,
                    movieFullPath
                );
                if (
                    ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                        outPath,
                        movieFullPath,
                        out string existingSuccessThumbnailPath
                    )
                )
                {
                    if (Path.Exists(errorMarkerPath))
                    {
                        File.Delete(errorMarkerPath);
                        log?.Invoke(
                            $"error marker deleted after failure fallback: '{errorMarkerPath}', success='{existingSuccessThumbnailPath}'"
                        );
                    }
                }
                else if (!Path.Exists(errorMarkerPath))
                {
                    File.WriteAllBytes(errorMarkerPath, []);
                    log?.Invoke($"error marker created: '{errorMarkerPath}'");
                }
            }
            catch (Exception markerEx)
            {
                log?.Invoke($"error marker write failed: '{markerEx.Message}'");
            }
        }

        public static void CleanupSuccessMarker(
            string outPath,
            string movieFullPath,
            Action<string> log
        )
        {
            if (!TryDeleteErrorMarkerForMovie(outPath, movieFullPath, out string errorMarkerPath))
            {
                return;
            }

            log?.Invoke($"error marker deleted after success: '{errorMarkerPath}'");
        }

        public static void DeleteFileQuietly(string path)
        {
            TryDeleteFileQuietly(path);
        }

        private static bool TryDeleteErrorMarkerForMovie(
            string outPath,
            string movieFullPath,
            out string errorMarkerPath
        )
        {
            errorMarkerPath = "";
            if (string.IsNullOrWhiteSpace(outPath) || string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            string candidate = ThumbnailPathResolver.BuildErrorMarkerPath(outPath, movieFullPath);
            if (string.IsNullOrWhiteSpace(candidate) || !Path.Exists(candidate))
            {
                return false;
            }

            try
            {
                File.Delete(candidate);
                errorMarkerPath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteFileQuietly(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // 一時ファイル削除失敗は後続処理を優先する。
            }
        }
    }
}
