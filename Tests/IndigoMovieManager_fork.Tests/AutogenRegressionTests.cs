using System.Reflection;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class AutogenRegressionTests
{
    private const string EngineEnvName = "IMM_THUMB_ENGINE";

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

    private static ThumbnailJobContext CreateContext(bool isManual, int tabIndex, long fileSizeBytes)
    {
        return new ThumbnailJobContext
        {
            QueueObj = new QueueObj
            {
                Tabindex = tabIndex,
                MovieId = 1,
                MovieFullPath = @"C:\dummy\movie.mp4",
            },
            TabInfo = new TabInfo(tabIndex, "testdb"),
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
