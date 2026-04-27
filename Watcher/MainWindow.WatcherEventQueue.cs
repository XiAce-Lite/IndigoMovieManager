using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // watch イベント入口は Created / Renamed を共通 queue へ寄せ、イベントハンドラを薄く保つ。
        private readonly SemaphoreSlim _watchEventRunLock = new(1, 1);
        private readonly object _watchEventRequestSync = new();
        private readonly Queue<WatchEventRequest> _watchEventRequests = new();
        private Task _watchEventProcessingTask = Task.CompletedTask;
        private Task _watchCreatedEventProcessingTask = Task.CompletedTask;
        private bool _watchEventShutdownRequested;

        // watch イベント要求を共通 queue へ積み、イベントハンドラから重い処理を切り離す。
        private Task QueueWatchEventAsync(WatchEventRequest request, string trigger)
        {
            if (string.IsNullOrWhiteSpace(request.FullPath))
            {
                return Task.CompletedTask;
            }

            Task processingTask;
            bool skippedByShutdown = false;
            lock (_watchEventRequestSync)
            {
                if (_watchEventShutdownRequested)
                {
                    skippedByShutdown = true;
                    processingTask = Task.CompletedTask;
                }
                else
                {
                    _watchEventRequests.Enqueue(request);
                    if (_watchEventProcessingTask.IsCompleted)
                    {
                        _watchEventProcessingTask = ProcessWatchEventQueueAsync();
                    }

                    processingTask = _watchEventProcessingTask;
                }
            }

            if (skippedByShutdown)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watch event skipped by shutdown: trigger={trigger} kind={request.Kind} path='{request.FullPath}' old='{request.OldFullPath}'"
                );
                return Task.CompletedTask;
            }

            DebugRuntimeLog.Write(
                "watch",
                $"watch event queued: trigger={trigger} kind={request.Kind} path='{request.FullPath}' old='{request.OldFullPath}'"
            );

            return processingTask;
        }

        // Closing開始後は新規イベント受付を止め、現在走っているqueue/taskだけを穏当にdrainする。
        private void BeginWatchEventQueueShutdownForClosing()
        {
            BeginCheckFolderQueueShutdownForClosing();
            lock (_watchEventRequestSync)
            {
                _watchEventShutdownRequested = true;
            }
        }

        // shutdown 中は watch queue -> created ready の順で、合計500ms以内だけ待機する。
        private void DrainWatchEventPipelinesForShutdown()
        {
            const int shutdownDrainBudgetMs = 500;
            long deadlineTick = Environment.TickCount64 + shutdownDrainBudgetMs;
            WaitWatchPipelineTaskForShutdown(
                () => _watchEventProcessingTask,
                "watch-event-queue",
                deadlineTick
            );
            WaitWatchPipelineTaskForShutdown(
                () => _watchCreatedEventProcessingTask,
                "watch-created-pipeline",
                deadlineTick
            );
        }

        // queue runner が task 参照を差し替えるため、完了ごとに再取得して安定化を確認する。
        private void WaitWatchPipelineTaskForShutdown(
            Func<Task> taskSelector,
            string taskName,
            long deadlineTick
        )
        {
            while (true)
            {
                Task targetTask;
                lock (_watchEventRequestSync)
                {
                    targetTask = taskSelector?.Invoke() ?? Task.CompletedTask;
                }

                if (targetTask.IsCompleted)
                {
                    return;
                }

                long remainingMsLong = deadlineTick - Environment.TickCount64;
                if (remainingMsLong <= 0)
                {
                    DebugRuntimeLog.Write(
                        "lifecycle",
                        $"{taskName} wait timeout: shutdown budget exhausted status={targetTask.Status}"
                    );
                    return;
                }

                int remainingMs = remainingMsLong > int.MaxValue
                    ? int.MaxValue
                    : (int)remainingMsLong;
                Task completed = Task.WhenAny(targetTask, Task.Delay(remainingMs))
                    .GetAwaiter()
                    .GetResult();
                if (!ReferenceEquals(completed, targetTask))
                {
                    DebugRuntimeLog.Write(
                        "lifecycle",
                        $"{taskName} wait timeout: {remainingMs}ms status={targetTask.Status}"
                    );
                    return;
                }

                if (targetTask.IsFaulted)
                {
                    string message = targetTask.Exception?.GetBaseException()?.Message ?? "unknown";
                    DebugRuntimeLog.Write("lifecycle", $"{taskName} faulted: {message}");
                }
            }
        }

        // Renamed は直列処理を保ち、Created の ready 待ちは別タスクへ逃がして後続イベントを詰まらせない。
        private async Task ProcessWatchEventQueueAsync()
        {
            await _watchEventRunLock.WaitAsync();
            try
            {
                while (true)
                {
                    WatchEventRequest request;
                    lock (_watchEventRequestSync)
                    {
                        if (_watchEventRequests.Count < 1)
                        {
                            _watchEventProcessingTask = Task.CompletedTask;
                            break;
                        }

                        request = _watchEventRequests.Dequeue();
                    }

                    await ProcessQueuedWatchEventAsync(request);
                }
            }
            finally
            {
                _watchEventRunLock.Release();
            }
        }

        // Created のコピー待ちは長くなるため、queue runner から切り離して rename などを先へ流す。
        private Task ProcessQueuedWatchEventAsync(WatchEventRequest request)
        {
            if (request.Kind == WatchEventKind.Created)
            {
                StartCreatedWatchEventReadyPipeline(request.FullPath);
                return Task.CompletedTask;
            }

            return ProcessWatchEventAsync(request);
        }

        private void StartCreatedWatchEventReadyPipeline(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }

            // Created の ready 待ちは queue runner から切り離しつつ、created 同士は直列化して過剰並列を防ぐ。
            lock (_watchEventRequestSync)
            {
                Task previousTask = _watchCreatedEventProcessingTask ?? Task.CompletedTask;
                _watchCreatedEventProcessingTask = ChainCreatedWatchEventReadyPipelineAsync(
                    previousTask,
                    fullPath
                );
            }

            DebugRuntimeLog.Write(
                "watch",
                $"created event ready wait detached: '{fullPath}'"
            );
        }

        private async Task ChainCreatedWatchEventReadyPipelineAsync(
            Task previousTask,
            string fullPath
        )
        {
            try
            {
                await previousTask;
            }
            catch
            {
                // 先行タスク失敗があっても後続 created は止めない。
            }

            await ProcessCreatedWatchEventReadyPipelineAsync(fullPath);
        }

        private async Task ProcessCreatedWatchEventReadyPipelineAsync(string fullPath)
        {
            try
            {
                await ProcessCreatedWatchEventAsync(fullPath);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"created watch event processing failed: path='{fullPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        // event kind ごとに処理を振り分け、重い実処理はイベントハンドラ外で行う。
        private async Task ProcessWatchEventAsync(WatchEventRequest request)
        {
            try
            {
                switch (request.Kind)
                {
                    case WatchEventKind.Created:
                        await ProcessCreatedWatchEventAsync(request.FullPath);
                        break;
                    case WatchEventKind.Renamed:
                        await RenameThumbAsync(request.FullPath, request.OldFullPath);
                        break;
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watch event processing failed: kind={request.Kind} path='{request.FullPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        // Created は queue 化後にファイル準備待ちと zero-byte 判定を行い、その後 watch 本流へ合流する。
        private async Task ProcessCreatedWatchEventAsync(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }

            if (IsWatchSuppressedByUi())
            {
                MarkWatchWorkDeferredWhileSuppressed($"created:{fullPath}");
                return;
            }

            bool fileReady = await WaitForWatchCreatedFileReadyAsync(fullPath);
            if (!fileReady)
            {
#if DEBUG
                Debug.WriteLine($"ファイル {fullPath} にアクセスできません。");
#endif
                return;
            }

            if (IsZeroByteMovieFile(fullPath, out long fileLength))
            {
                int? watchTabIndex = ResolveWatchMissingThumbnailTabIndex(MainVM.DbInfo.CurrentTabIndex);
                if (watchTabIndex.HasValue)
                {
                    TryCreateErrorMarkerForSkippedMovie(
                        fullPath,
                        watchTabIndex.Value,
                        "zero-byte movie(created event)"
                    );
                }
                DebugRuntimeLog.Write(
                    "watch",
                    $"skip zero-byte movie on created event: '{fullPath}' size={fileLength}"
                );
                return;
            }

            DebugRuntimeLog.Write(
                "watch",
                $"created event rerouted to queued watch scan: '{fullPath}'"
            );
            await QueueCheckFolderAsync(CheckMode.Watch, $"created:{fullPath}");
        }

        // コピー中ファイルは最大10回待機し、watch ハンドラ外で準備完了を確認する。
        private static async Task<bool> WaitForWatchCreatedFileReadyAsync(string fullPath)
        {
            const int maxRetry = 10;
            int retry = 0;
            while (retry < maxRetry)
            {
                try
                {
                    using var stream = File.Open(
                        fullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read
                    );
                    return true;
                }
                catch (IOException)
                {
                    await Task.Delay(1000);
                    retry++;
                }
            }

            return false;
        }

        private enum WatchEventKind
        {
            Created = 0,
            Renamed = 1,
        }

        private readonly record struct WatchEventRequest(
            WatchEventKind Kind,
            string FullPath,
            string OldFullPath
        );
    }
}
