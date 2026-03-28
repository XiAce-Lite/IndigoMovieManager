using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchFolderDropRegistrationPolicyTests
{
    [Test]
    public void Build_新規フォルダだけを追加候補へ残す()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string existingDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "existing")).FullName;
            string newDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "new")).FullName;

            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                [newDirectory],
                [existingDirectory]
            );

            Assert.That(result.DirectoriesToAdd, Is.EqualTo([Path.GetFullPath(newDirectory)]));
            Assert.That(result.DuplicateCount, Is.Zero);
            Assert.That(result.InvalidCount, Is.Zero);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void Build_重複と非フォルダを件数へ集約する()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string existingDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "existing")).FullName;
            string filePath = Path.Combine(tempRoot, "sample.txt");
            File.WriteAllText(filePath, "sample");

            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                [existingDirectory, filePath, existingDirectory],
                [existingDirectory]
            );

            Assert.That(result.DirectoriesToAdd, Is.Empty);
            Assert.That(result.DuplicateCount, Is.EqualTo(2));
            Assert.That(result.InvalidCount, Is.EqualTo(1));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void Build_既存登録の重複はパス大小文字を吸収して1件として扱う()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string existingDirectory = Directory.CreateDirectory(
                Path.Combine(tempRoot, "existing")
            ).FullName;
            string duplicatedDirectory = existingDirectory.ToUpperInvariant();

            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                [duplicatedDirectory],
                [existingDirectory]
            );

            Assert.That(result.DirectoriesToAdd, Is.Empty);
            Assert.That(result.DuplicateCount, Is.EqualTo(1));
            Assert.That(result.InvalidCount, Is.Zero);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void CanAccept_登録可能なフォルダが1件でもあれば受け付ける()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string directoryPath = Directory.CreateDirectory(Path.Combine(tempRoot, "drop")).FullName;
            string filePath = Path.Combine(tempRoot, "sample.txt");
            File.WriteAllText(filePath, "sample");

            bool result = WatchFolderDropRegistrationPolicy.CanAccept([filePath, directoryPath]);

            Assert.That(result, Is.True);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void NormalizeDirectoryPath_不正パスは空文字へ落とす()
    {
        string result = WatchFolderDropRegistrationPolicy.NormalizeDirectoryPath(" \0 ");

        Assert.That(result, Is.EqualTo(string.Empty));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_WatchFolderDropRegistrationPolicyTests",
            Guid.NewGuid().ToString("N")
        );
        return Directory.CreateDirectory(path).FullName;
    }
}
