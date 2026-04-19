using Notification.Wpf;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
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

        // folder first-hit 通知の文言組み立ても通知側へ寄せ、Watcher 側の lambda を薄くする。
        private Action BuildNotifyFolderFirstHitAction(string folderPath)
        {
            return () => ShowFolderUpdatedNoticeIfNeeded(folderPath);
        }

        // 更新検知時も開始時と同じ通知ゲートを通し、同一監視中の重複表示を防ぐ。
        private void ShowFolderUpdatedNoticeIfNeeded(string folderPath)
        {
            ShowFolderMonitoringNoticeIfNeeded("フォルダ監視中", $"{folderPath}に更新あり。");
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
                $"Everything連携を利用できないため通常監視で継続します。({strategyDetailMessage})",
                NotificationType.Information,
                "ProgressArea"
            );
            _hasShownEverythingFallbackNotice = true;
        }
    }
}
