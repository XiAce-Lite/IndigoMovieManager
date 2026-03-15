using IndigoMovieManager.Thumbnail;
using System.Drawing;
using System.Drawing.Imaging;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class ErrorMarkerTests
{
    [Test]
    public void BuildErrorMarkerFileName_ReturnsExpectedFormat()
    {
        // 動画名から「動画名.#ERROR.jpg」が生成されること。
        string result = ThumbnailPathResolver.BuildErrorMarkerFileName("movie1.mp4");
        Assert.That(result, Is.EqualTo("movie1.#ERROR.jpg"));
    }

    [Test]
    public void BuildErrorMarkerFileName_WithPathInput_ExtractsFileNameOnly()
    {
        // フルパスが渡されても拡張子なしのファイル名本体だけ使われること。
        string result = ThumbnailPathResolver.BuildErrorMarkerFileName(
            @"C:\Videos\subfolder\my movie.avi"
        );
        Assert.That(result, Is.EqualTo("my movie.#ERROR.jpg"));
    }

    [Test]
    public void BuildErrorMarkerFileName_EmptyInput_ReturnsMarkerOnly()
    {
        string result = ThumbnailPathResolver.BuildErrorMarkerFileName("");
        Assert.That(result, Is.EqualTo(".#ERROR.jpg"));
    }

    [Test]
    public void BuildErrorMarkerPath_CombinesOutPathAndMarkerFileName()
    {
        string result = ThumbnailPathResolver.BuildErrorMarkerPath(@"C:\Thumbs", "movie1.mp4");
        Assert.That(result, Is.EqualTo(@"C:\Thumbs\movie1.#ERROR.jpg"));
    }

    [Test]
    public void IsErrorMarker_DetectsErrorMarkerFile()
    {
        Assert.That(ThumbnailPathResolver.IsErrorMarker(@"C:\Thumbs\movie1.#ERROR.jpg"), Is.True);
    }

    [Test]
    public void IsErrorMarker_ReturnsFalse_ForNormalThumbnail()
    {
        Assert.That(
            ThumbnailPathResolver.IsErrorMarker(@"C:\Thumbs\movie1.#a1b2c3d4.jpg"),
            Is.False
        );
    }

    [Test]
    public void IsErrorMarker_ReturnsFalse_ForNullOrEmpty()
    {
        Assert.That(ThumbnailPathResolver.IsErrorMarker(null), Is.False);
        Assert.That(ThumbnailPathResolver.IsErrorMarker(""), Is.False);
        Assert.That(ThumbnailPathResolver.IsErrorMarker("   "), Is.False);
    }

    [Test]
    public void IsErrorMarker_CaseInsensitive()
    {
        // 大文字小文字を無視して判定できること。
        Assert.That(ThumbnailPathResolver.IsErrorMarker(@"movie1.#error.jpg"), Is.True);
        Assert.That(ThumbnailPathResolver.IsErrorMarker(@"movie1.#Error.jpg"), Is.True);
    }

    [Test]
    public void TryFindExistingSuccessThumbnailPath_正常jpgがある時はTrueを返す()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-marker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string successPath = Path.Combine(tempRoot, "movie1.#abc12345.jpg");
            using Bitmap bmp = new(8, 8);
            using Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            bmp.Save(successPath, ImageFormat.Jpeg);

            bool result = ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                tempRoot,
                "movie1.mp4",
                out string resolvedPath
            );

            Assert.That(result, Is.True);
            Assert.That(resolvedPath, Is.EqualTo(successPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void TryFindExistingSuccessThumbnailPath_ERRORだけならFalseを返す()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-marker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            File.WriteAllBytes(Path.Combine(tempRoot, "movie1.#ERROR.jpg"), []);

            bool result = ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                tempRoot,
                "movie1.mp4",
                out string resolvedPath
            );

            Assert.That(result, Is.False);
            Assert.That(resolvedPath, Is.Empty);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
