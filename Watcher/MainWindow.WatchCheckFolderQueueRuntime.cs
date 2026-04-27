using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // フォルダ再走査は単一実行に固定し、重複要求は後続1回へ圧縮する。
        private readonly SemaphoreSlim _checkFolderRunLock = new(1, 1);
        private readonly object _checkFolderRequestSync = new();
        private bool _hasPendingCheckFolderRequest;
        private CheckMode _pendingCheckFolderMode = CheckMode.Auto;
        private string _pendingCheckFolderFirstTrigger;
        private string _pendingCheckFolderLastTrigger;
        private int _pendingCheckFolderCompressionCount;
        private bool _isCheckFolderRunActive;
        private CheckMode _runningCheckFolderMode = CheckMode.Auto;
        private bool _hasPendingManualReloadDeferredRescueSuppression;
        private bool _isRunningManualReloadDeferredRescueSuppression;
        private bool _isCheckFolderQueueShutdownRequested;

        // テストでは本経路の呼び出し回数だけ観測し、既存の制御自体はそのまま通す。
        internal Action<string, string> QueueCheckFolderAsyncRequestedForTesting { get; set; }
        internal Func<string, string, Task> QueueCheckFolderAsyncForTesting { get; set; }

        private readonly struct CheckFolderQueueRequestTrace
        {
            internal CheckFolderQueueRequestTrace(
                CheckMode mode,
                string firstTrigger,
                string lastTrigger,
                int compressionCount
            )
            {
                Mode = mode;
                FirstTrigger = firstTrigger;
                LastTrigger = lastTrigger;
                CompressionCount = compressionCount;
            }

            internal CheckMode Mode { get; }
            internal string FirstTrigger { get; }
            internal string LastTrigger { get; }
            internal int CompressionCount { get; }
        }

        /// <summary>
        /// フォルダ更新要求をキューにブチ込む！連打されても後続1回に圧縮してPCの爆発を防ぐ超優秀な門番処理！🚧
        /// </summary>
        private Task QueueCheckFolderAsync(CheckMode mode, string trigger)
        {
            if (TryRejectQueueCheckFolderRequestForShutdown(mode, trigger))
            {
                return Task.CompletedTask;
            }

            if (TryInvokeQueueCheckFolderTestHook(mode, trigger, out Task testTask))
            {
                return testTask;
            }

            if (TryDeferQueueCheckFolderRequest(mode, trigger))
            {
                return Task.CompletedTask;
            }

            if (TrySkipRedundantEverythingPollScanRequest(mode, trigger))
            {
                return Task.CompletedTask;
            }

            QueueCheckFolderAsyncRequestedForTesting?.Invoke(mode.ToString(), trigger);
            RegisterManualReloadDeferredRescueSuppressionIfNeeded(mode, trigger);
            string queueTraceMessage = EnqueueCheckFolderRequest(mode, trigger);

            DebugRuntimeLog.Write(
                "watch-check",
                queueTraceMessage
            );
            return ProcessCheckFolderQueueAsync();
        }

        private void BeginCheckFolderQueueShutdownForClosing()
        {
            lock (_checkFolderRequestSync)
            {
                _isCheckFolderQueueShutdownRequested = true;
                _hasPendingCheckFolderRequest = false;
                ClearPendingCheckFolderTrace();
            }
        }

        private bool TryRejectQueueCheckFolderRequestForShutdown(CheckMode mode, string trigger)
        {
            bool isShutdownRequested;
            lock (_checkFolderRequestSync)
            {
                isShutdownRequested = _isCheckFolderQueueShutdownRequested;
            }

            if (!isShutdownRequested)
            {
                return false;
            }

            DebugRuntimeLog.Write(
                "watch-check",
                $"scan request skipped by shutdown: mode={mode} trigger={trigger}"
            );
            return true;
        }

        // テストフックが差し込まれている時だけ、通常キューを通さずに観測を優先する。
        private bool TryInvokeQueueCheckFolderTestHook(
            CheckMode mode,
            string trigger,
            out Task task
        )
        {
            Func<string, string, Task> testHook = QueueCheckFolderAsyncForTesting;
            if (testHook == null)
            {
                task = Task.CompletedTask;
                return false;
            }

            QueueCheckFolderAsyncRequestedForTesting?.Invoke(mode.ToString(), trigger);
            task = testHook(mode.ToString(), trigger);
            return true;
        }

        // watch抑止とuser-priority抑止の入口判定をまとめ、Queue入口の見通しを保つ。
        private bool TryDeferQueueCheckFolderRequest(CheckMode mode, string trigger)
        {
            if (ShouldSuppressWatchWorkByUi(IsWatchSuppressedByUi(), mode == CheckMode.Watch))
            {
                MarkWatchWorkDeferredWhileSuppressed(trigger);
                return true;
            }

            if (
                ShouldDeferBackgroundWorkForUserPriority(
                    IsUserPriorityWorkActive(),
                    mode == CheckMode.Manual
                )
                && TryMarkWatchWorkDeferredForUserPriorityCatchUp($"user-priority:{trigger}")
            )
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan request deferred by user priority: mode={mode} trigger={trigger}"
                );
                return true;
            }

            return false;
        }

        // pendingが既にある時は mode を強い側へマージし、後続1回へ圧縮する。
        private string EnqueueCheckFolderRequest(CheckMode mode, string trigger)
        {
            string queueTraceMessage;
            lock (_checkFolderRequestSync)
            {
                if (_hasPendingCheckFolderRequest)
                {
                    CheckMode previousMode = _pendingCheckFolderMode;
                    _pendingCheckFolderMode = MergeCheckMode(_pendingCheckFolderMode, mode);
                    _pendingCheckFolderLastTrigger = trigger;
                    _pendingCheckFolderCompressionCount++;
                    queueTraceMessage =
                        $"scan request compressed: mode={previousMode}->{_pendingCheckFolderMode} {BuildWatchCheckFolderQueueTraceSummary(_pendingCheckFolderFirstTrigger, _pendingCheckFolderLastTrigger, _pendingCheckFolderCompressionCount)}";
                }
                else
                {
                    _pendingCheckFolderMode = mode;
                    _pendingCheckFolderFirstTrigger = trigger;
                    _pendingCheckFolderLastTrigger = trigger;
                    _pendingCheckFolderCompressionCount = 0;
                    _hasPendingCheckFolderRequest = true;
                    queueTraceMessage =
                        $"scan request accepted: mode={mode} {BuildWatchCheckFolderQueueTraceSummary(_pendingCheckFolderFirstTrigger, _pendingCheckFolderLastTrigger, _pendingCheckFolderCompressionCount)}";
                }
            }

            return queueTraceMessage;
        }

        // EverythingPoll は定期確認なので、既に Watch/Manual が走る・待つ状態なら重ねて積まない。
        private bool TrySkipRedundantEverythingPollScanRequest(CheckMode mode, string trigger)
        {
            bool shouldSkip;
            CheckMode runningMode;
            CheckMode pendingMode;
            lock (_checkFolderRequestSync)
            {
                runningMode = _runningCheckFolderMode;
                pendingMode = _pendingCheckFolderMode;
                shouldSkip = ShouldSkipRedundantEverythingPollScanRequest(
                    mode,
                    trigger,
                    _isCheckFolderRunActive,
                    runningMode,
                    _hasPendingCheckFolderRequest,
                    pendingMode
                );
            }

            if (!shouldSkip)
            {
                return false;
            }

            DebugRuntimeLog.Write(
                "watch-check",
                $"everything poll scan skipped as duplicate: running={runningMode} pending={pendingMode}"
            );
            return true;
        }

        private static bool ShouldSkipRedundantEverythingPollScanRequest(
            CheckMode mode,
            string trigger,
            bool isRunActive,
            CheckMode runningMode,
            bool hasPendingRequest,
            CheckMode pendingMode
        )
        {
            if (
                mode != CheckMode.Watch
                || !string.Equals(trigger, "EverythingPoll", StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            if (isRunActive && IsWatchScanSupersetMode(runningMode))
            {
                return true;
            }

            return hasPendingRequest && IsWatchScanSupersetMode(pendingMode);
        }

        private static bool IsWatchScanSupersetMode(CheckMode mode)
        {
            return mode == CheckMode.Watch || mode == CheckMode.Manual;
        }

        // trigger文字列から由来とpathらしさだけを短く残し、圧縮後も因果を追える形へ整える。
        private static string BuildWatchCheckFolderQueueTraceSummary(
            string firstTrigger,
            string lastTrigger,
            int compressionCount
        )
        {
            return
                $"compressed={compressionCount} first={FormatWatchCheckFolderQueueTriggerForLog(firstTrigger)} last={FormatWatchCheckFolderQueueTriggerForLog(lastTrigger)}";
        }

        private static string FormatWatchCheckFolderQueueTriggerForLog(string trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger))
            {
                return "empty";
            }

            int separatorIndex = trigger.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= trigger.Length - 1)
            {
                return trigger;
            }

            string source = trigger.Substring(0, separatorIndex);
            string value = trigger.Substring(separatorIndex + 1);
            if (LooksLikeWindowsAbsolutePath(value))
            {
                string fileName = Path.GetFileName(value);
                string pathText = string.IsNullOrEmpty(fileName) ? value : fileName;
                return $"{source} path='{EscapeWatchCheckFolderQueueLogValue(pathText)}'";
            }

            return $"{source} value='{EscapeWatchCheckFolderQueueLogValue(value)}'";
        }

        private static bool LooksLikeWindowsAbsolutePath(string value)
        {
            return value.Length >= 3
                && char.IsLetter(value[0])
                && value[1] == ':'
                && (value[2] == '\\' || value[2] == '/');
        }

        private static string EscapeWatchCheckFolderQueueLogValue(string value)
        {
            return value.Replace("'", "''");
        }

        // 単一ランナーでキューを消化し、同時実行を防ぐ。
        private async Task ProcessCheckFolderQueueAsync()
        {
            await WaitForCheckFolderRunnerAsync();

            try
            {
                int processedCount = 0;
                CheckFolderQueueRequestTrace lastTrace = default;
                while (TryDequeuePendingCheckFolderRequest(out CheckFolderQueueRequestTrace request))
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan request dequeued: mode={request.Mode} {BuildWatchCheckFolderQueueTraceSummary(request.FirstTrigger, request.LastTrigger, request.CompressionCount)}"
                    );
                    MarkCheckFolderRunActive(request.Mode);
                    bool suppressMissingThumbnailRescue =
                        TryBeginManualReloadDeferredRescueSuppression(request.Mode);
                    try
                    {
                        await CheckFolderAsync(request.Mode);
                        processedCount++;
                        lastTrace = request;
                    }
                    finally
                    {
                        EndManualReloadDeferredRescueSuppression(suppressMissingThumbnailRescue);
                        ClearCheckFolderRunActive(request.Mode);
                    }
                }

                if (processedCount > 0)
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"scan queue drained: runs={processedCount} mode={lastTrace.Mode} {BuildWatchCheckFolderQueueTraceSummary(lastTrace.FirstTrigger, lastTrace.LastTrigger, lastTrace.CompressionCount)}"
                    );
                }
            }
            finally
            {
                _checkFolderRunLock.Release();
            }
        }

        // 0ms待機だと解放直前に入った要求を取りこぼすため、順番待ちで必ず取得する。
        private Task WaitForCheckFolderRunnerAsync()
        {
            return _checkFolderRunLock.WaitAsync();
        }

        // pending の取り出しとdequeueを1か所に寄せ、runner側は処理ループだけを見る。
        private bool TryDequeuePendingCheckFolderRequest(
            out CheckFolderQueueRequestTrace request
        )
        {
            lock (_checkFolderRequestSync)
            {
                if (!_hasPendingCheckFolderRequest)
                {
                    request = default;
                    return false;
                }

                request = new CheckFolderQueueRequestTrace(
                    _pendingCheckFolderMode,
                    _pendingCheckFolderFirstTrigger,
                    _pendingCheckFolderLastTrigger,
                    _pendingCheckFolderCompressionCount
                );
                _hasPendingCheckFolderRequest = false;
                ClearPendingCheckFolderTrace();
                return true;
            }
        }

        private void ClearPendingCheckFolderTrace()
        {
            _pendingCheckFolderFirstTrigger = null;
            _pendingCheckFolderLastTrigger = null;
            _pendingCheckFolderCompressionCount = 0;
        }

        // 実行中 mode を見える化し、poll が同種の後続走査を積む前に止められるようにする。
        private void MarkCheckFolderRunActive(CheckMode mode)
        {
            lock (_checkFolderRequestSync)
            {
                _runningCheckFolderMode = mode;
                _isCheckFolderRunActive = true;
            }
        }

        private void ClearCheckFolderRunActive(CheckMode mode)
        {
            lock (_checkFolderRequestSync)
            {
                if (_isCheckFolderRunActive && _runningCheckFolderMode == mode)
                {
                    _isCheckFolderRunActive = false;
                    _runningCheckFolderMode = CheckMode.Auto;
                }
            }
        }

        // Header再読込の deferred manual scan だけは、同じ run の欠損救済を最後まで止める。
        private void RegisterManualReloadDeferredRescueSuppressionIfNeeded(
            CheckMode mode,
            string trigger
        )
        {
            if (mode != CheckMode.Manual || !IsManualReloadDeferredScanTrigger(trigger))
            {
                return;
            }

            lock (_checkFolderRequestSync)
            {
                _hasPendingManualReloadDeferredRescueSuppression = true;
            }

            DebugRuntimeLog.Write(
                "watch-check",
                $"manual reload deferred rescue suppression registered: trigger={trigger}"
            );
        }

        // dequeue された Manual run が deferred reload 起点なら、その1回だけ抑止を有効にする。
        private bool TryBeginManualReloadDeferredRescueSuppression(CheckMode mode)
        {
            if (mode != CheckMode.Manual)
            {
                return false;
            }

            lock (_checkFolderRequestSync)
            {
                if (!_hasPendingManualReloadDeferredRescueSuppression)
                {
                    return false;
                }

                _hasPendingManualReloadDeferredRescueSuppression = false;
                _isRunningManualReloadDeferredRescueSuppression = true;
            }

            DebugRuntimeLog.Write(
                "watch-check",
                "manual reload deferred rescue suppression begin"
            );
            return true;
        }

        // deferred reload 起点の Manual run 終了で抑止も閉じ、他経路へ漏らさない。
        private void EndManualReloadDeferredRescueSuppression(bool wasActive)
        {
            if (!wasActive)
            {
                return;
            }

            lock (_checkFolderRequestSync)
            {
                _isRunningManualReloadDeferredRescueSuppression = false;
            }

            DebugRuntimeLog.Write(
                "watch-check",
                "manual reload deferred rescue suppression end"
            );
        }

        private bool IsManualReloadDeferredRescueSuppressionActive()
        {
            lock (_checkFolderRequestSync)
            {
                return _isRunningManualReloadDeferredRescueSuppression;
            }
        }
    }
}
