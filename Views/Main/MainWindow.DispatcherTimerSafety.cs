using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int FaultCleanupSuppressedNativeErrorCode = 8;
        private const string DispatcherTimerStopStackMarker =
            "System.Windows.Threading.DispatcherTimer.Stop";
        private const string DispatcherTimerFaultCleanupStackMarker =
            "IndigoMovieManager.MainWindow.HandleDispatcherTimerInfrastructureFault";
        private const string DispatcherTimerCleanupStopStackMarker =
            "IndigoMovieManager.MainWindow.StopDispatcherTimerDuringInfrastructureFaultCleanup";

        private readonly HashSet<string> _dispatcherTimerFailureLogKeys =
            new(StringComparer.Ordinal);
        private int _dispatcherTimerInfrastructureFaulted;

        internal bool HasDispatcherTimerInfrastructureFault =>
            App.HasDispatcherTimerInfrastructureFault
            || System.Threading.Volatile.Read(ref _dispatcherTimerInfrastructureFaulted) == 1;

        // VS の WPF 可視化支援や終了競合で SetWin32Timer が落ちても、UI 全体を巻き込まずログへ逃がす。
        private bool TryStartDispatcherTimer(DispatcherTimer timer, string timerName)
        {
            if (timer == null)
            {
                return false;
            }

            Dispatcher dispatcher = Dispatcher;
            if (
                !ShouldAllowDispatcherTimerStartCore(
                    HasDispatcherTimerInfrastructureFault,
                    dispatcher != null,
                    dispatcher?.HasShutdownStarted ?? false,
                    dispatcher?.HasShutdownFinished ?? false
                )
            )
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
                // start 失敗は fault 伝播を最優先し、cleanup 側の継続契約とは分けて扱う。
                if (
                    !TryHandleDispatcherTimerStartWin32ExceptionCore(
                        ex,
                        handledException => HandleDispatcherTimerInfrastructureFault(timerName, handledException)
                    )
                )
                {
                    throw;
                }

                return false;
            }
            catch (InvalidOperationException) when (
                Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished
            )
            {
                return false;
            }
        }

        // fault や shutdown 中は start 自体を見送り、再始動を安全側へ倒す。
        internal static bool ShouldAllowDispatcherTimerStartCore(
            bool hasDispatcherTimerInfrastructureFault,
            bool hasDispatcher,
            bool hasShutdownStarted,
            bool hasShutdownFinished
        )
        {
            return !hasDispatcherTimerInfrastructureFault
                && hasDispatcher
                && !hasShutdownStarted
                && !hasShutdownFinished;
        }

        // start/stop catch は App 側の既知 suppress 条件と同じ幅だけを fault 扱いに寄せる。
        internal static bool ShouldHandleDispatcherTimerInfrastructureFaultCore(
            Win32Exception exception,
            string stackTraceOverride = null,
            MethodBase targetSiteOverride = null
        )
        {
            return App.ShouldSuppressKnownDispatcherTimerWin32Exception(
                exception,
                stackTraceOverride,
                targetSiteOverride
            );
        }

        // start 側は既知の WPF timer fault だけを fault handler へ渡す。
        internal static bool TryHandleDispatcherTimerStartWin32ExceptionCore(
            Win32Exception exception,
            Action<Win32Exception> faultHandler,
            string stackTraceOverride = null,
            MethodBase targetSiteOverride = null
        )
        {
            if (
                !ShouldHandleDispatcherTimerInfrastructureFaultCore(
                    exception,
                    stackTraceOverride,
                    targetSiteOverride
                )
            )
            {
                return false;
            }

            faultHandler?.Invoke(exception);
            return true;
        }

        // 通常 stop は従来どおり narrow 判定だけを使い、広く握り潰さない。
        private void StopDispatcherTimerSafely(DispatcherTimer timer, string timerName)
        {
            StopDispatcherTimerCore(timer, timerName, isFaultCleanupStop: false);
        }

        // fault cleanup 中だけは secondary な stop 例外で後始末全体を止めない。
        private void StopDispatcherTimerDuringInfrastructureFaultCleanup(
            DispatcherTimer timer,
            string timerName
        )
        {
            StopDispatcherTimerCore(timer, timerName, isFaultCleanupStop: true);
        }

        // cleanup stop と通常 stop の吸収条件を 1 か所へ寄せ、契約を追いやすくする。
        internal static bool ShouldSuppressDispatcherTimerStopWin32ExceptionCore(
            bool isFaultCleanupStop,
            Win32Exception exception,
            string stackTraceOverride = null,
            MethodBase targetSiteOverride = null
        )
        {
            if (exception == null)
            {
                return false;
            }

            if (!isFaultCleanupStop)
            {
                return ShouldHandleDispatcherTimerInfrastructureFaultCore(
                    exception,
                    stackTraceOverride,
                    targetSiteOverride
                );
            }

            return ShouldContinueDispatcherTimerFaultCleanupAfterStopExceptionCore(
                exception,
                stackTraceOverride,
                targetSiteOverride
            );
        }

        // cleanup 中は追加 stop 例外が出ても、既知の fault 文脈なら縮退停止を最後まで続ける。
        internal static bool ShouldContinueDispatcherTimerFaultCleanupAfterStopExceptionCore(
            Win32Exception exception,
            string stackTraceOverride = null,
            MethodBase targetSiteOverride = null
        )
        {
            if (exception == null)
            {
                return false;
            }

            return ShouldHandleDispatcherTimerInfrastructureFaultCore(
                    exception,
                    stackTraceOverride,
                    targetSiteOverride
                )
                || ShouldSuppressDispatcherTimerFaultCleanupStopWin32ExceptionCore(
                    exception,
                    stackTraceOverride,
                    targetSiteOverride
                );
        }

        // cleanup 側は追加例外をログへ寄せて継続し、再 fault 伝播では止めない。
        internal static bool TryHandleDispatcherTimerFaultCleanupStopWin32ExceptionCore(
            Win32Exception exception,
            Action<Win32Exception> cleanupContinuationAction,
            string stackTraceOverride = null,
            MethodBase targetSiteOverride = null
        )
        {
            if (
                !ShouldContinueDispatcherTimerFaultCleanupAfterStopExceptionCore(
                    exception,
                    stackTraceOverride,
                    targetSiteOverride
                )
            )
            {
                return false;
            }

            cleanupContinuationAction?.Invoke(exception);
            return true;
        }

        // cleanup 完走のための補助許可も、native error=8 と cleanup stop 文脈に限定する。
        internal static bool ShouldSuppressDispatcherTimerFaultCleanupStopWin32ExceptionCore(
            Win32Exception exception,
            string stackTraceOverride = null,
            MethodBase targetSiteOverride = null
        )
        {
            if (exception == null || exception.NativeErrorCode != FaultCleanupSuppressedNativeErrorCode)
            {
                return false;
            }

            string stackTrace = stackTraceOverride ?? exception.StackTrace ?? "";
            if (string.IsNullOrWhiteSpace(stackTrace))
            {
                return false;
            }

            MethodBase targetSite = targetSiteOverride ?? exception.TargetSite;
            bool hasDispatcherTimerStopStackMarker = stackTrace.Contains(
                DispatcherTimerStopStackMarker,
                StringComparison.Ordinal
            );
            bool hasFaultCleanupStackMarker =
                stackTrace.Contains(
                    DispatcherTimerFaultCleanupStackMarker,
                    StringComparison.Ordinal
                )
                || stackTrace.Contains(
                    DispatcherTimerCleanupStopStackMarker,
                    StringComparison.Ordinal
                );

            return hasFaultCleanupStackMarker
                && (hasDispatcherTimerStopStackMarker || IsDispatcherTimerStopTargetSite(targetSite));
        }

        private static bool IsDispatcherTimerStopTargetSite(MethodBase targetSite)
        {
            return string.Equals(targetSite?.Name, nameof(DispatcherTimer.Stop), StringComparison.Ordinal)
                && string.Equals(
                    targetSite?.DeclaringType?.FullName,
                    typeof(DispatcherTimer).FullName,
                    StringComparison.Ordinal
                );
        }

        // 停止側は終了競合だけ静かに吸収し、通常経路では narrow 判定を維持する。
        private void StopDispatcherTimerCore(
            DispatcherTimer timer,
            string timerName,
            bool isFaultCleanupStop
        )
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
                if (
                    isFaultCleanupStop
                    && TryHandleDispatcherTimerFaultCleanupStopWin32ExceptionCore(
                        ex,
                        cleanupException =>
                            LogDispatcherTimerFailureOnce(
                                timerName,
                                "stop-cleanup",
                                cleanupException
                            )
                    )
                )
                {
                    return;
                }

                if (
                    !TryHandleDispatcherTimerStartWin32ExceptionCore(
                        ex,
                        handledException => HandleDispatcherTimerInfrastructureFault(timerName, handledException)
                    )
                )
                {
                    throw;
                }
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
            App.RecordDispatcherTimerInfrastructureFault();
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
            StopDispatcherTimerDuringInfrastructureFaultCleanup(timer, nameof(timer));
            StopDispatcherTimerDuringInfrastructureFaultCleanup(
                _thumbnailProgressUiTimer,
                nameof(_thumbnailProgressUiTimer)
            );
            StopDispatcherTimerDuringInfrastructureFaultCleanup(
                _thumbnailErrorUiTimer,
                nameof(_thumbnailErrorUiTimer)
            );
            StopDispatcherTimerDuringInfrastructureFaultCleanup(
                _debugTabRefreshTimer,
                nameof(_debugTabRefreshTimer)
            );
            StopDispatcherTimerDuringInfrastructureFaultCleanup(
                _upperTabViewportRefreshTimer,
                nameof(_upperTabViewportRefreshTimer)
            );
            StopDispatcherTimerDuringInfrastructureFaultCleanup(
                _upperTabStartupAppendRetryTimer,
                nameof(_upperTabStartupAppendRetryTimer)
            );
        }
    }
}
