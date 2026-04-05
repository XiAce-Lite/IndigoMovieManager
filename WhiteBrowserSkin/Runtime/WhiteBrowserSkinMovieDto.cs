namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// WebView 側へ渡す動画 1 件分の v1 契約 DTO。
    /// </summary>
    public sealed class WhiteBrowserSkinMovieDto
    {
        public string DbIdentity { get; init; } = "";
        public long MovieId { get; init; }
        public string RecordKey { get; init; } = "";
        public string MovieName { get; init; } = "";
        public string MoviePath { get; init; } = "";
        public string ThumbUrl { get; init; } = "";
        public string ThumbRevision { get; init; } = "";
        public string ThumbSourceKind { get; init; } = "";
        public int ThumbNaturalWidth { get; init; }
        public int ThumbNaturalHeight { get; init; }
        public int ThumbSheetColumns { get; init; }
        public int ThumbSheetRows { get; init; }
        public string Length { get; init; } = "";
        public long Size { get; init; }
        public string[] Tags { get; init; } = Array.Empty<string>();
        public long Score { get; init; }
        public bool Exists { get; init; }
        public bool Selected { get; init; }
    }

    /// <summary>
    /// wb.update の戻り値を、ページング情報と一緒に返す。
    /// </summary>
    public sealed class WhiteBrowserSkinUpdateResponse
    {
        public int StartIndex { get; init; }
        public int RequestedCount { get; init; }
        public int TotalCount { get; init; }
        public WhiteBrowserSkinMovieDto[] Items { get; init; } = Array.Empty<WhiteBrowserSkinMovieDto>();
    }
}
