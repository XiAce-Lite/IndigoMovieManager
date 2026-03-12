using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 明示救済はQueueDBへ混ぜず、別キューで1本ずつ静かに処理する。
        private const int ThumbnailRescueIdleRetryDelayMs = 1500;
        private static readonly string[] ThumbnailRescueRepairExtensions =
        [
            ".mp4",
            ".mkv",
            ".avi",
            ".wmv",
            ".asf",
            ".divx",
        ];
        private static readonly string[] ThumbnailRescueRepairErrorKeywords =
        [
            "invalid data found",
            "moov atom not found",
            "video stream is missing",
            "no frames decoded",
            "find stream info failed",
            "stream info failed",
            "failed to open input",
            "avformat_open_input failed",
            "avformat_find_stream_info failed",
        ];
        private readonly ConcurrentQueue<ThumbnailRescueWorkItem> _thumbnailRescueQueue = new();
        private readonly ConcurrentDictionary<string, byte> _thumbnailRescuePendingKeys = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly SemaphoreSlim _thumbnailRescueSignal = new(0);
        private readonly IVideoIndexRepairService _thumbnailIndexRepairService =
            new VideoIndexRepairService();
        private Task _thumbnailRescueTask;
        private CancellationTokenSource _thumbnailRescueCts = new();
        private static readonly string[] ThumbnailErrorPlaceholderFileNames =
        [
            "errorSmall.jpg",
            "errorBig.jpg",
            "errorGrid.jpg",
            "errorList.jpg",
        ];

        // 起動中は救済ループを常駐させ、明示要求を受けたらすぐ拾えるようにする。
        private void EnsureThumbnailRescueTaskRunning(string trigger)
        {
            if (_thumbnailRescueTask != null && !_thumbnailRescueTask.IsCompleted)
            {
                return;
            }

            DebugRuntimeLog.TaskStart(nameof(RunThumbnailRescueLoopAsync), $"trigger={trigger}");
            _thumbnailRescueTask = RunThumbnailRescueLoopAsync(_thumbnailRescueCts.Token);
        }

        // DB切替や再起動時は古い要求を捨て、新しいMainDB前提で救済を立て直す。
        private void RestartThumbnailRescueTask()
        {
            ClearThumbnailRescueQueue();
            _thumbnailRescueCts.Cancel();
            DebugRuntimeLog.Write("thumbnail-rescue", "thumbnail rescue token canceled for restart.");

            _thumbnailRescueCts = new CancellationTokenSource();
            EnsureThumbnailRescueTaskRunning("RestartThumbnailTask");
        }

        // 明示救済要求だけを別キューへ積み、通常QueueDB経路とは切り離す。
        private bool TryEnqueueThumbnailRescueJob(
            QueueObj queueObj,
            bool requiresIdle,
            string reason
        )
        {
            if (queueObj == null || string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return false;
            }

            if (!isThumbnailQueueInputEnabled)
            {
                DebugRuntimeLog.Write("thumbnail-rescue", "enqueue skipped: input disabled.");
                return false;
            }

            QueueObj rescueQueueObj = CloneQueueObj(queueObj);
            rescueQueueObj.IsRescueRequest = true;

            if (TryGetMovieFileLength(rescueQueueObj.MovieFullPath, out long fileLength))
            {
                rescueQueueObj.MovieSizeBytes = fileLength;
            }

            string key = GetThumbnailJobKey(rescueQueueObj);
            if (!_thumbnailRescuePendingKeys.TryAdd(key, 0))
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"enqueue skipped duplicated: key={key} reason={reason}"
                );
                return false;
            }

            _thumbnailRescueQueue.Enqueue(
                new ThumbnailRescueWorkItem(rescueQueueObj, requiresIdle, reason)
            );
            _thumbnailProgressRuntime.RecordEnqueue(rescueQueueObj);
            RequestThumbnailProgressSnapshotRefresh();
            _thumbnailRescueSignal.Release();
            EnsureThumbnailRescueTaskRunning("rescue-enqueue");

            DebugRuntimeLog.Write(
                "thumbnail-rescue",
                $"enqueue accepted: path='{rescueQueueObj.MovieFullPath}' tab={rescueQueueObj.Tabindex} idle_only={requiresIdle} reason={reason}"
            );
            return true;
        }

        // UI上で error 画像が見えている動画は、通常キューへ戻さず救済レーンへ隔離する。
        private bool TryEnqueueThumbnailDisplayErrorRescueJob(QueueObj queueObj, string reason)
        {
            if (queueObj == null)
            {
                return false;
            }

            string currentDbName = MainVM?.DbInfo?.DBName ?? "";
            string currentThumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            TabInfo targetTabInfo = new(queueObj.Tabindex, currentDbName, currentThumbFolder);
            TryDeleteThumbnailErrorMarker(targetTabInfo.OutPath, queueObj.MovieFullPath);

            return TryEnqueueThumbnailRescueJob(
                queueObj,
                requiresIdle: true,
                reason: reason
            );
        }

        // 組み込みの error 代替画像だけを検出し、通常のパス名に含まれる error 文字列とは分離する。
        internal static bool IsThumbnailErrorPlaceholderPath(string thumbPath)
        {
            if (string.IsNullOrWhiteSpace(thumbPath))
            {
                return false;
            }

            string fileName = Path.GetFileName(thumbPath.Trim());
            for (int i = 0; i < ThumbnailErrorPlaceholderFileNames.Length; i++)
            {
                if (
                    string.Equals(
                        fileName,
                        ThumbnailErrorPlaceholderFileNames[i],
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        // 手動救済時は stale な失敗固定マーカーを先に消し、再実行を妨げない。
        private void TryDeleteThumbnailErrorMarker(string thumbOutPath, string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(thumbOutPath) || string.IsNullOrWhiteSpace(movieFullPath))
            {
                return;
            }

            try
            {
                string errorMarkerPath = Thumbnail.ThumbnailPathResolver.BuildErrorMarkerPath(
                    thumbOutPath,
                    movieFullPath
                );
                if (!Path.Exists(errorMarkerPath))
                {
                    return;
                }

                File.Delete(errorMarkerPath);
                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"deleted stale error marker: '{errorMarkerPath}'"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"delete error marker failed: movie='{movieFullPath}' reason='{ex.Message}'"
                );
            }
        }

        // 救済は1本ずつ処理し、通常キューとは別に静かに進める。
        private async Task RunThumbnailRescueLoopAsync(CancellationToken cts)
        {
            string endStatus = "completed";
            try
            {
                while (true)
                {
                    await _thumbnailRescueSignal.WaitAsync(cts).ConfigureAwait(false);
                    if (!_thumbnailRescueQueue.TryDequeue(out ThumbnailRescueWorkItem workItem))
                    {
                        continue;
                    }

                    if (
                        workItem.RequiresIdle
                        && TryGetCurrentQueueActiveCount(out int activeCount)
                        && activeCount > 0
                    )
                    {
                        // 通常キューが生きている間は後ろへ回し、アイドルを待つ。
                        _thumbnailRescueQueue.Enqueue(workItem);
                        await Task.Delay(ThumbnailRescueIdleRetryDelayMs, cts).ConfigureAwait(false);
                        _thumbnailRescueSignal.Release();
                        continue;
                    }

                    await ProcessThumbnailRescueWorkItemAsync(workItem, cts).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                endStatus = "canceled";
                throw;
            }
            catch (Exception ex)
            {
                endStatus = $"fault message='{ex.Message}'";
                throw;
            }
            finally
            {
                DebugRuntimeLog.TaskEnd(nameof(RunThumbnailRescueLoopAsync), $"status={endStatus}");
            }
        }

        // 実処理前後で進捗UIとログをそろえ、通常キューと見分けられるようにする。
        private async Task ProcessThumbnailRescueWorkItemAsync(
            ThumbnailRescueWorkItem workItem,
            CancellationToken cts
        )
        {
            QueueObj queueObj = workItem?.QueueObj;
            if (queueObj == null)
            {
                return;
            }

            string jobKey = GetThumbnailJobKey(queueObj);
            Stopwatch sw = Stopwatch.StartNew();
            _thumbnailProgressRuntime.MarkJobStarted(queueObj);
            RequestThumbnailProgressSnapshotRefresh();

            try
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"start: path='{queueObj.MovieFullPath}' tab={queueObj.Tabindex} reason={workItem.Reason}"
                );
                await RunThumbnailRescueWorkflowAsync(queueObj, cts).ConfigureAwait(false);
                sw.Stop();
                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"end: path='{queueObj.MovieFullPath}' tab={queueObj.Tabindex} elapsed_ms={sw.ElapsedMilliseconds}"
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"failed: path='{queueObj.MovieFullPath}' tab={queueObj.Tabindex} elapsed_ms={sw.ElapsedMilliseconds} reason='{ex.Message}'"
                );
            }
            finally
            {
                _thumbnailProgressRuntime.MarkJobCompleted(queueObj);
                RequestThumbnailProgressSnapshotRefresh();
                _thumbnailRescuePendingKeys.TryRemove(jobKey, out _);
            }
        }

        // 救済レーンでは通常より重い順路を許し、必要時だけ repair を差し込む。
        private async Task RunThumbnailRescueWorkflowAsync(QueueObj queueObj, CancellationToken cts)
        {
            try
            {
                await CreateThumbAsync(queueObj, false, cts).ConfigureAwait(false);
                return;
            }
            catch (ThumbnailCreateFailureException firstFailure)
            {
                string failureReason = firstFailure.FailureReason ?? "";
                if (!ShouldTryThumbnailIndexRepair(queueObj?.MovieFullPath, failureReason))
                {
                    throw;
                }

                using ThumbnailRescueRepairLease repairLease =
                    await TryCreateThumbnailRescueRepairLeaseAsync(queueObj, failureReason, cts)
                        .ConfigureAwait(false);
                if (repairLease == null)
                {
                    throw;
                }

                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"retry with repaired source: path='{queueObj?.MovieFullPath}' repaired='{repairLease.RepairedMoviePath}'"
                );
                await CreateThumbAsync(
                        queueObj,
                        false,
                        cts,
                        repairLease.RepairedMoviePath
                    )
                    .ConfigureAwait(false);
            }
        }

        private async Task<ThumbnailRescueRepairLease> TryCreateThumbnailRescueRepairLeaseAsync(
            QueueObj queueObj,
            string failureReason,
            CancellationToken cts
        )
        {
            string movieFullPath = queueObj?.MovieFullPath ?? "";
            if (!ShouldTryThumbnailIndexRepair(movieFullPath, failureReason))
            {
                return null;
            }

            VideoIndexProbeResult probeResult = await _thumbnailIndexRepairService
                .ProbeAsync(movieFullPath, cts)
                .ConfigureAwait(false);
            DebugRuntimeLog.Write(
                "thumbnail-repair",
                $"probe: movie='{movieFullPath}' detected={probeResult.IsIndexCorruptionDetected} reason='{probeResult.DetectionReason}' format='{probeResult.ContainerFormat}' error='{probeResult.ErrorCode}'"
            );
            if (!probeResult.IsIndexCorruptionDetected)
            {
                return null;
            }

            string repairedMoviePath = BuildThumbnailRescueRepairOutputPath(movieFullPath);
            VideoIndexRepairResult repairResult = await _thumbnailIndexRepairService
                .RepairAsync(movieFullPath, repairedMoviePath, cts)
                .ConfigureAwait(false);
            DebugRuntimeLog.Write(
                "thumbnail-repair",
                $"repair: movie='{movieFullPath}' success={repairResult.IsSuccess} output='{repairResult.OutputPath}' reason='{repairResult.ErrorMessage}'"
            );
            if (!repairResult.IsSuccess || !Path.Exists(repairResult.OutputPath))
            {
                TryDeleteThumbnailRescueRepairFile(repairedMoviePath);
                return null;
            }

            return new ThumbnailRescueRepairLease(repairResult.OutputPath);
        }

        internal static bool ShouldTryThumbnailIndexRepair(
            string movieFullPath,
            string failureReason
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath) || string.IsNullOrWhiteSpace(failureReason))
            {
                return false;
            }

            string extension = Path.GetExtension(movieFullPath ?? "");
            if (
                !ThumbnailRescueRepairExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            string normalizedReason = failureReason.Trim().ToLowerInvariant();
            for (int i = 0; i < ThumbnailRescueRepairErrorKeywords.Length; i++)
            {
                if (normalizedReason.Contains(ThumbnailRescueRepairErrorKeywords[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildThumbnailRescueRepairOutputPath(string movieFullPath)
        {
            string repairRoot = Path.Combine(
                Path.GetTempPath(),
                "IndigoMovieManager_fork_workthree",
                "thumbnail-repair"
            );
            Directory.CreateDirectory(repairRoot);

            string extension = Path.GetExtension(movieFullPath ?? "");
            string normalizedExtension =
                string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase)
                    ? extension
                    : ".mkv";
            string fileName = $"{Guid.NewGuid():N}_repair{normalizedExtension}";
            return Path.Combine(repairRoot, fileName);
        }

        private static void TryDeleteThumbnailRescueRepairFile(string repairedMoviePath)
        {
            if (string.IsNullOrWhiteSpace(repairedMoviePath))
            {
                return;
            }

            try
            {
                if (Path.Exists(repairedMoviePath))
                {
                    File.Delete(repairedMoviePath);
                }
            }
            catch
            {
                // 一時修復ファイルの掃除失敗は本体処理より優先しない。
            }
        }

        // 未処理の救済要求を破棄して、旧DB向けの残り仕事を持ち越さない。
        private void ClearThumbnailRescueQueue()
        {
            while (_thumbnailRescueQueue.TryDequeue(out _))
            {
            }

            _thumbnailRescuePendingKeys.Clear();
            while (_thumbnailRescueSignal.Wait(0))
            {
            }
        }

        private sealed class ThumbnailRescueWorkItem
        {
            public ThumbnailRescueWorkItem(QueueObj queueObj, bool requiresIdle, string reason)
            {
                QueueObj = queueObj;
                RequiresIdle = requiresIdle;
                Reason = reason ?? "";
            }

            public QueueObj QueueObj { get; }
            public bool RequiresIdle { get; }
            public string Reason { get; }
        }

        private sealed class ThumbnailRescueRepairLease : IDisposable
        {
            public ThumbnailRescueRepairLease(string repairedMoviePath)
            {
                RepairedMoviePath = repairedMoviePath ?? "";
            }

            public string RepairedMoviePath { get; }

            public void Dispose()
            {
                TryDeleteThumbnailRescueRepairFile(RepairedMoviePath);
            }
        }
    }
}
