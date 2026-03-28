namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// メタ情報が取得できない環境向けの既定実装。
    /// </summary>
    internal sealed class NoOpVideoMetadataProvider : IVideoMetadataProvider
    {
        public static NoOpVideoMetadataProvider Instance { get; } = new();

        private NoOpVideoMetadataProvider() { }

        public bool TryGetVideoCodec(string moviePath, out string codec)
        {
            codec = "";
            return false;
        }

        public bool TryGetDurationSec(string moviePath, out double durationSec)
        {
            durationSec = 0;
            return false;
        }
    }
}
