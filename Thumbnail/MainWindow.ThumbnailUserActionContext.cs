using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int ThumbnailUserActionOverlayCloseDelayMs = 6500;
        private CancellationTokenSource _thumbnailUserActionOverlayCloseCts;

        private enum ThumbnailUserActionSelectionContext
        {
            CurrentUpperTab = 0,
            BottomThumbnailError = 1,
        }

        private enum ThumbnailDirectIndexRepairStartResult
        {
            Started = 0,
            Busy = 1,
            Invalid = 2,
        }

        internal readonly record struct ThumbnailIndexRepairSelectionSummary(
            int SelectedCount,
            int SupportedCount,
            int UnsupportedCount
        );

        internal readonly record struct ThumbnailRescueUserActionRequest(
            int TargetTabIndex,
            ThumbnailQueuePriority Priority,
            string Reason,
            bool UseDedicatedManualWorkerSlot,
            bool SkipWhenSuccessExists,
            string RescueMode,
            bool DeleteErrorMarkerFirst
        );

        internal readonly record struct ThumbnailRescueUserActionDispatchResult(
            int SelectedCount,
            int AcceptedCount,
            int DuplicateRequestCount,
            int ExistingSuccessCount
        );

        internal readonly record struct ThumbnailDirectIndexRepairDispatchResult(
            int SelectedCount,
            int StartedCount,
            int BusyCount,
            int UnsupportedCount
        );

        // ユーザーが単動画だけを明示操作した時は、一般待ちへ埋もれないよう差し込み job 扱いへ寄せる。
        internal static bool ShouldUseDedicatedManualWorkerSlotForThumbnailUserAction(
            bool requestedDedicatedManualWorkerSlot,
            int selectedMovieCount
        )
        {
            if (requestedDedicatedManualWorkerSlot)
            {
                return true;
            }

            return selectedMovieCount == 1;
        }

        // 右クリック共通メニューは複数一覧から開かれるため、発火元の一覧を先に確定する。
        private ThumbnailUserActionSelectionContext ResolveThumbnailUserActionSelectionContext(
            object sender
        )
        {
            return IsThumbnailErrorBottomContextMenuInvocation(sender)
                ? ThumbnailUserActionSelectionContext.BottomThumbnailError
                : ThumbnailUserActionSelectionContext.CurrentUpperTab;
        }

        // 下部サムネ失敗タブから開いた右クリックだけを見分け、上側タブ選択の誤参照を防ぐ。
        private bool IsThumbnailErrorBottomContextMenuInvocation(object sender)
        {
            ContextMenu contextMenu = ResolveOwningContextMenu(sender);
            if (contextMenu?.PlacementTarget is not DependencyObject placementTarget)
            {
                return false;
            }

            DataGrid errorDataGrid = GetThumbnailErrorDataGrid();
            return errorDataGrid != null && IsSameOrDescendantOf(placementTarget, errorDataGrid);
        }

        // MenuItem から親 ContextMenu まで辿り、共通メニューでも発火元コントロールを引けるようにする。
        private static ContextMenu ResolveOwningContextMenu(object sender)
        {
            if (sender is ContextMenu directContextMenu)
            {
                return directContextMenu;
            }

            DependencyObject current = sender as DependencyObject;
            while (current != null)
            {
                if (current is ContextMenu contextMenu)
                {
                    return contextMenu;
                }

                current = ResolveParent(current);
            }

            return null;
        }

        private static bool IsSameOrDescendantOf(DependencyObject candidate, DependencyObject ancestor)
        {
            for (DependencyObject current = candidate; current != null; current = ResolveParent(current))
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
            }

            return false;
        }

        private static DependencyObject ResolveParent(DependencyObject current)
        {
            if (current == null)
            {
                return null;
            }

            DependencyObject logicalParent = LogicalTreeHelper.GetParent(current);
            if (logicalParent != null)
            {
                return logicalParent;
            }

            return current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : null;
        }

        // サムネ系ユーザー操作だけは、発火元一覧に応じた対象動画一覧を返す。
        private List<MovieRecords> ResolveSelectedMovieRecordsForThumbnailUserAction(object sender)
        {
            if (
                ResolveThumbnailUserActionSelectionContext(sender)
                == ThumbnailUserActionSelectionContext.BottomThumbnailError
            )
            {
                return NormalizeThumbnailUserActionMovieRecords(
                    GetSelectedThumbnailErrorRecords().Select(record => record?.MovieRecord)
                );
            }

            return NormalizeThumbnailUserActionMovieRecords(GetSelectedItemsByTabIndex());
        }

        private MovieRecords ResolveSelectedMovieRecordForThumbnailUserAction(object sender)
        {
            return ResolveSelectedMovieRecordsForThumbnailUserAction(sender).FirstOrDefault();
        }

        // 同じ動画が複数行から見えても、手動操作の要求は動画単位で一度だけ扱う。
        private static List<MovieRecords> NormalizeThumbnailUserActionMovieRecords(
            IEnumerable<MovieRecords> records
        )
        {
            List<MovieRecords> result = [];
            HashSet<string> seenMoviePaths = new(StringComparer.OrdinalIgnoreCase);

            foreach (MovieRecords record in records ?? [])
            {
                if (record == null || string.IsNullOrWhiteSpace(record.Movie_Path))
                {
                    continue;
                }

                string moviePath = record.Movie_Path.Trim();
                if (!seenMoviePaths.Add(moviePath))
                {
                    continue;
                }

                result.Add(record);
            }

            return result;
        }

        // ユーザー要請の開始結果だけは、トーストに埋もれないよう必ずポップアップで返す。
        private void ShowThumbnailUserActionPopup(
            string title,
            string message,
            MessageBoxImage image = MessageBoxImage.Information
        )
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (!IsLoaded || _uiHangNotificationCoordinator == null)
            {
                void showPopup()
                {
                    MessageBox.Show(
                        this,
                        message ?? "",
                        string.IsNullOrWhiteSpace(title) ? "サムネイル" : title,
                        MessageBoxButton.OK,
                        image
                    );
                }

                if (Dispatcher.CheckAccess())
                {
                    showPopup();
                    return;
                }

                Dispatcher.Invoke(showPopup);
                return;
            }

            void showOverlay()
            {
                UiHangNotificationLevel level = ResolveThumbnailUserActionOverlayLevel(
                    title,
                    message,
                    image
                );
                ShowThumbnailUserActionOverlay(
                    BuildThumbnailUserActionOverlayMessage(title, message, level),
                    level,
                    ThumbnailUserActionOverlayCloseDelayMs
                );
            }

            if (Dispatcher.CheckAccess())
            {
                showOverlay();
                return;
            }

            Dispatcher.Invoke(showOverlay);
        }

        // OK だけの結果通知は modal にせず、長め表示の overlay msg へ寄せる。
        private void ShowThumbnailUserActionOverlay(
            string message,
            UiHangNotificationLevel level,
            int closeDelayMs
        )
        {
            if (_uiHangNotificationCoordinator == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            CancellationTokenSource nextCts = new();
            CancellationTokenSource previousCts = Interlocked.Exchange(
                ref _thumbnailUserActionOverlayCloseCts,
                nextCts
            );
            previousCts?.Cancel();
            previousCts?.Dispose();

            _uiHangNotificationCoordinator.ShowExplicitStatus(
                level,
                message,
                allowBackground: false
            );

            _ = HideThumbnailUserActionOverlayLaterAsync(nextCts, Math.Max(1500, closeDelayMs));
        }

        private async Task HideThumbnailUserActionOverlayLaterAsync(
            CancellationTokenSource cts,
            int delayMs
        )
        {
            try
            {
                await Task.Delay(delayMs, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (!ReferenceEquals(_thumbnailUserActionOverlayCloseCts, cts))
            {
                return;
            }

            void hideOverlay()
            {
                if (!ReferenceEquals(_thumbnailUserActionOverlayCloseCts, cts))
                {
                    return;
                }

                _uiHangNotificationCoordinator?.HideExplicitStatus();
                _uiHangNotificationCoordinator?.ReevaluateVisibility();
            }

            try
            {
                if (Dispatcher.CheckAccess())
                {
                    hideOverlay();
                }
                else
                {
                    await Dispatcher.InvokeAsync(hideOverlay);
                }
            }
            finally
            {
                if (ReferenceEquals(_thumbnailUserActionOverlayCloseCts, cts))
                {
                    _thumbnailUserActionOverlayCloseCts = null;
                }

                cts.Dispose();
            }
        }

        internal static string BuildThumbnailUserActionOverlayMessage(
            string title,
            string message,
            UiHangNotificationLevel level
        )
        {
            string safeTitle = title?.Trim() ?? "";
            string safeMessage = message?.Trim() ?? "";
            bool prefersMultiLine = safeMessage.Contains(Environment.NewLine, StringComparison.Ordinal);
            string[] messageLines = SplitThumbnailUserActionMessageLines(safeMessage);
            if (string.IsNullOrWhiteSpace(safeTitle))
            {
                return BuildThumbnailUserActionOverlayBodyOnly(
                    safeMessage,
                    prefersMultiLine,
                    level,
                    messageLines
                );
            }

            if (string.IsNullOrWhiteSpace(safeMessage))
            {
                return safeTitle;
            }

            if (level == UiHangNotificationLevel.Success && messageLines.Length > 1)
            {
                return BuildSuccessThumbnailUserActionOverlayMessage(safeTitle, messageLines);
            }

            string compactBody = string.Join(
                " / ",
                messageLines
            );
            if (string.IsNullOrWhiteSpace(compactBody))
            {
                return safeTitle;
            }

            return !prefersMultiLine && safeTitle.Length + 1 + compactBody.Length <= 52
                ? $"{safeTitle} {compactBody}"
                : $"{safeTitle}{Environment.NewLine}{compactBody}";
        }

        private static string BuildThumbnailUserActionOverlayBodyOnly(
            string message,
            bool prefersMultiLine = false,
            UiHangNotificationLevel level = UiHangNotificationLevel.Caution,
            string[] messageLines = null
        )
        {
            messageLines ??= SplitThumbnailUserActionMessageLines(message);
            if (level == UiHangNotificationLevel.Success && messageLines.Length > 2)
            {
                return string.Join(
                    Environment.NewLine,
                    messageLines[0],
                    messageLines[1],
                    string.Join(" / ", messageLines.Skip(2))
                );
            }

            string compactBody = string.Join(" / ", messageLines);
            return (!prefersMultiLine && compactBody.Length <= 52) || !compactBody.Contains(" / ")
                ? compactBody
                : ReplaceFirst(compactBody, " / ", Environment.NewLine);
        }

        private static string BuildSuccessThumbnailUserActionOverlayMessage(
            string title,
            string[] messageLines
        )
        {
            if (messageLines == null || messageLines.Length < 1)
            {
                return title ?? "";
            }

            if (messageLines.Length == 1)
            {
                return string.Concat(title, Environment.NewLine, messageLines[0]);
            }

            return string.Join(
                Environment.NewLine,
                title,
                messageLines[0],
                string.Join(" / ", messageLines.Skip(1))
            );
        }

        private static string[] SplitThumbnailUserActionMessageLines(string message)
        {
            return (message ?? "")
                .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
        }

        // overlay は最大 2 行運用なので、先頭の区切りだけを改行へ置き換える。
        private static string ReplaceFirst(string source, string oldValue, string newValue)
        {
            if (
                string.IsNullOrEmpty(source)
                || string.IsNullOrEmpty(oldValue)
                || oldValue.Equals(newValue, StringComparison.Ordinal)
            )
            {
                return source ?? "";
            }

            int index = source.IndexOf(oldValue, StringComparison.Ordinal);
            if (index < 0)
            {
                return source;
            }

            return string.Concat(
                source.AsSpan(0, index),
                newValue,
                source.AsSpan(index + oldValue.Length)
            );
        }

        // 受付成功・注意喚起・不開始を文言から寄せ、ユーザー要請の結果色を意味別で揃える。
        internal static UiHangNotificationLevel ResolveThumbnailUserActionOverlayLevel(
            string title,
            string message,
            MessageBoxImage image
        )
        {
            string normalized = string.Join(
                Environment.NewLine,
                new[] { title, message }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            );

            if (
                ContainsAny(
                    normalized,
                    "受け付けました。",
                    "開始しました。",
                    "反映しました。"
                )
            )
            {
                return UiHangNotificationLevel.Success;
            }

            if (
                ContainsAny(
                    normalized,
                    "対象タブを選択してから実行してください。",
                    "対象動画が選択されていません。",
                    "対象動画がありません。",
                    "インデックス再構築対象の動画がありません。",
                    "空いてから再実行してください。",
                    "既に実行中です。",
                    "既に救済中または救済待ち",
                    "既に正常サムネイルあり",
                    "対象外 ",
                    "処理先のサムネイルタブを特定できませんでした。"
                )
            )
            {
                return UiHangNotificationLevel.Caution;
            }

            if (
                ContainsAny(
                    normalized,
                    "受け付けられませんでした。",
                    "開始できませんでした。",
                    "反映できませんでした。"
                )
            )
            {
                return UiHangNotificationLevel.Warning;
            }

            return image switch
            {
                MessageBoxImage.Information => UiHangNotificationLevel.Success,
                MessageBoxImage.Error => UiHangNotificationLevel.Warning,
                MessageBoxImage.Warning => UiHangNotificationLevel.Warning,
                _ => UiHangNotificationLevel.Caution,
            };
        }

        private static bool ContainsAny(string text, params string[] fragments)
        {
            if (string.IsNullOrWhiteSpace(text) || fragments == null || fragments.Length < 1)
            {
                return false;
            }

            foreach (string fragment in fragments)
            {
                if (string.IsNullOrWhiteSpace(fragment))
                {
                    continue;
                }

                if (text.Contains(fragment, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // rescue request 系は入口ごとの差を DTO に閉じ込め、件数集計だけを共通化する。
        private ThumbnailRescueUserActionDispatchResult DispatchThumbnailRescueUserAction(
            IEnumerable<MovieRecords> records,
            ThumbnailRescueUserActionRequest request
        )
        {
            List<MovieRecords> normalizedRecords = NormalizeThumbnailUserActionMovieRecords(records);
            if (normalizedRecords.Count < 1 || request.TargetTabIndex < 0)
            {
                return new ThumbnailRescueUserActionDispatchResult(0, 0, 0, 0);
            }

            string currentDbName = MainVM?.DbInfo?.DBName ?? "";
            string currentThumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            string targetThumbOutPath = ResolveThumbnailOutPath(
                request.TargetTabIndex,
                currentDbName,
                currentThumbFolder
            );
            bool useDedicatedManualWorkerSlot =
                ShouldUseDedicatedManualWorkerSlotForThumbnailUserAction(
                    request.UseDedicatedManualWorkerSlot,
                    normalizedRecords.Count
                );
            if (useDedicatedManualWorkerSlot && normalizedRecords.Count == 1)
            {
                RememberManualThumbnailRescueMoviePath(normalizedRecords[0].Movie_Path);
            }

            int acceptedCount = 0;
            int duplicateRequestCount = 0;
            int existingSuccessCount = 0;
            bool shouldShowDedicatedManualPanel =
                useDedicatedManualWorkerSlot && normalizedRecords.Count == 1;
            string firstAcceptedMoviePath = "";

            foreach (MovieRecords record in normalizedRecords)
            {
                QueueObj queueObj = new()
                {
                    MovieId = record.Movie_Id,
                    MovieFullPath = record.Movie_Path,
                    Hash = record.Hash,
                    Tabindex = request.TargetTabIndex,
                    Priority = request.Priority,
                };

                if (request.DeleteErrorMarkerFirst)
                {
                    TryDeleteThumbnailErrorMarker(targetThumbOutPath, queueObj.MovieFullPath);
                }

                ThumbnailRescueRequestResult enqueueResult = TryEnqueueThumbnailRescueJobDetailed(
                    queueObj,
                    requiresIdle: false,
                    reason: request.Reason,
                    useDedicatedManualWorkerSlot: useDedicatedManualWorkerSlot,
                    skipWhenSuccessExists: request.SkipWhenSuccessExists,
                    rescueMode: request.RescueMode
                );
                switch (enqueueResult)
                {
                    case ThumbnailRescueRequestResult.Accepted:
                    case ThumbnailRescueRequestResult.Promoted:
                        acceptedCount++;
                        if (
                            shouldShowDedicatedManualPanel
                            && string.IsNullOrWhiteSpace(firstAcceptedMoviePath)
                        )
                        {
                            firstAcceptedMoviePath = record.Movie_Path ?? "";
                        }
                        break;
                    case ThumbnailRescueRequestResult.DuplicateExistingRequest:
                        duplicateRequestCount++;
                        break;
                    case ThumbnailRescueRequestResult.SkippedExistingSuccess:
                        existingSuccessCount++;
                        break;
                }
            }

            if (shouldShowDedicatedManualPanel && !string.IsNullOrWhiteSpace(firstAcceptedMoviePath))
            {
                ShowTransientThumbnailProgressRescueWorkerPanel(
                    firstAcceptedMoviePath,
                    "差し込み救済",
                    "手動救済を受け付けました。"
                );
            }

            return new ThumbnailRescueUserActionDispatchResult(
                normalizedRecords.Count,
                acceptedCount,
                duplicateRequestCount,
                existingSuccessCount
            );
        }

        // direct index repair は manual slot の開始結果だけを集め、UI 側では同じ result DTO を見る。
        private ThumbnailDirectIndexRepairDispatchResult DispatchThumbnailDirectIndexRepairUserAction(
            IEnumerable<MovieRecords> records,
            int targetTabIndex,
            string reason
        )
        {
            List<MovieRecords> normalizedRecords = NormalizeThumbnailUserActionMovieRecords(records);
            int startedCount = 0;
            int busyCount = 0;
            int unsupportedCount = 0;

            foreach (MovieRecords record in normalizedRecords)
            {
                if (!CanTryThumbnailIndexRepair(record.Movie_Path))
                {
                    unsupportedCount++;
                    continue;
                }

                ThumbnailDirectIndexRepairStartResult startResult =
                    TryStartThumbnailDirectIndexRepairWorkerDetailed(record.Movie_Path);
                DebugRuntimeLog.Write(
                    "thumbnail-rescue",
                    $"context index repair direct start: movie='{record.Movie_Path}' tab={targetTabIndex} result={startResult} reason={reason}"
                );
                switch (startResult)
                {
                    case ThumbnailDirectIndexRepairStartResult.Started:
                        startedCount++;
                        break;
                    case ThumbnailDirectIndexRepairStartResult.Busy:
                        busyCount++;
                        break;
                }
            }

            return new ThumbnailDirectIndexRepairDispatchResult(
                normalizedRecords.Count,
                startedCount,
                busyCount,
                unsupportedCount
            );
        }

        internal static string BuildThumbnailRescueUserActionPopupMessage(
            string actionLabel,
            int selectedCount,
            int acceptedCount,
            int duplicateRequestCount,
            int existingSuccessCount
        )
        {
            string safeActionLabel = string.IsNullOrWhiteSpace(actionLabel) ? "サムネイル救済" : actionLabel;
            if (selectedCount < 1)
            {
                return "対象動画が選択されていません。";
            }

            bool isAlreadyQueuedOnly =
                acceptedCount < 1 && duplicateRequestCount > 0 && existingSuccessCount < 1;
            bool isExistingSuccessOnly =
                acceptedCount < 1 && duplicateRequestCount < 1 && existingSuccessCount > 0;
            bool isNoOpOnly =
                acceptedCount < 1 && duplicateRequestCount > 0 && existingSuccessCount > 0;

            List<string> lines =
            [
                acceptedCount > 0
                    ? $"{safeActionLabel}を受け付けました。"
                    : isAlreadyQueuedOnly
                        ? $"{safeActionLabel}は既に実行中です。"
                    : isExistingSuccessOnly
                        ? $"{safeActionLabel}は不要でした。"
                    : isNoOpOnly
                        ? $"{safeActionLabel}の対象は既に処理済みまたは実行中です。"
                    : $"{safeActionLabel}は受け付けられませんでした。",
                $"対象 {selectedCount}件 / 受付 {acceptedCount}件",
            ];

            if (duplicateRequestCount > 0)
            {
                lines.Add($"既に救済中または救済待ち {duplicateRequestCount}件");
            }

            if (existingSuccessCount > 0)
            {
                lines.Add($"既に正常サムネイルあり {existingSuccessCount}件");
            }

            return string.Join(Environment.NewLine, lines);
        }

        internal static MessageBoxImage ResolveThumbnailRescueUserActionPopupImage(
            int acceptedCount,
            int duplicateRequestCount,
            int existingSuccessCount
        )
        {
            if (acceptedCount > 0)
            {
                return MessageBoxImage.Information;
            }

            if (duplicateRequestCount < 1 && existingSuccessCount > 0)
            {
                return MessageBoxImage.Information;
            }

            if (duplicateRequestCount > 0 || existingSuccessCount > 0)
            {
                return MessageBoxImage.Warning;
            }

            return MessageBoxImage.Warning;
        }

        internal static string BuildThumbnailIndexRepairUserActionPopupMessage(
            int selectedCount,
            int startedCount,
            int busyCount,
            int unsupportedCount
        )
        {
            if (selectedCount < 1)
            {
                return "対象動画が選択されていません。";
            }

            if (startedCount < 1 && busyCount > 0)
            {
                return "手動救済worker 2本が稼働中です。空いてから再実行してください。";
            }

            if (startedCount < 1 && unsupportedCount > 0)
            {
                return "インデックス再構築対象の動画がありません。";
            }

            List<string> lines =
            [
                startedCount > 0
                    ? "インデックス再構築を開始しました。"
                    : "インデックス再構築は開始できませんでした。",
                $"対象 {selectedCount}件 / 開始 {startedCount}件",
            ];

            if (busyCount > 0)
            {
                lines.Add($"空き不足 {busyCount}件");
            }

            if (unsupportedCount > 0)
            {
                lines.Add($"対象外 {unsupportedCount}件");
            }

            return string.Join(Environment.NewLine, lines);
        }

        // 直接インデックス再構築は同じ動画を二重起動しないため、件数表示も動画単位で揃える。
        internal static ThumbnailIndexRepairSelectionSummary SummarizeThumbnailIndexRepairSelection(
            IEnumerable<string> moviePaths
        )
        {
            int supportedCount = 0;
            int unsupportedCount = 0;
            HashSet<string> seenMoviePaths = new(StringComparer.OrdinalIgnoreCase);

            foreach (string moviePath in moviePaths ?? [])
            {
                if (string.IsNullOrWhiteSpace(moviePath))
                {
                    continue;
                }

                string normalizedMoviePath = moviePath.Trim();
                if (!seenMoviePaths.Add(normalizedMoviePath))
                {
                    continue;
                }

                if (CanTryThumbnailIndexRepair(normalizedMoviePath))
                {
                    supportedCount++;
                    continue;
                }

                unsupportedCount++;
            }

            return new ThumbnailIndexRepairSelectionSummary(
                SelectedCount: supportedCount + unsupportedCount,
                SupportedCount: supportedCount,
                UnsupportedCount: unsupportedCount
            );
        }

        internal static string BuildThumbnailQueueUserActionPopupMessage(
            string actionLabel,
            int selectedCount,
            int queuedCount
        )
        {
            string safeActionLabel = string.IsNullOrWhiteSpace(actionLabel)
                ? "サムネイル処理"
                : actionLabel;
            if (selectedCount < 1)
            {
                return "対象動画が選択されていません。";
            }

            return string.Join(
                Environment.NewLine,
                queuedCount > 0
                    ? $"{safeActionLabel}を開始しました。"
                    : $"{safeActionLabel}は開始できませんでした。",
                $"対象 {selectedCount}件 / 開始 {queuedCount}件"
            );
        }

        internal static string BuildThumbnailBlackConfirmUserActionPopupMessage(
            int selectedCount,
            int generatedCount
        )
        {
            if (selectedCount < 1)
            {
                return "対象動画が選択されていません。";
            }

            return string.Join(
                Environment.NewLine,
                generatedCount > 0 ? "黒確定を反映しました。" : "黒確定は反映できませんでした。",
                $"対象 {selectedCount}件 / 反映 {generatedCount}件"
            );
        }
    }
}
