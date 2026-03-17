namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル生成の public 入口を 1 本化するための DTO。
    /// </summary>
    public sealed class ThumbnailCreateArgs
    {
        public QueueObj QueueObj { get; init; }
        public ThumbnailRequest Request { get; init; }
        public string DbName { get; init; } = "";
        public string ThumbFolder { get; init; } = "";
        public bool IsResizeThumb { get; init; }
        public bool IsManual { get; init; }
        public string SourceMovieFullPathOverride { get; init; } = "";
        public string InitialEngineHint { get; init; } = "";
        public ThumbInfo ThumbInfoOverride { get; init; }
    }

    /// <summary>
    /// ブックマーク用 1 枚生成の public 入口をまとめる DTO。
    /// </summary>
    public sealed class ThumbnailBookmarkArgs
    {
        public string MovieFullPath { get; init; } = "";
        public string SaveThumbPath { get; init; } = "";
        public int CapturePos { get; init; }
    }
}
