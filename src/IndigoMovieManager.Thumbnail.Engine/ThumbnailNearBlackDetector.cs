using System.Drawing;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 真っ黒寄りのサムネイルを軽く弾く判定だけをまとめる。
    /// </summary>
    internal static class ThumbnailNearBlackDetector
    {
        private const double NearBlackThumbnailLumaThreshold = 2d;
        private const int NearBlackThumbnailSampleStep = 4;

        // 真っ黒フレームを軽く弾くため、間引きサンプリングで平均輝度だけを見る。
        public static bool IsNearBlackBitmap(Bitmap source, out double averageLuma)
        {
            averageLuma = 0d;
            if (source == null || source.Width < 1 || source.Height < 1)
            {
                return false;
            }

            double sum = 0d;
            int count = 0;
            for (int y = 0; y < source.Height; y += NearBlackThumbnailSampleStep)
            {
                for (int x = 0; x < source.Width; x += NearBlackThumbnailSampleStep)
                {
                    Color pixel = source.GetPixel(x, y);
                    sum +=
                        (0.2126d * pixel.R) + (0.7152d * pixel.G) + (0.0722d * pixel.B);
                    count++;
                }
            }

            if (count < 1)
            {
                return false;
            }

            averageLuma = sum / count;
            return averageLuma <= NearBlackThumbnailLumaThreshold;
        }

        // 保存済みjpgを開ける時だけ、真っ黒な結果を失敗へ戻せるようにする。
        public static bool IsNearBlackImageFile(string imagePath, out double averageLuma)
        {
            averageLuma = 0d;
            if (string.IsNullOrWhiteSpace(imagePath) || !Path.Exists(imagePath))
            {
                return false;
            }

            try
            {
                using Bitmap bitmap = new(imagePath);
                return IsNearBlackBitmap(bitmap, out averageLuma);
            }
            catch
            {
                return false;
            }
        }
    }
}
