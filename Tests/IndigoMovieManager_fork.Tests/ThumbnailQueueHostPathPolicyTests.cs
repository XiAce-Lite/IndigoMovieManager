using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailQueueHostPathPolicyTests
{
    [Test]
    public void ResolveQueueDbPath_Host設定ディレクトリを優先する()
    {
        string originalQueueDbDirectoryPath =
            ThumbnailQueueHostPathPolicy.ResolveQueueDbDirectoryPath();
        string customQueueDbDirectoryPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-host-policy-{Guid.NewGuid():N}",
            "QueueDb"
        );

        try
        {
            ThumbnailQueueHostPathPolicy.Configure(
                queueDbDirectoryPath: customQueueDbDirectoryPath
            );

            string resolved = QueueDbPathResolver.ResolveQueueDbPath(@"C:\db\anime.wb");

            Assert.That(
                Path.GetDirectoryName(resolved),
                Is.EqualTo(customQueueDbDirectoryPath)
            );
        }
        finally
        {
            ThumbnailQueueHostPathPolicy.Configure(
                queueDbDirectoryPath: originalQueueDbDirectoryPath
            );
            TryDeleteDirectory(Path.GetDirectoryName(customQueueDbDirectoryPath) ?? "");
        }
    }

    [Test]
    public void ResolveFailureDbPath_Host設定ディレクトリを優先する()
    {
        string originalFailureDbDirectoryPath =
            ThumbnailQueueHostPathPolicy.ResolveFailureDbDirectoryPath();
        string customFailureDbDirectoryPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-failure-host-policy-{Guid.NewGuid():N}",
            "FailureDb"
        );

        try
        {
            ThumbnailQueueHostPathPolicy.Configure(
                failureDbDirectoryPath: customFailureDbDirectoryPath
            );

            string resolved = ThumbnailFailureDbPathResolver.ResolveFailureDbPath(
                @"C:\db\anime.wb"
            );

            Assert.That(
                Path.GetDirectoryName(resolved),
                Is.EqualTo(customFailureDbDirectoryPath)
            );
        }
        finally
        {
            ThumbnailQueueHostPathPolicy.Configure(
                failureDbDirectoryPath: originalFailureDbDirectoryPath
            );
            TryDeleteDirectory(Path.GetDirectoryName(customFailureDbDirectoryPath) ?? "");
        }
    }

    [Test]
    public void Configure_空文字でfallbackへ戻せる()
    {
        string fallbackLogDirectoryPath = ThumbnailQueueHostPathPolicy.ResolveLogDirectoryPath();

        try
        {
            ThumbnailQueueHostPathPolicy.Configure(logDirectoryPath: @"C:\custom\logs");
            ThumbnailQueueHostPathPolicy.Configure(logDirectoryPath: "");

            string resolved = ThumbnailQueueHostPathPolicy.ResolveLogDirectoryPath();

            Assert.That(resolved, Is.EqualTo(fallbackLogDirectoryPath));
        }
        finally
        {
            ThumbnailQueueHostPathPolicy.Configure(logDirectoryPath: fallbackLogDirectoryPath);
        }
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
            // テスト後始末の削除失敗は握りつぶす。
        }
    }
}
