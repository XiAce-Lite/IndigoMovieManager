namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// WebView 側へ渡す動画 1 件分の v1 契約 DTO。
    /// 旧 WhiteBrowser skin が参照する小文字 alias も同居させ、
    /// 新旧どちらの skin でも同じ payload をそのまま使えるようにする。
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

        // 旧 WhiteBrowser skin 互換の alias 群。
        public long id { get; init; }
        public string title { get; init; } = "";
        public string artist { get; init; } = "";
        public string drive { get; init; } = "";
        public string dir { get; init; } = "";
        public string ext { get; init; } = "";
        public string kana { get; init; } = "";
        public string[] tags { get; init; } = Array.Empty<string>();
        public string container { get; init; } = "";
        public string video { get; init; } = "";
        public string audio { get; init; } = "";
        public string extra { get; init; } = "";
        public string fileDate { get; init; } = "";
        public string comments { get; init; } = "";
        public string lenSec { get; init; } = "";
        public int offset { get; init; }
        public string path { get; init; } = "";
        public string thum { get; init; } = "";
        public string len { get; init; } = "";
        public long size { get; init; }
        public long score { get; init; }
        public bool exist { get; init; }
        public int select { get; init; }
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
