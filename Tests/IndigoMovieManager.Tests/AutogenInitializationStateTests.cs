using System.Reflection;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public class AutogenInitializationStateTests
{
    [Test]
    public void CanHandle_初期化失敗キャッシュ時はFalseを返す()
    {
        EngineInitState snapshot = CaptureInitState();
        try
        {
            SetInitState(
                new EngineInitState(
                    IsInitialized: false,
                    InitAttempted: true,
                    InitFailureReason: "simulated init failed"
                )
            );

            var engine = new FfmpegAutoGenThumbnailGenerationEngine();
            bool canHandle = engine.CanHandle(null);

            Assert.That(canHandle, Is.False);
        }
        finally
        {
            RestoreInitState(snapshot);
        }
    }

    [Test]
    public void CreateBookmarkInternal_キャンセル済みトークンは握り潰さず伝播する()
    {
        var engine = new FfmpegAutoGenThumbnailGenerationEngine();
        MethodInfo method =
            typeof(FfmpegAutoGenThumbnailGenerationEngine).GetMethod(
                "CreateBookmarkInternal",
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("method not found: CreateBookmarkInternal");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(engine, ["dummy.mp4", "dummy.jpg", 1, cts.Token])
        );
        Assert.That(ex?.InnerException, Is.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task CreateAsync_初期化失敗キャッシュ時は例外でなく失敗結果を返す()
    {
        EngineInitState snapshot = CaptureInitState();
        try
        {
            SetInitState(
                new EngineInitState(
                    IsInitialized: false,
                    InitAttempted: true,
                    InitFailureReason: "simulated init failed"
                )
            );

            var engine = new FfmpegAutoGenThumbnailGenerationEngine();
            var context = new ThumbnailJobContext
            {
                SaveThumbFileName = "dummy.jpg",
                DurationSec = 12,
            };

            ThumbnailCreateResult result1 = await engine.CreateAsync(context, CancellationToken.None);
            ThumbnailCreateResult result2 = await engine.CreateAsync(context, CancellationToken.None);

            Assert.That(result1.IsSuccess, Is.False);
            Assert.That(result2.IsSuccess, Is.False);
            Assert.That(result1.ErrorMessage, Does.Contain("simulated init failed"));
            Assert.That(result2.ErrorMessage, Does.Contain("simulated init failed"));
        }
        finally
        {
            RestoreInitState(snapshot);
        }
    }

    private static EngineInitState CaptureInitState()
    {
        return new EngineInitState(
            IsInitialized: (bool)(GetField("_isInitialized").GetValue(null) ?? false),
            InitAttempted: (bool)(GetField("_initAttempted").GetValue(null) ?? false),
            InitFailureReason: (string)(GetField("_initFailureReason").GetValue(null) ?? "")
        );
    }

    private static void RestoreInitState(EngineInitState state)
    {
        SetInitState(state);
    }

    private static void SetInitState(EngineInitState state)
    {
        GetField("_isInitialized").SetValue(null, state.IsInitialized);
        GetField("_initAttempted").SetValue(null, state.InitAttempted);
        GetField("_initFailureReason").SetValue(null, state.InitFailureReason ?? "");
    }

    private static FieldInfo GetField(string name)
    {
        return typeof(FfmpegAutoGenThumbnailGenerationEngine).GetField(
                name,
                BindingFlags.Static | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"field not found: {name}");
    }

    private readonly record struct EngineInitState(
        bool IsInitialized,
        bool InitAttempted,
        string InitFailureReason
    );
}
