using System.Drawing;
using IndigoMovieManager.Thumbnail.Decoders;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// フレーム取得の細かい再試行規則を service 本体から切り離す。
    /// </summary>
    internal static class ThumbnailFrameReadRetryHelper
    {
        public static bool TryReadFrameWithRetry(
            IThumbnailFrameSource frameSource,
            TimeSpan baseTime,
            out Bitmap frameBitmap
        )
        {
            frameBitmap = null;
            if (frameSource == null)
            {
                return false;
            }

            for (int i = 0; i <= 100; i++)
            {
                TimeSpan tryTime = baseTime + TimeSpan.FromMilliseconds(i * 100);
                if (tryTime < TimeSpan.Zero)
                {
                    tryTime = TimeSpan.Zero;
                }

                if (frameSource.TryReadFrame(tryTime, out frameBitmap))
                {
                    return true;
                }
            }

            // 1秒未満～1秒付近の短尺動画は、0秒起点の細かい時刻で拾えることがある。
            if (baseTime <= TimeSpan.FromSeconds(1))
            {
                for (int ms = 0; ms <= 1000; ms += 33)
                {
                    if (frameSource.TryReadFrame(TimeSpan.FromMilliseconds(ms), out frameBitmap))
                    {
                        return true;
                    }
                }
            }

            frameBitmap?.Dispose();
            frameBitmap = null;
            return false;
        }
    }
}
