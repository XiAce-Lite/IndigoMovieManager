namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatcherRenameBridgePolicyTests
{
    [Test]
    public void IsMoviePathMatchForRename_case違いでも一致として扱う()
    {
        bool actual = MainWindow.IsMoviePathMatchForRename(
            @"C:\Movies\Sample.MP4",
            @"c:\movies\sample.mp4"
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void BuildBookmarkRenameDestinationPath_親フォルダは変えずにファイル名だけ差し替える()
    {
        string actual = MainWindow.BuildBookmarkRenameDestinationPath(
            @"C:\Bookmarks\OldName\Collection\clip_OldName_scene.jpg",
            "OldName",
            "NewName"
        );

        Assert.That(
            actual,
            Is.EqualTo(@"C:\Bookmarks\OldName\Collection\clip_NewName_scene.jpg")
        );
    }

    [Test]
    public void ShouldQueueWatchScanForUntrackedRename_同一パスや未存在ファイルは除外する()
    {
        Assert.That(
            MainWindow.ShouldQueueWatchScanForUntrackedRename(
                @"C:\Movies\same.mp4",
                @"C:\Movies\same.mp4"
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldQueueWatchScanForUntrackedRename(
                @"C:\Movies\missing.mp4",
                @"C:\Movies\before.mp4"
            ),
            Is.False
        );
    }

    [Test]
    public async Task ShouldQueueWatchScanForUntrackedRename_新パスが存在すればtrueを返す()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string newMoviePath = Path.Combine(tempRoot, "after.mp4");
        await File.WriteAllBytesAsync(newMoviePath, [0x1]);

        try
        {
            Assert.That(
                MainWindow.ShouldQueueWatchScanForUntrackedRename(
                    newMoviePath,
                    Path.Combine(tempRoot, "before.mp4")
                ),
                Is.True
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
}
