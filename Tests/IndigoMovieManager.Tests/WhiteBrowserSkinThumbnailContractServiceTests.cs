using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using IndigoMovieManager;
using IndigoMovieManager.Skin.Runtime;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinThumbnailContractServiceTests
{
    private readonly WhiteBrowserSkinThumbnailContractService service = new();

    [Test]
    public void BuildDbIdentity_表記ゆれを吸収して同じ値になる()
    {
        string left = WhiteBrowserSkinThumbnailContractService.BuildDbIdentity(
            @"C:\data\imm\main.wb"
        );
        string right = WhiteBrowserSkinThumbnailContractService.BuildDbIdentity(
            @"""C:/DATA/IMM/MAIN.wb"""
        );

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.Not.Empty);
            Assert.That(left, Is.EqualTo(right));
        });
    }

    [Test]
    public void BuildRecordKey_dbIdentityとmovieIdをそのまま連結する()
    {
        Assert.That(
            WhiteBrowserSkinThumbnailContractService.BuildRecordKey("abc123", 77),
            Is.EqualTo("abc123:77")
        );
    }

    [Test]
    public void BuildContractValues_空値ケースを明示する()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WhiteBrowserSkinThumbnailContractService.BuildDbIdentity(""), Is.EqualTo(""));
            Assert.That(
                WhiteBrowserSkinThumbnailContractService.BuildRecordKey("", 77),
                Is.EqualTo("")
            );
            Assert.That(
                WhiteBrowserSkinThumbnailContractService.BuildThumbRevision("", "managed-thumbnail"),
                Is.EqualTo("0")
            );
        });
    }

    [Test]
    public void Create_同名画像だけある場合はsource_image_directになる()
    {
        string tempRoot = CreateTempDirectory("imm-wbskin-direct");
        try
        {
            string moviePath = Path.Combine(tempRoot, "cover-target.mp4");
            string sourceImagePath = Path.Combine(tempRoot, "cover-target.png");
            CreateMovieFile(moviePath);
            CreateSampleImage(sourceImagePath, 320, 180, ImageFormat.Png);

            MovieRecords movie = CreateMovieRecord(moviePath, "cover-target", thumbPath: "");
            WhiteBrowserSkinThumbnailContractDto dto = service.Create(
                movie,
                new WhiteBrowserSkinThumbnailResolveContext
                {
                    DbFullPath = Path.Combine(tempRoot, "main.wb"),
                    DisplayTabIndex = 0,
                    SelectedMovieId = movie.Movie_Id,
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(
                    dto.ThumbSourceKind,
                    Is.EqualTo(WhiteBrowserSkinThumbnailSourceKinds.SourceImageDirect)
                );
                Assert.That(dto.ThumbPath, Is.EqualTo(sourceImagePath));
                Assert.That(dto.ThumbUrl, Does.StartWith("https://thum.local/__external/"));
                Assert.That(dto.ThumbUrl, Does.Contain("?rev="));
                Assert.That(
                    dto.ThumbRevision,
                    Is.EqualTo(
                        ComputeExpectedThumbRevision(
                            sourceImagePath,
                            WhiteBrowserSkinThumbnailSourceKinds.SourceImageDirect
                        )
                    )
                );
                Assert.That(dto.ThumbNaturalWidth, Is.EqualTo(320));
                Assert.That(dto.ThumbNaturalHeight, Is.EqualTo(180));
                Assert.That(dto.ThumbSheetColumns, Is.EqualTo(1));
                Assert.That(dto.ThumbSheetRows, Is.EqualTo(1));
                Assert.That(dto.Selected, Is.True);
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void Create_管理サムネと同名画像がある場合はsource_image_importedになる()
    {
        string tempRoot = CreateTempDirectory("imm-wbskin-imported");
        try
        {
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            string moviePath = Path.Combine(tempRoot, "cover-target.mp4");
            string sourceImagePath = Path.Combine(tempRoot, "cover-target.jpg");
            string thumbPath = Path.Combine(thumbRoot, "cover-target.#hash.jpg");
            CreateMovieFile(moviePath);
            CreateSampleImage(thumbPath, 360, 90, ImageFormat.Jpeg);
            WhiteBrowserThumbInfoSerializer.AppendToJpeg(
                thumbPath,
                new ThumbnailSheetSpec
                {
                    ThumbCount = 3,
                    ThumbWidth = 120,
                    ThumbHeight = 90,
                    ThumbColumns = 3,
                    ThumbRows = 1,
                    CaptureSeconds = [12, 34, 56],
                }
            );
            CreateSampleImage(sourceImagePath, 1920, 1080, ImageFormat.Jpeg);
            ThumbnailSourceImageImportMarkerHelper.Synchronize(thumbPath, isSourceImageImported: true);

            MovieRecords movie = CreateMovieRecord(moviePath, "cover-target", thumbPath);
            WhiteBrowserSkinThumbnailContractDto dto = service.Create(
                movie,
                new WhiteBrowserSkinThumbnailResolveContext
                {
                    DbFullPath = Path.Combine(tempRoot, "main.wb"),
                    ManagedThumbnailRootPath = thumbRoot,
                    DisplayTabIndex = 0,
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(
                    dto.ThumbSourceKind,
                    Is.EqualTo(WhiteBrowserSkinThumbnailSourceKinds.SourceImageImported)
                );
                Assert.That(dto.ThumbPath, Is.EqualTo(thumbPath));
                Assert.That(dto.ThumbUrl, Does.StartWith("https://thum.local/"));
                Assert.That(dto.ThumbUrl, Does.Contain("?rev="));
                Assert.That(dto.ThumbNaturalWidth, Is.EqualTo(360));
                Assert.That(dto.ThumbNaturalHeight, Is.EqualTo(90));
                Assert.That(dto.ThumbSheetColumns, Is.EqualTo(3));
                Assert.That(dto.ThumbSheetRows, Is.EqualTo(1));
                Assert.That(
                    dto.ThumbRevision,
                    Is.EqualTo(
                        ComputeExpectedThumbRevision(
                            thumbPath,
                            WhiteBrowserSkinThumbnailSourceKinds.SourceImageImported
                        )
                    )
                );
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void Create_更新された管理サムネはキャッシュ済みでも新しいサイズ情報を返す()
    {
        string tempRoot = CreateTempDirectory("imm-wbskin-cache-refresh");
        try
        {
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            string moviePath = Path.Combine(tempRoot, "cover-target.mp4");
            string thumbPath = Path.Combine(thumbRoot, "cover-target.#hash.jpg");
            CreateMovieFile(moviePath);
            CreateManagedThumbnailWithMetadata(
                thumbPath,
                width: 360,
                height: 90,
                columns: 3,
                rows: 1,
                captureSeconds: [12, 34, 56]
            );

            MovieRecords movie = CreateMovieRecord(moviePath, "cover-target", thumbPath);
            WhiteBrowserSkinThumbnailResolveContext context = new()
            {
                DbFullPath = Path.Combine(tempRoot, "main.wb"),
                ManagedThumbnailRootPath = thumbRoot,
                DisplayTabIndex = 0,
            };

            WhiteBrowserSkinThumbnailContractDto before = service.Create(movie, context);

            Thread.Sleep(20);
            CreateManagedThumbnailWithMetadata(
                thumbPath,
                width: 320,
                height: 240,
                columns: 2,
                rows: 2,
                captureSeconds: [10, 20, 30, 40]
            );

            WhiteBrowserSkinThumbnailContractDto after = service.Create(movie, context);

            Assert.Multiple(() =>
            {
                Assert.That(before.ThumbNaturalWidth, Is.EqualTo(360));
                Assert.That(before.ThumbNaturalHeight, Is.EqualTo(90));
                Assert.That(before.ThumbSheetColumns, Is.EqualTo(3));
                Assert.That(before.ThumbSheetRows, Is.EqualTo(1));
                Assert.That(after.ThumbNaturalWidth, Is.EqualTo(320));
                Assert.That(after.ThumbNaturalHeight, Is.EqualTo(240));
                Assert.That(after.ThumbSheetColumns, Is.EqualTo(2));
                Assert.That(after.ThumbSheetRows, Is.EqualTo(2));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void Create_ERRORマーカーはerror_placeholderになる()
    {
        string tempRoot = CreateTempDirectory("imm-wbskin-error");
        try
        {
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            string moviePath = Path.Combine(tempRoot, "cover-target.mp4");
            string errorMarkerPath = Path.Combine(thumbRoot, "cover-target.#ERROR.jpg");
            CreateMovieFile(moviePath);
            File.WriteAllBytes(errorMarkerPath, [0x01, 0x02, 0x03, 0x04]);

            MovieRecords movie = CreateMovieRecord(moviePath, "cover-target", errorMarkerPath);
            WhiteBrowserSkinThumbnailContractDto dto = service.Create(
                movie,
                new WhiteBrowserSkinThumbnailResolveContext
                {
                    DbFullPath = Path.Combine(tempRoot, "main.wb"),
                    ManagedThumbnailRootPath = thumbRoot,
                    DisplayTabIndex = 0,
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(
                    dto.ThumbSourceKind,
                    Is.EqualTo(WhiteBrowserSkinThumbnailSourceKinds.ErrorPlaceholder)
                );
                Assert.That(dto.ThumbPath, Is.EqualTo(errorMarkerPath));
                Assert.That(
                    dto.ThumbRevision,
                    Is.EqualTo(
                        ComputeExpectedThumbRevision(
                            errorMarkerPath,
                            WhiteBrowserSkinThumbnailSourceKinds.ErrorPlaceholder
                        )
                    )
                );
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void Create_動画もサムネも無いときはmissing_file_placeholderになる()
    {
        string tempRoot = CreateTempDirectory("imm-wbskin-missing");
        try
        {
            string moviePath = Path.Combine(tempRoot, "missing-target.mp4");
            MovieRecords movie = CreateMovieRecord(moviePath, "missing-target", thumbPath: "");
            movie.IsExists = false;

            WhiteBrowserSkinThumbnailContractDto dto = service.Create(
                movie,
                new WhiteBrowserSkinThumbnailResolveContext
                {
                    DbFullPath = Path.Combine(tempRoot, "main.wb"),
                    DisplayTabIndex = 0,
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(
                    dto.ThumbSourceKind,
                    Is.EqualTo(WhiteBrowserSkinThumbnailSourceKinds.MissingFilePlaceholder)
                );
                Assert.That(Path.GetFileName(dto.ThumbPath), Is.EqualTo("noFileSmall.jpg"));
                Assert.That(dto.ThumbUrl, Does.StartWith("https://thum.local/__external/"));
                Assert.That(dto.ThumbUrl, Does.Contain("?rev="));
                Assert.That(dto.ThumbRevision, Is.Not.Null);
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static MovieRecords CreateMovieRecord(
        string moviePath,
        string movieBody,
        string thumbPath
    )
    {
        return new MovieRecords
        {
            Movie_Id = 77,
            Movie_Name = Path.GetFileName(moviePath),
            Movie_Body = movieBody,
            Movie_Path = moviePath,
            Movie_Size = 12345,
            Movie_Length = "00:10:00",
            IsExists = true,
            ThumbPathSmall = thumbPath,
            ThumbPathBig = thumbPath,
            ThumbPathGrid = thumbPath,
            ThumbPathList = thumbPath,
            ThumbPathBig10 = thumbPath,
            ThumbDetail = thumbPath,
        };
    }

    private static void CreateMovieFile(string moviePath)
    {
        File.WriteAllBytes(moviePath, [0x10, 0x20, 0x30, 0x40]);
    }

    private static string ComputeExpectedThumbRevision(string thumbPath, string sourceKind)
    {
        string normalizedPath = NormalizePath(thumbPath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath))
        {
            return "0";
        }

        FileInfo fileInfo = new(normalizedPath);
        string fingerprint =
            $"{sourceKind ?? ""}|{normalizedPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))
            .ToLowerInvariant();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(path.Trim().Trim('"')).Replace('/', '\\').ToLowerInvariant();
        }
        catch
        {
            return path.Trim().Trim('"').Replace('/', '\\').ToLowerInvariant();
        }
    }

    private static void CreateSampleImage(
        string imagePath,
        int width,
        int height,
        ImageFormat format
    )
    {
        using Bitmap bitmap = new(width, height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.SteelBlue);
        graphics.FillRectangle(Brushes.Gold, 0, 0, width / 2, height / 2);
        bitmap.Save(imagePath, format);
    }

    private static void CreateManagedThumbnailWithMetadata(
        string imagePath,
        int width,
        int height,
        int columns,
        int rows,
        int[] captureSeconds
    )
    {
        if (File.Exists(imagePath))
        {
            File.Delete(imagePath);
        }

        CreateSampleImage(imagePath, width, height, ImageFormat.Jpeg);
        WhiteBrowserThumbInfoSerializer.AppendToJpeg(
            imagePath,
            new ThumbnailSheetSpec
            {
                ThumbCount = captureSeconds?.Length ?? 0,
                ThumbWidth = Math.Max(1, width / Math.Max(1, columns)),
                ThumbHeight = Math.Max(1, height / Math.Max(1, rows)),
                ThumbColumns = Math.Max(1, columns),
                ThumbRows = Math.Max(1, rows),
                CaptureSeconds = captureSeconds?.ToList() ?? [],
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
            // 一時ファイルの後始末は失敗しても本質ではない。
        }
    }
}
