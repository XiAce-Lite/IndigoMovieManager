using System.Drawing;
using System.Drawing.Imaging;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 画像の切り抜きと固定枠リサイズを service 本体から切り離す。
    /// </summary>
    internal static class ThumbnailImageTransformHelper
    {
        public static Rectangle GetAspectRect(int imgWidth, int imgHeight)
        {
            int w = imgWidth;
            int h = imgHeight;
            int wdiff = 0;
            int hdiff = 0;

            float aspect = (float)imgWidth / imgHeight;
            if (aspect > 1.34f)
            {
                h = (int)Math.Floor((decimal)imgHeight / 3);
                w = (int)Math.Floor((decimal)h * 4);
                h = imgHeight;
                wdiff = (imgWidth - w) / 2;
                hdiff = 0;
            }

            if (aspect < 1.33f)
            {
                w = (int)Math.Floor((decimal)imgWidth / 4);
                h = (int)Math.Floor((decimal)w * 3);
                w = imgWidth;
                hdiff = (imgHeight - h) / 2;
                wdiff = 0;
            }

            return new Rectangle(wdiff, hdiff, w, h);
        }

        public static Size ResolveDefaultTargetSize(Bitmap source)
        {
            int width = source.Width < 320 ? source.Width : 320;
            int height = source.Height < 240 ? source.Height : 240;

            if (width <= 0)
            {
                width = 320;
            }

            if (height <= 0)
            {
                height = 240;
            }

            return new Size(width, height);
        }

        public static Bitmap CropBitmap(Bitmap source, Rectangle cropRect)
        {
            Rectangle bounded = Rectangle.Intersect(
                new Rectangle(0, 0, source.Width, source.Height),
                cropRect
            );
            if (bounded.Width <= 0 || bounded.Height <= 0)
            {
                bounded = new Rectangle(0, 0, source.Width, source.Height);
            }

            Bitmap cropped = new(bounded.Width, bounded.Height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(cropped);
            g.DrawImage(
                source,
                new Rectangle(0, 0, bounded.Width, bounded.Height),
                bounded,
                GraphicsUnit.Pixel
            );
            return cropped;
        }

        public static Bitmap ResizeBitmap(
            Bitmap source,
            Size targetSize,
            double? sourceDisplayAspectRatio = null
        )
        {
            Bitmap resized = new(targetSize.Width, targetSize.Height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(resized);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Color.Black);

            // 固定枠の中へ元動画の比率を保ったまま収め、余白は黒で埋める。
            Rectangle drawRect = CalculateAspectFitRectangle(
                new Size(source.Width, source.Height),
                targetSize,
                sourceDisplayAspectRatio
            );
            g.DrawImage(source, drawRect);
            return resized;
        }

        public static Rectangle CalculateAspectFitRectangle(
            Size sourceSize,
            Size targetSize,
            double? sourceDisplayAspectRatio = null
        )
        {
            if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
            {
                return new Rectangle(
                    0,
                    0,
                    Math.Max(1, targetSize.Width),
                    Math.Max(1, targetSize.Height)
                );
            }

            if (targetSize.Width <= 0 || targetSize.Height <= 0)
            {
                return new Rectangle(0, 0, sourceSize.Width, sourceSize.Height);
            }

            double sourceAspect = ResolveAspectRatio(sourceSize, sourceDisplayAspectRatio);
            if (sourceAspect <= 0)
            {
                sourceAspect = (double)sourceSize.Width / sourceSize.Height;
            }

            double targetAspect = (double)targetSize.Width / targetSize.Height;

            // 4:3 ぴったり素材や、SAR補正後に目標比へ一致する素材は全面へ敷く。
            if (Math.Abs(sourceAspect - targetAspect) <= 0.01d)
            {
                return new Rectangle(0, 0, targetSize.Width, targetSize.Height);
            }

            int drawWidth;
            int drawHeight;
            if (sourceAspect >= targetAspect)
            {
                drawWidth = targetSize.Width;
                drawHeight = Math.Max(1, (int)Math.Round(targetSize.Width / sourceAspect));
            }
            else
            {
                drawHeight = targetSize.Height;
                drawWidth = Math.Max(1, (int)Math.Round(targetSize.Height * sourceAspect));
            }

            int offsetX = (targetSize.Width - drawWidth) / 2;
            int offsetY = (targetSize.Height - drawHeight) / 2;
            return new Rectangle(offsetX, offsetY, drawWidth, drawHeight);
        }

        public static double ResolveAspectRatio(
            Size sourceSize,
            double? sourceDisplayAspectRatio = null
        )
        {
            if (
                sourceDisplayAspectRatio.HasValue
                && sourceDisplayAspectRatio.Value > 0
                && !double.IsNaN(sourceDisplayAspectRatio.Value)
                && !double.IsInfinity(sourceDisplayAspectRatio.Value)
            )
            {
                return sourceDisplayAspectRatio.Value;
            }

            if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
            {
                return 0;
            }

            return (double)sourceSize.Width / sourceSize.Height;
        }
    }
}
