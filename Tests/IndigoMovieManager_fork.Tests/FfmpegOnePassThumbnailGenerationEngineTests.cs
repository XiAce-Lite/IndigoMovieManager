using System.Diagnostics;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class FfmpegOnePassThumbnailGenerationEngineTests
{
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

        Exception? ex = Assert.ThrowsAsync<Exception>(
            async () => await FfmpegOnePassThumbnailGenerationEngine.RunProcessAsync(psi, cts.Token)
        );

        sw.Stop();
        Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
    }
}
