using System.Windows.Threading;

namespace IndigoMovieManager
{
    internal enum UiHangNotificationLevel
    {
        None = 0,
        Success = 1,
        Caution = 2,
        Warning = 3,
        Critical = 4,
    }

    internal sealed class UiHangNotificationCoordinator : IDisposable
    {
        private const int DetectThresholdMs = 250;
        private const int WarningThresholdMs = 1000;
        private const int ShowConsecutiveCount = 2;
        private const int RecoverConsecutiveCount = 3;

        private readonly UiHangHeartbeatMonitor _heartbeatMonitor;
        private readonly NativeOverlayHost _overlayHost;
        private readonly UiHangActivityTracker _activityTracker;
        private readonly Func<UiHangHeartbeatSample, bool> _dangerStateResolver;
        private readonly Func<UiHangNotificationLevel, bool> _visibilityResolver;
        private readonly object _gate = new();
        private bool _started;
        private bool _isVisible;
        private int _consecutiveOverThreshold;
        private int _consecutiveNormal;
        private UiHangNotificationLevel _currentLevel;
        private string _currentMessage = "";
        private bool _hasExplicitStatus;
        private UiHangNotificationLevel _explicitLevel;
        private string _explicitMessage = "";
        private bool _explicitAllowBackground;
        private bool _disposed;

        internal UiHangNotificationCoordinator(
            Dispatcher dispatcher,
            UiHangActivityTracker activityTracker,
            Func<UiHangHeartbeatSample, bool> dangerStateResolver,
            Func<UiHangNotificationLevel, bool> visibilityResolver
        )
            : this(
                new UiHangHeartbeatMonitor(dispatcher),
                new NativeOverlayHost(),
                activityTracker,
                dangerStateResolver,
                visibilityResolver
            ) { }

        internal UiHangNotificationCoordinator(
            UiHangHeartbeatMonitor heartbeatMonitor,
            NativeOverlayHost overlayHost,
            UiHangActivityTracker activityTracker,
            Func<UiHangHeartbeatSample, bool> dangerStateResolver,
            Func<UiHangNotificationLevel, bool> visibilityResolver
        )
        {
            _heartbeatMonitor =
                heartbeatMonitor ?? throw new ArgumentNullException(nameof(heartbeatMonitor));
            _overlayHost = overlayHost ?? throw new ArgumentNullException(nameof(overlayHost));
            _activityTracker = activityTracker ?? throw new ArgumentNullException(nameof(activityTracker));
            _dangerStateResolver =
                dangerStateResolver ?? throw new ArgumentNullException(nameof(dangerStateResolver));
            _visibilityResolver =
                visibilityResolver ?? throw new ArgumentNullException(nameof(visibilityResolver));
            _heartbeatMonitor.SampleObserved += HandleHeartbeatSample;
        }

        internal void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_started)
                {
                    return;
                }

                _started = true;
            }

            _overlayHost.Start();
            _heartbeatMonitor.Start();
        }

        internal void Stop()
        {
            lock (_gate)
            {
                if (!_started)
                {
                    return;
                }

                _started = false;
                _isVisible = false;
                _consecutiveOverThreshold = 0;
                _consecutiveNormal = 0;
                _currentLevel = UiHangNotificationLevel.None;
                _currentMessage = "";
            }

            _heartbeatMonitor.Stop();
            _overlayHost.Hide();
            _overlayHost.Stop();
        }

        internal void UpdatePlacement(UiHangOverlayPlacement placement)
        {
            _overlayHost.UpdatePlacement(placement);
        }

        internal void UpdateOwnerWindowHandle(nint ownerWindowHandle)
        {
            _overlayHost.UpdateOwnerWindowHandle(ownerWindowHandle);
        }

        internal void ShowExplicitStatus(
            UiHangNotificationLevel level,
            string message,
            bool allowBackground
        )
        {
            bool shouldShow;
            lock (_gate)
            {
                if (!_started)
                {
                    return;
                }

                _hasExplicitStatus = true;
                _explicitLevel = level;
                _explicitMessage = message ?? "";
                _explicitAllowBackground = allowBackground;
                shouldShow = allowBackground || _visibilityResolver(level);
                _isVisible = shouldShow;
            }

            if (shouldShow)
            {
                _overlayHost.Show(level, message);
            }
            else
            {
                _overlayHost.Hide();
            }
        }

        internal void HideExplicitStatus()
        {
            bool shouldHide;
            lock (_gate)
            {
                shouldHide = _hasExplicitStatus && _isVisible;
                _hasExplicitStatus = false;
                _explicitLevel = UiHangNotificationLevel.None;
                _explicitMessage = "";
                _explicitAllowBackground = false;
                _isVisible = false;
            }

            if (shouldHide)
            {
                _overlayHost.Hide();
            }
        }

        internal void ReevaluateVisibility()
        {
            bool shouldShow = false;
            bool shouldHide = false;
            UiHangNotificationLevel levelToShow = UiHangNotificationLevel.None;
            string messageToShow = "";

            lock (_gate)
            {
                if (!_started)
                {
                    return;
                }

                if (_hasExplicitStatus)
                {
                    bool explicitCanDisplay =
                        _explicitAllowBackground || _visibilityResolver(_explicitLevel);
                    if (!explicitCanDisplay && _isVisible)
                    {
                        _isVisible = false;
                        shouldHide = true;
                    }
                    else if (explicitCanDisplay && !_isVisible)
                    {
                        _isVisible = true;
                        shouldShow = true;
                        levelToShow = _explicitLevel;
                        messageToShow = _explicitMessage;
                    }

                    goto apply;
                }

                if (_currentLevel == UiHangNotificationLevel.None)
                {
                    return;
                }

                bool canDisplay = _visibilityResolver(_currentLevel);
                if (!canDisplay && _isVisible)
                {
                    _isVisible = false;
                    shouldHide = true;
                }
                else if (canDisplay && !_isVisible && _consecutiveOverThreshold >= ShowConsecutiveCount)
                {
                    _isVisible = true;
                    shouldShow = true;
                    levelToShow = _currentLevel;
                    messageToShow = _currentMessage;
                }

            apply:
                ;
            }

            if (shouldHide)
            {
                _overlayHost.Hide();
            }

            if (shouldShow)
            {
                _overlayHost.Show(levelToShow, messageToShow);
            }
        }

        // heartbeat の生値をここで段階化し、表示条件と復帰条件だけを持つ。
        private void HandleHeartbeatSample(UiHangHeartbeatSample sample)
        {
            bool isDangerState = _dangerStateResolver(sample);
            UiHangNotificationLevel nextLevel = ResolveLevel(sample.DelayMs, isDangerState);
            UiHangActivitySnapshot activitySnapshot = _activityTracker.GetCurrentSnapshot();
            string nextMessage = BuildMessage(nextLevel, activitySnapshot.Kind);
            bool canDisplay = _visibilityResolver(nextLevel);
            bool shouldShow = false;
            bool shouldHide = false;
            bool shouldUpdate = false;

            lock (_gate)
            {
                if (!_started)
                {
                    return;
                }

                if (_hasExplicitStatus)
                {
                    _currentLevel = nextLevel;
                    _currentMessage = nextMessage;
                    if (nextLevel == UiHangNotificationLevel.None)
                    {
                        _consecutiveOverThreshold = 0;
                        _consecutiveNormal++;
                    }
                    else
                    {
                        _consecutiveNormal = 0;
                        _consecutiveOverThreshold++;
                    }

                    return;
                }

                if (nextLevel == UiHangNotificationLevel.None)
                {
                    _consecutiveOverThreshold = 0;
                    _consecutiveNormal++;

                    if (_isVisible && _consecutiveNormal >= RecoverConsecutiveCount)
                    {
                        shouldHide = true;
                        _isVisible = false;
                        _currentLevel = UiHangNotificationLevel.None;
                        _currentMessage = "";
                    }
                }
                else
                {
                    _consecutiveNormal = 0;
                    _consecutiveOverThreshold++;
                    bool levelChanged = _currentLevel != nextLevel;
                    bool messageChanged = _currentMessage != nextMessage;
                    _currentLevel = nextLevel;
                    _currentMessage = nextMessage;

                    if (!canDisplay)
                    {
                        if (_isVisible && nextLevel != UiHangNotificationLevel.Critical)
                        {
                            shouldHide = true;
                            _isVisible = false;
                        }
                    }
                    else if (!_isVisible && _consecutiveOverThreshold >= ShowConsecutiveCount)
                    {
                        _isVisible = true;
                        shouldShow = true;
                    }
                    else if (_isVisible && (levelChanged || messageChanged))
                    {
                        shouldUpdate = true;
                    }
                }
            }

            if (shouldShow)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"ui hang detected: level={nextLevel} activity={activitySnapshot.Kind} delay_ms={sample.DelayMs} pending={sample.IsPending} danger={isDangerState} foreground_only={nextLevel != UiHangNotificationLevel.Critical}"
                );
                _overlayHost.Show(nextLevel, nextMessage);
                return;
            }

            if (shouldUpdate)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"ui hang updated: level={nextLevel} activity={activitySnapshot.Kind} delay_ms={sample.DelayMs} pending={sample.IsPending} danger={isDangerState}"
                );
                _overlayHost.Update(nextLevel, nextMessage);
                return;
            }

            if (shouldHide)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    nextLevel == UiHangNotificationLevel.None
                        ? "ui hang recovered"
                        : $"ui hang hidden by background state: level={nextLevel}"
                );
                _overlayHost.Hide();
            }
        }

        internal static UiHangNotificationLevel ResolveLevel(long delayMs, bool isDangerState)
        {
            if (isDangerState)
            {
                return UiHangNotificationLevel.Critical;
            }

            if (delayMs >= WarningThresholdMs)
            {
                return UiHangNotificationLevel.Warning;
            }

            if (delayMs >= DetectThresholdMs)
            {
                return UiHangNotificationLevel.Caution;
            }

            return UiHangNotificationLevel.None;
        }

        internal static string BuildMessage(
            UiHangNotificationLevel level,
            UiHangActivityKind activityKind
        )
        {
            string activityLabel = activityKind switch
            {
                UiHangActivityKind.Watch => "監視処理",
                UiHangActivityKind.Database => "DB処理",
                UiHangActivityKind.Thumbnail => "サムネイル処理",
                UiHangActivityKind.Startup => "起動処理",
                _ => "UI処理",
            };

            return level switch
            {
                UiHangNotificationLevel.Caution => $"{activityLabel}で応答低下を検知",
                UiHangNotificationLevel.Warning => $"{activityLabel}を継続中",
                UiHangNotificationLevel.Critical => $"{activityLabel}の応答停止の可能性があります",
                _ => "",
            };
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UiHangNotificationCoordinator));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _heartbeatMonitor.SampleObserved -= HandleHeartbeatSample;
            Stop();
            _heartbeatMonitor.Dispose();
            _overlayHost.Dispose();
            _disposed = true;
        }
    }
}
