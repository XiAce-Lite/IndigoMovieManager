using IndigoMovieManager.Thumbnail.RescueWorker;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class RescueWorkerApplicationTests
{
    private const string EngineTimeoutEnvName = "IMM_THUMB_RESCUE_ENGINE_TIMEOUT_SEC";

    [Test]
    public async Task RunWithTimeoutAsync_制限時間超過ならTimeoutExceptionへ変換する()
    {
        TimeoutException ex = Assert.ThrowsAsync<TimeoutException>(
            async () =>
                await RescueWorkerApplication.RunWithTimeoutAsync(
                    async cts =>
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200), cts);
                        return 1;
                    },
                    TimeSpan.FromMilliseconds(30),
                    "engine attempt timeout: failure_id=1 engine=ffmpeg1pass"
                )
        );

        Assert.That(ex?.Message, Does.Contain("timeout_sec=0"));
    }

    [Test]
    public async Task RunWithTimeoutAsync_時間内完了なら結果を返す()
    {
        int value = await RescueWorkerApplication.RunWithTimeoutAsync(
            _ => Task.FromResult(42),
            TimeSpan.FromSeconds(1),
            "unused"
        );

        Assert.That(value, Is.EqualTo(42));
    }

    [Test]
    public void ResolveTimeoutSeconds_環境変数が不正なら既定値へ戻す()
    {
        string? previous = Environment.GetEnvironmentVariable(EngineTimeoutEnvName);

        try
        {
            Environment.SetEnvironmentVariable(EngineTimeoutEnvName, "abc");

            TimeSpan timeout = RescueWorkerApplication.ResolveEngineAttemptTimeout();

            Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(120)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EngineTimeoutEnvName, previous);
        }
    }

    [Test]
    public void ResolveTimeoutSeconds_環境変数が小さすぎても下限へ丸める()
    {
        string? previous = Environment.GetEnvironmentVariable(EngineTimeoutEnvName);

        try
        {
            Environment.SetEnvironmentVariable(EngineTimeoutEnvName, "1");

            TimeSpan timeout = RescueWorkerApplication.ResolveEngineAttemptTimeout();

            Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EngineTimeoutEnvName, previous);
        }
    }
}
