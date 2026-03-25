using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailRequestArgumentValidatorTests
{
    [Test]
    public void ValidateCreateArgs_nullはArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ThumbnailRequestArgumentValidator.ValidateCreateArgs(null!)
        );
    }

    [Test]
    public void ValidateCreateArgs_入口欠落はArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            ThumbnailRequestArgumentValidator.ValidateCreateArgs(new ThumbnailCreateArgs())
        )!;

        Assert.That(ex.ParamName, Is.EqualTo("args"));
    }

    [Test]
    public void ValidateCreateArgs_MovieFullPath欠落はArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            ThumbnailRequestArgumentValidator.ValidateCreateArgs(
                new ThumbnailCreateArgs
                {
                    Request = new ThumbnailRequest { MovieFullPath = "" },
                    DbName = "db",
                    ThumbFolder = @"C:\thumbs",
                }
            )
        )!;

        Assert.That(ex.ParamName, Is.EqualTo("args"));
    }

    [Test]
    public void ValidateCreateArgs_QueueObj経由でMovieFullPathがあれば通る()
    {
        Assert.DoesNotThrow(() =>
            ThumbnailRequestArgumentValidator.ValidateCreateArgs(
                new ThumbnailCreateArgs
                {
                    QueueObj = new QueueObj { MovieFullPath = @"C:\movie.mp4" },
                    DbName = "db",
                    ThumbFolder = @"C:\thumbs",
                }
            )
        );
    }

    [Test]
    public void ValidateBookmarkArgs_nullはArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ThumbnailRequestArgumentValidator.ValidateBookmarkArgs(null!)
        );
    }

    [Test]
    public void ValidateBookmarkArgs_MovieFullPath欠落はArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            ThumbnailRequestArgumentValidator.ValidateBookmarkArgs(
                new ThumbnailBookmarkArgs
                {
                    MovieFullPath = "",
                    SaveThumbPath = @"C:\thumb.jpg",
                }
            )
        )!;

        Assert.That(ex.ParamName, Is.EqualTo("args"));
    }

    [Test]
    public void ValidateBookmarkArgs_SaveThumbPath欠落はArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            ThumbnailRequestArgumentValidator.ValidateBookmarkArgs(
                new ThumbnailBookmarkArgs
                {
                    MovieFullPath = @"C:\movie.mp4",
                    SaveThumbPath = "",
                }
            )
        )!;

        Assert.That(ex.ParamName, Is.EqualTo("args"));
    }

    [Test]
    public void ValidateBookmarkArgs_必須項目が揃えば通る()
    {
        Assert.DoesNotThrow(() =>
            ThumbnailRequestArgumentValidator.ValidateBookmarkArgs(
                new ThumbnailBookmarkArgs
                {
                    MovieFullPath = @"C:\movie.mp4",
                    SaveThumbPath = @"C:\thumb.jpg",
                    CapturePos = 123,
                }
            )
        );
    }
}
