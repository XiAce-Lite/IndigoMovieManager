namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// 初回描画に必要な HTML と付帯情報を束ねる。
    /// Host 側はこれを受け取り、そのまま NavigateToString へ流す。
    /// </summary>
    public sealed class WhiteBrowserSkinRenderDocument
    {
        public WhiteBrowserSkinRenderDocument(
            string html,
            string sourceEncodingName,
            string skinBaseUri,
            string thumbnailBaseUri
        )
        {
            Html = html ?? "";
            SourceEncodingName = sourceEncodingName ?? "";
            SkinBaseUri = skinBaseUri ?? "";
            ThumbnailBaseUri = thumbnailBaseUri ?? "";
        }

        public string Html { get; }
        public string SourceEncodingName { get; }
        public string SkinBaseUri { get; }
        public string ThumbnailBaseUri { get; }
    }
}
