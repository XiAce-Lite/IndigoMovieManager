namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル生成時に使う動画メタ情報の取得口。
    /// </summary>
    public interface IVideoMetadataProvider
    {
        bool TryGetVideoCodec(string moviePath, out string codec);

        bool TryGetDurationSec(string moviePath, out double durationSec);
    }
}
