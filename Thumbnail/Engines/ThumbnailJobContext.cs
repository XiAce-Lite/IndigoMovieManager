namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// エンジンが生成判断と実行に使う入力情報。
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

        public int PanelCount => (TabInfo?.Columns ?? 0) * (TabInfo?.Rows ?? 0);
    }
}
