using System.Drawing;
using DrawingBrushes = System.Drawing.Brushes;
using DrawingColor = System.Drawing.Color;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndigoMovieManager.Converter;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ThumbnailProgressPreviewConverterTests
{
    [SetUp]
    public void SetUp()
    {
        ThumbnailPreviewCache.Shared.Clear();
        ThumbnailPreviewLatencyTracker.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        ThumbnailPreviewCache.Shared.Clear();
        ThumbnailPreviewLatencyTracker.Reset();
    }

    [Test]
    public void Convert_メモリキャッシュがあればファイルより優先して返す()
    {
        const string cacheKey = "worker-preview-key";
        WriteableBitmap preview = new(2, 2, 96, 96, PixelFormats.Bgr24, null);
        byte[] pixels =
        [
            0,
            0,
            0,
            255,
            255,
            255,
            255,
            0,
            0,
            0,
            255,
            0,
        ];
        preview.WritePixels(new System.Windows.Int32Rect(0, 0, 2, 2), pixels, 6, 0);
        preview.Freeze();

        long revision = ThumbnailPreviewCache.Shared.Store(cacheKey, preview);
        ThumbnailProgressPreviewConverter converter = new();

        object result = converter.Convert(
            [cacheKey, revision, @"C:\not-exists\thumb.jpg"],
            typeof(ImageSource),
            "72",
            CultureInfo.InvariantCulture
        );

        Assert.That(result, Is.SameAs(preview));
    }

    [Test]
    public void Convert_キャッシュ未命中時はfallbackをデコード高さ付きで返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string imagePath = Path.Combine(tempRoot, "sample.jpg");
            CreateJpeg(imagePath, 640, 360);

            ThumbnailProgressPreviewConverter converter = new();
            object result = converter.Convert(
                ["missing-cache-key", 1L, imagePath],
                typeof(ImageSource),
                "72",
                CultureInfo.InvariantCulture
            );

            Assert.That(result, Is.InstanceOf<BitmapSource>());
            BitmapSource bitmap = (BitmapSource)result;
            Assert.That(bitmap.PixelHeight, Is.LessThanOrEqualTo(72));
            Assert.That(bitmap.PixelWidth, Is.GreaterThan(0));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string CreateTempRoot()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_tests",
            nameof(ThumbnailProgressPreviewConverterTests),
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    // fallback経路確認用に、十分大きいJPEGを作る。
    private static void CreateJpeg(string path, int width, int height)
    {
        using Bitmap bitmap = new(width, height, DrawingPixelFormat.Format24bppRgb);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(DrawingColor.DarkSlateBlue);
        g.FillRectangle(DrawingBrushes.OrangeRed, 40, 40, 240, 120);
        bitmap.Save(path, DrawingImageFormat.Jpeg);
    }
}
