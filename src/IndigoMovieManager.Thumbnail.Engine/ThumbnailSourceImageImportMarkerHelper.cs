namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// same-name 画像取り込み由来の管理サムネを、後段の契約解決で見分けるための sidecar marker。
    /// 通常生成で上書きされた時は削除し、runtime 判定の誤爆を防ぐ。
    /// </summary>
    public static class ThumbnailSourceImageImportMarkerHelper
    {
        public const string MarkerExtension = ".source-image-import";

        public static string BuildMarkerPath(string thumbnailPath)
        {
            return string.IsNullOrWhiteSpace(thumbnailPath)
                ? ""
                : $"{thumbnailPath}{MarkerExtension}";
        }

        public static bool HasMarker(string thumbnailPath)
        {
            string markerPath = BuildMarkerPath(thumbnailPath);
            return !string.IsNullOrWhiteSpace(markerPath) && File.Exists(markerPath);
        }

        public static void Synchronize(string thumbnailPath, bool isSourceImageImported)
        {
            string markerPath = BuildMarkerPath(thumbnailPath);
            if (string.IsNullOrWhiteSpace(markerPath))
            {
                return;
            }

            try
            {
                if (isSourceImageImported)
                {
                    string markerDirectory = Path.GetDirectoryName(markerPath) ?? "";
                    if (!string.IsNullOrWhiteSpace(markerDirectory))
                    {
                        Directory.CreateDirectory(markerDirectory);
                    }

                    File.WriteAllText(markerPath, "source-image-import");
                    return;
                }

                if (File.Exists(markerPath))
                {
                    File.Delete(markerPath);
                }
            }
            catch
            {
                // marker は補助情報なので、失敗しても本体成功を潰さない。
            }
        }
    }
}
