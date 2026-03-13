using System.Reflection;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class AutogenRegressionTests
{
    private const string EngineEnvName = "IMM_THUMB_ENGINE";
    private const string AutogenEngineParallelEnvName = "IMM_THUMB_AUTOGEN_ENGINE_PARALLEL";
    private const string AutogenNativeParallelEnvName = "IMM_THUMB_AUTOGEN_NATIVE_PARALLEL";

    [Test]
    public void Router_Default_UsesAutogenFirst_ForNormalThumbnail()
    {
        // 通常サムネイルでは autogen を最初に選ぶことを固定する。
        var autogen = new FakeEngine("autogen");
        var ffmedia = new FakeEngine("ffmediatoolkit");
        var ffmpeg1pass = new FakeEngine("ffmpeg1pass");
        var opencv = new FakeEngine("opencv");
        var router = new ThumbnailEngineRouter([ffmedia, ffmpeg1pass, opencv, autogen]);

        var context = CreateContext(isManual: false, tabIndex: 0, fileSizeBytes: 1024);
        var selected = router.ResolveForThumbnail(context);

        Assert.That(selected.EngineId, Is.EqualTo("autogen"));
    }

    [Test]
    public void Router_Manual_TimeSpecified_UsesAutogenFirst()
    {
        // 手動サムネイルも既定は autogen を最初に選ぶ。
        var autogen = new FakeEngine("autogen");
        var ffmedia = new FakeEngine("ffmediatoolkit");
        var ffmpeg1pass = new FakeEngine("ffmpeg1pass");
        var opencv = new FakeEngine("opencv");
        var router = new ThumbnailEngineRouter([autogen, ffmedia, ffmpeg1pass, opencv]);

        var context = CreateContext(isManual: true, tabIndex: 0, fileSizeBytes: 1024);
        var selected = router.ResolveForThumbnail(context);

        Assert.That(selected.EngineId, Is.EqualTo("autogen"));
    }

    [Test]
    public void Router_RescueLane_UsesFfmpegOnePassFirst()
    {
        // 救済レーンだけは通常系と分離し、ffmpeg1pass を先頭にする。
        var autogen = new FakeEngine("autogen");
        var ffmedia = new FakeEngine("ffmediatoolkit");
        var ffmpeg1pass = new FakeEngine("ffmpeg1pass");
        var opencv = new FakeEngine("opencv");
        var router = new ThumbnailEngineRouter([autogen, ffmedia, ffmpeg1pass, opencv]);

        var context = CreateContext(isManual: false, tabIndex: 0, fileSizeBytes: 1024);
        context.QueueObj.IsRescueRequest = true;
        var selected = router.ResolveForThumbnail(context);

        Assert.That(selected.EngineId, Is.EqualTo("ffmpeg1pass"));
    }

    [Test]
    public void Router_ForcedEngineEnv_Wins()
    {
        // 強制指定があるときは通常判定より環境変数を優先する。
        string? rawBackup = Environment.GetEnvironmentVariable(EngineEnvName);
        bool hadBackup = rawBackup != null;
        string backup = rawBackup ?? string.Empty;
        try
        {
            Environment.SetEnvironmentVariable(EngineEnvName, "ffmediatoolkit");

            var autogen = new FakeEngine("autogen");
            var ffmedia = new FakeEngine("ffmediatoolkit");
            var ffmpeg1pass = new FakeEngine("ffmpeg1pass");
            var opencv = new FakeEngine("opencv");
            var router = new ThumbnailEngineRouter([autogen, ffmedia, ffmpeg1pass, opencv]);

            var context = CreateContext(isManual: false, tabIndex: 0, fileSizeBytes: 1024);
            var selected = router.ResolveForThumbnail(context);

            Assert.That(selected.EngineId, Is.EqualTo("ffmediatoolkit"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EngineEnvName, hadBackup ? backup : null);
        }
    }

    [Test]
    public void Service_AutogenSelected_FallbackOrder_IsStable()
    {
        // autogen 失敗時のフォールバック順が意図どおりであることを固定する。
        string? rawBackup = Environment.GetEnvironmentVariable(EngineEnvName);
        bool hadBackup = rawBackup != null;
        string backup = rawBackup ?? string.Empty;
        try
        {
            Environment.SetEnvironmentVariable(EngineEnvName, "auto");

            var autogen = new FakeEngine("autogen");
            var ffmedia = new FakeEngine("ffmediatoolkit");
            var ffmpeg1pass = new FakeEngine("ffmpeg1pass");
            var opencv = new FakeEngine("opencv");
            var service = new ThumbnailCreationService(ffmedia, ffmpeg1pass, opencv, autogen);

            var context = CreateContext(isManual: false, tabIndex: 0, fileSizeBytes: 1024);
            var order = InvokeBuildThumbnailEngineOrder(service, autogen, context);
            string actual = string.Join(">", order.Select(x => x.EngineId));

            Assert.That(actual, Is.EqualTo("autogen>ffmediatoolkit>ffmpeg1pass>opencv"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EngineEnvName, hadBackup ? backup : null);
        }
    }

    [Test]
    public void Service_RescueSelected_FallbackOrder_IsStable()
    {
        // 救済時は ffmpeg1pass -> autogen の順を維持する。
        string? rawBackup = Environment.GetEnvironmentVariable(EngineEnvName);
        bool hadBackup = rawBackup != null;
        string backup = rawBackup ?? string.Empty;
        try
        {
            Environment.SetEnvironmentVariable(EngineEnvName, "auto");

            var autogen = new FakeEngine("autogen");
            var ffmedia = new FakeEngine("ffmediatoolkit");
            var ffmpeg1pass = new FakeEngine("ffmpeg1pass");
            var opencv = new FakeEngine("opencv");
            var service = new ThumbnailCreationService(ffmedia, ffmpeg1pass, opencv, autogen);

            var context = CreateContext(isManual: false, tabIndex: 0, fileSizeBytes: 1024);
            context.QueueObj.IsRescueRequest = true;
            var order = InvokeBuildThumbnailEngineOrder(service, ffmpeg1pass, context);
            string actual = string.Join(">", order.Select(x => x.EngineId));

            Assert.That(actual, Is.EqualTo("ffmpeg1pass>autogen>ffmediatoolkit>opencv"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EngineEnvName, hadBackup ? backup : null);
        }
    }

    [Test]
    public void Autogen_EngineParallelLimit_DefaultsToOne_WhenEnvMissing()
    {
        string? rawBackup = Environment.GetEnvironmentVariable(AutogenEngineParallelEnvName);
        bool hadBackup = rawBackup != null;
        string backup = rawBackup ?? string.Empty;
        try
        {
            Environment.SetEnvironmentVariable(AutogenEngineParallelEnvName, null);

            int resolved = InvokeResolveParallelLimit(AutogenEngineParallelEnvName, 1);

            Assert.That(resolved, Is.EqualTo(1));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AutogenEngineParallelEnvName,
                hadBackup ? backup : null
            );
        }
    }

    [Test]
    public void Autogen_ParallelLimit_ClampsInvalidValues()
    {
        string? engineBackup = Environment.GetEnvironmentVariable(AutogenEngineParallelEnvName);
        bool hadEngineBackup = engineBackup != null;
        string engineValue = engineBackup ?? string.Empty;
        string? nativeBackup = Environment.GetEnvironmentVariable(AutogenNativeParallelEnvName);
        bool hadNativeBackup = nativeBackup != null;
        string nativeValue = nativeBackup ?? string.Empty;
        try
        {
            Environment.SetEnvironmentVariable(AutogenEngineParallelEnvName, "999");
            Environment.SetEnvironmentVariable(AutogenNativeParallelEnvName, "abc");

            int engineResolved = InvokeResolveParallelLimit(AutogenEngineParallelEnvName, 1);
            int nativeResolved = InvokeResolveParallelLimit(AutogenNativeParallelEnvName, 1);

            Assert.That(engineResolved, Is.EqualTo(8));
            Assert.That(nativeResolved, Is.EqualTo(1));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AutogenEngineParallelEnvName,
                hadEngineBackup ? engineValue : null
            );
            Environment.SetEnvironmentVariable(
                AutogenNativeParallelEnvName,
                hadNativeBackup ? nativeValue : null
            );
        }
    }

    private static ThumbnailJobContext CreateContext(bool isManual, int tabIndex, long fileSizeBytes)
    {
        string testThumbRoot = BuildTestThumbRoot();
        return new ThumbnailJobContext
        {
            QueueObj = new QueueObj
            {
                Tabindex = tabIndex,
                MovieId = 1,
                MovieFullPath = @"C:\dummy\movie.mp4",
            },
            // テストがリポジトリ直下の Thumb を触らないよう、一時ルートを明示する。
            TabInfo = new TabInfo(tabIndex, "testdb", testThumbRoot),
            ThumbInfo = new ThumbInfo(),
            MovieFullPath = @"C:\dummy\movie.mp4",
            SaveThumbFileName = @"C:\dummy\out.jpg",
            IsResizeThumb = true,
            IsManual = isManual,
            DurationSec = 120,
            FileSizeBytes = fileSizeBytes,
            AverageBitrateMbps = 8,
            HasEmojiPath = false,
            VideoCodec = "h264",
        };
    }

    private static string BuildTestThumbRoot()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_workthree.Tests",
            "thumb",
            Guid.NewGuid().ToString("N")
        );
    }

    private static List<IThumbnailGenerationEngine> InvokeBuildThumbnailEngineOrder(
        ThumbnailCreationService service,
        IThumbnailGenerationEngine selectedEngine,
        ThumbnailJobContext context
    )
    {
        MethodInfo? method = typeof(ThumbnailCreationService).GetMethod(
            "BuildThumbnailEngineOrder",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        Assert.That(method != null, Is.True);

        object? raw = method!.Invoke(service, [selectedEngine, context]);
        return raw as List<IThumbnailGenerationEngine> ?? [];
    }

    private static int InvokeResolveParallelLimit(string envName, int defaultValue)
    {
        MethodInfo? method = typeof(FfmpegAutoGenThumbnailGenerationEngine).GetMethod(
            "ResolveParallelLimit",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        Assert.That(method != null, Is.True);

        object? raw = method!.Invoke(null, [envName, defaultValue]);
        return raw is int value ? value : -1;
    }

    private sealed class FakeEngine : IThumbnailGenerationEngine
    {
        public FakeEngine(string engineId)
        {
            EngineId = engineId;
            EngineName = engineId;
        }

        public string EngineId { get; }
        public string EngineName { get; }

        public bool CanHandle(ThumbnailJobContext context)
        {
            return true;
        }

        public Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken cts = default
        )
        {
            return Task.FromResult(
                ThumbnailCreationService.CreateFailedResult(
                    context?.SaveThumbFileName ?? string.Empty,
                    context?.DurationSec,
                    "test"
                )
            );
        }

        public Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            return Task.FromResult(false);
        }
    }
}
