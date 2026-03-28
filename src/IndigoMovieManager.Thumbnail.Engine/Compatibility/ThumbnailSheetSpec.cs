namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// WB互換メタへ流し込む純データだけを保持する。
    /// </summary>
    public sealed class ThumbnailSheetSpec
    {
        public int ThumbCount { get; set; } = 1;
        public int ThumbWidth { get; set; } = 160;
        public int ThumbHeight { get; set; } = 120;
        public int ThumbColumns { get; set; } = 1;
        public int ThumbRows { get; set; } = 1;
        public List<int> CaptureSeconds { get; set; } = [];

        public int TotalWidth => ThumbColumns * ThumbWidth;
        public int TotalHeight => ThumbRows * ThumbHeight;

        public ThumbnailSheetSpec Clone()
        {
            return new ThumbnailSheetSpec
            {
                ThumbCount = ThumbCount,
                ThumbWidth = ThumbWidth,
                ThumbHeight = ThumbHeight,
                ThumbColumns = ThumbColumns,
                ThumbRows = ThumbRows,
                CaptureSeconds = [.. CaptureSeconds],
            };
        }

        public ThumbInfo ToThumbInfo()
        {
            return ThumbInfo.FromSheetSpec(this);
        }

        public static ThumbnailSheetSpec FromThumbInfo(ThumbInfo thumbInfo)
        {
            if (thumbInfo == null)
            {
                return new ThumbnailSheetSpec();
            }

            return new ThumbnailSheetSpec
            {
                ThumbCount = thumbInfo.ThumbCounts,
                ThumbWidth = thumbInfo.ThumbWidth,
                ThumbHeight = thumbInfo.ThumbHeight,
                ThumbColumns = thumbInfo.ThumbColumns,
                ThumbRows = thumbInfo.ThumbRows,
                CaptureSeconds = thumbInfo.ThumbSec != null ? [.. thumbInfo.ThumbSec] : [],
            };
        }
    }
}
