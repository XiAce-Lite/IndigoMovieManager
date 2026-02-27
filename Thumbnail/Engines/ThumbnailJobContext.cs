namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// サムネイル生成1件分のコンテキスト情報。
    /// ThumbnailCreationService が構築し、エンジンとルーターへ渡す。
    /// </summary>
    internal sealed class ThumbnailJobContext
    {
        public QueueObj QueueObj { get; init; }
        public TabInfo TabInfo { get; init; }
        public ThumbInfo ThumbInfo { get; init; }
        public string MovieFullPath { get; init; } = "";
        public string SaveThumbFileName { get; init; } = "";
        public bool IsResizeThumb { get; init; }
        public bool IsManual { get; init; }
        public double? DurationSec { get; init; }
        public long FileSizeBytes { get; init; }
        public double? AverageBitrateMbps { get; init; }
        public bool HasEmojiPath { get; init; }
        public string VideoCodec { get; init; } = "";

        /// <summary>
        /// タイルパネルの総数（= columns × rows）。
        /// </summary>
        public int PanelCount =>
            (ThumbInfo != null) ? ThumbInfo.ThumbRows * ThumbInfo.ThumbColumns : 1;
    }
}
