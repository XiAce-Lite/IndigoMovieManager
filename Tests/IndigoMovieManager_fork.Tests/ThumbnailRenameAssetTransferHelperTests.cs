using System.Drawing;
using System.Drawing.Imaging;
using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class ThumbnailRenameAssetTransferHelperTests
{
    [Test]
    public void TryBuildRenamedThumbnailPath_ハッシュ付きjpgは本体名だけ差し替える()
    {
        string result = ThumbnailRenameAssetTransferHelper.TryBuildRenamedThumbnailPath(
            @"C:\thumb\small\old-name.#abc12345.jpg",
            @"C:\movie\old-name.mp4",
            @"C:\movie\new-name.mkv"
        );

        Assert.That(result, Is.EqualTo(@"C:\thumb\small\new-name.#abc12345.jpg"));
    }

    [Test]
    public void TryBuildRenamedThumbnailPath_素のjpgも差し替える()
    {
        string result = ThumbnailRenameAssetTransferHelper.TryBuildRenamedThumbnailPath(
            @"C:\thumb\detail\old-name.jpg",
            @"C:\movie\old-name.mp4",
            @"C:\movie\new-name.mkv"
        );

        Assert.That(result, Is.EqualTo(@"C:\thumb\detail\new-name.jpg"));
    }

    [Test]
    public void RenameThumbnailFiles_表示中サムネと旧命名jpgを新名へ寄せる()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-rename-{Guid.NewGuid():N}");
        string smallDir = Path.Combine(tempRoot, "small");
        string detailDir = Path.Combine(tempRoot, "detail");
        Directory.CreateDirectory(smallDir);
        Directory.CreateDirectory(detailDir);

        try
        {
            string oldMoviePath = @"C:\movie\old-name.mp4";
            string newMoviePath = @"C:\movie\new-name.mp4";
            string sourceSmallPath = Path.Combine(smallDir, "old-name.#abc12345.jpg");
            string sourceDetailPath = Path.Combine(detailDir, "old-name.jpg");
            string ignoredPlaceholderPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");
            CreateJpeg(sourceSmallPath);
            CreateJpeg(sourceDetailPath);
            CreateJpeg(ignoredPlaceholderPath);

            MovieRecords movie = new()
            {
                ThumbPathSmall = sourceSmallPath,
                ThumbDetail = sourceDetailPath,
                ThumbPathBig = ignoredPlaceholderPath,
            };

            ThumbnailRenameAssetTransferHelper.RenameThumbnailFiles(
                movie,
                tempRoot,
                oldMoviePath,
                newMoviePath
            );

            string expectedSmallPath = Path.Combine(smallDir, "new-name.#abc12345.jpg");
            string expectedDetailPath = Path.Combine(detailDir, "new-name.jpg");

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(sourceSmallPath), Is.False);
                Assert.That(File.Exists(sourceDetailPath), Is.False);
                Assert.That(File.Exists(expectedSmallPath), Is.True);
                Assert.That(File.Exists(expectedDetailPath), Is.True);
                Assert.That(movie.ThumbPathSmall, Is.EqualTo(expectedSmallPath));
                Assert.That(movie.ThumbDetail, Is.EqualTo(expectedDetailPath));
                Assert.That(movie.ThumbPathBig, Is.EqualTo(ignoredPlaceholderPath));
            });
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    // 実サムネと同じJPEGを最小サイズで作り、ファイル移送だけを検証しやすくする。
    private static void CreateJpeg(string path)
    {
        using Bitmap bitmap = new(8, 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        bitmap.Save(path, ImageFormat.Jpeg);
    }
}
