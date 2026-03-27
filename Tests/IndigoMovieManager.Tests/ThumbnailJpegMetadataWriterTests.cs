using System.Drawing;
using System.Drawing.Imaging;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailJpegMetadataWriterTests
{
    [Test]
    public void TrySaveJpegWithThumbInfo_WB互換メタ付きで保存できる()
    {
        string tempDir = CreateTempDirectory("imm-jpeg-meta-save");
        string savePath = Path.Combine(tempDir, "sample.jpg");

        try
        {
            using Bitmap bitmap = new(160, 120);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);

            ThumbInfo thumbInfo = CreateGridThumbInfo();

            bool ok = ThumbnailJpegMetadataWriter.TrySaveJpegWithThumbInfo(
                bitmap,
                savePath,
                thumbInfo,
                out string errorMessage
            );

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True, errorMessage);
                Assert.That(File.Exists(savePath), Is.True);
                Assert.That(
                    WhiteBrowserThumbInfoSerializer.TryReadFromJpeg(
                        savePath,
                        out ThumbnailSheetSpec actual
                    ),
                    Is.True
                );
                Assert.That(actual.ThumbWidth, Is.EqualTo(160));
                Assert.That(actual.ThumbHeight, Is.EqualTo(120));
                Assert.That(actual.ThumbCount, Is.EqualTo(1));
            });
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public void TryEnsureThumbInfoMetadata_既に一致していれば追記を省略する()
    {
        string tempDir = CreateTempDirectory("imm-jpeg-meta-skip");
        string savePath = Path.Combine(tempDir, "sample.jpg");

        try
        {
            using Bitmap bitmap = new(160, 120);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            bitmap.Save(savePath, ImageFormat.Jpeg);

            ThumbInfo thumbInfo = CreateGridThumbInfo();

            bool firstOk = ThumbnailJpegMetadataWriter.TryEnsureThumbInfoMetadata(
                savePath,
                thumbInfo,
                out string firstError
            );
            long lengthAfterFirst = new FileInfo(savePath).Length;

            bool secondOk = ThumbnailJpegMetadataWriter.TryEnsureThumbInfoMetadata(
                savePath,
                thumbInfo,
                out string secondError
            );
            long lengthAfterSecond = new FileInfo(savePath).Length;

            Assert.Multiple(() =>
            {
                Assert.That(firstOk, Is.True, firstError);
                Assert.That(secondOk, Is.True, secondError);
                Assert.That(lengthAfterSecond, Is.EqualTo(lengthAfterFirst));
            });
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static ThumbInfo CreateGridThumbInfo()
    {
        return ThumbInfo.FromSheetSpec(
            new ThumbnailSheetSpec
            {
                ThumbWidth = 160,
                ThumbHeight = 120,
                ThumbColumns = 1,
                ThumbRows = 1,
                ThumbCount = 1,
                CaptureSeconds = [1],
            }
        );
    }

    private static string CreateTempDirectory(string prefix)
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
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
