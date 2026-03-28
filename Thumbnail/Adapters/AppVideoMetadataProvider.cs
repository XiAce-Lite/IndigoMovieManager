namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// アプリ本体の MovieInfo を使ってメタ情報を取得する。
    /// </summary>
    internal sealed class AppVideoMetadataProvider : IVideoMetadataProvider
    {
        public bool TryGetVideoCodec(string moviePath, out string codec)
        {
            codec = "";
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return false;
            }

            try
            {
                MovieInfo info = new(moviePath, noHash: true);
                if (string.IsNullOrWhiteSpace(info.VideoCodec))
                {
                    return false;
                }

                codec = info.VideoCodec;
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail",
                    $"metadata codec read failed: movie='{moviePath}', err='{ex.Message}'"
                );
                return false;
            }
        }

        public bool TryGetDurationSec(string moviePath, out double durationSec)
        {
            durationSec = 0;
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return false;
            }

            try
            {
                MovieInfo info = new(moviePath, noHash: true);
                if (info.MovieLength <= 0)
                {
                    return false;
                }

                durationSec = info.MovieLength;
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail",
                    $"metadata duration read failed: movie='{moviePath}', err='{ex.Message}'"
                );
                return false;
            }
        }
    }
}
