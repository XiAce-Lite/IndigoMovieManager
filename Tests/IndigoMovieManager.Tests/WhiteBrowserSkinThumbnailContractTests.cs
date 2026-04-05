using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using IndigoMovieManager.Skin.Runtime;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinThumbnailContractTests
{
    [Test]
    public void DbIdentity_正規化されたDBパスから安定したrecordKeyを作れる()
    {
        string baseDir = CreateTempDirectory("imm-webview2-contract-db");
        string dbPathA = Path.Combine(baseDir, "db", "..", "main.wb");
        string dbPathB =
            $"\"{Path.Combine(baseDir, "main.wb").Replace('\\', '/').ToUpperInvariant()}\"";

        try
        {
            string dbIdentityA = WhiteBrowserSkinThumbnailContractService.BuildDbIdentity(dbPathA);
            string dbIdentityB = WhiteBrowserSkinThumbnailContractService.BuildDbIdentity(dbPathB);
            string recordKeyA = WhiteBrowserSkinThumbnailContractService.BuildRecordKey(
                dbIdentityA,
                123
            );
            string recordKeyB = WhiteBrowserSkinThumbnailContractService.BuildRecordKey(
                dbIdentityB,
                123
            );

            Assert.Multiple(() =>
            {
                Assert.That(dbIdentityA, Is.Not.Empty);
                Assert.That(dbIdentityA, Is.EqualTo(dbIdentityB));
                Assert.That(recordKeyA, Is.EqualTo(recordKeyB));
                Assert.That(recordKeyA, Is.EqualTo($"{dbIdentityA}:123"));
            });
        }
        finally
        {
            TryDeleteDirectory(baseDir);
        }
    }

    [Test]
    public void ThumbUrl_managed配下はthum_local相対パスへrev付きで変換できる()
    {
        string thumbRoot = Path.Combine(Path.GetTempPath(), "imm-thumb-root");
        string thumbPath = Path.Combine(thumbRoot, "Small", "movie.#abc123.jpg");

        string actual = WhiteBrowserSkinThumbnailUrlCodec.BuildThumbUrl(
            thumbPath,
            thumbRoot,
            "rev-token"
        );

        Assert.That(
            actual,
            Is.EqualTo("https://thum.local/Small/movie.%23abc123.jpg?rev=rev-token")
        );
    }

    [Test]
    public void ThumbUrl_external配下はthum_local_externalルートへrev付きで変換できる()
    {
        string thumbUrl = WhiteBrowserSkinThumbnailUrlCodec.BuildThumbUrl(
            @"C:\videos\movie.jpg",
            @"C:\thumb",
            "7"
        );

        Assert.Multiple(() =>
        {
            Assert.That(thumbUrl, Does.StartWith("https://thum.local/__external/"));
            Assert.That(thumbUrl, Does.Contain("/movie.jpg"));
            Assert.That(thumbUrl, Does.EndWith("?rev=7"));
        });
    }

    [Test]
    public void ThumbUrl_external配下はURLから元パスへ戻せる()
    {
        string originalPath = @"C:\videos\covers\movie.jpg";
        string thumbUrl = WhiteBrowserSkinThumbnailUrlCodec.BuildThumbUrl(
            originalPath,
            @"C:\thumb",
            "rev-token"
        );

        bool ok = WhiteBrowserSkinThumbnailUrlCodec.TryResolveThumbPath(
            thumbUrl,
            @"C:\thumb",
            out string resolvedPath
        );

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(resolvedPath, Is.EqualTo(originalPath));
        });
    }

    [Test]
    public void ThumbUrl_managedTraversalは解決拒否する()
    {
        bool ok = WhiteBrowserSkinThumbnailUrlCodec.TryResolveThumbPath(
            "https://thum.local/..%2F..%2FWindows%2Fwin.ini?rev=1",
            @"C:\thumb",
            out string resolvedPath
        );

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(resolvedPath, Is.EqualTo(""));
        });
    }

    [Test]
    public void ImportedMarker_管理サムネをsource_image_importedとして固定できる()
    {
        string tempRoot = CreateTempDirectory("imm-webview2-contract-import-marker");
        string thumbPath = Path.Combine(tempRoot, "movie.#hash.jpg");

        try
        {
            CreateSampleJpeg(thumbPath);
            ThumbnailSourceImageImportMarkerHelper.Synchronize(thumbPath, true);

            Assert.That(ThumbnailSourceImageImportMarkerHelper.HasMarker(thumbPath), Is.True);

            ThumbnailSourceImageImportMarkerHelper.Synchronize(thumbPath, false);

            Assert.That(ThumbnailSourceImageImportMarkerHelper.HasMarker(thumbPath), Is.False);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void WBメタ付きJpeg_寸法と列行を復元できる()
    {
        string tempRoot = CreateTempDirectory("imm-webview2-contract-thumbinfo");
        string jpgPath = Path.Combine(tempRoot, "sample.jpg");
        ThumbnailSheetSpec expected = new()
        {
            ThumbCount = 4,
            ThumbWidth = 160,
            ThumbHeight = 90,
            ThumbColumns = 2,
            ThumbRows = 2,
            CaptureSeconds = [10, 20, 30, 40],
        };

        try
        {
            CreateSampleJpeg(jpgPath);
            WhiteBrowserThumbInfoSerializer.AppendToJpeg(jpgPath, expected);

            ThumbInfo actual = new();
            actual.GetThumbInfo(jpgPath);

            Assert.Multiple(() =>
            {
                Assert.That(actual.IsThumbnail, Is.True);
                Assert.That(actual.TotalWidth, Is.EqualTo(320));
                Assert.That(actual.TotalHeight, Is.EqualTo(180));
                Assert.That(actual.ThumbColumns, Is.EqualTo(expected.ThumbColumns));
                Assert.That(actual.ThumbRows, Is.EqualTo(expected.ThumbRows));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void PlaceholderKind_エラー画像と欠損ファイルを分ける()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                ThumbnailErrorPlaceholderHelper.IsPlaceholderPath(@"C:\app\Images\errorGrid.jpg"),
                Is.True
            );
            Assert.That(
                ThumbnailErrorPlaceholderHelper.IsPlaceholderPath(@"C:\thumb\movie.#ERROR.jpg"),
                Is.False
            );
            Assert.That(File.Exists(@"C:\this\path\should\not\exist\missing.jpg"), Is.False);
        });
    }

    private static void CreateSampleJpeg(string jpgPath)
    {
        using Bitmap bitmap = new(320, 240);
        using Graphics graphics = Graphics.FromImage(bitmap);

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
            // 後始末失敗は本体の契約確認より優先しない。
        }
    }
}
