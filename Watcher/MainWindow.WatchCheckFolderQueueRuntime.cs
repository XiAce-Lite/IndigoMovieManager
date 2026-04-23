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

        // テストでは本経路の呼び出し回数だけ観測し、既存の制御自体はそのまま通す。
        internal Action<string, string> QueueCheckFolderAsyncRequestedForTesting { get; set; }
        internal Func<string, string, Task> QueueCheckFolderAsyncForTesting { get; set; }

        /// <summary>
        /// フォルダ更新要求をキューにブチ込む！連打されても後続1回に圧縮してPCの爆発を防ぐ超優秀な門番処理！🚧
        /// </summary>
        private Task QueueCheckFolderAsync(CheckMode mode, string trigger)
        {
            if (TryInvokeQueueCheckFolderTestHook(mode, trigger, out Task testTask))
            {
                return testTask;
            }

            if (TryDeferQueueCheckFolderRequest(mode, trigger))
            {
                return Task.CompletedTask;
            }

            QueueCheckFolderAsyncRequestedForTesting?.Invoke(mode.ToString(), trigger);
            EnqueueCheckFolderRequest(mode);

            DebugRuntimeLog.Write(
                "watch-check",
                $"scan request queued: mode={mode} trigger={trigger}"
            );
            return ProcessCheckFolderQueueAsync();
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
            )
            {
                MarkWatchWorkDeferredForBackgroundCatchUp($"user-priority:{trigger}");
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

        // 単一ランナーでキューを消化し、同時実行を防ぐ。
        private async Task ProcessCheckFolderQueueAsync()
        {
            await WaitForCheckFolderRunnerAsync();

            try
            {
                while (TryDequeuePendingCheckFolderMode(out CheckMode modeToRun))
                {
                    await CheckFolderAsync(modeToRun);
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
    }
}
