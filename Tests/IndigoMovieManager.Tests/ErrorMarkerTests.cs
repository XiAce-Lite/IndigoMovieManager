using IndigoMovieManager.Thumbnail;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;

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

    [Test]
    public void RememberSuccessThumbnailPath_既存キャッシュへ保存成功を即反映する()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-marker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string firstSuccessPath = Path.Combine(tempRoot, "movie1.#abc12345.jpg");
            CreateJpeg(firstSuccessPath);

            bool warmupResult = ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                tempRoot,
                "movie1.mp4",
                out _
            );

            Assert.That(warmupResult, Is.True);

            string secondSuccessPath = Path.Combine(tempRoot, "movie2.#def67890.jpg");
            CreateJpeg(secondSuccessPath);
            ThumbnailPathResolver.RememberSuccessThumbnailPath(secondSuccessPath);

            bool result = ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                tempRoot,
                "movie2.mp4",
                out string resolvedPath
            );

            Assert.That(result, Is.True);
            Assert.That(resolvedPath, Is.EqualTo(secondSuccessPath));
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
    public void TryFindExistingSuccessThumbnailPath_古いキャッシュを返した後に背景更新で追いつく()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-marker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string firstSuccessPath = Path.Combine(tempRoot, "movie1.#abc12345.jpg");
            CreateJpeg(firstSuccessPath);

            bool warmupResult = ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                tempRoot,
                "movie1.mp4",
                out _
            );

            Assert.That(warmupResult, Is.True);

            Thread.Sleep(TimeSpan.FromMilliseconds(1100));

            string secondSuccessPath = Path.Combine(tempRoot, "movie2.#def67890.jpg");
            CreateJpeg(secondSuccessPath);
            Directory.SetLastWriteTimeUtc(tempRoot, DateTime.UtcNow.AddSeconds(2));

            bool firstLookupResult = ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                tempRoot,
                "movie2.mp4",
                out string firstResolvedPath
            );

            Assert.That(firstLookupResult, Is.False);
            Assert.That(firstResolvedPath, Is.Empty);

            bool backgroundRefreshObserved = SpinWait.SpinUntil(
                () =>
                    ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                        tempRoot,
                        "movie2.mp4",
                        out _
                    ),
                TimeSpan.FromSeconds(3)
            );

            Assert.That(backgroundRefreshObserved, Is.True);

            bool secondLookupResult = ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                tempRoot,
                "movie2.mp4",
                out string resolvedPath
            );

            Assert.That(secondLookupResult, Is.True);
            Assert.That(resolvedPath, Is.EqualTo(secondSuccessPath));
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
    public void PrewarmSuccessThumbnailPathIndex_初回参照前に背景構築できる()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-marker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string successPath = Path.Combine(tempRoot, "movie3.#xyz99999.jpg");
            CreateJpeg(successPath);

            ThumbnailPathResolver.PrewarmSuccessThumbnailPathIndex(tempRoot);

            bool cacheObserved = SpinWait.SpinUntil(
                () => ThumbnailPathResolver.HasCachedSuccessThumbnailPathIndex(tempRoot),
                TimeSpan.FromSeconds(3)
            );

            Assert.That(cacheObserved, Is.True);

            bool result = ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                tempRoot,
                "movie3.mp4",
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

    // 実ファイルを作って、0byte除外ロジックに引っかからない成功jpgを用意する。
    private static void CreateJpeg(string thumbnailPath)
    {
        using Bitmap bmp = new(8, 8);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        bmp.Save(thumbnailPath, ImageFormat.Jpeg);
    }
}
