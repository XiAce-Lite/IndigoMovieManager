namespace IndigoMovieManager.Thumbnail.Decoders
{
    /// <summary>
    /// サムネイルデコーダーの生成窓口。
    /// 環境変数で実装切替できるようにしておく。
    /// </summary>
    internal static class ThumbnailFrameDecoderFactory
    {
        private const string DecoderEnvName = "IMM_THUMB_DECODER";

        public static IThumbnailFrameDecoder CreateDefault()
        {
            string decoderName = Environment.GetEnvironmentVariable(DecoderEnvName)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(decoderName))
            {
                return new FfMediaToolkitThumbnailFrameDecoder();
            }

            if (string.Equals(decoderName, "ffmediatoolkit", StringComparison.OrdinalIgnoreCase))
            {
                return new FfMediaToolkitThumbnailFrameDecoder();
            }

            if (
                string.Equals(decoderName, "opencv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(decoderName, "opencvsharp", StringComparison.OrdinalIgnoreCase)
            )
            {
                return new OpenCvThumbnailFrameDecoder();
            }

            ThumbnailRuntimeLog.Write(
                "thumbnail",
                $"unknown thumb decoder '{decoderName}'. fallback=ffmediatoolkit"
            );
            return new FfMediaToolkitThumbnailFrameDecoder();
        }
    }
}