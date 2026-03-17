using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Bitmap から UI 非依存プレビュー DTO を作る処理を切り離す。
    /// </summary>
    internal static class ThumbnailPreviewFrameFactory
    {
        public static ThumbnailPreviewFrame CreateFromBitmap(Bitmap source, int maxHeight = 120)
        {
            if (source == null || source.Width < 1 || source.Height < 1)
            {
                return null;
            }

            Size scaledSize = ResolvePreviewTargetSize(source.Size, maxHeight);
            using Bitmap normalized = new(
                scaledSize.Width,
                scaledSize.Height,
                PixelFormat.Format24bppRgb
            );
            using (Graphics g = Graphics.FromImage(normalized))
            {
                g.Clear(Color.Black);
                g.DrawImage(source, 0, 0, scaledSize.Width, scaledSize.Height);
            }

            BitmapData bitmapData = null;
            try
            {
                bitmapData = normalized.LockBits(
                    new Rectangle(0, 0, normalized.Width, normalized.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb
                );
                int stride = bitmapData.Stride;
                if (stride < 1)
                {
                    return null;
                }

                int pixelByteLength = stride * normalized.Height;
                if (pixelByteLength < 1)
                {
                    return null;
                }

                byte[] pixelBytes = new byte[pixelByteLength];
                Marshal.Copy(bitmapData.Scan0, pixelBytes, 0, pixelByteLength);
                return new ThumbnailPreviewFrame
                {
                    PixelBytes = pixelBytes,
                    Width = normalized.Width,
                    Height = normalized.Height,
                    Stride = stride,
                    PixelFormat = ThumbnailPreviewPixelFormat.Bgr24,
                };
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bitmapData != null)
                {
                    normalized.UnlockBits(bitmapData);
                }
            }
        }

        private static Size ResolvePreviewTargetSize(Size sourceSize, int maxHeight)
        {
            if (sourceSize.Width < 1 || sourceSize.Height < 1)
            {
                return new Size(1, 1);
            }

            int safeMaxHeight = maxHeight < 1 ? sourceSize.Height : maxHeight;
            if (sourceSize.Height <= safeMaxHeight)
            {
                return sourceSize;
            }

            double scale = (double)safeMaxHeight / sourceSize.Height;
            int width = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
            return new Size(width, safeMaxHeight);
        }
    }
}
