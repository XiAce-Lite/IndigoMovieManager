using IndigoMovieManager.Thumbnail.Decoders;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// FFMediaToolkit を使うサムネ生成エンジン。
    /// </summary>
    internal sealed class FfMediaToolkitThumbnailGenerationEngine
        : FrameDecoderThumbnailGenerationEngine
    {
        public FfMediaToolkitThumbnailGenerationEngine()
            : base("ffmediatoolkit", new FfMediaToolkitThumbnailFrameDecoder()) { }
    }
}