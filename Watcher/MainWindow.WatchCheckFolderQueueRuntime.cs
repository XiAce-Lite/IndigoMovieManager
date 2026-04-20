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
            Func<string, string, Task> testHook = QueueCheckFolderAsyncForTesting;
            if (testHook != null)
            {
                QueueCheckFolderAsyncRequestedForTesting?.Invoke(mode.ToString(), trigger);
                return testHook(mode.ToString(), trigger);
            }

            if (ShouldSuppressWatchWorkByUi(IsWatchSuppressedByUi(), mode == CheckMode.Watch))
            {
                MarkWatchWorkDeferredWhileSuppressed(trigger);
                return Task.CompletedTask;
            }

            if (ShouldDeferBackgroundWorkForUserPriority(IsUserPriorityWorkActive(), mode == CheckMode.Manual))
            {
                MarkWatchWorkDeferredWhileSuppressed($"user-priority:{trigger}");
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"scan request deferred by user priority: mode={mode} trigger={trigger}"
                );
                return Task.CompletedTask;
            }

            QueueCheckFolderAsyncRequestedForTesting?.Invoke(mode.ToString(), trigger);

            lock (_checkFolderRequestSync)
            {
                if (_hasPendingCheckFolderRequest)
                {
                    _pendingCheckFolderMode = MergeCheckMode(_pendingCheckFolderMode, mode);
                }
                else
                {
                    _pendingCheckFolderMode = mode;
                    _hasPendingCheckFolderRequest = true;
                }
            }

            DebugRuntimeLog.Write(
                "watch-check",
                $"scan request queued: mode={mode} trigger={trigger}"
            );
            return ProcessCheckFolderQueueAsync();
        }

        // 単一ランナーでキューを消化し、同時実行を防ぐ。
        private async Task ProcessCheckFolderQueueAsync()
        {
            // 0ms待機だと、解放直前に入った要求を取りこぼす競合が起きるため、順番待ちで必ず取得する。
            await _checkFolderRunLock.WaitAsync();

            try
            {
                while (true)
                {
                    CheckMode modeToRun;
                    lock (_checkFolderRequestSync)
                    {
                        if (!_hasPendingCheckFolderRequest)
                        {
                            break;
                        }

                        modeToRun = _pendingCheckFolderMode;
                        _hasPendingCheckFolderRequest = false;
                    }

                    await CheckFolderAsync(modeToRun);
                }
            }
            finally
            {
                _checkFolderRunLock.Release();
            }
        }
    }
}
