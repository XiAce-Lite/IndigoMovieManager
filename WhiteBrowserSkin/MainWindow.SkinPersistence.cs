using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IndigoMovieManager.Skin;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int WhiteBrowserSkinStatePersistBatchWindowMs = 100;

        // skin 状態保存は単一ライターへ寄せ、UI からは request を積むだけにする。
        private readonly Channel<WhiteBrowserSkinStatePersistRequest> _whiteBrowserSkinStatePersistChannel =
            Channel.CreateUnbounded<WhiteBrowserSkinStatePersistRequest>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                }
            );

        private readonly WhiteBrowserSkinStatePersister _whiteBrowserSkinStatePersister;
        private Task _whiteBrowserSkinStatePersisterTask;
        private CancellationTokenSource _whiteBrowserSkinStatePersisterCts = new();

        private bool TryEnqueueWhiteBrowserSkinStatePersistRequest(
            WhiteBrowserSkinStatePersistRequest request
        )
        {
            if (
                request == null
                || string.IsNullOrWhiteSpace(request.DbFullPath)
                || string.IsNullOrWhiteSpace(request.Key)
            )
            {
                return false;
            }

            bool queued = _whiteBrowserSkinStatePersistChannel.Writer.TryWrite(request);
            if (!queued)
            {
                DebugRuntimeLog.Write(
                    "skin-db",
                    $"persist queue rejected: db='{request.DbFullPath}' target={request.TargetKind} profile='{request.ProfileName}' key='{request.Key}'"
                );
            }

            return queued;
        }

        private bool TryEnqueueExternalSkinProfileWrite(string dbFullPath, string skinName, string key, string value)
        {
            if (
                string.IsNullOrWhiteSpace(dbFullPath)
                || string.IsNullOrWhiteSpace(skinName)
                || string.IsNullOrWhiteSpace(key)
            )
            {
                return false;
            }

            return TryEnqueueWhiteBrowserSkinStatePersistRequest(
                WhiteBrowserSkinStatePersistRequest.CreateProfile(dbFullPath, skinName, key, value ?? "")
            );
        }

        private async Task RunWhiteBrowserSkinStatePersisterSupervisorAsync(CancellationToken cts)
        {
            await Task.Yield();

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await _whiteBrowserSkinStatePersister.RunAsync(cts).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "skin-db",
                        $"skin state persister restart scheduled: {ex.Message}"
                    );
                    try
                    {
                        await Task.Delay(500, cts).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        private void BeginWhiteBrowserSkinStatePersisterShutdown()
        {
            _whiteBrowserSkinStatePersistChannel.Writer.TryComplete();
            DebugRuntimeLog.Write(
                "lifecycle",
                "shutdown: skin state persister input complete requested."
            );
        }

        private void DrainWhiteBrowserSkinStatePersisterForShutdown()
        {
            if (_whiteBrowserSkinStatePersisterTask == null)
            {
                return;
            }

            try
            {
                Task completed = Task
                    .WhenAny(_whiteBrowserSkinStatePersisterTask, Task.Delay(500))
                    .GetAwaiter()
                    .GetResult();
                if (ReferenceEquals(completed, _whiteBrowserSkinStatePersisterTask))
                {
                    if (_whiteBrowserSkinStatePersisterTask.IsFaulted)
                    {
                        string message =
                            _whiteBrowserSkinStatePersisterTask.Exception?.GetBaseException()?.Message
                            ?? "unknown";
                        DebugRuntimeLog.Write(
                            "lifecycle",
                            $"skin-state-persister faulted: {message}"
                        );
                    }

                    return;
                }

                DebugRuntimeLog.Write(
                    "lifecycle",
                    $"skin-state-persister drain timeout: 500ms status={_whiteBrowserSkinStatePersisterTask.Status}"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "lifecycle",
                    $"skin-state-persister drain wait failed: {ex.Message}"
                );
            }

            _whiteBrowserSkinStatePersisterCts.Cancel();
            WaitBackgroundTaskForShutdown(_whiteBrowserSkinStatePersisterTask, "skin-state-persister");
        }
    }
}
