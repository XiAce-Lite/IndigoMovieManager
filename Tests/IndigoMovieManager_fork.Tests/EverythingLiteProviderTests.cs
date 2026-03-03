using System.Collections.Generic;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class EverythingLiteProviderTests
{
    [SetUp]
    public void SetUp()
    {
        // キャッシュ共有によるケース間干渉を防ぐ。
        EverythingLiteProvider.ClearCacheForTesting();
    }

    [TearDown]
    public void TearDown()
    {
        // テスト後にキャッシュを明示クリアする。
        EverythingLiteProvider.ClearCacheForTesting();
    }

    [Test]
    public void CollectMoviePaths_IncludeSubdirectoriesFalse_ReturnsOnlyDirectChildren()
    {
        string root = CreateTempDir();
        try
        {
            string directMovie = Path.Combine(root, "direct.mp4");
            File.WriteAllText(directMovie, "x");
            File.WriteAllText(Path.Combine(root, "note.txt"), "x");

            string subDir = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            string subMovie = Path.Combine(subDir, "deep.mkv");
            File.WriteAllText(subMovie, "x");

            EverythingLiteProvider provider = new();
            FileIndexMovieResult result = provider.CollectMoviePaths(
                new FileIndexQueryOptions
                {
                    RootPath = root,
                    IncludeSubdirectories = false,
                    CheckExt = "*.mp4,*.mkv",
                    ChangedSinceUtc = null,
                }
            );

            Assert.That(result.Success, Is.True);
            Assert.That(result.MoviePaths.Count, Is.EqualTo(1));
            Assert.That(result.MoviePaths, Does.Contain(directMovie));
            Assert.That(result.MoviePaths, Does.Not.Contain(subMovie));
            Assert.That(
                result.Reason.StartsWith(EverythingReasonCodes.OkPrefix, StringComparison.Ordinal),
                Is.True
            );
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Test]
    public void CollectMoviePaths_IncludeSubdirectoriesTrue_ReturnsNestedMovies()
    {
        string root = CreateTempDir();
        try
        {
            string directMovie = Path.Combine(root, "direct.mp4");
            File.WriteAllText(directMovie, "x");

            string subDir = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            string subMovie = Path.Combine(subDir, "deep.mkv");
            File.WriteAllText(subMovie, "x");

            EverythingLiteProvider provider = new();
            FileIndexMovieResult result = provider.CollectMoviePaths(
                new FileIndexQueryOptions
                {
                    RootPath = root,
                    IncludeSubdirectories = true,
                    CheckExt = "*.mp4,*.mkv",
                    ChangedSinceUtc = null,
                }
            );

            Assert.That(result.Success, Is.True);
            Assert.That(result.MoviePaths.Count, Is.EqualTo(2));
            Assert.That(result.MoviePaths, Does.Contain(directMovie));
            Assert.That(result.MoviePaths, Does.Contain(subMovie));
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Test]
    public void CollectThumbnailBodies_ReturnsBodySet()
    {
        string thumbFolder = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(thumbFolder, "alpha.#abcd.jpg"), "x");
            File.WriteAllText(Path.Combine(thumbFolder, "beta.jpg"), "x");
            File.WriteAllText(Path.Combine(thumbFolder, "ignore.png"), "x");

            EverythingLiteProvider provider = new();
            FileIndexThumbnailBodyResult result = provider.CollectThumbnailBodies(thumbFolder);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Bodies, Does.Contain("alpha"));
            Assert.That(result.Bodies, Does.Contain("beta"));
            Assert.That(result.Bodies.Count, Is.EqualTo(2));
            Assert.That(result.Reason, Is.EqualTo(EverythingReasonCodes.Ok));
        }
        finally
        {
            DeleteTempDir(thumbFolder);
        }
    }

    [Test]
    public void CollectMoviePaths_SecondCallWithinCooldown_UsesCachedIndex()
    {
        string root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "movie.mp4"), "x");

            EverythingLiteProvider provider = new();
            FileIndexQueryOptions options = new()
            {
                RootPath = root,
                IncludeSubdirectories = true,
                CheckExt = "*.mp4",
                ChangedSinceUtc = null,
            };

            FileIndexMovieResult first = provider.CollectMoviePaths(options);
            FileIndexMovieResult second = provider.CollectMoviePaths(options);

            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.True);
            Assert.That(first.Reason.Contains("index=rebuilt", StringComparison.Ordinal), Is.True);
            Assert.That(second.Reason.Contains("index=cached", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Test]
    public void CollectMoviePaths_ManyRoots_CacheEntryCountIsLimited()
    {
        List<string> roots = [];
        try
        {
            EverythingLiteProvider provider = new();
            int target = EverythingLiteProvider.GetCacheCapacityForTesting() + 8;
            for (int i = 0; i < target; i++)
            {
                string root = CreateTempDir();
                roots.Add(root);
                File.WriteAllText(Path.Combine(root, $"movie_{i}.mp4"), "x");

                FileIndexMovieResult result = provider.CollectMoviePaths(
                    new FileIndexQueryOptions
                    {
                        RootPath = root,
                        IncludeSubdirectories = true,
                        CheckExt = "*.mp4",
                        ChangedSinceUtc = null,
                    }
                );
                Assert.That(result.Success, Is.True);
            }

            Assert.That(
                EverythingLiteProvider.GetCacheEntryCountForTesting(),
                Is.LessThanOrEqualTo(EverythingLiteProvider.GetCacheCapacityForTesting())
            );
        }
        finally
        {
            foreach (string root in roots)
            {
                DeleteTempDir(root);
            }
        }
    }

    private static string CreateTempDir()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "EverythingLiteProviderTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDir(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, true);
    }
}
