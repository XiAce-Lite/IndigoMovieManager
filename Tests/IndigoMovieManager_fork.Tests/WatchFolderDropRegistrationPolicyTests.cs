using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WatchFolderDropRegistrationPolicyTests
{
    [Test]
    public void CanAccept_フォルダを1件でも含めばTrueを返す()
    {
        string tempRoot = CreateTempRoot();
        string folderPath = Path.Combine(tempRoot, "watch");
        string filePath = Path.Combine(tempRoot, "sample.txt");

        Directory.CreateDirectory(folderPath);
        File.WriteAllText(filePath, "x");

        try
        {
            bool actual = WatchFolderDropRegistrationPolicy.CanAccept([filePath, folderPath]);

            Assert.That(actual, Is.True);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Test]
    public void Build_既存重複と無効入力を分けて返す()
    {
        string tempRoot = CreateTempRoot();
        string existingFolder = Path.Combine(tempRoot, "existing");
        string newFolder = Path.Combine(tempRoot, "new");
        string filePath = Path.Combine(tempRoot, "sample.txt");

        Directory.CreateDirectory(existingFolder);
        Directory.CreateDirectory(newFolder);
        File.WriteAllText(filePath, "x");

        try
        {
            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                [newFolder, existingFolder, newFolder, filePath, Path.Combine(tempRoot, "missing")],
                [existingFolder + Path.DirectorySeparatorChar]
            );

            Assert.That(result.DirectoriesToAdd, Is.EqualTo([newFolder]));
            Assert.That(result.DuplicateCount, Is.EqualTo(2));
            Assert.That(result.InvalidCount, Is.EqualTo(2));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    private static string CreateTempRoot()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"watch-folder-drop-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private static void DeleteTempRoot(string tempRoot)
    {
        if (!string.IsNullOrWhiteSpace(tempRoot) && Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
