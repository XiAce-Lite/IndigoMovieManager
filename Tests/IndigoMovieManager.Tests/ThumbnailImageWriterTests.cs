using System.Drawing;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailImageWriterTests
{
    [Test]
    public void SaveCombinedThumbnail_2x2配置でJPEG保存できる()
    {
        string tempRoot = CreateTempRoot();
        List<Bitmap> frames =
        [
            CreateSolidBitmap(Color.Red),
            CreateSolidBitmap(Color.Lime),
            CreateSolidBitmap(Color.Blue),
            CreateSolidBitmap(Color.Yellow),
        ];

        try
        {
            string outputPath = Path.Combine(tempRoot, "thumb.jpg");

            bool saved = ThumbnailImageWriter.SaveCombinedThumbnail(outputPath, frames, 2, 2);

            Assert.That(saved, Is.True);
            Assert.That(File.Exists(outputPath), Is.True);

            using Bitmap actual = new(outputPath);
            Color topLeft = actual.GetPixel(20, 15);
            Color topRight = actual.GetPixel(60, 15);
            Color bottomLeft = actual.GetPixel(20, 45);
            Color bottomRight = actual.GetPixel(60, 45);

            Assert.Multiple(() =>
            {
                Assert.That(topLeft.R, Is.GreaterThan(topLeft.G + 40));
                Assert.That(topLeft.R, Is.GreaterThan(topLeft.B + 40));
                Assert.That(topRight.G, Is.GreaterThan(topRight.R + 40));
                Assert.That(topRight.G, Is.GreaterThan(topRight.B + 40));
                Assert.That(bottomLeft.B, Is.GreaterThan(bottomLeft.R + 40));
                Assert.That(bottomLeft.B, Is.GreaterThan(bottomLeft.G + 40));
                Assert.That(bottomRight.R, Is.GreaterThan(120));
                Assert.That(bottomRight.G, Is.GreaterThan(120));
                Assert.That(bottomRight.B, Is.LessThan(120));
            });
        }
        finally
        {
            for (int i = 0; i < frames.Count; i++)
            {
                frames[i]?.Dispose();
            }

            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void TrySaveJpegWithRetry_保存先が空ならFalseを返す()
    {
        using Bitmap bitmap = CreateSolidBitmap(Color.Orange);

        bool saved = ThumbnailImageWriter.TrySaveJpegWithRetry(bitmap, "", out string errorMessage);

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.False);
            Assert.That(errorMessage, Is.EqualTo("save path is empty"));
        });
    }

    private static Bitmap CreateSolidBitmap(Color color)
    {
        Bitmap bitmap = new(40, 30);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
            // 一時ディレクトリ削除失敗はテスト本体より優先しない。
        }
    }
}
