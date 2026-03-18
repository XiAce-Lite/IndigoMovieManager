using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailFileNameLookupTests
{
    [Test]
    public void BuildThumbnailFileNameLookup_jpgだけを大小文字無視で保持する()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "sample.#abc.jpg"), "jpg");
            File.WriteAllText(Path.Combine(tempRoot, "ignore.txt"), "txt");

            HashSet<string> lookup = MainWindow.BuildThumbnailFileNameLookup(tempRoot);

            Assert.Multiple(() =>
            {
                Assert.That(lookup.Contains("sample.#abc.jpg"), Is.True);
                Assert.That(lookup.Contains("SAMPLE.#ABC.JPG"), Is.True);
                Assert.That(lookup.Contains("ignore.txt"), Is.False);
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
    public void BuildThumbnailFileNameLookup_ERRORや通常jpgも保持する()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "normal.jpg"), "jpg");
            File.WriteAllText(Path.Combine(tempRoot, "movie.#ERROR.jpg"), "err");

            HashSet<string> lookup = MainWindow.BuildThumbnailFileNameLookup(tempRoot);

            Assert.Multiple(() =>
            {
                Assert.That(lookup.Contains("normal.jpg"), Is.True);
                Assert.That(lookup.Contains("movie.#ERROR.jpg"), Is.True);
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
}
