using System.Drawing;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class ThumbnailAspectRatioTests
{
    [Test]
    public void CalculateAspectFitRectangle_横長動画は上下に余白を入れる()
    {
        Rectangle rect = ThumbnailCreationService.CalculateAspectFitRectangle(
            new Size(1920, 1080),
            new Size(320, 240)
        );

        Assert.That(rect, Is.EqualTo(new Rectangle(0, 30, 320, 180)));
    }

    [Test]
    public void CalculateAspectFitRectangle_縦長動画は左右に余白を入れる()
    {
        Rectangle rect = ThumbnailCreationService.CalculateAspectFitRectangle(
            new Size(1080, 1920),
            new Size(320, 240)
        );

        Assert.That(rect, Is.EqualTo(new Rectangle(92, 0, 135, 240)));
    }

    [Test]
    public void CalculateAspectFitRectangle_DARが4対3なら720x480でも全面描画する()
    {
        Rectangle rect = ThumbnailCreationService.CalculateAspectFitRectangle(
            new Size(720, 480),
            new Size(320, 240),
            4d / 3d
        );

        Assert.That(rect, Is.EqualTo(new Rectangle(0, 0, 320, 240)));
    }

    [Test]
    public void ResizeBitmap_元動画比率を保って黒帯を入れる()
    {
        using Bitmap source = new(1920, 1080);
        using (Graphics g = Graphics.FromImage(source))
        {
            g.Clear(Color.Red);
        }

        using Bitmap resized = ThumbnailCreationService.ResizeBitmap(source, new Size(320, 240));

        Assert.That(resized.Width, Is.EqualTo(320));
        Assert.That(resized.Height, Is.EqualTo(240));
        Assert.That(resized.GetPixel(10, 10).ToArgb(), Is.EqualTo(Color.Black.ToArgb()));
        Assert.That(resized.GetPixel(160, 120).ToArgb(), Is.EqualTo(Color.Red.ToArgb()));
    }

    [Test]
    public void ResizeBitmap_DARが4対3なら720x480相当でも黒枠を入れない()
    {
        using Bitmap source = new(720, 480);
        using (Graphics g = Graphics.FromImage(source))
        {
            g.Clear(Color.Red);
        }

        using Bitmap resized = ThumbnailCreationService.ResizeBitmap(
            source,
            new Size(320, 240),
            4d / 3d
        );

        Assert.That(resized.Width, Is.EqualTo(320));
        Assert.That(resized.Height, Is.EqualTo(240));
        Assert.That(resized.GetPixel(10, 10).ToArgb(), Is.EqualTo(Color.Red.ToArgb()));
        Assert.That(resized.GetPixel(160, 120).ToArgb(), Is.EqualTo(Color.Red.ToArgb()));
    }
}
