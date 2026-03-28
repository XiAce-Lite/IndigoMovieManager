using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const string ManualThumbnailRescueInfoToastTitle = "手動救済";
        private const string ManualThumbnailRescueSuccessToastTitle = "手動救済 成功";
        private const string ManualThumbnailRescueFailureToastTitle = "手動救済 失敗";
        private const int ManualThumbnailRescueInfoCloseDelayMs = 2600;
        private const int ManualThumbnailRescueSuccessCloseDelayMs = 2800;
        private const int ManualThumbnailRescueFailureCloseDelayMs = 3600;
        private const int ManualThumbnailRescueProgressStateIdle = 0;
        private const int ManualThumbnailRescueProgressStateRunning = 1;
        private const int ManualThumbnailRescueProgressStateResultShown = 2;
        private const int ManualThumbnailRescueProgressStateResolvingResult = 3;
        private readonly IThumbnailQueueProgressPresenter _manualThumbnailRescueProgressPresenter =
            new AppThumbnailQueueProgressPresenter();
        private readonly NotificationManager _manualThumbnailRescueNotificationManager = new();
        private IThumbnailQueueProgressHandle _manualThumbnailRescueProgressHandle =
            NoOpThumbnailQueueProgressHandle.Instance;
        private int _manualThumbnailRescueProgressState;
        private CancellationTokenSource _manualThumbnailRescueCloseCts;
        private string _manualThumbnailRescueMoviePath = "";
        private readonly object _manualThumbnailRescueSlotSyncRoot = new();
        private readonly Dictionary<string, long> _manualThumbnailRescueRequestedFailureIds = new(
            StringComparer.Ordinal
        );
        private readonly Dictionary<string, string> _manualThumbnailDirectIndexRepairMoviePaths = new(
            StringComparer.OrdinalIgnoreCase
        );

        // rescue worker のslot別ログを1箇所へ集め、manual slot だけミニ進捗へ反映する。
        private void HandleThumbnailRescueWorkerLog(string slotLabel, string message)
        {
            string prefixedMessage = $"{slotLabel}: {message}";
            DebugRuntimeLog.Write("thumbnail-rescue-worker", prefixedMessage);

            if (!slotLabel.StartsWith("manual-slot", StringComparison.Ordinal))
            {
                return;
            }

            HandleManualThumbnailRescueWorkerLog(slotLabel, message);
        }

        // manual slot の起動系ログだけを見て、右下の小さな進捗表示を出し入れする。
        private void HandleManualThumbnailRescueWorkerLog(string slotLabel, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (message.Contains("direct index repair worker launched:", StringComparison.Ordinal))
            {
                if (!HasTrackedManualThumbnailDirectIndexRepairRequest(slotLabel))
                {
                    return;
                }
                ReportManualThumbnailRescueProgress(
                    "インデックス再構築worker を起動しました。",
                    true
                );
                return;
            }

            if (message.Contains("rescue worker launched:", StringComparison.Ordinal))
            {
                if (!HasTrackedManualThumbnailRescueRequest(slotLabel))
                {
                    return;
                }
                ReportManualThumbnailRescueProgress("救済worker を起動しました。", true);
                return;
            }

            if (message.Contains("rescue worker stdout: rescue leased:", StringComparison.Ordinal))
            {
                if (!ShouldHandleTrackedManualThumbnailRescueLog(slotLabel, message))
                {
                    return;
                }
                RememberManualThumbnailRescueMoviePath(
                    ExtractManualThumbnailRescueMoviePath(message)
                );
                ReportManualThumbnailRescueProgress("対象動画を救済中です。", true);
                return;
            }

            if (
                message.Contains(
                    "rescue worker stdout: direct index repair start:",
                    StringComparison.Ordinal
                )
            )
            {
                if (
                    !TryExtractManualThumbnailDirectIndexRepairMoviePath(
                        message,
                        out string moviePath
                    )
                    || !ShouldHandleTrackedManualThumbnailDirectIndexRepairLog(slotLabel, moviePath)
                )
                {
                    return;
                }
                RememberManualThumbnailRescueMoviePath(moviePath);
                ReportManualThumbnailRescueProgress("動画をインデックス再構築中です。", true);
                return;
            }

            if (message.Contains("rescue worker stdout: rescue succeeded:", StringComparison.Ordinal))
            {
                if (!ShouldHandleTrackedManualThumbnailRescueLog(slotLabel, message))
                {
                    return;
                }
                Interlocked.Exchange(
                    ref _manualThumbnailRescueProgressState,
                    ManualThumbnailRescueProgressStateResolvingResult
                );
                _ = HandleManualThumbnailRescueSucceededAsync(slotLabel, message);
                return;
            }

            if (
                message.Contains(
                    "rescue worker stdout: direct index repair succeeded:",
                    StringComparison.Ordinal
                )
            )
            {
                if (
                    !TryExtractManualThumbnailDirectIndexRepairSuccessInfo(
                        message,
                        out string moviePath,
                        out string repairedMoviePath
                    )
                    || !ShouldHandleTrackedManualThumbnailDirectIndexRepairLog(slotLabel, moviePath)
                )
                {
                    return;
                }

                HandleManualThumbnailDirectIndexRepairSucceeded(slotLabel, repairedMoviePath);
                return;
            }

            if (message.Contains("rescue worker stdout: rescue gave up:", StringComparison.Ordinal))
            {
                if (!ShouldHandleTrackedManualThumbnailRescueLog(slotLabel, message))
                {
                    return;
                }
                ReportManualThumbnailRescueResult(
                    "救済失敗。詳細はログを確認してください。",
                    ManualThumbnailRescueFailureCloseDelayMs,
                    NotificationType.Error,
                    ManualThumbnailRescueFailureToastTitle
                );
                ClearTrackedManualThumbnailRescueRequest(slotLabel);
                return;
            }

            if (
                message.Contains(
                    "rescue worker stdout: direct index repair failed:",
                    StringComparison.Ordinal
                )
            )
            {
                if (
                    !TryExtractManualThumbnailDirectIndexRepairMoviePath(
                        message,
                        out string moviePath
                    )
                    || !ShouldHandleTrackedManualThumbnailDirectIndexRepairLog(slotLabel, moviePath)
                )
                {
                    return;
                }

                RememberManualThumbnailRescueMoviePath(moviePath);
                ReportManualThumbnailRescueResult(
                    "インデックス再構築失敗。詳細はログを確認してください。",
                    ManualThumbnailRescueFailureCloseDelayMs,
                    NotificationType.Error,
                    ManualThumbnailRescueFailureToastTitle
                );
                ClearTrackedManualThumbnailDirectIndexRepairRequest(slotLabel);
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

            if (message.Contains("direct index repair launch failed:", StringComparison.Ordinal))
            {
                ReportManualThumbnailRescueResult(
                    "インデックス再構築worker を起動できませんでした。",
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

            if (message.Contains("direct index repair launch skipped:", StringComparison.Ordinal))
            {
                ReportManualThumbnailRescueResult(
                    "インデックス再構築worker を起動できませんでした。",
                    ManualThumbnailRescueFailureCloseDelayMs,
                    NotificationType.Error,
                    ManualThumbnailRescueFailureToastTitle
                );
                return;
            }

            if (message.Contains("manual rescue slots are busy.", StringComparison.Ordinal))
            {
                ReportManualThumbnailRescueNotice(
                    "手動救済worker 2本が稼働中です。空き次第開始します。"
                );
                return;
            }

            if (message.Contains("manual direct index repair slots are busy.", StringComparison.Ordinal))
            {
                ReportManualThumbnailRescueNotice(
                    "手動救済worker 2本が稼働中です。空いてから再実行してください。"
                );
                return;
            }

            if (message.Contains("rescue worker exited:", StringComparison.Ordinal))
            {
                bool hadTrackedRescueRequest = HasTrackedManualThumbnailRescueRequest(slotLabel);
                bool hadTrackedDirectIndexRepairRequest =
                    HasTrackedManualThumbnailDirectIndexRepairRequest(slotLabel);
                ClearTrackedManualThumbnailRescueRequest(slotLabel);
                ClearTrackedManualThumbnailDirectIndexRepairRequest(slotLabel);
                int progressState = Volatile.Read(ref _manualThumbnailRescueProgressState);
                if (
                    progressState == ManualThumbnailRescueProgressStateResultShown
                    || progressState == ManualThumbnailRescueProgressStateResolvingResult
                )
                {
                    return;
                }

                if (hadTrackedDirectIndexRepairRequest)
                {
                    ReportManualThumbnailRescueResult(
                        "インデックス再構築worker が終了しました。再起動後に再実行してください。",
                        ManualThumbnailRescueFailureCloseDelayMs,
                        NotificationType.Error,
                        ManualThumbnailRescueFailureToastTitle
                    );
                    return;
                }

                if (hadTrackedRescueRequest)
                {
                    ReportManualThumbnailRescueResult(
                        "救済worker が終了しました。詳細はログを確認してください。",
                        ManualThumbnailRescueFailureCloseDelayMs,
                        NotificationType.Error,
                        ManualThumbnailRescueFailureToastTitle
                    );
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
                    Interlocked.Exchange(
                        ref _manualThumbnailRescueProgressState,
                        ManualThumbnailRescueProgressStateRunning
                    );
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
                    Interlocked.Exchange(
                        ref _manualThumbnailRescueProgressState,
                        ManualThumbnailRescueProgressStateResultShown
                    );
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

        // 手動救済が duplicate 等で受理されなかった時も、必ず反応を返す。
        private void ReportManualThumbnailRescueNotice(string message)
        {
            ReportManualThumbnailRescueResult(
                message,
                ManualThumbnailRescueInfoCloseDelayMs,
                NotificationType.Information,
                ManualThumbnailRescueInfoToastTitle
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
                string displayMessage = BuildManualThumbnailRescueProgressMessage(message);
                _manualThumbnailRescueNotificationManager.Show(
                    title,
                    displayMessage,
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

        // success ログ到着直後に fast path でUI反映を試し、文言も結果に合わせて変える。
        private async Task HandleManualThumbnailRescueSucceededAsync(string slotLabel, string message)
        {
            bool reflectedImmediately = false;
            try
            {
                if (
                    TryExtractManualThumbnailRescueSuccessInfo(
                        message,
                        out long failureId,
                        out string outputThumbPath
                    )
                )
                {
                    CancellationToken token = _thumbCheckCts?.Token ?? CancellationToken.None;
                    reflectedImmediately = await TryReflectRescuedThumbnailRecordImmediatelyAsync(
                            failureId,
                            outputThumbPath,
                            token
                        )
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                reflectedImmediately = false;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-rescue-worker",
                    $"manual rescue immediate reflect failed: {ex.Message}"
                );
            }

            ReportManualThumbnailRescueResult(
                reflectedImmediately ? "救済成功。反映しました。" : "救済成功。反映待ちです。",
                ManualThumbnailRescueSuccessCloseDelayMs,
                NotificationType.Success,
                ManualThumbnailRescueSuccessToastTitle
            );
            ClearTrackedManualThumbnailRescueRequest(slotLabel);
        }

        // direct index repair 成功時は repaired 側の名前を UI へ返し、FailureDb 即時反映は行わない。
        private void HandleManualThumbnailDirectIndexRepairSucceeded(
            string slotLabel,
            string repairedMoviePath
        )
        {
            RememberManualThumbnailRescueMoviePath(repairedMoviePath);
            ReportManualThumbnailRescueResult(
                "インデックス再構築成功。",
                ManualThumbnailRescueSuccessCloseDelayMs,
                NotificationType.Success,
                ManualThumbnailRescueSuccessToastTitle
            );
            ClearTrackedManualThumbnailDirectIndexRepairRequest(slotLabel);
        }

        // slot ごとに要求した failure_id を保持し、他ジョブの通知が混ざらないようにする。
        private void RememberManualThumbnailRescueSlotRequest(string slotLabel, long failureId)
        {
            if (string.IsNullOrWhiteSpace(slotLabel) || failureId < 1)
            {
                return;
            }

            lock (_manualThumbnailRescueSlotSyncRoot)
            {
                _manualThumbnailDirectIndexRepairMoviePaths.Remove(slotLabel);
                _manualThumbnailRescueRequestedFailureIds[slotLabel] = failureId;
            }
        }

        private void ClearTrackedManualThumbnailRescueRequest(string slotLabel)
        {
            if (string.IsNullOrWhiteSpace(slotLabel))
            {
                return;
            }

            lock (_manualThumbnailRescueSlotSyncRoot)
            {
                _manualThumbnailRescueRequestedFailureIds.Remove(slotLabel);
            }
        }

        private void RememberManualThumbnailDirectIndexRepairRequest(
            string slotLabel,
            string moviePath
        )
        {
            if (string.IsNullOrWhiteSpace(slotLabel) || string.IsNullOrWhiteSpace(moviePath))
            {
                return;
            }

            lock (_manualThumbnailRescueSlotSyncRoot)
            {
                _manualThumbnailRescueRequestedFailureIds.Remove(slotLabel);
                _manualThumbnailDirectIndexRepairMoviePaths[slotLabel] = NormalizeTrackedMoviePath(
                    moviePath
                );
            }
        }

        private void ClearTrackedManualThumbnailDirectIndexRepairRequest(string slotLabel)
        {
            if (string.IsNullOrWhiteSpace(slotLabel))
            {
                return;
            }

            lock (_manualThumbnailRescueSlotSyncRoot)
            {
                _manualThumbnailDirectIndexRepairMoviePaths.Remove(slotLabel);
            }
        }

        private bool HasTrackedManualThumbnailRescueRequest(string slotLabel)
        {
            if (string.IsNullOrWhiteSpace(slotLabel))
            {
                return false;
            }

            lock (_manualThumbnailRescueSlotSyncRoot)
            {
                return _manualThumbnailRescueRequestedFailureIds.TryGetValue(slotLabel, out long failureId)
                    && failureId > 0;
            }
        }

        private bool ShouldHandleTrackedManualThumbnailRescueLog(string slotLabel, string message)
        {
            if (string.IsNullOrWhiteSpace(slotLabel) || string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            if (!TryExtractManualThumbnailRescueFailureId(message, out long failureId))
            {
                return false;
            }

            lock (_manualThumbnailRescueSlotSyncRoot)
            {
                return _manualThumbnailRescueRequestedFailureIds.TryGetValue(
                        slotLabel,
                        out long requestedFailureId
                    ) && requestedFailureId == failureId;
            }
        }

        private bool HasTrackedManualThumbnailDirectIndexRepairRequest(string slotLabel)
        {
            if (string.IsNullOrWhiteSpace(slotLabel))
            {
                return false;
            }

            lock (_manualThumbnailRescueSlotSyncRoot)
            {
                return _manualThumbnailDirectIndexRepairMoviePaths.TryGetValue(
                        slotLabel,
                        out string moviePath
                    ) && !string.IsNullOrWhiteSpace(moviePath);
            }
        }

        private bool ShouldHandleTrackedManualThumbnailDirectIndexRepairLog(
            string slotLabel,
            string moviePath
        )
        {
            if (string.IsNullOrWhiteSpace(slotLabel) || string.IsNullOrWhiteSpace(moviePath))
            {
                return false;
            }

            string normalizedMoviePath = NormalizeTrackedMoviePath(moviePath);
            lock (_manualThumbnailRescueSlotSyncRoot)
            {
                return _manualThumbnailDirectIndexRepairMoviePaths.TryGetValue(
                        slotLabel,
                        out string requestedMoviePath
                    )
                    && string.Equals(
                        requestedMoviePath,
                        normalizedMoviePath,
                        StringComparison.OrdinalIgnoreCase
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

        private static bool TryExtractManualThumbnailDirectIndexRepairMoviePath(
            string message,
            out string moviePath
        )
        {
            moviePath = "";
            const string moviePrefix = " movie='";

            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            int startIndex = message.IndexOf(moviePrefix, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return false;
            }

            startIndex += moviePrefix.Length;
            int endIndex = message.IndexOf('\'', startIndex);
            if (endIndex <= startIndex)
            {
                return false;
            }

            moviePath = message.Substring(startIndex, endIndex - startIndex);
            return !string.IsNullOrWhiteSpace(moviePath);
        }

        private static bool TryExtractManualThumbnailDirectIndexRepairSuccessInfo(
            string message,
            out string moviePath,
            out string repairedMoviePath
        )
        {
            moviePath = "";
            repairedMoviePath = "";
            const string repairedPrefix = " repaired='";

            if (!TryExtractManualThumbnailDirectIndexRepairMoviePath(message, out moviePath))
            {
                return false;
            }

            int startIndex = message.IndexOf(repairedPrefix, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return false;
            }

            startIndex += repairedPrefix.Length;
            int endIndex = message.IndexOf('\'', startIndex);
            if (endIndex <= startIndex)
            {
                return false;
            }

            repairedMoviePath = message.Substring(startIndex, endIndex - startIndex);
            return !string.IsNullOrWhiteSpace(repairedMoviePath);
        }

        // success ログから failure_id と出力jpgだけを抜き、即時反映の入力に使う。
        internal static bool TryExtractManualThumbnailRescueSuccessInfo(
            string message,
            out long failureId,
            out string outputThumbPath
        )
        {
            const string successPrefix = "rescue worker stdout: rescue succeeded: ";
            const string failureIdPrefix = "failure_id=";
            const string outputPrefix = " output='";

            failureId = 0;
            outputThumbPath = "";

            if (
                string.IsNullOrWhiteSpace(message)
                || !message.Contains(successPrefix, StringComparison.Ordinal)
            )
            {
                return false;
            }

            int failureIdStartIndex = message.IndexOf(failureIdPrefix, StringComparison.Ordinal);
            if (failureIdStartIndex < 0)
            {
                return false;
            }

            failureIdStartIndex += failureIdPrefix.Length;
            int failureIdEndIndex = message.IndexOf(' ', failureIdStartIndex);
            if (failureIdEndIndex < 0)
            {
                failureIdEndIndex = message.Length;
            }

            if (
                !long.TryParse(
                    message.Substring(failureIdStartIndex, failureIdEndIndex - failureIdStartIndex),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out failureId
                )
            )
            {
                failureId = 0;
                return false;
            }

            int outputStartIndex = message.IndexOf(outputPrefix, StringComparison.Ordinal);
            if (outputStartIndex < 0)
            {
                failureId = 0;
                return false;
            }

            outputStartIndex += outputPrefix.Length;
            int outputEndIndex = message.LastIndexOf('\'');
            if (outputEndIndex <= outputStartIndex)
            {
                failureId = 0;
                return false;
            }

            outputThumbPath = message.Substring(outputStartIndex, outputEndIndex - outputStartIndex);
            return !string.IsNullOrWhiteSpace(outputThumbPath);
        }

        // leased / gave up / succeeded 共通で failure_id だけを抜き、要求対象と照合する。
        private static bool TryExtractManualThumbnailRescueFailureId(
            string message,
            out long failureId
        )
        {
            const string failureIdPrefix = "failure_id=";

            failureId = 0;
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            int failureIdStartIndex = message.IndexOf(failureIdPrefix, StringComparison.Ordinal);
            if (failureIdStartIndex < 0)
            {
                return false;
            }

            failureIdStartIndex += failureIdPrefix.Length;
            int failureIdEndIndex = failureIdStartIndex;
            while (
                failureIdEndIndex < message.Length
                && char.IsDigit(message, failureIdEndIndex)
            )
            {
                failureIdEndIndex++;
            }

            if (failureIdEndIndex <= failureIdStartIndex)
            {
                return false;
            }

            return long.TryParse(
                message.Substring(failureIdStartIndex, failureIdEndIndex - failureIdStartIndex),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out failureId
            );
        }

        private static string NormalizeTrackedMoviePath(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(moviePath.Trim());
            }
            catch
            {
                return moviePath.Trim();
            }
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
                    Interlocked.Exchange(
                        ref _manualThumbnailRescueProgressState,
                        ManualThumbnailRescueProgressStateIdle
                    );
                })
            );
        }
    }
}
