using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public class ThumbnailOutputMarkerCoordinatorTests
{
    [Test]
    public void ResetExistingOutputBeforeAutomaticAttempt_既存jpgを消す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string outputPath = Path.Combine(tempRoot, "out.jpg");
            File.WriteAllBytes(outputPath, [0x01, 0x02]);

            ThumbnailOutputMarkerCoordinator.ResetExistingOutputBeforeAutomaticAttempt(outputPath);

            Assert.That(File.Exists(outputPath), Is.False);
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
    public void ApplyFailureMarker_成功jpgが無ければErrorMarkerを作る()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = Path.Combine(tempRoot, "movie.mp4");
            File.WriteAllBytes(moviePath, [0x11, 0x22]);

            string outPath = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(outPath);

            ThumbnailOutputMarkerCoordinator.ApplyFailureMarker(outPath, moviePath, null);

            string markerPath = ThumbnailPathResolver.BuildErrorMarkerPath(outPath, moviePath);
            Assert.That(File.Exists(markerPath), Is.True);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }
}
