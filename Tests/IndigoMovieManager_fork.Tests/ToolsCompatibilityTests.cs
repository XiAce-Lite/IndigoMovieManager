using System.Drawing;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ToolsCompatibilityTests
{
    [Test]
    public void GetHashCRC32_先頭128KBだけを既存互換形式で返す()
    {
        string tempDir = CreateTempDirectory("imm-tools-crc32");
        string filePath = Path.Combine(tempDir, "movie.bin");

        try
        {
            byte[] content = new byte[200 * 1024];
            for (int i = 0; i < content.Length; i++)
            {
                content[i] = (byte)(i % 251);
            }

            File.WriteAllBytes(filePath, content);

            string actual = Tools.GetHashCRC32(filePath);
            string extracted = MovieHashCalculator.GetHashCrc32(filePath);

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.EqualTo("73edb138"));
                Assert.That(extracted, Is.EqualTo(actual));
            });
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public void GetHashCRC32_存在しないファイルは空文字を返す()
    {
        string actual = Tools.GetHashCRC32(@"C:\missing\movie.bin");
        string extracted = MovieHashCalculator.GetHashCrc32(@"C:\missing\movie.bin");

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(""));
            Assert.That(extracted, Is.EqualTo(actual));
        });
    }

    [Test]
    public void ConvertTagsWithNewLine_重複除去して改行連結する()
    {
        string actual = Tools.ConvertTagsWithNewLine(["anime", "idol", "anime", "live"]);
        string extracted = ThumbnailTagFormatter.ConvertTagsWithNewLine(
            ["anime", "idol", "anime", "live"]
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                actual,
                Is.EqualTo($"anime{Environment.NewLine}idol{Environment.NewLine}live")
            );
            Assert.That(extracted, Is.EqualTo(actual));
        });
    }

    [Test]
    public void ClearTempJpg_temp配下のjpgだけ掃除して他は残す()
    {
        string tempDir = CreateTempDirectory("imm-tools-clear");
        string workingDir = Path.Combine(tempDir, "work");
        string tempSubDir = Path.Combine(workingDir, "temp", "nested");
        string firstJpg = Path.Combine(workingDir, "temp", "old1.jpg");
        string secondJpg = Path.Combine(tempSubDir, "old2.jpg");
        string keepText = Path.Combine(workingDir, "temp", "keep.txt");
        string oldCurrentDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.CreateDirectory(tempSubDir);
            File.WriteAllText(firstJpg, "jpg1");
            File.WriteAllText(secondJpg, "jpg2");
            File.WriteAllText(keepText, "txt");

            Directory.SetCurrentDirectory(workingDir);
            Tools.ClearTempJpg();
            File.WriteAllText(firstJpg, "jpg1");
            File.WriteAllText(secondJpg, "jpg2");
            ThumbnailTempFileCleaner.ClearCurrentWorkingTempJpg();

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(firstJpg), Is.False);
                Assert.That(File.Exists(secondJpg), Is.False);
                Assert.That(File.Exists(keepText), Is.True);
            });
        }
        finally
        {
            Directory.SetCurrentDirectory(oldCurrentDirectory);
            TryDeleteDirectory(tempDir);
        }
    }

    [Test]
    public void ConcatImages_行列順で1枚へ結合する()
    {
        string tempDir = CreateTempDirectory("imm-tools-concat");
        string[] imagePaths =
        [
            Path.Combine(tempDir, "1.png"),
            Path.Combine(tempDir, "2.png"),
            Path.Combine(tempDir, "3.png"),
            Path.Combine(tempDir, "4.png"),
        ];

        try
        {
            CreateSolidPng(imagePaths[0], Color.Red);
            CreateSolidPng(imagePaths[1], Color.Green);
            CreateSolidPng(imagePaths[2], Color.Blue);
            CreateSolidPng(imagePaths[3], Color.Yellow);

            using Bitmap actual = Tools.ConcatImages(imagePaths.ToList(), columns: 2, rows: 2);
            using Bitmap extracted = ThumbnailSheetComposer.ConcatImages(
                imagePaths.ToList(),
                columns: 2,
                rows: 2
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.Width, Is.EqualTo(16));
                Assert.That(actual.Height, Is.EqualTo(12));
                Assert.That(actual.GetPixel(2, 2).ToArgb(), Is.EqualTo(Color.Red.ToArgb()));
                Assert.That(actual.GetPixel(10, 2).ToArgb(), Is.EqualTo(Color.Green.ToArgb()));
                Assert.That(actual.GetPixel(2, 8).ToArgb(), Is.EqualTo(Color.Blue.ToArgb()));
                Assert.That(actual.GetPixel(10, 8).ToArgb(), Is.EqualTo(Color.Yellow.ToArgb()));
                Assert.That(extracted.Width, Is.EqualTo(actual.Width));
                Assert.That(extracted.Height, Is.EqualTo(actual.Height));
                Assert.That(extracted.GetPixel(2, 2).ToArgb(), Is.EqualTo(actual.GetPixel(2, 2).ToArgb()));
                Assert.That(extracted.GetPixel(10, 2).ToArgb(), Is.EqualTo(actual.GetPixel(10, 2).ToArgb()));
                Assert.That(extracted.GetPixel(2, 8).ToArgb(), Is.EqualTo(actual.GetPixel(2, 8).ToArgb()));
                Assert.That(extracted.GetPixel(10, 8).ToArgb(), Is.EqualTo(actual.GetPixel(10, 8).ToArgb()));
            });
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void CreateSolidPng(string filePath, Color color)
    {
        using Bitmap bitmap = new(8, 6);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(filePath);
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
