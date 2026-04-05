using System.Diagnostics;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class NormalLaneTimeoutLiveTests
{
    private const string InputMovieEnvName = "IMM_NORMAL_TIMEOUT_LIVE_INPUT";

    [Test]
    public async Task Live_超巨大動画が通常キューtimeoutで打ち切られる()
    {
        string moviePath = Environment.GetEnvironmentVariable(InputMovieEnvName)?.Trim().Trim('"') ?? "";
        if (string.IsNullOrWhiteSpace(moviePath) || !File.Exists(moviePath))
        {
            Assert.Ignore($"{InputMovieEnvName} に存在する動画ファイルを設定してください。");
            return;
        }

        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_normal_timeout_live",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);

        try
        {
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            // 通常キュー相当の入力だけを作り、救済worker を通さずに本流を確認する。
            FileInfo fileInfo = new(moviePath);
            QueueObj queueObj = new()
            {
                MovieId = 1,
                Tabindex = 0,
                MovieFullPath = moviePath,
                MovieSizeBytes = fileInfo.Length,
            };

            TimeSpan timeout = MainWindow.ResolveThumbnailNormalLaneTimeout();
            var service = ThumbnailCreationServiceFactory.CreateDefault();
            Stopwatch sw = Stopwatch.StartNew();

            // MainWindow の通常レーン timeout と同じ包み方で service を呼び出す。
            TimeoutException ex = Assert.ThrowsAsync<TimeoutException>(
                async () =>
                {
                    using CancellationTokenSource callerCts = new();
                    using CancellationTokenSource timeoutCts = new(timeout);
                    using CancellationTokenSource linkedCts =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            callerCts.Token,
                            timeoutCts.Token
                        );

                    try
                    {
                        _ = await service.CreateThumbAsync(
                            new ThumbnailCreateArgs
                            {
                                Request = queueObj.ToThumbnailRequest(),
                                DbName = "live-timeout",
                                ThumbFolder = thumbRoot,
                                IsResizeThumb = true,
                                IsManual = false,
                            },
                            linkedCts.Token
                        );
                    }
                    catch (OperationCanceledException)
                        when (timeoutCts.IsCancellationRequested && !callerCts.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"thumbnail normal lane timeout: movie='{moviePath}', tab={queueObj.Tabindex}, timeout_sec={timeout.TotalSeconds:0}"
                        );
                    }
                }
            )!;

            sw.Stop();
            TestContext.Out.WriteLine(
                $"live timeout confirmed: movie='{moviePath}' timeout_sec={timeout.TotalSeconds:0} elapsed_ms={sw.ElapsedMilliseconds} message='{ex.Message}'"
            );

            Assert.That(ex.Message, Does.Contain("thumbnail normal lane timeout"));
            Assert.That(sw.Elapsed, Is.LessThan(timeout + TimeSpan.FromSeconds(10)));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
