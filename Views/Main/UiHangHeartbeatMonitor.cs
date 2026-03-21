using System.Diagnostics;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    internal readonly record struct UiHangHeartbeatSample(long DelayMs, bool IsPending);

    internal sealed class UiHangHeartbeatMonitor : IDisposable
    {
        private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);

        private readonly Dispatcher _dispatcher;
        private readonly TimeSpan _interval;
        private readonly object _gate = new();
        private CancellationTokenSource _cts;
        private Task _loopTask;
        private long _pendingPostedTimestamp;
        private long _pendingSequence;
        private bool _probePending;
        private bool _disposed;

        internal UiHangHeartbeatMonitor(Dispatcher dispatcher, TimeSpan? interval = null)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _interval = interval ?? DefaultInterval;
        }

        internal event Action<UiHangHeartbeatSample> SampleObserved;

        internal void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_cts != null)
                {
                    return;
                }

                _cts = new CancellationTokenSource();
                _loopTask = Task.Run(() => RunAsync(_cts.Token));
            }
        }

        internal void Stop()
        {
            CancellationTokenSource ctsToCancel;
            Task loopTask;

            lock (_gate)
            {
                if (_cts == null)
                {
                    return;
                }

                ctsToCancel = _cts;
                loopTask = _loopTask;
                _cts = null;
                _loopTask = null;
                _probePending = false;
                _pendingPostedTimestamp = 0;
                _pendingSequence = 0;
            }

            try
            {
                ctsToCancel.Cancel();
                loopTask?.Wait(250);
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException))
            {
                // 停止指示で抜けただけなので握りつぶす。
            }
            finally
            {
                ctsToCancel.Dispose();
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            using PeriodicTimer timer = new(_interval);

            TryQueueProbe();

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    PublishPendingDelayIfNeeded();
                    TryQueueProbe();
                }
            }
            catch (OperationCanceledException)
            {
                // 停止指示で抜ける通常経路。
            }
        }

        // UI 側に未実行の heartbeat がある間は、背景側から経過遅延だけを観測し続ける。
        private void PublishPendingDelayIfNeeded()
        {
            long postedTimestamp;

            lock (_gate)
            {
                if (!_probePending)
                {
                    return;
                }

                postedTimestamp = _pendingPostedTimestamp;
            }

            RaiseSampleObserved(new UiHangHeartbeatSample(GetElapsedMilliseconds(postedTimestamp), true));
        }

        // 未処理の probe が無い時だけ 1 件投げ、UI キューを無駄に膨らませない。
        private void TryQueueProbe()
        {
            long sequence;

            lock (_gate)
            {
                if (_cts == null || _probePending)
                {
                    return;
                }

                _probePending = true;
                _pendingPostedTimestamp = Stopwatch.GetTimestamp();
                sequence = ++_pendingSequence;
            }

            try
            {
                _ = _dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => CompleteProbe(sequence))
                );
            }
            catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
            {
                lock (_gate)
                {
                    if (_pendingSequence == sequence)
                    {
                        _probePending = false;
                        _pendingPostedTimestamp = 0;
                    }
                }
            }
        }

        // UI スレッドに戻れた瞬間の遅延だけを 1 件確定として通知する。
        private void CompleteProbe(long sequence)
        {
            long postedTimestamp;

            lock (_gate)
            {
                if (!_probePending || _pendingSequence != sequence)
                {
                    return;
                }

                postedTimestamp = _pendingPostedTimestamp;
                _probePending = false;
                _pendingPostedTimestamp = 0;
            }

            RaiseSampleObserved(new UiHangHeartbeatSample(GetElapsedMilliseconds(postedTimestamp), false));
        }

        private void RaiseSampleObserved(UiHangHeartbeatSample sample)
        {
            SampleObserved?.Invoke(sample);
        }

        private static long GetElapsedMilliseconds(long postedTimestamp)
        {
            if (postedTimestamp <= 0)
            {
                return 0;
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - postedTimestamp;
            return (long)(elapsedTicks * 1000.0 / Stopwatch.Frequency);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UiHangHeartbeatMonitor));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;
        }
    }
}
