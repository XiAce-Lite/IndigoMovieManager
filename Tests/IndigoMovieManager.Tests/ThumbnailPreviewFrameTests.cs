using System.Drawing;
using System.Drawing.Imaging;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class ThumbnailPreviewFrameTests
{
    [Test]
    public void CreatePreviewFrameFromBitmap_高さ上限で縮小してBgr24を返す()
    {
        using Bitmap source = new(640, 360, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(source))
        {
            g.Clear(Color.DarkBlue);
            g.FillRectangle(Brushes.Orange, 10, 10, 200, 120);
        }

        ThumbnailPreviewFrame frame = ThumbnailPreviewFrameFactory.CreateFromBitmap(source, 120);

        Assert.That(frame, Is.Not.Null);
        Assert.That(frame.Height, Is.EqualTo(120));
        Assert.That(frame.Width, Is.EqualTo(213));
        Assert.That(frame.PixelFormat, Is.EqualTo(ThumbnailPreviewPixelFormat.Bgr24));
        Assert.That(frame.Stride, Is.GreaterThanOrEqualTo(frame.Width * 3));
        Assert.That(frame.IsValid(), Is.True);
    }
}
