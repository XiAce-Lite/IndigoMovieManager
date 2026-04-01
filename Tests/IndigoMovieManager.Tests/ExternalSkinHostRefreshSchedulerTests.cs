using System.Threading;
using System.Windows.Threading;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ExternalSkinHostRefreshSchedulerTests
{
    [Test]
    public async Task Queue_実行中に追加されたrefreshを直列化し最後の要求へ畳める()
    {
        RefreshSerializationResult result = await RunOnStaDispatcherAsync(async () =>
        {
            List<(int Generation, string Reason)> invocations = [];
            int currentConcurrency = 0;
            int maxConcurrency = 0;
            TaskCompletionSource<bool> firstStarted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> releaseFirst = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> secondCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            ExternalSkinHostRefreshScheduler scheduler = new(
                Dispatcher.CurrentDispatcher,
                async (generation, reason) =>
                {
                    currentConcurrency++;
                    maxConcurrency = Math.Max(maxConcurrency, currentConcurrency);
                    invocations.Add((generation, reason));

                    if (invocations.Count == 1)
                    {
                        firstStarted.TrySetResult(true);
                        await releaseFirst.Task;
                    }

                    if (invocations.Count == 2)
                    {
                        secondCompleted.TrySetResult(true);
                    }

                    currentConcurrency--;
                },
                ex => throw new AssertionException($"refresh drain failed: {ex.Message}")
            );

            scheduler.Queue("window-loaded");
            await WaitAsync(firstStarted.Task, TimeSpan.FromSeconds(5), "最初の refresh が始まりませんでした。");

            scheduler.Queue("dbinfo-Skin");
            scheduler.Queue("dbinfo-DBFullPath");
            releaseFirst.TrySetResult(true);

            await WaitAsync(
                secondCompleted.Task,
                TimeSpan.FromSeconds(5),
                "畳み込まれた 2 回目の refresh が完了しませんでした。"
            );

            return new RefreshSerializationResult(
                maxConcurrency,
                invocations.ToArray(),
                scheduler.CurrentGeneration
            );
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.MaxConcurrency, Is.EqualTo(1));
            Assert.That(result.Invocations, Has.Length.EqualTo(2));
            Assert.That(result.Invocations[0], Is.EqualTo((1, "window-loaded")));
            Assert.That(result.Invocations[1], Is.EqualTo((3, "dbinfo-DBFullPath")));
            Assert.That(result.FinalGeneration, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Queue_skinとDB切替が競合しても最後は最新generationだけ適用候補になる()
    {
        RefreshApplyResult result = await RunOnStaDispatcherAsync(async () =>
        {
            List<(int Generation, string Reason)> refreshed = [];
            List<(int Generation, string Reason)> applied = [];
            TaskCompletionSource<bool> firstStarted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> releaseFirst = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> latestApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            ExternalSkinHostRefreshScheduler? scheduler = null;
            scheduler = new ExternalSkinHostRefreshScheduler(
                Dispatcher.CurrentDispatcher,
                async (generation, reason) =>
                {
                    refreshed.Add((generation, reason));

                    if (generation == 1)
                    {
                        firstStarted.TrySetResult(true);
                        await releaseFirst.Task;
                    }

                    // MainWindow 側と同じく、完了時点で最新 generation だけを適用対象にする。
                    if (generation == scheduler!.CurrentGeneration)
                    {
                        applied.Add((generation, reason));
                        latestApplied.TrySetResult(true);
                    }
                },
                ex => throw new AssertionException($"refresh drain failed: {ex.Message}")
            );

            scheduler.Queue("dbinfo-Skin");
            await WaitAsync(firstStarted.Task, TimeSpan.FromSeconds(5), "最初の refresh が始まりませんでした。");

            scheduler.Queue("dbinfo-DBFullPath");
            releaseFirst.TrySetResult(true);

            await WaitAsync(
                latestApplied.Task,
                TimeSpan.FromSeconds(5),
                "最新 generation の適用判定が完了しませんでした。"
            );

            return new RefreshApplyResult(refreshed.ToArray(), applied.ToArray());
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Refreshed, Has.Length.EqualTo(2));
            Assert.That(result.Refreshed[0], Is.EqualTo((1, "dbinfo-Skin")));
            Assert.That(result.Refreshed[1], Is.EqualTo((2, "dbinfo-DBFullPath")));
            Assert.That(result.Applied, Is.EqualTo(new[] { (2, "dbinfo-DBFullPath") }));
        });
    }

    private static async Task WaitAsync(Task task, TimeSpan timeout, string timeoutMessage)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completedTask, task))
        {
            throw new AssertionException(timeoutMessage);
        }

        await task;
    }

    private static Task<T> RunOnStaDispatcherAsync<T>(Func<Task<T>> action)
    {
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(
            () =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)
                );
                _ = ExecuteAsync();
                Dispatcher.Run();

                async Task ExecuteAsync()
                {
                    try
                    {
                        T result = await action();
                        completion.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                    finally
                    {
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(
                            DispatcherPriority.Background
                        );
                    }
                }
            }
        );
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed record RefreshSerializationResult(
        int MaxConcurrency,
        (int Generation, string Reason)[] Invocations,
        int FinalGeneration
    );

    private sealed record RefreshApplyResult(
        (int Generation, string Reason)[] Refreshed,
        (int Generation, string Reason)[] Applied
    );
}
