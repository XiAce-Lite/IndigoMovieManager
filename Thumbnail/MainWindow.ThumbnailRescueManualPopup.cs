using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IndigoMovieManager.Thumbnail;
using Notification.Wpf;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string ManualThumbnailRescueProgressTitle = "手動救済";
        private const string ManualThumbnailRescueSuccessToastTitle = "手動救済 成功";
        private const string ManualThumbnailRescueFailureToastTitle = "手動救済 失敗";
        private const int ManualThumbnailRescueSuccessCloseDelayMs = 2800;
        private const int ManualThumbnailRescueFailureCloseDelayMs = 3600;
        private readonly IThumbnailQueueProgressPresenter _manualThumbnailRescueProgressPresenter =
            new AppThumbnailQueueProgressPresenter();
        private readonly NotificationManager _manualThumbnailRescueNotificationManager = new();
        private IThumbnailQueueProgressHandle _manualThumbnailRescueProgressHandle =
            NoOpThumbnailQueueProgressHandle.Instance;
        private int _manualThumbnailRescueProgressState;
        private CancellationTokenSource _manualThumbnailRescueCloseCts;
        private string _manualThumbnailRescueMoviePath = "";

        // rescue worker のslot別ログを1箇所へ集め、manual slot だけミニ進捗へ反映する。
        private void HandleThumbnailRescueWorkerLog(string slotLabel, string message)
        {
            string prefixedMessage = $"{slotLabel}: {message}";
            DebugRuntimeLog.Write("thumbnail-rescue-worker", prefixedMessage);

            if (!string.Equals(slotLabel, "manual-slot", StringComparison.Ordinal))
            {
                return;
            }

            HandleManualThumbnailRescueWorkerLog(message);
        }

        // manual slot の起動系ログだけを見て、右下の小さな進捗表示を出し入れする。
        private void HandleManualThumbnailRescueWorkerLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (message.Contains("rescue worker launched:", StringComparison.Ordinal))
            {
                ReportManualThumbnailRescueProgress("救済worker を起動しました。", true);
                return;
            }

            if (message.Contains("rescue worker stdout: rescue leased:", StringComparison.Ordinal))
            {
                RememberManualThumbnailRescueMoviePath(
                    ExtractManualThumbnailRescueMoviePath(message)
                );
                ReportManualThumbnailRescueProgress("対象動画を救済中です。", true);
                return;
            }

            if (message.Contains("rescue worker stdout: rescue succeeded:", StringComparison.Ordinal))
            {
                ReportManualThumbnailRescueResult(
                    "救済成功。反映待ちです。",
                    ManualThumbnailRescueSuccessCloseDelayMs,
                    NotificationType.Success,
                    ManualThumbnailRescueSuccessToastTitle
                );
                return;
            }

            if (message.Contains("rescue worker stdout: rescue gave up:", StringComparison.Ordinal))
            {
                ReportManualThumbnailRescueResult(
                    "救済失敗。詳細はログを確認してください。",
                    ManualThumbnailRescueFailureCloseDelayMs,
                    NotificationType.Error,
                    ManualThumbnailRescueFailureToastTitle
                );
                return;
            }

            if (message.Contains("rescue worker launch failed:", StringComparison.Ordinal))
            {
                ReportManualThumbnailRescueResult(
                    "救済worker を起動できませんでした。",
                    ManualThumbnailRescueFailureCloseDelayMs,
                    NotificationType.Error,
                    ManualThumbnailRescueFailureToastTitle
                );
                return;
            }

            if (message.Contains("rescue worker launch skipped:", StringComparison.Ordinal))
            {
                ReportManualThumbnailRescueResult(
                    "救済worker を起動できませんでした。",
                    ManualThumbnailRescueFailureCloseDelayMs,
                    NotificationType.Error,
                    ManualThumbnailRescueFailureToastTitle
                );
                return;
            }

            if (message.Contains("rescue worker exited:", StringComparison.Ordinal))
            {
                if (Volatile.Read(ref _manualThumbnailRescueProgressState) == 2)
                {
                    return;
                }

                CloseManualThumbnailRescueProgress();
            }
        }

        // 既存の ProgressArea を使い、manual slot 用だけインデターミネート表示を共有する。
        private void ReportManualThumbnailRescueProgress(string message, bool isIndeterminate)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    CancelManualThumbnailRescueCloseReservation();
                    if (ReferenceEquals(_manualThumbnailRescueProgressHandle, NoOpThumbnailQueueProgressHandle.Instance))
                    {
                        _manualThumbnailRescueProgressHandle =
                            _manualThumbnailRescueProgressPresenter.Show(
                                ManualThumbnailRescueProgressTitle
                            ) ?? NoOpThumbnailQueueProgressHandle.Instance;
                    }
                    Interlocked.Exchange(ref _manualThumbnailRescueProgressState, 1);
                    string displayMessage = BuildManualThumbnailRescueProgressMessage(message);

                    _manualThumbnailRescueProgressHandle.Report(
                        0,
                        displayMessage,
                        ManualThumbnailRescueProgressTitle,
                        isIndeterminate
                    );
                })
            );
        }

        private void ReportManualThumbnailRescueResult(
            string message,
            int closeDelayMs,
            NotificationType toastType,
            string toastTitle
        )
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    CancelManualThumbnailRescueCloseReservation();
                    if (ReferenceEquals(_manualThumbnailRescueProgressHandle, NoOpThumbnailQueueProgressHandle.Instance))
                    {
                        _manualThumbnailRescueProgressHandle =
                            _manualThumbnailRescueProgressPresenter.Show(
                                ManualThumbnailRescueProgressTitle
                            ) ?? NoOpThumbnailQueueProgressHandle.Instance;
                    }
                    Interlocked.Exchange(ref _manualThumbnailRescueProgressState, 2);
                    string displayMessage = BuildManualThumbnailRescueProgressMessage(message);

                    _manualThumbnailRescueProgressHandle.Report(
                        100,
                        displayMessage,
                        ManualThumbnailRescueProgressTitle,
                        false
                    );
                    ShowManualThumbnailRescueToast(toastTitle, message, toastType);
                    ReserveManualThumbnailRescueClose(closeDelayMs);
                })
            );
        }

        private void ShowManualThumbnailRescueToast(
            string title,
            string message,
            NotificationType type
        )
        {
            try
            {
                _manualThumbnailRescueNotificationManager.Show(
                    title,
                    message,
                    type,
                    "ProgressArea",
                    TimeSpan.FromSeconds(4)
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-worker",
                    $"manual rescue toast failed: {ex.Message}"
                );
            }
        }

        private void ReserveManualThumbnailRescueClose(int closeDelayMs)
        {
            CancellationTokenSource cts = new();
            _manualThumbnailRescueCloseCts = cts;
            _ = CloseManualThumbnailRescueProgressLaterAsync(closeDelayMs, cts.Token);
        }

        private async Task CloseManualThumbnailRescueProgressLaterAsync(
            int closeDelayMs,
            CancellationToken cancellationToken
        )
        {
            try
            {
                await Task.Delay(closeDelayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            CloseManualThumbnailRescueProgress();
        }

        private void CancelManualThumbnailRescueCloseReservation()
        {
            _manualThumbnailRescueCloseCts?.Cancel();
            _manualThumbnailRescueCloseCts?.Dispose();
            _manualThumbnailRescueCloseCts = null;
        }

        // 手動救済の右下 popup へ、いま扱っている動画パスを常に添える。
        private void RememberManualThumbnailRescueMoviePath(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return;
            }

            _manualThumbnailRescueMoviePath = moviePath.Trim();
        }

        // 元の進捗表示に合わせ、状態文の下へ動画パスを1行で差し込む。
        private string BuildManualThumbnailRescueProgressMessage(string message)
        {
            string safeMessage = message ?? "";
            string movieName = ResolveManualThumbnailRescueMovieName(_manualThumbnailRescueMoviePath);
            if (string.IsNullOrWhiteSpace(movieName))
            {
                return safeMessage;
            }

            return string.IsNullOrWhiteSpace(safeMessage)
                ? movieName
                : $"{safeMessage}{Environment.NewLine}{movieName}";
        }

        // popup ではフルパスではなく、ひと目で分かる動画名だけを見せる。
        private static string ResolveManualThumbnailRescueMovieName(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return "";
            }

            string fileName = Path.GetFileName(moviePath.Trim());
            return string.IsNullOrWhiteSpace(fileName) ? moviePath.Trim() : fileName;
        }

        // rescue leased ログの movie='...' 部分だけを抜き、実際に処理中の動画へ追従する。
        private static string ExtractManualThumbnailRescueMoviePath(string message)
        {
            const string moviePrefix = " movie='";
            const string prioritySuffix = "' priority=";

            if (string.IsNullOrWhiteSpace(message))
            {
                return "";
            }

            int startIndex = message.IndexOf(moviePrefix, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return "";
            }

            startIndex += moviePrefix.Length;
            int endIndex = message.IndexOf(prioritySuffix, startIndex, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                endIndex = message.LastIndexOf('\'');
            }

            return endIndex > startIndex
                ? message.Substring(startIndex, endIndex - startIndex)
                : "";
        }

        private void CloseManualThumbnailRescueProgress()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    CancelManualThumbnailRescueCloseReservation();
                    _manualThumbnailRescueProgressHandle.Dispose();
                    _manualThumbnailRescueProgressHandle = NoOpThumbnailQueueProgressHandle.Instance;
                    _manualThumbnailRescueMoviePath = "";
                    Interlocked.Exchange(ref _manualThumbnailRescueProgressState, 0);
                })
            );
        }
    }
}
