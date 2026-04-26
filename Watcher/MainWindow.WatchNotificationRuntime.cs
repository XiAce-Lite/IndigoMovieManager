using Notification.Wpf;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 通知に使うフォルダパスは前後空白を除去して扱い、空値通知を防ぐ。
        internal static string NormalizeWatchFolderPathForNotice(string folderPath)
        {
            return folderPath?.Trim() ?? "";
        }

        // 通知ノイズを抑えるため、空のフォルダ名では通知を出さない。
        internal static bool CanShowWatchFolderNotice(string folderPath)
        {
            return !string.IsNullOrWhiteSpace(NormalizeWatchFolderPathForNotice(folderPath));
        }

        internal static string BuildWatchFolderScanStartNoticeMessage(string folderPath)
        {
            string normalizedFolderPath = NormalizeWatchFolderPathForNotice(folderPath);
            return $"{normalizedFolderPath} 監視実施中…";
        }

        internal static string BuildWatchFolderUpdatedNoticeMessage(string folderPath)
        {
            string normalizedFolderPath = NormalizeWatchFolderPathForNotice(folderPath);
            return $"{normalizedFolderPath}に更新あり。";
        }

        // detail が空の時に末尾の空括弧を出さないようにして読みやすさを保つ。
        internal static string BuildEverythingFallbackNoticeMessage(string strategyDetailMessage)
        {
            const string baseMessage = "Everything連携を利用できないため通常監視で継続します。";
            return string.IsNullOrWhiteSpace(strategyDetailMessage)
                ? baseMessage
                : $"{baseMessage}({strategyDetailMessage})";
        }

        // NotificationManager は内部でウィンドウ資源を抱えるため、走査ごとに増やさず MainWindow で共有する。
        private readonly NotificationManager _watchNotificationManager = new();

        // Everything連携の通知は監視中に一度だけ出し、同じ内容を繰り返し表示しない。
        private bool _hasShownEverythingModeNotice;
        private bool _hasShownEverythingFallbackNotice;

        // 「フォルダ監視中」通知も監視中は一度だけに抑制する。
        private bool _hasShownFolderMonitoringNotice;

        // フォルダ監視中の通知は、開始時と更新検知時の両方から同じ入口で抑制する。
        private void ShowFolderMonitoringNoticeIfNeeded(string title, string message)
        {
            if (_hasShownFolderMonitoringNotice)
            {
                return;
            }

            _watchNotificationManager.Show(
                title,
                message,
                NotificationType.Notification,
                "ProgressArea"
            );
            _hasShownFolderMonitoringNotice = true;
        }

        // 走査開始時の文言組み立てを寄せ、Watcher 側ではフォルダ名だけ渡せば済むようにする。
        private void ShowFolderScanStartNoticeIfNeeded(string folderPath)
        {
            if (!CanShowWatchFolderNotice(folderPath))
            {
                return;
            }

            ShowFolderMonitoringNoticeIfNeeded(
                "フォルダ監視中",
                BuildWatchFolderScanStartNoticeMessage(folderPath)
            );
        }

        // scan start のログと開始トーストを同じ入口へ寄せ、Watcher 側の直書きを減らす。
        private void HandleWatchFolderScanStart(string folderPath, object mode)
        {
            DebugRuntimeLog.Write("watch-check", BuildWatchScanStartMessage(folderPath, mode));
            ShowFolderScanStartNoticeIfNeeded(folderPath);
        }

        // folder first-hit 通知の文言組み立ても通知側へ寄せ、Watcher 側の lambda を薄くする。
        private Action BuildNotifyFolderFirstHitAction(string folderPath)
        {
            return () => ShowFolderUpdatedNoticeIfNeeded(folderPath);
        }

        // 更新検知時も開始時と同じ通知ゲートを通し、同一監視中の重複表示を防ぐ。
        private void ShowFolderUpdatedNoticeIfNeeded(string folderPath)
        {
            if (!CanShowWatchFolderNotice(folderPath))
            {
                return;
            }

            ShowFolderMonitoringNoticeIfNeeded(
                "フォルダ監視中",
                BuildWatchFolderUpdatedNoticeMessage(folderPath)
            );
        }

        // Everything連携での高速スキャン通知は、監視中に一度だけ出す。
        private void ShowEverythingModeNoticeIfNeeded()
        {
            if (_hasShownEverythingModeNotice)
            {
                return;
            }

            _watchNotificationManager.Show(
                "Everything連携",
                "Everything連携で高速スキャンを実行中です。",
                NotificationType.Notification,
                "ProgressArea"
            );
            _hasShownEverythingModeNotice = true;
        }

        // Everything連携が使えない時のフォールバック通知も、同一監視中は一度だけにする。
        private void ShowEverythingFallbackNoticeIfNeeded(string strategyDetailMessage)
        {
            if (_hasShownEverythingFallbackNotice)
            {
                return;
            }

            _watchNotificationManager.Show(
                "Everything連携",
                BuildEverythingFallbackNoticeMessage(strategyDetailMessage),
                NotificationType.Information,
                "ProgressArea"
            );
            _hasShownEverythingFallbackNotice = true;
        }

        // scan strategy 通知は連携状態の取得も内側へ寄せ、Watcher 側の判断材料を減らす。
        private void HandleWatchScanStrategyNotices(string strategy, string strategyDetailMessage)
        {
            bool isIntegrationConfigured = _indexProviderFacade.IsIntegrationConfigured(
                GetEverythingIntegrationMode()
            );
            (
                bool shouldShowEverythingModeNotice,
                bool shouldShowEverythingFallbackNotice
            ) = ResolveWatchScanStrategyNoticePlan(strategy, isIntegrationConfigured);

            if (shouldShowEverythingModeNotice)
            {
                ShowEverythingModeNoticeIfNeeded();
            }
            else if (shouldShowEverythingFallbackNotice)
            {
                ShowEverythingFallbackNoticeIfNeeded(strategyDetailMessage);
            }
        }

        // strategy 通知と UI reload 診断ログをまとめて処理し、Watcher 側の直列呼び出しを減らす。
        private (bool UseIncrementalUiMode, bool CanUseQueryOnlyWatchReload)
            HandleWatchScanStrategyAndUiReloadDiagnostics(
                CheckMode mode,
                string checkFolder,
                string strategy,
                string strategyDetailMessage,
                int newMovieCount,
                int incrementalUiUpdateThreshold,
                bool canUseQueryOnlyWatchReload
            )
        {
            HandleWatchScanStrategyNotices(strategy, strategyDetailMessage);
            return ResolveAndWriteWatchScanUiReloadDiagnostics(
                mode,
                checkFolder,
                newMovieCount,
                incrementalUiUpdateThreshold,
                canUseQueryOnlyWatchReload
            );
        }
    }
}
