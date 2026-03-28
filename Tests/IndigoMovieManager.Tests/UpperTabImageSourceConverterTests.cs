using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Data;
using IndigoMovieManager.UpperTabs.Common;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabImageSourceConverterTests
{
    [Test]
    public void 非アクティブタブでは画像更新しない()
    {
        Assert.That(UpperTabActivationGate.ShouldApplyImageUpdate(false), Is.False);
        Assert.That(UpperTabActivationGate.ShouldApplyImageUpdate(true), Is.True);
        Assert.That(UpperTabActivationGate.ShouldApplyImageUpdate(null), Is.True);
    }

    [Test]
    public void 非アクティブ時のconverterはDoNothingを返す()
    {
        UpperTabImageSourceConverter converter = new();

        object actual = converter.Convert(
            [@"C:\thumb\a.jpg", true, false],
            typeof(System.Windows.Media.ImageSource),
            UpperTabDecodeProfile.SmallDecodePixelHeight,
            CultureInfo.InvariantCulture
        );

        Assert.That(actual, Is.SameAs(Binding.DoNothing));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void 選択中で画像が存在すればImageSourceを返す()
    {
        UpperTabImageSourceConverter converter = new();
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");

        try
        {
            using Bitmap bitmap = new(8, 8);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.Red);
            }

            bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);

            object actual = converter.Convert(
                [tempPath, true, true],
                typeof(System.Windows.Media.ImageSource),
                UpperTabDecodeProfile.GridDecodePixelHeight,
                CultureInfo.InvariantCulture
            );

            Assert.That(actual, Is.AssignableTo<System.Windows.Media.ImageSource>());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
