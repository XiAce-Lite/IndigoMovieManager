using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ThumbnailCreationServicePublicRequestTests
{
    [Test]
    public async Task CreateThumbAsync_Args入口を通せる()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            string placeholderPath = Path.Combine(tempRoot, "args-no-file.jpg");
            File.WriteAllBytes(placeholderPath, [0x41, 0x42, 0x43, 0x44]);

            var hostRuntime = new TestThumbnailCreationHostRuntime(placeholderPath);
            var service = ThumbnailCreationServiceFactory.Create(hostRuntime);

            ThumbnailCreateResult result = await service.CreateThumbAsync(
                new ThumbnailCreateArgs
                {
                    QueueObj = new QueueObj
                    {
                        MovieId = 901,
                        Tabindex = 1,
                        MovieFullPath = Path.Combine(tempRoot, "missing-args.mp4"),
                        Hash = "args-entry",
                    },
                    DbName = "testdb",
                    ThumbFolder = thumbRoot,
                    IsResizeThumb = true,
                }
            );

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(File.Exists(result.SaveThumbFileName), Is.True);
            Assert.That(
                File.ReadAllBytes(result.SaveThumbFileName),
                Is.EqualTo(File.ReadAllBytes(placeholderPath))
            );
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
    public async Task CreateBookmarkThumbAsync_Args入口を通せる()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            string saveThumbPath = Path.Combine(tempRoot, "bookmark.jpg");
            File.WriteAllBytes(moviePath, [0x01, 0x02, 0x03, 0x04]);

            var bookmarkEngine = new RecordingBookmarkEngine("ffmediatoolkit");
            var service = ThumbnailCreationServiceTestFactory.CreateForTesting(
                bookmarkEngine,
                new RecordingBookmarkEngine("ffmpeg1pass"),
                new RecordingBookmarkEngine("opencv"),
                new RecordingBookmarkEngine("autogen")
            );

            bool created = await service.CreateBookmarkThumbAsync(
                new ThumbnailBookmarkArgs
                {
                    MovieFullPath = moviePath,
                    SaveThumbPath = saveThumbPath,
                    CapturePos = 123,
                }
            );

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.True);
                Assert.That(bookmarkEngine.BookmarkCallCount, Is.EqualTo(1));
                Assert.That(bookmarkEngine.LastMovieFullPath, Is.EqualTo(moviePath));
                Assert.That(bookmarkEngine.LastSaveThumbPath, Is.EqualTo(saveThumbPath));
                Assert.That(bookmarkEngine.LastCapturePos, Is.EqualTo(123));
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

    [Test]
    public void CreateBookmarkThumbAsync_MovieFullPathが空ならArgumentException()
    {
        var service = ThumbnailCreationServiceTestFactory.CreateForTesting(
            new RecordingBookmarkEngine("ffmediatoolkit"),
            new RecordingBookmarkEngine("ffmpeg1pass"),
            new RecordingBookmarkEngine("opencv"),
            new RecordingBookmarkEngine("autogen")
        );

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.CreateBookmarkThumbAsync(
                new ThumbnailBookmarkArgs
                {
                    MovieFullPath = "",
                    SaveThumbPath = @"C:\bookmark.jpg",
                    CapturePos = 123,
                }
            )
        );

        Assert.That(ex!.ParamName, Is.EqualTo("args"));
    }

    [Test]
    public void CreateBookmarkThumbAsync_SaveThumbPathが空ならArgumentException()
    {
        var service = ThumbnailCreationServiceTestFactory.CreateForTesting(
            new RecordingBookmarkEngine("ffmediatoolkit"),
            new RecordingBookmarkEngine("ffmpeg1pass"),
            new RecordingBookmarkEngine("opencv"),
            new RecordingBookmarkEngine("autogen")
        );

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.CreateBookmarkThumbAsync(
                new ThumbnailBookmarkArgs
                {
                    MovieFullPath = @"C:\movie.mp4",
                    SaveThumbPath = "",
                    CapturePos = 123,
                }
            )
        );

        Assert.That(ex!.ParamName, Is.EqualTo("args"));
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

    private sealed class TestThumbnailCreationHostRuntime : IThumbnailCreationHostRuntime
    {
        private readonly string placeholderPath;

        public TestThumbnailCreationHostRuntime(string placeholderPath)
        {
            this.placeholderPath = placeholderPath;
        }

        public string ResolveMissingMoviePlaceholderPath(int tabIndex)
        {
            return placeholderPath;
        }

        public string ResolveProcessLogPath(string fileName)
        {
            return Path.Combine(Path.GetDirectoryName(placeholderPath)!, fileName);
        }
    }

    private sealed class RecordingBookmarkEngine : IThumbnailGenerationEngine
    {
        public RecordingBookmarkEngine(string engineId)
        {
            EngineId = engineId;
            EngineName = engineId;
        }

        public string EngineId { get; }
        public string EngineName { get; }
        public int BookmarkCallCount { get; private set; }
        public string LastMovieFullPath { get; private set; } = "";
        public string LastSaveThumbPath { get; private set; } = "";
        public int LastCapturePos { get; private set; }

        public bool CanHandle(ThumbnailJobContext context)
        {
            return true;
        }

        public Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken cts = default
        )
        {
            return Task.FromResult(
                ThumbnailCreateResultFactory.CreateFailed(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    $"{EngineId} should not create thumbnail"
                )
            );
        }

        public Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            BookmarkCallCount++;
            LastMovieFullPath = movieFullPath;
            LastSaveThumbPath = saveThumbPath;
            LastCapturePos = capturePos;
            return Task.FromResult(true);
        }
    }
}
