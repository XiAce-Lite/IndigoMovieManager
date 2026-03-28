namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 生成成否の DTO 構築を service 本体から切り離す。
    /// </summary>
    internal static class ThumbnailCreateResultFactory
    {
        public static ThumbnailCreateResult CreateSuccess(
            string saveThumbFileName,
            double? durationSec,
            ThumbnailPreviewFrame previewFrame = null
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = true,
                PreviewFrame = previewFrame,
            };
        }

        public static ThumbnailCreateResult CreateFailed(
            string saveThumbFileName,
            double? durationSec,
            string errorMessage,
            ThumbnailPreviewFrame previewFrame = null
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = false,
                ErrorMessage = errorMessage ?? "",
                PreviewFrame = previewFrame,
            };
        }
    }
}
