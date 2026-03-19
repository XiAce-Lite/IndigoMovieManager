using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.UpperTabs.Rescue;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly ObservableCollection<UpperTabRescueHistoryItemViewModel> _upperTabRescueHistoryItems =
            [];

        // 救済タブの選択変更では既存の詳細同期に続けて、下段の履歴だけを差し替える。
        private void UpperTabRescueListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            List_SelectionChanged(sender, e);
            RefreshUpperTabRescueHistoryPanel();
        }

        private void RefreshUpperTabRescueHistoryPanel()
        {
            if (UpperTabRescueViewHost == null)
            {
                return;
            }

            if (UpperTabRescueViewHost.RescueHistoryDataGridControl.ItemsSource == null)
            {
                UpperTabRescueViewHost.RescueHistoryDataGridControl.ItemsSource =
                    _upperTabRescueHistoryItems;
            }

            UpperTabRescueListItemViewModel selectedItem = GetSelectedUpperTabRescueItems().FirstOrDefault();
            if (selectedItem == null)
            {
                ClearUpperTabRescueHistoryPanel(
                    "動画を選ぶと履歴を表示します。",
                    "履歴を表示する動画が未選択です。"
                );
                return;
            }

            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            int targetTabIndex = GetSelectedUpperTabRescueTargetOption()?.TabIndex ?? UpperTabGridFixedIndex;
            string targetTabName = GetThumbnailTabDisplayName(targetTabIndex);
            SetUpperTabRescueHistoryTargetText(
                $"{selectedItem.MovieName} / {targetTabName}"
            );

            if (failureDbService == null)
            {
                ReplaceUpperTabRescueHistoryItems([]);
                SetUpperTabRescueHistoryEmptyMessage("FailureDb を開けないため履歴を読めません。", true);
                return;
            }

            string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(selectedItem.MoviePath);
            List<ThumbnailFailureRecord> records =
            [
                .. failureDbService
                    .GetFailureRecords(limit: 400)
                    .Where(record =>
                        record != null
                        && string.Equals(record.MoviePathKey, moviePathKey, StringComparison.Ordinal)
                        && record.TabIndex == targetTabIndex
                        && (
                            string.Equals(record.Lane, "normal", StringComparison.Ordinal)
                            || string.Equals(record.Lane, "slow", StringComparison.Ordinal)
                            || string.Equals(record.Lane, "rescue", StringComparison.Ordinal)
                        )
                    )
                    .OrderByDescending(record => record.UpdatedAtUtc)
                    .ThenByDescending(record => record.FailureId)
                    .Take(40),
            ];
            ReplaceUpperTabRescueHistoryItems(
                records.Select(BuildUpperTabRescueHistoryItem)
            );
            SetUpperTabRescueHistoryEmptyMessage("履歴はまだありません。", _upperTabRescueHistoryItems.Count < 1);
        }

        private void ClearUpperTabRescueHistoryPanel(string targetText, string emptyMessage)
        {
            ReplaceUpperTabRescueHistoryItems([]);
            SetUpperTabRescueHistoryTargetText(targetText);
            SetUpperTabRescueHistoryEmptyMessage(emptyMessage, true);
        }

        private void ReplaceUpperTabRescueHistoryItems(
            IEnumerable<UpperTabRescueHistoryItemViewModel> items
        )
        {
            _upperTabRescueHistoryItems.Clear();
            foreach (UpperTabRescueHistoryItemViewModel item in items ?? [])
            {
                _upperTabRescueHistoryItems.Add(item);
            }
        }

        private void SetUpperTabRescueHistoryTargetText(string text)
        {
            if (UpperTabRescueViewHost == null)
            {
                return;
            }

            UpperTabRescueViewHost.HistoryTargetTextBlockControl.Text = text ?? "";
        }

        private void SetUpperTabRescueHistoryEmptyMessage(string text, bool isVisible)
        {
            if (UpperTabRescueViewHost == null)
            {
                return;
            }

            UpperTabRescueViewHost.HistoryEmptyTextBlockControl.Text = text ?? "";
            UpperTabRescueViewHost.HistoryEmptyTextBlockControl.Visibility = isVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // FailureDb の親行と試行行を、救済タブ下段の読みやすい時系列へ落とす。
        private UpperTabRescueHistoryItemViewModel BuildUpperTabRescueHistoryItem(
            ThumbnailFailureRecord record
        )
        {
            ThumbnailErrorProgressSnapshot progressSnapshot =
                ParseThumbnailErrorProgressSnapshot(record?.ExtraJson ?? "");
            DateTime timestampUtc = record?.UpdatedAtUtc ?? DateTime.MinValue;
            if (timestampUtc <= DateTime.MinValue)
            {
                timestampUtc = record?.CreatedAtUtc ?? DateTime.MinValue;
            }

            return new UpperTabRescueHistoryItemViewModel
            {
                TimestampText = timestampUtc > DateTime.MinValue
                    ? timestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                    : "",
                LaneText = ResolveUpperTabRescueHistoryLaneText(record),
                ActionText = ResolveUpperTabRescueHistoryActionText(record, progressSnapshot),
                ResultText = ResolveUpperTabRescueHistoryResultText(record),
                AttemptText = ResolveUpperTabRescueHistoryAttemptText(record, progressSnapshot),
                EngineText = ResolveUpperTabRescueHistoryEngineText(record, progressSnapshot),
                DetailText = ResolveUpperTabRescueHistoryDetailText(record, progressSnapshot),
            };
        }

        private static string ResolveUpperTabRescueHistoryLaneText(ThumbnailFailureRecord record)
        {
            return (record?.Lane ?? "") switch
            {
                "normal" => "通常要求",
                "slow" => "slow要求",
                "rescue" => "救済worker",
                _ => record?.Lane ?? "",
            };
        }

        private static string ResolveUpperTabRescueHistoryActionText(
            ThumbnailFailureRecord record,
            ThumbnailErrorProgressSnapshot progressSnapshot
        )
        {
            if (record == null)
            {
                return "";
            }

            if (string.Equals(record.Lane, "rescue"))
            {
                bool repairApplied = record.RepairApplied || progressSnapshot.RepairApplied;
                return repairApplied ? "repair後試行" : "直接試行";
            }

            string phase = progressSnapshot.Phase ?? "";
            if (!string.IsNullOrWhiteSpace(phase))
            {
                return phase switch
                {
                    "manual_rescue_request" => "救済要求受付",
                    "direct_start" => "直接救済開始",
                    "direct_engine_attempt" => "直接エンジン試行",
                    "direct_engine_failed" => "直接エンジン失敗",
                    "direct_engine_exception" => "直接エンジン例外",
                    "direct_exhausted" => "直接経路終了",
                    "repair_probe" => "repair判定",
                    "repair_probe_negative" => "repair不要",
                    "repair_execute" => "repair実行",
                    "repair_failed" => "repair失敗",
                    "repair_engine_attempt" => "repair後エンジン試行",
                    "repair_engine_failed" => "repair後エンジン失敗",
                    "repair_engine_exception" => "repair後エンジン例外",
                    "repair_exhausted" => "repair経路終了",
                    "repair_rescue" => "repair後成功",
                    "direct" => "直接成功",
                    "route_exhausted" => "救済経路終了",
                    "reflected" => "UI反映完了",
                    "reflected_no_ui_match" => "UI反映完了",
                    "requeue_output_missing" => "出力欠損で再投入",
                    _ => phase,
                };
            }

            return (record.Status ?? "") switch
            {
                "pending_rescue" => "救済待機",
                "processing_rescue" => "救済進行",
                "rescued" => "救済完了",
                "reflected" => "UI反映完了",
                "gave_up" => "救済打ち切り",
                "skipped" => "救済対象外",
                "attempt_failed" => "エンジン失敗",
                _ => record.Status ?? "",
            };
        }

        private static string ResolveUpperTabRescueHistoryResultText(ThumbnailFailureRecord record)
        {
            return (record?.Status ?? "") switch
            {
                "pending_rescue" => "待機",
                "processing_rescue" => "進行中",
                "rescued" => "成功",
                "reflected" => "反映済み",
                "gave_up" => "失敗",
                "skipped" => "対象外",
                "attempt_failed" => "失敗",
                _ => record?.Status ?? "",
            };
        }

        private static string ResolveUpperTabRescueHistoryAttemptText(
            ThumbnailFailureRecord record,
            ThumbnailErrorProgressSnapshot progressSnapshot
        )
        {
            int attemptNo = progressSnapshot?.AttemptNo ?? 0;
            if (attemptNo < 1)
            {
                attemptNo = record?.AttemptNo ?? 0;
            }

            return attemptNo > 0 ? attemptNo.ToString() : "";
        }

        private static string ResolveUpperTabRescueHistoryEngineText(
            ThumbnailFailureRecord record,
            ThumbnailErrorProgressSnapshot progressSnapshot
        )
        {
            string engine = progressSnapshot?.Engine ?? "";
            if (string.IsNullOrWhiteSpace(engine))
            {
                engine = record?.Engine ?? "";
            }

            return string.IsNullOrWhiteSpace(engine) ? "-" : engine;
        }

        private static string ResolveUpperTabRescueHistoryDetailText(
            ThumbnailFailureRecord record,
            ThumbnailErrorProgressSnapshot progressSnapshot
        )
        {
            List<string> parts = [];

            string failureReason = progressSnapshot?.CurrentFailureReason ?? "";
            if (string.IsNullOrWhiteSpace(failureReason))
            {
                failureReason = record?.FailureReason ?? "";
            }

            string normalizedReason = NormalizeThumbnailErrorDetailText(failureReason);
            if (!string.IsNullOrWhiteSpace(normalizedReason))
            {
                parts.Add(normalizedReason);
            }

            string detail = progressSnapshot?.Detail ?? "";
            if (
                !string.IsNullOrWhiteSpace(detail)
                && !parts.Contains(detail)
            )
            {
                parts.Add(detail);
            }

            string failureKindText = ResolveThumbnailErrorFailureKindText(
                progressSnapshot?.CurrentFailureKind ?? "",
                record?.FailureKind ?? ThumbnailFailureKind.Unknown
            );
            if (
                !string.IsNullOrWhiteSpace(failureKindText)
                && !parts.Contains(failureKindText)
            )
            {
                parts.Add(failureKindText);
            }

            if (
                !string.IsNullOrWhiteSpace(record?.OutputThumbPath)
                && string.Equals(record.Status, "rescued")
            )
            {
                parts.Add($"出力: {record.OutputThumbPath}");
            }

            return string.Join(" / ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }
}
