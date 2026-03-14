using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailRescueWorkerLauncherTests
{
    private const string RescueWorkerExeName = "IndigoMovieManager.Thumbnail.RescueWorker.exe";

    [Test]
    public void TryResolveWorkerSourceDirectory_環境変数候補を最優先する()
    {
        string testRoot = CreateTempDirectory("imm-rescue-launcher-resolve");
        string envDirectory = Path.Combine(testRoot, "env-worker");
        string appBaseDirectory = Path.Combine(testRoot, "app-base");
        string envExePath = Path.Combine(envDirectory, RescueWorkerExeName);
        string fallbackExePath = Path.Combine(appBaseDirectory, RescueWorkerExeName);

        try
        {
            Directory.CreateDirectory(envDirectory);
            Directory.CreateDirectory(appBaseDirectory);
            File.WriteAllText(envExePath, "env");
            File.WriteAllText(fallbackExePath, "fallback");

            bool resolved = ThumbnailRescueWorkerLauncher.TryResolveWorkerSourceDirectory(
                appBaseDirectory,
                envExePath,
                out string sourceDirectory,
                out string workerExePath
            );

            Assert.That(resolved, Is.True);
            Assert.That(sourceDirectory, Is.EqualTo(envDirectory));
            Assert.That(workerExePath, Is.EqualTo(envExePath));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [Test]
    public void BuildGenerationDirectory_同じexeなら同じgenerationになる()
    {
        string testRoot = CreateTempDirectory("imm-rescue-launcher-generation");
        string workerExePath = Path.Combine(testRoot, RescueWorkerExeName);
        string generationRoot = Path.Combine(testRoot, "sessions");

        try
        {
            File.WriteAllText(workerExePath, "worker");
            File.SetLastWriteTimeUtc(
                workerExePath,
                new DateTime(2026, 3, 14, 10, 0, 0, DateTimeKind.Utc)
            );

            string directory1 = ThumbnailRescueWorkerLauncher.BuildGenerationDirectory(
                generationRoot,
                workerExePath
            );
            string directory2 = ThumbnailRescueWorkerLauncher.BuildGenerationDirectory(
                generationRoot,
                workerExePath
            );

            Assert.That(directory1, Is.EqualTo(directory2));
            Assert.That(Directory.Exists(directory1), Is.True);
            Assert.That(Path.GetFileName(directory1), Does.StartWith("worker_v"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [Test]
    public void CleanupOldSessions_最新3世代保持と7日超session掃除を行う()
    {
        string testRoot = CreateTempDirectory("imm-rescue-launcher-cleanup");
        DateTime nowUtc = new(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

        try
        {
            string generation1 = CreateGenerationDirectory(testRoot, "gen1", nowUtc.AddDays(-1));
            string generation2 = CreateGenerationDirectory(testRoot, "gen2", nowUtc.AddDays(-2));
            string generation3 = CreateGenerationDirectory(testRoot, "gen3", nowUtc.AddDays(-3));
            string generation4 = CreateGenerationDirectory(testRoot, "gen4", nowUtc.AddDays(-4));
            string expiredSession = CreateSessionDirectory(
                generation1,
                "session-expired",
                nowUtc.AddDays(-8)
            );
            string activeSession = CreateSessionDirectory(
                generation1,
                "session-active",
                nowUtc.AddDays(-2)
            );
            _ = CreateSessionDirectory(generation4, "session-old-generation", nowUtc.AddDays(-1));

            ThumbnailRescueWorkerLauncher.CleanupOldSessions(testRoot, nowUtc);

            Assert.That(Directory.Exists(generation1), Is.True);
            Assert.That(Directory.Exists(generation2), Is.True);
            Assert.That(Directory.Exists(generation3), Is.True);
            Assert.That(Directory.Exists(generation4), Is.False);
            Assert.That(Directory.Exists(expiredSession), Is.False);
            Assert.That(Directory.Exists(activeSession), Is.True);
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [Test]
    public void FormatWorkerPipeLogLine_標準出力を整形する()
    {
        string message = ThumbnailRescueWorkerLauncher.FormatWorkerPipeLogLine(
            "stdout",
            " rescue leased: failure_id=1 "
        );

        Assert.That(message, Is.EqualTo("rescue worker stdout: rescue leased: failure_id=1"));
    }

    [Test]
    public void FormatWorkerPipeLogLine_空行は捨てる()
    {
        string message = ThumbnailRescueWorkerLauncher.FormatWorkerPipeLogLine("stderr", "   ");

        Assert.That(message, Is.Empty);
    }

    private static string CreateGenerationDirectory(
        string rootPath,
        string directoryName,
        DateTime creationTimeUtc
    )
    {
        string directoryPath = Path.Combine(rootPath, directoryName);
        Directory.CreateDirectory(directoryPath);
        Directory.SetCreationTimeUtc(directoryPath, creationTimeUtc);
        return directoryPath;
    }

    private static string CreateSessionDirectory(
        string generationDirectory,
        string directoryName,
        DateTime creationTimeUtc
    )
    {
        string directoryPath = Path.Combine(generationDirectory, directoryName);
        Directory.CreateDirectory(directoryPath);
        Directory.SetCreationTimeUtc(directoryPath, creationTimeUtc);
        return directoryPath;
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
