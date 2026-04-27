using System;
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
        private bool _isCheckFolderRunActive;
        private CheckMode _runningCheckFolderMode = CheckMode.Auto;
        private bool _hasPendingManualReloadDeferredRescueSuppression;
        private bool _isRunningManualReloadDeferredRescueSuppression;
        private bool _isCheckFolderQueueShutdownRequested;

        // テストでは本経路の呼び出し回数だけ観測し、既存の制御自体はそのまま通す。
        internal Action<string, string> QueueCheckFolderAsyncRequestedForTesting { get; set; }
        internal Func<string, string, Task> QueueCheckFolderAsyncForTesting { get; set; }

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
            EnqueueCheckFolderRequest(mode);

            DebugRuntimeLog.Write(
                "watch-check",
                $"scan request queued: mode={mode} trigger={trigger}"
            );
            return ProcessCheckFolderQueueAsync();
        }

        private void BeginCheckFolderQueueShutdownForClosing()
        {
            lock (_checkFolderRequestSync)
            {
                _isCheckFolderQueueShutdownRequested = true;
                _hasPendingCheckFolderRequest = false;
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
        private void EnqueueCheckFolderRequest(CheckMode mode)
        {
            lock (_checkFolderRequestSync)
            {
                if (_hasPendingCheckFolderRequest)
                {
                    _pendingCheckFolderMode = MergeCheckMode(_pendingCheckFolderMode, mode);
                    return;
                }

                _pendingCheckFolderMode = mode;
                _hasPendingCheckFolderRequest = true;
            }
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

        // 単一ランナーでキューを消化し、同時実行を防ぐ。
        private async Task ProcessCheckFolderQueueAsync()
        {
            await WaitForCheckFolderRunnerAsync();

            try
            {
                while (TryDequeuePendingCheckFolderMode(out CheckMode modeToRun))
                {
                    MarkCheckFolderRunActive(modeToRun);
                    bool suppressMissingThumbnailRescue =
                        TryBeginManualReloadDeferredRescueSuppression(modeToRun);
                    try
                    {
                        await CheckFolderAsync(modeToRun);
                    }
                    finally
                    {
                        EndManualReloadDeferredRescueSuppression(suppressMissingThumbnailRescue);
                        ClearCheckFolderRunActive(modeToRun);
                    }
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
        private bool TryDequeuePendingCheckFolderMode(out CheckMode mode)
        {
            lock (_checkFolderRequestSync)
            {
                if (!_hasPendingCheckFolderRequest)
                {
                    mode = CheckMode.Auto;
                    return false;
                }

                mode = _pendingCheckFolderMode;
                _hasPendingCheckFolderRequest = false;
                return true;
            }
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
