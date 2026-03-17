using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailLayoutProfileTests
{
    [Test]
    public void Resolve_通常タブ0はSmallレイアウトを返す()
    {
        ThumbnailLayoutProfile layout = ThumbnailLayoutProfileResolver.Resolve(0);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Width, Is.EqualTo(120));
            Assert.That(layout.Height, Is.EqualTo(90));
            Assert.That(layout.Columns, Is.EqualTo(3));
            Assert.That(layout.Rows, Is.EqualTo(1));
            Assert.That(layout.DivCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void Resolve_詳細タブ標準モードは160x120x1x1を返す()
    {
        ThumbnailLayoutProfile layout = ThumbnailLayoutProfileResolver.Resolve(
            99,
            ThumbnailDetailModeRuntime.Standard
        );

        Assert.That(layout.FolderName, Is.EqualTo("160x120x1x1"));
    }

    [Test]
    public void Resolve_詳細タブ互換モードは120x90x1x1を返す()
    {
        ThumbnailLayoutProfile layout = ThumbnailLayoutProfileResolver.Resolve(
            99,
            ThumbnailDetailModeRuntime.WhiteBrowserCompatible
        );

        Assert.That(layout.FolderName, Is.EqualTo("120x90x1x1"));
    }

    [Test]
    public void ResolveRuntimeThumbRoot_WhiteBrowser同居DBはthum配下を返す()
    {
        string whiteBrowserRoot = CreateTempDirectory("imm-thumbroot-whitebrowser");
        string mainDbPath = Path.Combine(whiteBrowserRoot, "maimai.wb");
        string whiteBrowserExePath = Path.Combine(whiteBrowserRoot, "WhiteBrowser.exe");

        try
        {
            File.WriteAllText(whiteBrowserExePath, "wb");
            File.WriteAllText(mainDbPath, "db");

            string resolved = ThumbRootResolver.ResolveRuntimeThumbRoot(mainDbPath, "maimai");

            Assert.That(resolved, Is.EqualTo(Path.Combine(whiteBrowserRoot, "thum", "maimai")));
        }
        finally
        {
            TryDeleteDirectory(whiteBrowserRoot);
        }
    }

    [Test]
    public void ResolveRuntimeThumbRoot_明示設定があればそのまま使う()
    {
        string resolved = ThumbRootResolver.ResolveRuntimeThumbRoot(
            @"C:\db\anime.wb",
            "anime",
            @"D:\thumbs\anime"
        );

        Assert.That(resolved, Is.EqualTo(@"D:\thumbs\anime"));
    }

    [Test]
    public void ResolveRuntimeThumbRoot_通常DBは現行のexe配下Thumbへ戻す()
    {
        string resolved = ThumbRootResolver.ResolveRuntimeThumbRoot(@"D:\movie\anime.wb", "anime");

        Assert.That(resolved, Is.EqualTo(ThumbRootResolver.GetDefaultThumbRoot("anime")));
    }

    [Test]
    public void ReadRuntimeMode_標準モード適用後は詳細レイアウトが160x120になる()
    {
        WithDetailThumbnailMode(
            ThumbnailDetailModeRuntime.Standard,
            () =>
            {
                ThumbnailLayoutProfile layout = ThumbnailLayoutProfileResolver.Resolve(
                    99,
                    ThumbnailDetailModeRuntime.ReadRuntimeMode()
                );

                Assert.That(layout.FolderName, Is.EqualTo("160x120x1x1"));
            }
        );
    }

    [Test]
    public void ReadRuntimeMode_互換モード適用後は詳細レイアウトが120x90になる()
    {
        WithDetailThumbnailMode(
            ThumbnailDetailModeRuntime.WhiteBrowserCompatible,
            () =>
            {
                ThumbnailLayoutProfile layout = ThumbnailLayoutProfileResolver.Resolve(
                    99,
                    ThumbnailDetailModeRuntime.ReadRuntimeMode()
                );

                Assert.That(layout.FolderName, Is.EqualTo("120x90x1x1"));
            }
        );
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

    private static void WithDetailThumbnailMode(string mode, Action action)
    {
        string? oldMode = Environment.GetEnvironmentVariable("INDIGO_DETAIL_THUMB_MODE");
        try
        {
            ThumbnailDetailModeRuntime.ApplyToProcess(mode);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("INDIGO_DETAIL_THUMB_MODE", oldMode);
        }
    }
}
