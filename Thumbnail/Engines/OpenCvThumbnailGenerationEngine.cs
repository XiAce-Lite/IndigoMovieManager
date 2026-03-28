using IndigoMovieManager.Thumbnail.Decoders;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// OpenCvSharp を使うサムネ生成エンジン。
    /// </summary>
    internal sealed class OpenCvThumbnailGenerationEngine : FrameDecoderThumbnailGenerationEngine
    {
        public OpenCvThumbnailGenerationEngine()
            : base("opencv", new OpenCvThumbnailFrameDecoder()) { }
    }
}