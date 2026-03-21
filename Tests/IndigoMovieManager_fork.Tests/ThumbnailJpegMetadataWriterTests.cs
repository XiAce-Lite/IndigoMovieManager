using System.Drawing;
using System.Drawing.Imaging;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

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

    [Test]
    public void TryEnsureThumbInfoMetadata_thumbInfoがnullならfalseを返す()
    {
        string tempDir = CreateTempDirectory("imm-jpeg-meta-null-ensure");
        string savePath = Path.Combine(tempDir, "sample.jpg");

        try
        {
            using Bitmap bitmap = new(160, 120);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            bitmap.Save(savePath, ImageFormat.Jpeg);

            bool ok = ThumbnailJpegMetadataWriter.TryEnsureThumbInfoMetadata(
                savePath,
                null,
                out string errorMessage
            );

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.False);
                Assert.That(errorMessage, Is.EqualTo("thumb info is null"));
                Assert.That(File.Exists(savePath), Is.True);
            });
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public void TrySaveJpegWithThumbInfo_thumbInfoがnullならfalseで不完全jpgを残さない()
    {
        string tempDir = CreateTempDirectory("imm-jpeg-meta-null-save");
        string savePath = Path.Combine(tempDir, "sample.jpg");

        try
        {
            using Bitmap bitmap = new(160, 120);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);

            bool ok = ThumbnailJpegMetadataWriter.TrySaveJpegWithThumbInfo(
                bitmap,
                savePath,
                null,
                out string errorMessage
            );

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.False);
                Assert.That(errorMessage, Is.EqualTo("thumb info is null"));
                Assert.That(File.Exists(savePath), Is.False);
            });
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public void TryEnsureThumbInfoMetadata_既存メタが不一致でも再追記で修復後はサイズ増加が止まる()
    {
        string tempDir = CreateTempDirectory("imm-jpeg-meta-repair");
        string savePath = Path.Combine(tempDir, "sample.jpg");

        try
        {
            using Bitmap bitmap = new(160, 120);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            bitmap.Save(savePath, ImageFormat.Jpeg);

            // 先に不一致メタを入れて、修復時に末尾へ正しいメタが積み直される流れを作る。
            WhiteBrowserThumbInfoSerializer.AppendToJpeg(savePath, CreateMismatchedSheetSpec());
            long lengthBeforeRepair = new FileInfo(savePath).Length;
            ThumbInfo expectedThumbInfo = CreateGridThumbInfo();

            bool repaired = ThumbnailJpegMetadataWriter.TryEnsureThumbInfoMetadata(
                savePath,
                expectedThumbInfo,
                out string repairError
            );
            long lengthAfterRepair = new FileInfo(savePath).Length;

            bool stable = ThumbnailJpegMetadataWriter.TryEnsureThumbInfoMetadata(
                savePath,
                expectedThumbInfo,
                out string stableError
            );
            long lengthAfterStable = new FileInfo(savePath).Length;

            bool metadataRead = WhiteBrowserThumbInfoSerializer.TryReadFromJpeg(
                savePath,
                out ThumbnailSheetSpec actual
            );

            Assert.Multiple(() =>
            {
                Assert.That(repaired, Is.True, repairError);
                Assert.That(stable, Is.True, stableError);
                Assert.That(metadataRead, Is.True);
                Assert.That(actual.ThumbWidth, Is.EqualTo(160));
                Assert.That(actual.ThumbHeight, Is.EqualTo(120));
                Assert.That(actual.ThumbCount, Is.EqualTo(1));
                Assert.That(actual.CaptureSeconds, Is.EqualTo(new[] { 1 }));
                Assert.That(lengthAfterRepair, Is.GreaterThan(lengthBeforeRepair));
                Assert.That(lengthAfterStable, Is.EqualTo(lengthAfterRepair));
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

    private static ThumbnailSheetSpec CreateMismatchedSheetSpec()
    {
        return new ThumbnailSheetSpec
        {
            ThumbWidth = 80,
            ThumbHeight = 60,
            ThumbColumns = 1,
            ThumbRows = 1,
            ThumbCount = 1,
            CaptureSeconds = [9],
        };
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
