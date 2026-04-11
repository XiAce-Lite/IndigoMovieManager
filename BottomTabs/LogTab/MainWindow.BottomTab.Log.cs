using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Log;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string LogToolContentId = "ToolLog";
        private const int LogTabRefreshIntervalMs = 3000;

        private DateTime _logTabLastWriteTimeUtc = DateTime.MinValue;
        private DispatcherTimer _logTabRefreshTimer;
        private LogTabPresenter _logTabPresenter;

        private void InitializeLogTabSupport()
        {
            if (!ShouldShowDebugTab || LogTab == null || LogTabViewHost == null)
            {
                return;
            }

            if (_logTabPresenter != null)
            {
                return;
            }

            _logTabRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(LogTabRefreshIntervalMs),
            };
            _logTabRefreshTimer.Tick += LogTabRefreshTimer_Tick;
            _logTabPresenter = new LogTabPresenter(
                LogTab,
                _logTabRefreshTimer,
                () => ShouldShowDebugTab,
                forceRefresh => UpdateLogTabRefreshState(forceRefresh),
                isActive => UpdateLogTabRefreshTimerState(isActive)
            );
            _logTabPresenter.Initialize();
            RefreshLogTabPreview(force: true);
        }

        private bool IsLogTabActive()
        {
            return _logTabPresenter?.IsActive() == true;
        }

        private void LogTabRefreshTimer_Tick(object sender, EventArgs e)
        {
            _logTabPresenter?.HandleTimerTick(() => RefreshLogTabPreview());
        }

        // Logタブがアクティブな間だけ低頻度で更新し、前面に来た瞬間だけ強制反映する。
        private void UpdateLogTabRefreshState(bool forceRefresh)
        {
            bool isActive = IsLogTabActive();
            UpdateLogTabRefreshTimerState(isActive);

            if (forceRefresh || isActive || LogTabViewHost?.LogSwitchInfoTextBlock != null)
            {
                SetTextIfChanged(
                    LogTabViewHost?.LogSwitchInfoTextBlock,
                    BuildLogSwitchSummaryText()
                );
            }

            if (isActive && (forceRefresh || !(_logTabPresenter?.WasActive ?? false)))
            {
                RefreshLogTabPreview(force: true);
            }

            _logTabPresenter?.RecordRefreshState(isActive);
        }

        private void UpdateLogTabRefreshTimerState(bool isActive)
        {
            if (_logTabRefreshTimer == null)
            {
                return;
            }

            if (ShouldShowDebugTab && isActive)
            {
                if (!_logTabRefreshTimer.IsEnabled)
                {
                    TryStartDispatcherTimer(
                        _logTabRefreshTimer,
                        nameof(_logTabRefreshTimer)
                    );
                }

                return;
            }

            if (_logTabRefreshTimer.IsEnabled)
            {
                StopDispatcherTimerSafely(
                    _logTabRefreshTimer,
                    nameof(_logTabRefreshTimer)
                );
            }
        }

        // Debug構成の時だけ下部ペインへ差し込み、古いlayout復元でも下部へ戻す。
        private void ApplyLogTabVisibility()
        {
            if (LogTab == null || uxAnchorablePane2 == null)
            {
                return;
            }

            if (!ShouldShowDebugTab)
            {
                LogTab.IsSelected = false;
                LogTab.IsActive = false;
                LogTab.Hide();
                return;
            }

            if (
                LogTab.Parent is ILayoutContainer currentParent
                && !ReferenceEquals(currentParent, uxAnchorablePane2)
            )
            {
                currentParent.RemoveChild(LogTab);
            }

            if (!uxAnchorablePane2.Children.Contains(LogTab))
            {
                uxAnchorablePane2.Children.Add(LogTab);
            }

            LogTab.Show();
            RefreshLogTabPreview(force: true);
        }

        // Logタブ前面時だけ末尾を軽く読み直し、切替状態も合わせて見せる。
        private void RefreshLogTabPreview(bool force = false)
        {
            if (
                !ShouldShowDebugTab
                || (!force && !IsLogTabActive())
                || LogTabViewHost?.LogTextBox == null
                || LogTabViewHost?.LogPathTextBlock == null
            )
            {
                return;
            }

            string logPath = Path.Combine(AppLocalDataPaths.LogsPath, "debug-runtime.log");
            SetTextIfChanged(LogTabViewHost.LogPathTextBlock, logPath);
            SetTextIfChanged(LogTabViewHost.LogSwitchInfoTextBlock, BuildLogSwitchSummaryText());

            DateTime lastWriteTimeUtc = File.Exists(logPath)
                ? File.GetLastWriteTimeUtc(logPath)
                : DateTime.MinValue;
            if (!force && lastWriteTimeUtc == _logTabLastWriteTimeUtc)
            {
                return;
            }

            _logTabLastWriteTimeUtc = lastWriteTimeUtc;
            SetTextIfChanged(LogTabViewHost.LogTextBox, ReadDebugLogPreview(logPath));
            SetTextIfChanged(
                LogTabViewHost.LogInfoTextBlock,
                lastWriteTimeUtc == DateTime.MinValue
                    ? "debug-runtime.log はまだ作成されていません。"
                    : $"最終更新: {lastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            );

            if (force)
            {
                LogTabViewHost.ScrollLogToEnd();
            }
        }

        private void LogTabSwitchChanged_Click(object sender, RoutedEventArgs e)
        {
            // TwoWay バインド済みの設定値をそのまま永続化し、切替結果をすぐ画面へ戻す。
            Properties.Settings.Default.Save();
            UpdateLogTabRefreshState(forceRefresh: true);
        }

        private void LogRefreshPreview_Click(object sender, RoutedEventArgs e)
        {
            RefreshLogTabPreview(force: true);
        }

        private static string BuildLogSwitchSummaryText()
        {
            List<string> disabledGroups = [];
            if (!Properties.Settings.Default.DebugLogWatchEnabled)
            {
                disabledGroups.Add("Watcher");
            }

            if (!Properties.Settings.Default.DebugLogQueueEnabled)
            {
                disabledGroups.Add("Queue");
            }

            if (!Properties.Settings.Default.DebugLogThumbnailEnabled)
            {
                disabledGroups.Add("Thumbnail");
            }

            if (!Properties.Settings.Default.DebugLogUiEnabled)
            {
                disabledGroups.Add("UI・起動");
            }

            if (!Properties.Settings.Default.DebugLogSkinEnabled)
            {
                disabledGroups.Add("Skin");
            }

            if (!Properties.Settings.Default.DebugLogDebugToolEnabled)
            {
                disabledGroups.Add("Debug操作");
            }

            if (!Properties.Settings.Default.DebugLogDatabaseEnabled)
            {
                disabledGroups.Add("DB・外部");
            }

            if (!Properties.Settings.Default.DebugLogOtherEnabled)
            {
                disabledGroups.Add("その他");
            }

            return disabledGroups.Count < 1
                ? "切替は即保存されます。現在は全カテゴリが有効です。"
                : $"切替は即保存されます。現在OFF: {string.Join(", ", disabledGroups)}";
        }
    }
}
