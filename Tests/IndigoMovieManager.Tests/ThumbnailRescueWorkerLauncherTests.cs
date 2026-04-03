using System.Diagnostics;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailRescueWorkerLauncherTests
{
    private const string RescueWorkerExeName = "IndigoMovieManager.Thumbnail.RescueWorker.exe";

    [Test]
    public void LaunchSettings_補助dir一覧は正規化される()
    {
        string hostBaseDirectory = CreateTempDirectory("imm-rescue-launcher-settings");
        string supplementalDirectoryPath = Path.Combine(hostBaseDirectory, "runtimes");

        try
        {
            Directory.CreateDirectory(supplementalDirectoryPath);
            ThumbnailRescueWorkerLaunchSettings settings = new(
                sessionRootDirectoryPath: Path.Combine(hostBaseDirectory, "sessions"),
                logDirectoryPath: Path.Combine(hostBaseDirectory, "logs"),
                failureDbDirectoryPath: Path.Combine(hostBaseDirectory, "failuredb"),
                hostBaseDirectory: hostBaseDirectory,
                workerExecutablePath: Path.Combine(hostBaseDirectory, RescueWorkerExeName),
                supplementalDirectoryPaths: [supplementalDirectoryPath]
            );

            Assert.That(
                settings.SupplementalDirectoryPaths.Single(),
                Is.EqualTo(supplementalDirectoryPath)
            );
        }
        finally
        {
            TryDeleteDirectory(hostBaseDirectory);
        }
    }

    [Test]
    public void LaunchSettings_WorkerExecutablePathは引用符を外して正規化する()
    {
        string hostBaseDirectory = CreateTempDirectory("imm-rescue-launcher-worker-override");
        string workerExecutablePath = Path.Combine(hostBaseDirectory, "custom", RescueWorkerExeName);

        try
        {
            ThumbnailRescueWorkerLaunchSettings settings = new(
                sessionRootDirectoryPath: Path.Combine(hostBaseDirectory, "sessions"),
                logDirectoryPath: Path.Combine(hostBaseDirectory, "logs"),
                failureDbDirectoryPath: Path.Combine(hostBaseDirectory, "failuredb"),
                hostBaseDirectory: hostBaseDirectory,
                workerExecutablePath: $"\"{workerExecutablePath}\""
            );

            Assert.That(
                settings.WorkerExecutablePath,
                Is.EqualTo(workerExecutablePath)
            );
        }
        finally
        {
            TryDeleteDirectory(hostBaseDirectory);
        }
    }

    [Test]
    public void TryResolveWorkerExecutablePath_環境変数候補を最優先する()
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

            bool resolved =
                ThumbnailRescueWorkerLaunchSettingsFactory.TryResolveWorkerExecutablePath(
                appBaseDirectory,
                envExePath,
                out string workerExecutablePath
            );

            Assert.That(resolved, Is.True);
            Assert.That(workerExecutablePath, Is.EqualTo(envExePath));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [Test]
    public void TryResolveWorkerExecutablePath_PublishArtifactをbinより優先する()
    {
        string repoRoot = CreateTempDirectory("imm-rescue-launcher-artifact-priority");
        string hostBaseDirectory = Path.Combine(repoRoot, "bin", "x64", "Debug", "net8.0-windows");
        string artifactDirectory = Path.Combine(
            repoRoot,
            "artifacts",
            "rescue-worker",
            "publish",
            "Release-win-x64"
        );
        string fallbackDirectory = Path.Combine(
            repoRoot,
            "src",
            "IndigoMovieManager.Thumbnail.RescueWorker",
            "bin",
            "x64",
            "Debug",
            "net8.0-windows"
        );
        string artifactExePath = Path.Combine(artifactDirectory, RescueWorkerExeName);
        string fallbackExePath = Path.Combine(fallbackDirectory, RescueWorkerExeName);

        try
        {
            File.WriteAllText(Path.Combine(repoRoot, "IndigoMovieManager.csproj"), "<Project />");
            Directory.CreateDirectory(hostBaseDirectory);
            Directory.CreateDirectory(artifactDirectory);
            Directory.CreateDirectory(fallbackDirectory);
            File.WriteAllText(artifactExePath, "artifact");
            SeedCompletePublishedArtifact(artifactDirectory);
            File.WriteAllText(fallbackExePath, "fallback");

            bool resolved =
                ThumbnailRescueWorkerLaunchSettingsFactory.TryResolveWorkerExecutablePath(
                    hostBaseDirectory,
                    "",
                    out string workerExecutablePath
                );

            Assert.That(resolved, Is.True);
            Assert.That(workerExecutablePath, Is.EqualTo(artifactExePath));
        }
        finally
        {
            TryDeleteDirectory(repoRoot);
        }
    }

    [Test]
    public void TryResolveWorkerExecutablePath_互換version不一致artifactは採用しない()
    {
        string repoRoot = CreateTempDirectory("imm-rescue-launcher-artifact-version-mismatch");
        string hostBaseDirectory = Path.Combine(repoRoot, "bin", "x64", "Debug", "net8.0-windows");
        string artifactDirectory = Path.Combine(
            repoRoot,
            "artifacts",
            "rescue-worker",
            "publish",
            "Release-win-x64"
        );
        string fallbackDirectory = Path.Combine(
            repoRoot,
            "src",
            "IndigoMovieManager.Thumbnail.RescueWorker",
            "bin",
            "x64",
            "Debug",
            "net8.0-windows"
        );
        string artifactExePath = Path.Combine(artifactDirectory, RescueWorkerExeName);
        string fallbackExePath = Path.Combine(fallbackDirectory, RescueWorkerExeName);

        try
        {
            File.WriteAllText(Path.Combine(repoRoot, "IndigoMovieManager.sln"), "");
            Directory.CreateDirectory(hostBaseDirectory);
            Directory.CreateDirectory(artifactDirectory);
            Directory.CreateDirectory(fallbackDirectory);
            File.WriteAllText(artifactExePath, "artifact");
            CreatePublishArtifactMarker(artifactDirectory, "mismatch");
            File.WriteAllText(fallbackExePath, "fallback");

            bool resolved =
                ThumbnailRescueWorkerLaunchSettingsFactory.TryResolveWorkerExecutablePath(
                    hostBaseDirectory,
                    "",
                    out string workerExecutablePath
                );

            Assert.That(resolved, Is.True);
            Assert.That(workerExecutablePath, Is.EqualTo(fallbackExePath));
        }
        finally
        {
            TryDeleteDirectory(repoRoot);
        }
    }

    [Test]
    public void TryResolveWorkerExecutablePath_不足DLLのartifactは採用しない()
    {
        string repoRoot = CreateTempDirectory("imm-rescue-launcher-artifact-incomplete");
        string hostBaseDirectory = Path.Combine(repoRoot, "bin", "x64", "Debug", "net8.0-windows");
        string artifactDirectory = Path.Combine(
            repoRoot,
            "artifacts",
            "rescue-worker",
            "publish",
            "Release-win-x64"
        );
        string fallbackDirectory = Path.Combine(
            repoRoot,
            "src",
            "IndigoMovieManager.Thumbnail.RescueWorker",
            "bin",
            "x64",
            "Debug",
            "net8.0-windows"
        );
        string artifactExePath = Path.Combine(artifactDirectory, RescueWorkerExeName);
        string fallbackExePath = Path.Combine(fallbackDirectory, RescueWorkerExeName);

        try
        {
            File.WriteAllText(Path.Combine(repoRoot, "IndigoMovieManager.sln"), "");
            Directory.CreateDirectory(hostBaseDirectory);
            Directory.CreateDirectory(artifactDirectory);
            Directory.CreateDirectory(fallbackDirectory);
            File.WriteAllText(artifactExePath, "artifact");
            CreatePublishArtifactMarker(artifactDirectory);
            File.WriteAllText(fallbackExePath, "fallback");

            bool resolved =
                ThumbnailRescueWorkerLaunchSettingsFactory.TryResolveWorkerExecutablePath(
                    hostBaseDirectory,
                    "",
                    out string workerExecutablePath
                );

            Assert.That(resolved, Is.True);
            Assert.That(workerExecutablePath, Is.EqualTo(fallbackExePath));
        }
        finally
        {
            TryDeleteDirectory(repoRoot);
        }
    }

    [Test]
    public void TryResolveWorkerExecutablePath_originはartifactを返す()
    {
        string repoRoot = CreateTempDirectory("imm-rescue-launcher-artifact-origin");
        string hostBaseDirectory = Path.Combine(repoRoot, "bin", "x64", "Debug", "net8.0-windows");
        string artifactDirectory = Path.Combine(
            repoRoot,
            "artifacts",
            "rescue-worker",
            "publish",
            "Release-win-x64"
        );
        string artifactExePath = Path.Combine(artifactDirectory, RescueWorkerExeName);

        try
        {
            File.WriteAllText(Path.Combine(repoRoot, "IndigoMovieManager.sln"), "");
            Directory.CreateDirectory(hostBaseDirectory);
            Directory.CreateDirectory(artifactDirectory);
            File.WriteAllText(artifactExePath, "artifact");
            SeedCompletePublishedArtifact(artifactDirectory);

            bool resolved =
                ThumbnailRescueWorkerLaunchSettingsFactory.TryResolveWorkerExecutablePath(
                    hostBaseDirectory,
                    "",
                    out string workerExecutablePath,
                    out string workerExecutablePathOrigin
                );

            Assert.That(resolved, Is.True);
            Assert.That(workerExecutablePath, Is.EqualTo(artifactExePath));
            Assert.That(workerExecutablePathOrigin, Is.EqualTo("artifact"));
        }
        finally
        {
            TryDeleteDirectory(repoRoot);
        }
    }

    [Test]
    public void ResolveWorkerExecutablePathOrigin_project_buildを返す()
    {
        string hostBaseDirectory = CreateTempDirectory("imm-rescue-launcher-origin-project");
        string workerExecutablePath = Path.Combine(
            hostBaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "src",
            "IndigoMovieManager.Thumbnail.RescueWorker",
            "bin",
            "x64",
            "Debug",
            "net8.0-windows",
            RescueWorkerExeName
        );

        string origin = ThumbnailRescueWorkerLaunchSettingsFactory.ResolveWorkerExecutablePathOrigin(
            hostBaseDirectory,
            "",
            workerExecutablePath,
            workerExecutablePath,
            ""
        );

        Assert.That(origin, Is.EqualTo("project-build"));
    }

    [Test]
    public void TryResolveWorkerExecutablePath_デバッグ起動時でもartifactを優先する()
    {
        string repoRoot = CreateTempDirectory("imm-rescue-launcher-artifact-debug-priority");
        string hostBaseDirectory = Path.Combine(repoRoot, "bin", "x64", "Debug", "net8.0-windows");
        string artifactDirectory = Path.Combine(
            repoRoot,
            "artifacts",
            "rescue-worker",
            "publish",
            "Debug-win-x64"
        );
        string projectExePath = Path.Combine(
            repoRoot,
            "src",
            "IndigoMovieManager.Thumbnail.RescueWorker",
            "bin",
            "x64",
            "Debug",
            "net8.0-windows",
            RescueWorkerExeName
        );
        string artifactExePath = Path.Combine(artifactDirectory, RescueWorkerExeName);
        string fallbackExePath = Path.Combine(hostBaseDirectory, RescueWorkerExeName);

        try
        {
            File.WriteAllText(Path.Combine(repoRoot, "IndigoMovieManager.csproj"), "<Project />");
            Directory.CreateDirectory(artifactDirectory);
            Directory.CreateDirectory(hostBaseDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(projectExePath) ?? "");
            File.WriteAllText(artifactExePath, "artifact");
            SeedCompletePublishedArtifact(artifactDirectory);
            File.WriteAllText(projectExePath, "project");
            File.WriteAllText(fallbackExePath, "fallback");

            bool resolved =
                ThumbnailRescueWorkerLaunchSettingsFactory.TryResolveWorkerExecutablePath(
                    hostBaseDirectory,
                    "",
                    out string workerExecutablePath
                );

            Assert.That(resolved, Is.True);
            Assert.That(workerExecutablePath, Is.EqualTo(artifactExePath));
        }
        finally
        {
            TryDeleteDirectory(repoRoot);
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
    public void TryStartIfNeeded_予約中は二重起動しない()
    {
        string testRoot = CreateTempDirectory("imm-rescue-launcher-reserved");
        string hostBaseDirectory = Path.Combine(testRoot, "app");
        string workerSourceDirectory = Path.Combine(testRoot, "worker");
        string workerExecutablePath = Path.Combine(workerSourceDirectory, RescueWorkerExeName);
        string sessionRootDirectoryPath = Path.Combine(testRoot, "sessions");
        string logDirectoryPath = Path.Combine(testRoot, "logs");
        string failureDbDirectoryPath = Path.Combine(testRoot, "failuredb");
        string mainDbPath = Path.Combine(testRoot, "anime.wb");
        string thumbFolder = Path.Combine(testRoot, "thumbs");
        using ManualResetEventSlim launchReserved = new(false);
        using ManualResetEventSlim allowContinue = new(false);
        Task<bool> firstAttempt = Task.FromResult(false);

        try
        {
            Directory.CreateDirectory(hostBaseDirectory);
            Directory.CreateDirectory(workerSourceDirectory);
            File.Copy(
                Path.Combine(Environment.SystemDirectory, "rundll32.exe"),
                workerExecutablePath,
                overwrite: true
            );
            File.WriteAllText(mainDbPath, "");
            SeedPendingRescueRecord(mainDbPath, @"D:\movies\sample.mkv");

            ThumbnailRescueWorkerLaunchSettings settings = new(
                sessionRootDirectoryPath,
                logDirectoryPath,
                failureDbDirectoryPath,
                hostBaseDirectory,
                workerExecutablePath
            );
            using ThumbnailRescueWorkerLauncher launcher = new(
                settings,
                afterLaunchReserved: () =>
                {
                    launchReserved.Set();
                    Assert.That(
                        allowContinue.Wait(TimeSpan.FromSeconds(5)),
                        Is.True,
                        "最初の起動予約を解放できませんでした。"
                    );
                }
            );

            firstAttempt = Task.Run(() => launcher.TryStartIfNeeded(mainDbPath, "anime", thumbFolder));
            Assert.That(
                launchReserved.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "最初の起動が予約状態へ入りませんでした。"
            );

            bool secondAttempt = launcher.TryStartIfNeeded(mainDbPath, "anime", thumbFolder);

            Assert.That(secondAttempt, Is.False);
            allowContinue.Set();
            Assert.That(firstAttempt.Wait(TimeSpan.FromSeconds(10)), Is.True);
            _ = firstAttempt.Result;
        }
        finally
        {
            allowContinue.Set();
            if (!firstAttempt.IsCompleted)
            {
                firstAttempt.Wait(TimeSpan.FromSeconds(10));
            }

            TryDeleteDirectory(testRoot);
        }
    }

    [Test]
    public void TryStartIfNeeded_別launcherなら別枠で同時予約できる()
    {
        string testRoot = CreateTempDirectory("imm-rescue-launcher-dual-slot");
        string hostBaseDirectory = Path.Combine(testRoot, "app");
        string workerSourceDirectory = Path.Combine(testRoot, "worker");
        string workerExecutablePath = Path.Combine(workerSourceDirectory, RescueWorkerExeName);
        string sessionRootDirectoryPath = Path.Combine(testRoot, "sessions");
        string logDirectoryPath = Path.Combine(testRoot, "logs");
        string failureDbDirectoryPath = Path.Combine(testRoot, "failuredb");
        string mainDbPath = Path.Combine(testRoot, "anime.wb");
        string thumbFolder = Path.Combine(testRoot, "thumbs");
        using ManualResetEventSlim autoSlotReserved = new(false);
        using ManualResetEventSlim manualSlotReserved = new(false);
        using ManualResetEventSlim allowAutoSlotContinue = new(false);
        using ManualResetEventSlim allowManualSlotContinue = new(false);
        Task<bool> autoSlotAttempt = Task.FromResult(false);
        Task<bool> manualSlotAttempt = Task.FromResult(false);

        try
        {
            Directory.CreateDirectory(hostBaseDirectory);
            Directory.CreateDirectory(workerSourceDirectory);
            File.Copy(
                Path.Combine(Environment.SystemDirectory, "rundll32.exe"),
                workerExecutablePath,
                overwrite: true
            );
            File.WriteAllText(mainDbPath, "");
            SeedPendingRescueRecord(mainDbPath, @"D:\movies\sample.mkv");

            ThumbnailRescueWorkerLaunchSettings autoSlotSettings = new(
                Path.Combine(sessionRootDirectoryPath, "default"),
                logDirectoryPath,
                failureDbDirectoryPath,
                hostBaseDirectory,
                workerExecutablePath
            );
            ThumbnailRescueWorkerLaunchSettings manualSlotSettings = new(
                Path.Combine(sessionRootDirectoryPath, "manual"),
                logDirectoryPath,
                failureDbDirectoryPath,
                hostBaseDirectory,
                workerExecutablePath
            );

            using ThumbnailRescueWorkerLauncher autoSlotLauncher = new(
                autoSlotSettings,
                afterLaunchReserved: () =>
                {
                    autoSlotReserved.Set();
                    Assert.That(
                        allowAutoSlotContinue.Wait(TimeSpan.FromSeconds(5)),
                        Is.True,
                        "default slot の予約解放を待てませんでした。"
                    );
                }
            );
            using ThumbnailRescueWorkerLauncher manualSlotLauncher = new(
                manualSlotSettings,
                afterLaunchReserved: () =>
                {
                    manualSlotReserved.Set();
                    Assert.That(
                        allowManualSlotContinue.Wait(TimeSpan.FromSeconds(5)),
                        Is.True,
                        "manual slot の予約解放を待てませんでした。"
                    );
                }
            );

            autoSlotAttempt = Task.Run(
                () => autoSlotLauncher.TryStartIfNeeded(mainDbPath, "anime", thumbFolder)
            );
            Assert.That(
                autoSlotReserved.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "default slot の起動予約へ入れませんでした。"
            );

            manualSlotAttempt = Task.Run(
                () => manualSlotLauncher.TryStartIfNeeded(mainDbPath, "anime", thumbFolder)
            );
            Assert.That(
                manualSlotReserved.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "manual slot の起動予約へ入れませんでした。"
            );
            allowAutoSlotContinue.Set();
            allowManualSlotContinue.Set();
            Assert.That(autoSlotAttempt.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(manualSlotAttempt.Wait(TimeSpan.FromSeconds(10)), Is.True);
            _ = autoSlotAttempt.Result;
            _ = manualSlotAttempt.Result;
        }
        finally
        {
            allowAutoSlotContinue.Set();
            allowManualSlotContinue.Set();
            if (!autoSlotAttempt.IsCompleted)
            {
                autoSlotAttempt.Wait(TimeSpan.FromSeconds(10));
            }
            if (!manualSlotAttempt.IsCompleted)
            {
                manualSlotAttempt.Wait(TimeSpan.FromSeconds(10));
            }

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

    [Test]
    public void ResolveThumbFolderForWorker_未指定時はappBase配下のThumbを絶対化する()
    {
        string appBaseDirectory = CreateTempDirectory("imm-rescue-launcher-thumb-default");
        string mainDbPath = Path.Combine(appBaseDirectory, "anime.wb");

        try
        {
            string resolved = ThumbnailRescueWorkerLauncher.ResolveThumbFolderForWorker(
                mainDbPath,
                "anime",
                "",
                appBaseDirectory
            );

            Assert.That(
                resolved,
                Is.EqualTo(Path.Combine(appBaseDirectory, "Thumb", "anime"))
            );
        }
        finally
        {
            TryDeleteDirectory(appBaseDirectory);
        }
    }

    [Test]
    public void ResolveThumbFolderForWorker_相対設定はappBase基準で絶対化する()
    {
        string appBaseDirectory = CreateTempDirectory("imm-rescue-launcher-thumb-relative");
        string mainDbPath = Path.Combine(appBaseDirectory, "anime.wb");

        try
        {
            string resolved = ThumbnailRescueWorkerLauncher.ResolveThumbFolderForWorker(
                mainDbPath,
                "anime",
                @"ThumbStore\anime",
                appBaseDirectory
            );

            Assert.That(
                resolved,
                Is.EqualTo(Path.Combine(appBaseDirectory, "ThumbStore", "anime"))
            );
        }
        finally
        {
            TryDeleteDirectory(appBaseDirectory);
        }
    }

    [Test]
    public void ResolveThumbFolderForWorker_WhiteBrowser同居DBはthum配下を優先する()
    {
        string whiteBrowserRoot = CreateTempDirectory("imm-rescue-launcher-thumb-whitebrowser");
        string mainDbPath = Path.Combine(whiteBrowserRoot, "maimai.wb");
        string whiteBrowserExePath = Path.Combine(whiteBrowserRoot, "WhiteBrowser.exe");

        try
        {
            File.WriteAllText(whiteBrowserExePath, "wb");
            File.WriteAllText(mainDbPath, "db");

            string resolved = ThumbnailRescueWorkerLauncher.ResolveThumbFolderForWorker(
                mainDbPath,
                "maimai",
                "",
                Path.Combine(whiteBrowserRoot, "other-app-base")
            );

            Assert.That(
                resolved,
                Is.EqualTo(Path.Combine(whiteBrowserRoot, "thum", "maimai"))
            );
        }
        finally
        {
            TryDeleteDirectory(whiteBrowserRoot);
        }
    }

    [Test]
    public void BuildWorkerArguments_ThumbFolderを引数へ含める()
    {
        string arguments = ThumbnailRescueWorkerLauncher.BuildWorkerArguments(
            @"C:\db\anime.wb",
            @"D:\thumbs\anime",
            @"E:\logs",
            @"F:\failuredb"
        );

        Assert.That(
            arguments,
            Is.EqualTo(
                "--main-db \"C:\\db\\anime.wb\" --thumb-folder \"D:\\thumbs\\anime\" --log-dir \"E:\\logs\" --failure-db-dir \"F:\\failuredb\""
            )
        );
    }

    [Test]
    public void ResolveSupplementalPaths_HostBaseにあるruntimeとtoolsを列挙する()
    {
        string hostBaseDirectory = CreateTempDirectory("imm-rescue-launcher-supplemental-paths");

        try
        {
            Directory.CreateDirectory(Path.Combine(hostBaseDirectory, "runtimes", "win-x64"));
            Directory.CreateDirectory(Path.Combine(hostBaseDirectory, "tools", "ffmpeg-shared"));
            File.WriteAllText(
                Path.Combine(hostBaseDirectory, "SQLitePCLRaw.provider.e_sqlite3.dll"),
                "sqlite-provider"
            );

            IReadOnlyList<string> directoryPaths =
                ThumbnailRescueWorkerLaunchSettingsFactory.ResolveSupplementalDirectoryPaths(
                    hostBaseDirectory,
                    ""
                );
            IReadOnlyList<string> filePaths =
                ThumbnailRescueWorkerLaunchSettingsFactory.ResolveSupplementalFilePaths(
                    hostBaseDirectory,
                    ""
                );

            Assert.That(directoryPaths, Has.Count.EqualTo(2));
            Assert.That(
                directoryPaths,
                Does.Contain(Path.Combine(hostBaseDirectory, "runtimes"))
                    .And.Contain(Path.Combine(hostBaseDirectory, "tools"))
            );
            Assert.That(filePaths, Has.Count.EqualTo(1));
            Assert.That(
                filePaths.Single(),
                Is.EqualTo(
                    Path.Combine(hostBaseDirectory, "SQLitePCLRaw.provider.e_sqlite3.dll")
                )
            );
        }
        finally
        {
            TryDeleteDirectory(hostBaseDirectory);
        }
    }

    [Test]
    public void CreateDefault_PublishArtifact検出時は補助依存を空にする()
    {
        string repoRoot = CreateTempDirectory("imm-rescue-launcher-artifact-settings");
        string hostBaseDirectory = Path.Combine(repoRoot, "bin", "x64", "Debug", "net8.0-windows");
        string sessionRootDirectoryPath = Path.Combine(repoRoot, "sessions");
        string logDirectoryPath = Path.Combine(repoRoot, "logs");
        string failureDbDirectoryPath = Path.Combine(repoRoot, "failuredb");
        string artifactDirectory = Path.Combine(
            repoRoot,
            "artifacts",
            "rescue-worker",
            "publish",
            "Release-win-x64"
        );
        string artifactExePath = Path.Combine(artifactDirectory, RescueWorkerExeName);

        try
        {
            File.WriteAllText(Path.Combine(repoRoot, "IndigoMovieManager.sln"), "");
            Directory.CreateDirectory(hostBaseDirectory);
            Directory.CreateDirectory(Path.Combine(hostBaseDirectory, "runtimes", "win-x64"));
            Directory.CreateDirectory(Path.Combine(hostBaseDirectory, "tools", "ffmpeg-shared"));
            File.WriteAllText(
                Path.Combine(hostBaseDirectory, "SQLitePCLRaw.provider.e_sqlite3.dll"),
                "sqlite-provider"
            );
            Directory.CreateDirectory(artifactDirectory);
            File.WriteAllText(artifactExePath, "artifact");
            SeedCompletePublishedArtifact(artifactDirectory);

            ThumbnailRescueWorkerLaunchSettings settings =
                ThumbnailRescueWorkerLaunchSettingsFactory.CreateDefault(
                    sessionRootDirectoryPath,
                    logDirectoryPath,
                    failureDbDirectoryPath,
                    hostBaseDirectory,
                    ""
                );

            Assert.That(settings.WorkerExecutablePath, Is.EqualTo(artifactExePath));
            Assert.That(settings.SupplementalDirectoryPaths, Is.Empty);
            Assert.That(settings.SupplementalFilePaths, Is.Empty);
            Assert.That(settings.WorkerExecutablePathDiagnostic, Is.Empty);
        }
        finally
        {
            TryDeleteDirectory(repoRoot);
        }
    }

    [Test]
    public void CreateDefault_同梱rescue_worker検出時は補助依存を空にする()
    {
        string appBaseDirectory = CreateTempDirectory("imm-rescue-launcher-bundled-artifact");
        string sessionRootDirectoryPath = Path.Combine(appBaseDirectory, "sessions");
        string logDirectoryPath = Path.Combine(appBaseDirectory, "logs");
        string failureDbDirectoryPath = Path.Combine(appBaseDirectory, "failuredb");
        string bundledArtifactDirectory = Path.Combine(appBaseDirectory, "rescue-worker");
        string bundledArtifactExePath = Path.Combine(bundledArtifactDirectory, RescueWorkerExeName);

        try
        {
            Directory.CreateDirectory(Path.Combine(appBaseDirectory, "runtimes", "win-x64"));
            Directory.CreateDirectory(Path.Combine(appBaseDirectory, "tools", "ffmpeg-shared"));
            File.WriteAllText(
                Path.Combine(appBaseDirectory, "SQLitePCLRaw.provider.e_sqlite3.dll"),
                "sqlite-provider"
            );
            Directory.CreateDirectory(bundledArtifactDirectory);
            File.WriteAllText(bundledArtifactExePath, "artifact");
            SeedCompletePublishedArtifact(bundledArtifactDirectory);

            ThumbnailRescueWorkerLaunchSettings settings =
                ThumbnailRescueWorkerLaunchSettingsFactory.CreateDefault(
                    sessionRootDirectoryPath,
                    logDirectoryPath,
                    failureDbDirectoryPath,
                    appBaseDirectory,
                    ""
                );

            Assert.That(settings.WorkerExecutablePath, Is.EqualTo(bundledArtifactExePath));
            Assert.That(settings.SupplementalDirectoryPaths, Is.Empty);
            Assert.That(settings.SupplementalFilePaths, Is.Empty);
            Assert.That(settings.WorkerExecutablePathDiagnostic, Is.Empty);
        }
        finally
        {
            TryDeleteDirectory(appBaseDirectory);
        }
    }

    [Test]
    public void CreateDefault_互換version不一致artifactだけなら診断理由を保持する()
    {
        string repoRoot = CreateTempDirectory("imm-rescue-launcher-artifact-diagnostic");
        string hostBaseDirectory = Path.Combine(repoRoot, "bin", "x64", "Debug", "net8.0-windows");
        string sessionRootDirectoryPath = Path.Combine(repoRoot, "sessions");
        string logDirectoryPath = Path.Combine(repoRoot, "logs");
        string failureDbDirectoryPath = Path.Combine(repoRoot, "failuredb");
        string artifactDirectory = Path.Combine(
            repoRoot,
            "artifacts",
            "rescue-worker",
            "publish",
            "Release-win-x64"
        );
        string artifactExePath = Path.Combine(artifactDirectory, RescueWorkerExeName);

        try
        {
            File.WriteAllText(Path.Combine(repoRoot, "IndigoMovieManager.sln"), "");
            Directory.CreateDirectory(hostBaseDirectory);
            Directory.CreateDirectory(artifactDirectory);
            File.WriteAllText(artifactExePath, "artifact");
            CreatePublishArtifactMarker(artifactDirectory, "mismatch");

            ThumbnailRescueWorkerLaunchSettings settings =
                ThumbnailRescueWorkerLaunchSettingsFactory.CreateDefault(
                    sessionRootDirectoryPath,
                    logDirectoryPath,
                    failureDbDirectoryPath,
                    hostBaseDirectory,
                    ""
                );

            Assert.That(settings.WorkerExecutablePath, Is.Empty);
            Assert.That(
                settings.WorkerExecutablePathDiagnostic,
                Is.EqualTo("published artifact invalid: compatibilityVersion mismatch.")
            );
        }
        finally
        {
            TryDeleteDirectory(repoRoot);
        }
    }

    [Test]
    public void BuildWorkerLaunchSkippedMessage_診断理由を含める()
    {
        string message = ThumbnailRescueWorkerLauncher.BuildWorkerLaunchSkippedMessage(
            "rescue worker launch skipped",
            "published artifact invalid: compatibilityVersion mismatch."
        );

        Assert.That(
            message,
            Is.EqualTo(
                "rescue worker launch skipped: published artifact invalid: compatibilityVersion mismatch."
            )
        );
    }

    [Test]
    public void BuildWorkerLaunchSkippedMessage_診断理由が無ければ既定文言を使う()
    {
        string message = ThumbnailRescueWorkerLauncher.BuildWorkerLaunchSkippedMessage(
            "direct index repair launch skipped",
            ""
        );

        Assert.That(
            message,
            Is.EqualTo("direct index repair launch skipped: source worker not found.")
        );
    }

    [Test]
    public void OverlaySupplementalDependencies_Host指定のruntimeとtoolsをsessionへ補完する()
    {
        string testRoot = CreateTempDirectory("imm-rescue-launcher-runtime-merge");
        string sessionDirectory = Path.Combine(testRoot, "session");
        string hostBaseDirectory = Path.Combine(testRoot, "app");

        try
        {
            Directory.CreateDirectory(sessionDirectory);
            Directory.CreateDirectory(
                Path.Combine(hostBaseDirectory, "runtimes", "win-x64", "native")
            );
            Directory.CreateDirectory(Path.Combine(hostBaseDirectory, "tools", "ffmpeg-shared"));

            File.WriteAllText(
                Path.Combine(hostBaseDirectory, "runtimes", "win-x64", "native", "e_sqlite3.dll"),
                "sqlite-native"
            );
            File.WriteAllText(
                Path.Combine(hostBaseDirectory, "SQLitePCLRaw.provider.e_sqlite3.dll"),
                "sqlite-provider"
            );
            File.WriteAllText(
                Path.Combine(hostBaseDirectory, "tools", "ffmpeg-shared", "avcodec-61.dll"),
                "ffmpeg-shared"
            );

            ThumbnailRescueWorkerLauncher.OverlaySupplementalDependencies(
                [Path.Combine(hostBaseDirectory, "runtimes"), Path.Combine(hostBaseDirectory, "tools")],
                [Path.Combine(hostBaseDirectory, "SQLitePCLRaw.provider.e_sqlite3.dll")],
                sessionDirectory,
                _ => { }
            );

            Assert.That(
                File.Exists(
                    Path.Combine(sessionDirectory, "runtimes", "win-x64", "native", "e_sqlite3.dll")
                ),
                Is.True
            );
            Assert.That(
                File.Exists(Path.Combine(sessionDirectory, "SQLitePCLRaw.provider.e_sqlite3.dll")),
                Is.True
            );
            Assert.That(
                File.Exists(
                    Path.Combine(sessionDirectory, "tools", "ffmpeg-shared", "avcodec-61.dll")
                ),
                Is.True
            );
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [Test]
    public void BuildLaunchSourceLogLine_起動元とgenerationを記録する()
    {
        string testRoot = CreateTempDirectory("imm-rescue-launcher-source-log");

        try
        {
            ThumbnailRescueWorkerLaunchSettings settings = new(
                sessionRootDirectoryPath: Path.Combine(testRoot, "sessions"),
                logDirectoryPath: Path.Combine(testRoot, "logs"),
                failureDbDirectoryPath: Path.Combine(testRoot, "failuredb"),
                hostBaseDirectory: testRoot,
                workerExecutablePath: Path.Combine(testRoot, RescueWorkerExeName),
                workerExecutablePathOrigin: "artifact",
                supplementalDirectoryPaths: [],
                supplementalFilePaths: []
            );

            string line = ThumbnailRescueWorkerLauncher.BuildLaunchSourceLogLine(
                settings,
                Path.Combine(testRoot, "sessions", "worker_v1.0.0.0_7143fd72")
            );

            Assert.That(line, Does.Contain("origin=artifact"));
            Assert.That(line, Does.Contain("generation='worker_v1.0.0.0_7143fd72'"));
            Assert.That(line, Does.Contain("overlay_dirs=0"));
            Assert.That(line, Does.Contain("overlay_files=0"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [Test]
    public void TryTerminateProcess_実行中processを停止できる()
    {
        using Process process = new();
        bool started = false;
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add("Start-Sleep -Seconds 30");

        try
        {
            started = process.Start();
            Assert.That(started, Is.True);

            bool stopped = ThumbnailRescueWorkerLauncher.TryTerminateProcess(
                process,
                waitMilliseconds: 2000
            );

            Assert.That(stopped, Is.True);
            Assert.That(process.WaitForExit(5000), Is.True);
            Assert.That(process.HasExited, Is.True);
        }
        finally
        {
            // 起動成功後だけプロセス状態へ触れて、後始末で元例外を潰さないようにする。
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
    }

    [Test]
    public void TryTerminateSessionToolProcesses_session配下のffmpegを停止できる()
    {
        string testRoot = CreateTempDirectory("imm-rescue-launcher-session-tool-kill");
        string sessionDirectory = Path.Combine(testRoot, "session");
        string ffmpegDirectory = Path.Combine(sessionDirectory, "tools", "ffmpeg");
        string ffmpegExecutablePath = Path.Combine(ffmpegDirectory, "ffmpeg.exe");
        using Process process = new();
        bool started = false;

        try
        {
            Directory.CreateDirectory(ffmpegDirectory);
            File.Copy(
                Path.Combine(
                    Environment.SystemDirectory,
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"
                ),
                ffmpegExecutablePath
            );

            process.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.StartInfo.ArgumentList.Add("-NoLogo");
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-Command");
            process.StartInfo.ArgumentList.Add("Start-Sleep -Seconds 30");

            started = process.Start();
            Assert.That(started, Is.True);

            int terminatedCount = ThumbnailRescueWorkerLauncher.TryTerminateSessionToolProcesses(
                sessionDirectory
            );

            Assert.That(terminatedCount, Is.EqualTo(1));
            Assert.That(process.WaitForExit(5000), Is.True);
            Assert.That(process.HasExited, Is.True);
        }
        finally
        {
            // 起動成功後だけプロセス状態へ触れて、後始末で元例外を潰さないようにする。
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }

            TryDeleteDirectory(testRoot);
        }
    }

    private static void CreatePublishArtifactMarker(
        string artifactDirectory,
        string compatibilityVersion = RescueWorkerArtifactContract.CompatibilityVersion
    )
    {
        File.WriteAllText(
            Path.Combine(
                artifactDirectory,
                ThumbnailRescueWorkerLaunchSettingsFactory.PublishedArtifactMarkerFileName
            ),
            $$"""
            {
              "artifactType": "IndigoMovieManager.Thumbnail.RescueWorker",
              "compatibilityVersion": "{{compatibilityVersion}}"
            }
            """
        );
    }

    private static void SeedCompletePublishedArtifact(string artifactDirectory)
    {
        Directory.CreateDirectory(Path.Combine(artifactDirectory, "Images"));
        Directory.CreateDirectory(Path.Combine(artifactDirectory, "tools", "ffmpeg-shared"));
        Directory.CreateDirectory(Path.Combine(artifactDirectory, "runtimes", "win-x64", "native"));
        File.WriteAllText(Path.Combine(artifactDirectory, "Images", "noFileSmall.jpg"), "image");
        File.WriteAllText(
            Path.Combine(artifactDirectory, "SQLitePCLRaw.batteries_v2.dll"),
            "batteries"
        );
        File.WriteAllText(Path.Combine(artifactDirectory, "SQLitePCLRaw.core.dll"), "core");
        File.WriteAllText(
            Path.Combine(artifactDirectory, "SQLitePCLRaw.provider.e_sqlite3.dll"),
            "provider"
        );
        File.WriteAllText(Path.Combine(artifactDirectory, "System.Data.SQLite.dll"), "sqlite");
        File.WriteAllText(Path.Combine(artifactDirectory, "e_sqlite3.dll"), "native-root");
        File.WriteAllText(
            Path.Combine(artifactDirectory, "runtimes", "win-x64", "native", "e_sqlite3.dll"),
            "native"
        );
        CreatePublishArtifactMarker(artifactDirectory);
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

    private static void SeedPendingRescueRecord(string mainDbPath, string moviePath)
    {
        ThumbnailFailureDbService service = new(mainDbPath);
        DateTime nowUtc = new(2026, 3, 18, 4, 8, 37, DateTimeKind.Utc);

        _ = service.AppendFailureRecord(
            new ThumbnailFailureRecord
            {
                MoviePath = moviePath,
                MoviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath),
                TabIndex = 2,
                Lane = "normal",
                AttemptGroupId = Guid.NewGuid().ToString("N"),
                AttemptNo = 1,
                Status = "pending_rescue",
                FailureKind = ThumbnailFailureKind.Unknown,
                FailureReason = "test",
                SourcePath = moviePath,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
            }
        );
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
