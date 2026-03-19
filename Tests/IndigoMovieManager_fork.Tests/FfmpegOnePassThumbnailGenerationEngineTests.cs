using System.Diagnostics;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class FfmpegOnePassThumbnailGenerationEngineTests
{
    private const string FfmpegThreadCountEnvName = "IMM_THUMB_FFMPEG1PASS_THREADS";
    private const string FfmpegPriorityEnvName = "IMM_THUMB_FFMPEG1PASS_PRIORITY";

    [Test]
    public void RunProcessAsync_標準エラー待ち中でもキャンセルで抜けられる()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));
        ProcessStartInfo psi = new()
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("[Console]::Error.WriteLine('blocking'); Start-Sleep -Seconds 10");

        Stopwatch sw = Stopwatch.StartNew();

        Exception? ex = Assert.CatchAsync<OperationCanceledException>(
            async () => await FfmpegOnePassThumbnailGenerationEngine.RunProcessAsync(psi, cts.Token)
        );

        sw.Stop();
        Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void ResolveThreadCountFromEnvironment_1以上の整数だけ採用する()
    {
        WithEnvironmentVariable(
            FfmpegThreadCountEnvName,
            "2",
            () =>
            {
                int? actual = FfmpegOnePassThumbnailGenerationEngine.ResolveThreadCountFromEnvironment();
                Assert.That(actual, Is.EqualTo(2));
            }
        );
    }

    [Test]
    public void ResolveThreadCountFromEnvironment_不正値はnullへ落とす()
    {
        WithEnvironmentVariable(
            FfmpegThreadCountEnvName,
            "0",
            () =>
            {
                int? actual = FfmpegOnePassThumbnailGenerationEngine.ResolveThreadCountFromEnvironment();
                Assert.That(actual, Is.Null);
            }
        );
    }

    [Test]
    public void ResolveProcessPriorityClassFromEnvironment_eco向け別名を解決できる()
    {
        WithEnvironmentVariable(
            FfmpegPriorityEnvName,
            "low",
            () =>
            {
                ProcessPriorityClass? actual =
                    FfmpegOnePassThumbnailGenerationEngine.ResolveProcessPriorityClassFromEnvironment();
                Assert.That(actual, Is.EqualTo(ProcessPriorityClass.BelowNormal));
            }
        );
    }

    [Test]
    public void AddThreadArguments_指定時だけthreads引数を積む()
    {
        ProcessStartInfo psi = new();

        FfmpegOnePassThumbnailGenerationEngine.AddThreadArguments(psi, 1);

        Assert.That(psi.ArgumentList, Is.EqualTo(new[] { "-threads", "1" }));
    }

    [Test]
    public void ResolveEffectiveThreadCount_slowLaneは環境変数より1を優先する()
    {
        WithEnvironmentVariable(
            FfmpegThreadCountEnvName,
            "8",
            () =>
            {
                ThumbnailJobContext context = new() { IsSlowLane = true };
                int? actual = FfmpegOnePassThumbnailGenerationEngine.ResolveEffectiveThreadCount(
                    context
                );
                Assert.That(actual, Is.EqualTo(1));
            }
        );
    }

    [Test]
    public void ResolveEffectiveProcessPriorityClass_slowLaneはIdleを優先する()
    {
        WithEnvironmentVariable(
            FfmpegPriorityEnvName,
            "high",
            () =>
            {
                ThumbnailJobContext context = new() { IsSlowLane = true };
                ProcessPriorityClass? actual =
                    FfmpegOnePassThumbnailGenerationEngine.ResolveEffectiveProcessPriorityClass(
                        context
                    );
                Assert.That(actual, Is.EqualTo(ProcessPriorityClass.Idle));
            }
        );
    }

    private static void WithEnvironmentVariable(string name, string? value, Action action)
    {
        string? before = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, before);
        }
    }
}
