using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowDeleteActionTests
{
    [Test]
    public void TryDeleteThumbnailFile_使用中ファイルはfalseで返して落ちない()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string targetPath = Path.Combine(tempRoot, "locked-thumb.jpg");
        File.WriteAllText(targetPath, "lock");

        try
        {
            using FileStream lockStream = new(
                targetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None
            );

            bool deleted = MainWindow.TryDeleteThumbnailFile(
                targetPath,
                sendToRecycleBin: false,
                out string failureReason
            );

            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.False);
                Assert.That(failureReason, Is.Not.Empty);
                Assert.That(File.Exists(targetPath), Is.True);
            });
        }
        finally
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void TryDeleteThumbnailFile_解放後ファイルは削除できる()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"imm-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string targetPath = Path.Combine(tempRoot, "thumb.jpg");
        File.WriteAllText(targetPath, "delete");

        try
        {
            bool deleted = MainWindow.TryDeleteThumbnailFile(
                targetPath,
                sendToRecycleBin: false,
                out string failureReason
            );

            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.True);
                Assert.That(failureReason, Is.Empty);
                Assert.That(File.Exists(targetPath), Is.False);
            });
        }
        finally
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
