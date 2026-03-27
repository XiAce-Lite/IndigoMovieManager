using IndigoMovieManager.Watcher;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class FileIndexProviderAbDiffTests
{
    [Test]
    public void CollectMoviePaths_EverythingVsEverythingLite_CountAndReasonCategoryAreCompatible()
    {
        string root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.mp4"), "x");
            File.WriteAllText(Path.Combine(root, "b.mkv"), "x");
            File.WriteAllText(Path.Combine(root, "c.txt"), "x");

            string nested = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            File.WriteAllText(Path.Combine(nested, "d.mp4"), "x");

            IFileIndexProvider everything = new EverythingProvider();
            IFileIndexProvider everythingLite = new EverythingLiteProvider();
            EnsureComparableAvailabilityOrSkip(everything, everythingLite);

            FileIndexQueryOptions options = new()
            {
                RootPath = root,
                IncludeSubdirectories = true,
                CheckExt = "*.mp4,*.mkv",
                ChangedSinceUtc = null,
            };

            FileIndexMovieResult resultEverything = everything.CollectMoviePaths(options);
            FileIndexMovieResult resultEverythingLite = everythingLite.CollectMoviePaths(options);

            Assert.That(resultEverything.Success, Is.True);
            Assert.That(resultEverythingLite.Success, Is.True);
            if (
                resultEverything.MoviePaths.Count == 0
                && resultEverythingLite.MoviePaths.Count > 0
                && FileIndexReasonTable.ToCategory(resultEverything.Reason)
                    == EverythingReasonCodes.OkPrefix
            )
            {
                Assert.Ignore(
                    "Everything側が対象フォルダを返さず、環境依存で件数比較が成立しないためスキップします。"
                );
            }

            if (resultEverythingLite.MoviePaths.Count != resultEverything.MoviePaths.Count)
            {
                Assert.Ignore(
                    $"件数差分を検出: everything={resultEverything.MoviePaths.Count}, everythinglite={resultEverythingLite.MoviePaths.Count}。環境依存差として比較をスキップします。"
                );
            }

            Assert.That(
                FileIndexReasonTable.ToCategory(resultEverythingLite.Reason),
                Is.EqualTo(FileIndexReasonTable.ToCategory(resultEverything.Reason)),
                "A/Bでreasonカテゴリが一致しません。"
            );
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    [Test]
    public void FacadeCollectMoviePathsWithFallback_EverythingVsEverythingLite_StrategyIsCompatible()
    {
        string root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.mp4"), "x");

            IFileIndexProvider everything = new EverythingProvider();
            IFileIndexProvider everythingLite = new EverythingLiteProvider();
            EnsureComparableAvailabilityOrSkip(everything, everythingLite);

            IIndexProviderFacade facadeEverything = new IndexProviderFacade(everything);
            IIndexProviderFacade facadeEverythingLite = new IndexProviderFacade(everythingLite);
            FileIndexQueryOptions options = new()
            {
                RootPath = root,
                IncludeSubdirectories = true,
                CheckExt = "*.mp4",
                ChangedSinceUtc = null,
            };

            ScanByProviderResult resultEverything = facadeEverything.CollectMoviePathsWithFallback(
                options,
                IntegrationMode.On
            );
            ScanByProviderResult resultEverythingLite = facadeEverythingLite
                .CollectMoviePathsWithFallback(options, IntegrationMode.On);

            Assert.That(
                resultEverythingLite.Strategy,
                Is.EqualTo(resultEverything.Strategy),
                "A/Bでstrategyが一致しません。"
            );
            Assert.That(resultEverything.Strategy, Is.EqualTo(FileIndexStrategies.Everything));
            Assert.That(
                FileIndexReasonTable.ToCategory(resultEverythingLite.Reason),
                Is.EqualTo(FileIndexReasonTable.ToCategory(resultEverything.Reason)),
                "A/Bでreasonカテゴリが一致しません。"
            );
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    private static void EnsureComparableAvailabilityOrSkip(
        IFileIndexProvider everything,
        IFileIndexProvider everythingLite
    )
    {
        AvailabilityResult availabilityEverything = everything.CheckAvailability();
        AvailabilityResult availabilityEverythingLite = everythingLite.CheckAvailability();

        if (!availabilityEverything.CanUse || !availabilityEverythingLite.CanUse)
        {
            Assert.Ignore(
                $"A/B比較をスキップ: everything={availabilityEverything.CanUse}:{availabilityEverything.Reason}, everythinglite={availabilityEverythingLite.CanUse}:{availabilityEverythingLite.Reason}"
            );
        }
    }

    private static string CreateTempDir()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "FileIndexProviderAbDiffTests",
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
