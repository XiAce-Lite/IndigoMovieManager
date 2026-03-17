using System.Drawing;
using System.Drawing.Imaging;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailJobContextBuilderTests
{
    [Test]
    public void Build_auto生成ならbitrateとcodecを埋めたcontextを返す()
    {
        ThumbnailMovieMetaResolver resolver = new(new FakeVideoMetadataProvider("vp9"));
        ThumbnailJobContextBuilder builder = new(resolver);

        ThumbnailJobContextBuildOutcome actual = builder.Build(
            new ThumbnailJobContextBuildRequest
            {
                Request = new ThumbnailRequest
                {
                    MovieId = 1,
                    TabIndex = 0,
                    MovieFullPath = @"C:\movies\sample.mp4",
                    Hash = "hash1",
                },
                LayoutProfile = ThumbnailLayoutProfileResolver.Small,
                ThumbnailOutPath = @"C:\thumb",
                MovieFullPath = @"C:\movies\sample.mp4",
                SourceMovieFullPath = @"C:\movies\sample.mp4",
                SaveThumbFileName = @"C:\thumb\sample.jpg",
                IsResizeThumb = true,
                IsManual = false,
                DurationSec = 20,
                FileSizeBytes = 8_000_000,
                InitialEngineHint = " auto ",
            }
        );

        Assert.That(actual.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(actual.Context.VideoCodec, Is.EqualTo("vp9"));
            Assert.That(actual.Context.AverageBitrateMbps, Is.EqualTo(3.2d).Within(0.001d));
            Assert.That(actual.Context.InitialEngineHint, Is.EqualTo("auto"));
            Assert.That(actual.Context.ThumbInfo, Is.Not.Null);
            Assert.That(actual.Context.ThumbInfo.IsThumbnail, Is.True);
            Assert.That(actual.Context.ThumbInfo.ThumbSec.Count, Is.EqualTo(3));
        });
    }

    [Test]
    public void Build_manual生成なら既存ThumbInfoを読んで指定panel秒を更新する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string jpgPath = Path.Combine(tempRoot, "manual.jpg");
            CreateSolidJpeg(jpgPath, Color.White);
            ThumbInfo original = ThumbnailAutoThumbInfoBuilder.Build(
                ThumbnailLayoutProfileResolver.Small,
                120
            );
            WhiteBrowserThumbInfoSerializer.AppendToJpeg(jpgPath, original.ToSheetSpec());

            ThumbnailMovieMetaResolver resolver = new(new FakeVideoMetadataProvider("h264"));
            ThumbnailJobContextBuilder builder = new(resolver);

            ThumbnailJobContextBuildOutcome actual = builder.Build(
                new ThumbnailJobContextBuildRequest
                {
                    Request = new ThumbnailRequest
                    {
                        MovieId = 2,
                        TabIndex = 0,
                        MovieFullPath = Path.Combine(tempRoot, "movie.mp4"),
                        Hash = "hash2",
                        ThumbPanelPosition = 1,
                        ThumbTimePosition = 77,
                    },
                    LayoutProfile = ThumbnailLayoutProfileResolver.Small,
                    ThumbnailOutPath = tempRoot,
                    MovieFullPath = Path.Combine(tempRoot, "movie.mp4"),
                    SourceMovieFullPath = Path.Combine(tempRoot, "movie.mp4"),
                    SaveThumbFileName = jpgPath,
                    IsResizeThumb = true,
                    IsManual = true,
                    DurationSec = 120,
                    FileSizeBytes = 1024,
                }
            );

            Assert.That(actual.IsSuccess, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(actual.Context.ThumbInfo.IsThumbnail, Is.True);
                Assert.That(actual.Context.ThumbInfo.ThumbSec[1], Is.EqualTo(77));
                Assert.That(actual.Context.VideoCodec, Is.EqualTo("h264"));
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public void Build_manual生成でWB互換メタが無ければ失敗を返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string jpgPath = Path.Combine(tempRoot, "plain.jpg");
            CreateSolidJpeg(jpgPath, Color.White);

            ThumbnailMovieMetaResolver resolver = new(new FakeVideoMetadataProvider(""));
            ThumbnailJobContextBuilder builder = new(resolver);

            ThumbnailJobContextBuildOutcome actual = builder.Build(
                new ThumbnailJobContextBuildRequest
                {
                    Request = new ThumbnailRequest
                    {
                        MovieId = 3,
                        TabIndex = 0,
                        MovieFullPath = Path.Combine(tempRoot, "movie.mp4"),
                        Hash = "hash3",
                    },
                    LayoutProfile = ThumbnailLayoutProfileResolver.Small,
                    ThumbnailOutPath = tempRoot,
                    MovieFullPath = Path.Combine(tempRoot, "movie.mp4"),
                    SourceMovieFullPath = Path.Combine(tempRoot, "movie.mp4"),
                    SaveThumbFileName = jpgPath,
                    IsResizeThumb = true,
                    IsManual = true,
                    DurationSec = 60,
                    FileSizeBytes = 1024,
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(actual.IsSuccess, Is.False);
                Assert.That(
                    actual.ErrorMessage,
                    Is.EqualTo("manual source thumbnail metadata is missing")
                );
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void CreateSolidJpeg(string filePath, Color color)
    {
        using Bitmap bitmap = new(32, 24);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(filePath, ImageFormat.Jpeg);
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_tests",
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

    private sealed class FakeVideoMetadataProvider : IVideoMetadataProvider
    {
        private readonly string codec;

        public FakeVideoMetadataProvider(string codec)
        {
            this.codec = codec;
        }

        public bool TryGetVideoCodec(string moviePath, out string codec)
        {
            codec = this.codec;
            return !string.IsNullOrWhiteSpace(codec);
        }

        public bool TryGetDurationSec(string moviePath, out double durationSec)
        {
            durationSec = 0;
            return false;
        }
    }
}
