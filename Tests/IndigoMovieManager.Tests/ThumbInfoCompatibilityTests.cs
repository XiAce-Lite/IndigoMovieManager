using System.Drawing;
using System.Drawing.Imaging;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbInfoCompatibilityTests
{
    [Test]
    public void NewThumbInfo_秒数配列と終端マーカーをWB互換で構築する()
    {
        ThumbInfo thumbInfo = CreateSampleThumbInfo();

        thumbInfo.NewThumbInfo();

        Assert.Multiple(() =>
        {
            Assert.That(thumbInfo.SecBuffer.Length, Is.EqualTo((thumbInfo.ThumbSec.Count * 4) + 4));
            Assert.That(
                BitConverter.ToString(thumbInfo.SecBuffer[^4..]),
                Is.EqualTo("2D-4D-54-53")
            );
            Assert.That(BitConverter.ToInt32(thumbInfo.InfoBuffer, 0), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(thumbInfo.InfoBuffer, 12), Is.EqualTo(120));
            Assert.That(BitConverter.ToInt32(thumbInfo.InfoBuffer, 16), Is.EqualTo(90));
            Assert.That(BitConverter.ToInt32(thumbInfo.InfoBuffer, 20), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(thumbInfo.InfoBuffer, 24), Is.EqualTo(1));
        });
    }

    [Test]
    public void ThumbnailSheetSpec_ThumbInfoから独立コピーを作れる()
    {
        ThumbInfo thumbInfo = CreateSampleThumbInfo();

        ThumbnailSheetSpec spec = ThumbnailSheetSpec.FromThumbInfo(thumbInfo);
        thumbInfo.ThumbSec[0] = 999;

        Assert.Multiple(() =>
        {
            Assert.That(spec.ThumbCount, Is.EqualTo(3));
            Assert.That(spec.ThumbWidth, Is.EqualTo(120));
            Assert.That(spec.ThumbHeight, Is.EqualTo(90));
            Assert.That(spec.ThumbColumns, Is.EqualTo(3));
            Assert.That(spec.ThumbRows, Is.EqualTo(1));
            Assert.That(spec.CaptureSeconds, Is.EqualTo(new[] { 12, 34, 56 }));
        });
    }

    [Test]
    public void GetThumbInfo_WB互換メタを付けたJpegから復元できる()
    {
        string tempDir = CreateTempDirectory("imm-thumbinfo-roundtrip");
        string jpgPath = Path.Combine(tempDir, "sample.jpg");
        ThumbInfo expected = CreateSampleThumbInfo();

        try
        {
            expected.NewThumbInfo();

            // JPG末尾へWB互換メタを追記し、実ファイル経由の復元経路を固定する。
            CreateSampleJpeg(jpgPath);
            WhiteBrowserThumbInfoSerializer.AppendToJpeg(jpgPath, expected.ToSheetSpec());

            ThumbInfo actual = new();
            actual.GetThumbInfo(jpgPath);

            Assert.Multiple(() =>
            {
                Assert.That(actual.IsThumbnail, Is.True);
                Assert.That(actual.ThumbCounts, Is.EqualTo(expected.ThumbCounts));
                Assert.That(actual.ThumbWidth, Is.EqualTo(expected.ThumbWidth));
                Assert.That(actual.ThumbHeight, Is.EqualTo(expected.ThumbHeight));
                Assert.That(actual.ThumbColumns, Is.EqualTo(expected.ThumbColumns));
                Assert.That(actual.ThumbRows, Is.EqualTo(expected.ThumbRows));
                Assert.That(actual.ThumbSec, Is.EqualTo(expected.ThumbSec));
            });
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public void WhiteBrowserThumbInfoSerializer_Jpeg末尾メタから仕様へ復元できる()
    {
        string tempDir = CreateTempDirectory("imm-thumbinfo-serializer");
        string jpgPath = Path.Combine(tempDir, "serializer.jpg");
        ThumbnailSheetSpec expected = CreateSampleThumbInfo().ToSheetSpec();

        try
        {
            CreateSampleJpeg(jpgPath);
            WhiteBrowserThumbInfoSerializer.AppendToJpeg(jpgPath, expected);

            bool ok = WhiteBrowserThumbInfoSerializer.TryReadFromJpeg(
                jpgPath,
                out ThumbnailSheetSpec actual
            );

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True);
                Assert.That(actual, Is.Not.Null);
                Assert.That(actual.ThumbCount, Is.EqualTo(expected.ThumbCount));
                Assert.That(actual.ThumbWidth, Is.EqualTo(expected.ThumbWidth));
                Assert.That(actual.ThumbHeight, Is.EqualTo(expected.ThumbHeight));
                Assert.That(actual.ThumbColumns, Is.EqualTo(expected.ThumbColumns));
                Assert.That(actual.ThumbRows, Is.EqualTo(expected.ThumbRows));
                Assert.That(actual.CaptureSeconds, Is.EqualTo(expected.CaptureSeconds));
            });
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public void GetThumbInfo_末尾メタが無い通常Jpegはサムネ扱いしない()
    {
        string tempDir = CreateTempDirectory("imm-thumbinfo-plain");
        string jpgPath = Path.Combine(tempDir, "plain.jpg");

        try
        {
            CreateSampleJpeg(jpgPath);

            ThumbInfo actual = new();
            actual.GetThumbInfo(jpgPath);

            Assert.Multiple(() =>
            {
                Assert.That(actual.IsThumbnail, Is.False);
                Assert.That(actual.ThumbSec, Is.Empty);
                Assert.That(actual.ThumbCounts, Is.EqualTo(1));
            });
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static ThumbInfo CreateSampleThumbInfo()
    {
        ThumbInfo thumbInfo = new()
        {
            ThumbCounts = 3,
            ThumbWidth = 120,
            ThumbHeight = 90,
            ThumbColumns = 3,
            ThumbRows = 1,
        };
        thumbInfo.Add(12);
        thumbInfo.Add(34);
        thumbInfo.Add(56);
        return thumbInfo;
    }

    private static void CreateSampleJpeg(string jpgPath)
    {
        using Bitmap bitmap = new(320, 240);
        using Graphics graphics = Graphics.FromImage(bitmap);

        // 単色だと圧縮されすぎるので、段階色を入れてサイズを安定させる。
        for (int y = 0; y < bitmap.Height; y++)
        {
            using Pen pen = new(Color.FromArgb((y * 3) % 255, (y * 5) % 255, (y * 7) % 255));
            graphics.DrawLine(pen, 0, y, bitmap.Width - 1, y);
        }

        bitmap.Save(jpgPath, ImageFormat.Jpeg);
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
            // 一時ディレクトリ削除失敗はテスト後始末より優先しない。
        }
    }
}
