namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// エンジンが生成判断と実行に使う入力情報。
    /// </summary>
    internal sealed class ThumbnailJobContext
    {
        private ThumbnailRequest request = new();

        // 本流入力は中立契約へ寄せる。既存 initializer は QueueObj でも受け続ける。
        public ThumbnailRequest Request
        {
            get { return request; }
            init { request = value?.Clone() ?? new ThumbnailRequest(); }
        }

        public QueueObj QueueObj
        {
            get { return QueueObj.FromThumbnailRequest(request); }
            init { request = value?.ToThumbnailRequest() ?? new ThumbnailRequest(); }
        }
        public ThumbnailLayoutProfile LayoutProfile { get; init; }
        public string ThumbnailOutPath { get; init; } = "";
        public ThumbInfo ThumbInfo { get; init; }
        public string MovieFullPath { get; init; } = "";
        public string SaveThumbFileName { get; init; } = "";
        public bool IsResizeThumb { get; init; }
        public bool IsManual { get; init; }
        public double? DurationSec { get; init; }
        public long FileSizeBytes { get; init; }
        public bool IsSlowLane { get; init; }
        public bool IsUltraLargeMovie { get; init; }
        public double? AverageBitrateMbps { get; init; }
        public bool HasEmojiPath { get; init; }
        public string VideoCodec { get; init; } = "";
        public string InitialEngineHint { get; init; } = "";
        public string TraceId { get; init; } = "";

        public int PanelColumns => LayoutProfile?.Columns ?? 0;
        public int PanelRows => LayoutProfile?.Rows ?? 0;
        public int PanelWidth => LayoutProfile?.Width ?? 0;
        public int PanelHeight => LayoutProfile?.Height ?? 0;
        public int PanelCount => PanelColumns * PanelRows;
    }
}
