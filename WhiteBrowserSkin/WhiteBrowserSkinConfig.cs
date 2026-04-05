using System;

namespace IndigoMovieManager.Skin
{
    /// <summary>
    /// WhiteBrowser の div#config から拾った最小構成だけを保持する。
    /// 今回は既存 5 レイアウトへ安全に寄せるための値に絞る。
    /// </summary>
    public sealed class WhiteBrowserSkinConfig
    {
        public static WhiteBrowserSkinConfig Empty { get; } = new();

        public string SkinVersion { get; init; } = "";
        public int ThumbWidth { get; init; } = 160;
        public int ThumbHeight { get; init; } = 120;
        public int ThumbColumn { get; init; } = 1;
        public int ThumbRow { get; init; } = 1;
        public int SeamlessScroll { get; init; }
        public string ScrollId { get; init; } = "view";
        public int MultiSelect { get; init; }
    }
}
