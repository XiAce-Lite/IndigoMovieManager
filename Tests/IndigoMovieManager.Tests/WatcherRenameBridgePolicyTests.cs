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
}
