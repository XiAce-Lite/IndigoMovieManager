using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly HashSet<string> _dispatcherTimerFailureLogKeys =
            new(StringComparer.Ordinal);
        private int _dispatcherTimerInfrastructureFaulted;

        internal bool HasDispatcherTimerInfrastructureFault =>
            System.Threading.Volatile.Read(ref _dispatcherTimerInfrastructureFaulted) == 1;

        // VS の WPF 可視化支援や終了競合で SetWin32Timer が落ちても、UI 全体を巻き込まずログへ逃がす。
        private bool TryStartDispatcherTimer(DispatcherTimer timer, string timerName)
        {
            if (timer == null)
            {
                return false;
            }

            if (HasDispatcherTimerInfrastructureFault)
            {
                return false;
            }

            if (Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return false;
            }

            try
            {
                timer.Start();
                ClearDispatcherTimerFailureLogKey(timerName, "start");
                return true;
            }
            catch (Win32Exception ex)
            {
                LogDispatcherTimerFailureOnce(timerName, "start", ex);
                return false;
            }
            catch (InvalidOperationException) when (
                Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished
            )
            {
                return false;
            }
        }

        // 停止側は終了競合だけ静かに吸収し、閉じ際の追加例外を防ぐ。
        private void StopDispatcherTimerSafely(DispatcherTimer timer, string timerName)
        {
            if (timer == null)
            {
                return;
            }

            try
            {
                if (timer.IsEnabled)
                {
                    timer.Stop();
                }

                ClearDispatcherTimerFailureLogKey(timerName, "stop");
            }
            catch (Win32Exception ex)
            {
                LogDispatcherTimerFailureOnce(timerName, "stop", ex);
            }
            catch (InvalidOperationException) when (
                Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished
            )
            {
                // 終了途中の dispatcher 競合は想定内なので何もしない。
            }
        }

        private void LogDispatcherTimerFailureOnce(
            string timerName,
            string operation,
            Exception exception
        )
        {
            string safeTimerName = string.IsNullOrWhiteSpace(timerName) ? "(unnamed)" : timerName;
            string failureKey = $"{operation}:{safeTimerName}";
            lock (_dispatcherTimerFailureLogKeys)
            {
                if (!_dispatcherTimerFailureLogKeys.Add(failureKey))
                {
                    return;
                }
            }

            int nativeErrorCode = exception is Win32Exception win32 ? win32.NativeErrorCode : 0;
            bool hasShutdownStarted = Dispatcher?.HasShutdownStarted ?? false;
            bool hasShutdownFinished = Dispatcher?.HasShutdownFinished ?? false;
            DebugRuntimeLog.Write(
                "ui-timer",
                $"dispatcher timer {operation} failed: timer='{safeTimerName}' native_error={nativeErrorCode} shutdown_started={hasShutdownStarted} shutdown_finished={hasShutdownFinished} err='{exception.GetType().Name}: {exception.Message}'"
            );
        }

        private void ClearDispatcherTimerFailureLogKey(string timerName, string operation)
        {
            string safeTimerName = string.IsNullOrWhiteSpace(timerName) ? "(unnamed)" : timerName;
            string failureKey = $"{operation}:{safeTimerName}";
            lock (_dispatcherTimerFailureLogKeys)
            {
                _dispatcherTimerFailureLogKeys.Remove(failureKey);
            }
        }

        // WPF 内部タイマーが壊れた後は、非本質タイマーを止めて再発圧を下げる。
        internal void HandleDispatcherTimerInfrastructureFault(
            string origin,
            Win32Exception exception = null
        )
        {
            if (
                System.Threading.Interlocked.Exchange(ref _dispatcherTimerInfrastructureFaulted, 1)
                == 1
            )
            {
                return;
            }

            string safeOrigin = string.IsNullOrWhiteSpace(origin) ? "(unknown)" : origin;
            int nativeErrorCode = exception?.NativeErrorCode ?? 0;
            DebugRuntimeLog.Write(
                "ui-timer",
                $"dispatcher timer infrastructure faulted: origin='{safeOrigin}' native_error={nativeErrorCode}"
            );

            StopUiHangNotificationSupport();
            StopDispatcherTimerSafely(timer, nameof(timer));
            StopDispatcherTimerSafely(_thumbnailProgressUiTimer, nameof(_thumbnailProgressUiTimer));
            StopDispatcherTimerSafely(_thumbnailErrorUiTimer, nameof(_thumbnailErrorUiTimer));
            StopDispatcherTimerSafely(_debugTabRefreshTimer, nameof(_debugTabRefreshTimer));
            StopDispatcherTimerSafely(_logTabRefreshTimer, nameof(_logTabRefreshTimer));
            StopDispatcherTimerSafely(
                _upperTabViewportRefreshTimer,
                nameof(_upperTabViewportRefreshTimer)
            );
            StopDispatcherTimerSafely(
                _upperTabStartupAppendRetryTimer,
                nameof(_upperTabStartupAppendRetryTimer)
            );
        }
    }
}
