namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// HTML 正規化の結果をまとめて返す DTO。
    /// 受け側はこの結果をそのまま WebView2 へ流せばよい。
    /// </summary>
    public sealed class WhiteBrowserSkinEncodingNormalizationResult
    {
        public WhiteBrowserSkinEncodingNormalizationResult(
            string normalizedHtml,
            string sourceEncodingName,
            string injectedBaseUri,
            bool rewroteCharsetMeta,
            bool rewroteCompatibilityScripts
        )
        {
            NormalizedHtml = normalizedHtml ?? "";
            SourceEncodingName = sourceEncodingName ?? "";
            InjectedBaseUri = injectedBaseUri ?? "";
            RewroteCharsetMeta = rewroteCharsetMeta;
            RewroteCompatibilityScripts = rewroteCompatibilityScripts;
        }

        public string NormalizedHtml { get; }
        public string SourceEncodingName { get; }
        public string InjectedBaseUri { get; }
        public bool RewroteCharsetMeta { get; }
        public bool RewroteCompatibilityScripts { get; }
    }
}
