using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class TabInfoTests
{
    [Test]
    public void ResolveRuntimeThumbRoot_明示設定があればそのまま使う()
    {
        string resolved = TabInfo.ResolveRuntimeThumbRoot(
            @"C:\db\anime.wb",
            "anime",
            @"D:\thumbs\anime"
        );

        Assert.That(resolved, Is.EqualTo(@"D:\thumbs\anime"));
    }

    [Test]
    public void ResolveRuntimeThumbRoot_WhiteBrowser同居DBはthum配下を既定にする()
    {
        string whiteBrowserRoot = CreateTempDirectory("imm-tabinfo-whitebrowser");
        string mainDbPath = Path.Combine(whiteBrowserRoot, "maimai.wb");
        string whiteBrowserExePath = Path.Combine(whiteBrowserRoot, "WhiteBrowser.exe");

        try
        {
            File.WriteAllText(whiteBrowserExePath, "wb");
            File.WriteAllText(mainDbPath, "db");

            string resolved = TabInfo.ResolveRuntimeThumbRoot(mainDbPath, "maimai");

            Assert.That(resolved, Is.EqualTo(Path.Combine(whiteBrowserRoot, "thum", "maimai")));
        }
        finally
        {
            TryDeleteDirectory(whiteBrowserRoot);
        }
    }

    [Test]
    public void ResolveRuntimeThumbRoot_通常DBは現行のexe配下Thumbへ戻す()
    {
        string resolved = TabInfo.ResolveRuntimeThumbRoot(@"D:\movie\anime.wb", "anime");

        Assert.That(resolved, Is.EqualTo(TabInfo.GetDefaultThumbRoot("anime")));
    }

    private static string CreateTempDirectory(string prefix)
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
            // 一時ディレクトリ削除失敗はテスト後始末より優先しない。
        }
    }
}
