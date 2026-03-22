using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

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
    public void Build_既存登録が末尾セパレータ付きでも重複として扱う()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string existingDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "existing")).FullName;

            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                [existingDirectory],
                [existingDirectory + Path.DirectorySeparatorChar]
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
    public void Build_既存登録が末尾セパレータ無しでも重複として扱う()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string existingDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "existing")).FullName;

            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                [existingDirectory + Path.DirectorySeparatorChar],
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
    public void Build_末尾セパレータ付き単独ドロップでもcanonicalな返却に揃える()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string droppedDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "drop")).FullName;

            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                [droppedDirectory + Path.DirectorySeparatorChar],
                Array.Empty<string>()
            );

            Assert.That(result.DirectoriesToAdd, Is.EqualTo([Path.GetFullPath(droppedDirectory)]));
            Assert.That(result.DuplicateCount, Is.Zero);
            Assert.That(result.InvalidCount, Is.Zero);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void Build_同一ドロップ内の末尾セパレータ差異も重複として集約する()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string droppedDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "drop")).FullName;

            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                [droppedDirectory, droppedDirectory + Path.DirectorySeparatorChar],
                Array.Empty<string>()
            );

            Assert.That(result.DirectoriesToAdd, Is.EqualTo([Path.GetFullPath(droppedDirectory)]));
            Assert.That(result.DuplicateCount, Is.EqualTo(1));
            Assert.That(result.InvalidCount, Is.Zero);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void Build_同一ドロップ内が逆順でもcanonicalな返却に揃える()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string droppedDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "drop")).FullName;

            WatchFolderDropResult result = WatchFolderDropRegistrationPolicy.Build(
                [droppedDirectory + Path.DirectorySeparatorChar, droppedDirectory],
                Array.Empty<string>()
            );

            Assert.That(result.DirectoriesToAdd, Is.EqualTo([Path.GetFullPath(droppedDirectory)]));
            Assert.That(result.DuplicateCount, Is.EqualTo(1));
            Assert.That(result.InvalidCount, Is.Zero);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void CanAccept_登録可能なフォルダを見つけた時点で後続の不正入力を見に行かない()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string directoryPath = Directory.CreateDirectory(Path.Combine(tempRoot, "drop")).FullName;
            string filePath = Path.Combine(tempRoot, "sample.txt");
            File.WriteAllText(filePath, "sample");

            bool result = WatchFolderDropRegistrationPolicy.CanAccept(
                EnumeratePathsThatFailOnThirdAccess(filePath, directoryPath)
            );

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
            "IndigoMovieManager_fork_WatchFolderDropRegistrationPolicyTests",
            Guid.NewGuid().ToString("N")
        );
        return Directory.CreateDirectory(path).FullName;
    }

    private static IEnumerable<string> EnumeratePathsThatFailOnThirdAccess(
        string filePath,
        string directoryPath
    )
    {
        yield return filePath;
        yield return directoryPath;

        // first-hit 後にここへ進んだら、早期 return が壊れている。
        throw new AssertionException("CanAccept が 3 要素目まで列挙しました。");
    }
}
