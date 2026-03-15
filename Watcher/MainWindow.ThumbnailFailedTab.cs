using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using IndigoMovieManager.ViewModels;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int ThumbnailErrorTabIndex = 5;
        private static readonly int[] ThumbnailErrorTargetTabIndices = [0, 1, 2, 3, 4, 99];

        // ERROR マーカーと FailureDb の現行状態を突き合わせ、見せる一覧を組み直す。
        private void RefreshThumbnailErrorRecords()
        {
            DebugRuntimeLog.Write("thumbnail-error-tab", "error tab refresh start");

            Dictionary<string, ThumbnailFailureRecord> latestFailureRecordsByKey =
                LoadLatestThumbnailErrorRecordsByKey();
            var items = MainVM
                .MovieRecs.Select(movie => BuildThumbnailErrorRecord(movie, latestFailureRecordsByKey))
                .Where(x => x != null)
                .OrderByDescending(
                    x => x.ProgressUpdatedAt ?? x.LastMarkerWriteTime ?? DateTime.MinValue
                )
                .ThenBy(x => x.MovieName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            MainVM.ReplaceThumbnailErrorRecs(items);
            MainVM.ThumbnailErrorProgress.Apply(items);

            DebugRuntimeLog.Write(
                "thumbnail-error-tab",
                $"error tab refresh end: count={items.Length}"
            );
        }

        // FailureDb の最新親行を moviePathKey + tab 単位へ畳み、進行状況表示の材料にする。
        private Dictionary<string, ThumbnailFailureRecord> LoadLatestThumbnailErrorRecordsByKey()
        {
            Dictionary<string, ThumbnailFailureRecord> records = new(StringComparer.Ordinal);

            try
            {
                ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
                if (failureDbService == null)
                {
                    return records;
                }

                foreach (ThumbnailFailureRecord record in failureDbService.GetLatestMainFailureRecords())
                {
                    string key = BuildThumbnailFailureRecordKey(record.MoviePathKey, record.TabIndex);
                    records[key] = record;
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-error-tab",
                    $"error tab failuredb load failed: {ex.Message}"
                );
            }

            return records;
        }

        // 1 動画ぶんの ERROR 状態を 1 行へ集約し、救済中も一覧から消えないようにする。
        private ThumbnailErrorRecordViewModel BuildThumbnailErrorRecord(
            MovieRecords movie,
            IReadOnlyDictionary<string, ThumbnailFailureRecord> latestFailureRecordsByKey
        )
        {
            if (movie == null || string.IsNullOrWhiteSpace(movie.Movie_Path))
            {
                return null;
            }

            string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(movie.Movie_Path);
            List<int> failedTabs = [];
            DateTime? lastWriteTime = null;
            List<ThumbnailFailureRecord> visibleFailureRecords = [];

            foreach (int tabIndex in ThumbnailErrorTargetTabIndices)
            {
                if (!TryGetExistingThumbnailErrorMarkerPath(movie, tabIndex, out string markerPath))
                {
                    markerPath = "";
                }
                else
                {
                    if (!failedTabs.Contains(tabIndex))
                    {
                        failedTabs.Add(tabIndex);
                    }

                    DateTime markerWriteTime = File.GetLastWriteTime(markerPath);
                    if (!lastWriteTime.HasValue || markerWriteTime > lastWriteTime.Value)
                    {
                        lastWriteTime = markerWriteTime;
                    }
                }

                if (
                    latestFailureRecordsByKey != null
                    && latestFailureRecordsByKey.TryGetValue(
                        BuildThumbnailFailureRecordKey(moviePathKey, tabIndex),
                        out ThumbnailFailureRecord latestFailureRecord
                    )
                    && ShouldDisplayThumbnailErrorFailureRecord(latestFailureRecord)
                )
                {
                    if (!failedTabs.Contains(tabIndex))
                    {
                        failedTabs.Add(tabIndex);
                    }

                    visibleFailureRecords.Add(latestFailureRecord);
                }
            }

            if (failedTabs.Count == 0)
            {
                return null;
            }

            ThumbnailFailureRecord primaryFailureRecord =
                ResolvePrimaryThumbnailErrorFailureRecord(visibleFailureRecords);
            ThumbnailErrorProgressSnapshot progressSnapshot =
                ParseThumbnailErrorProgressSnapshot(primaryFailureRecord?.ExtraJson ?? "");
            DateTime? progressUpdatedAt = ResolveThumbnailErrorProgressUpdatedAt(
                primaryFailureRecord,
                lastWriteTime
            );
            bool hasMixedStatuses =
                visibleFailureRecords
                    .Select(x => x.Status ?? "")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() > 1;
            string progressStatusText = ResolveThumbnailErrorStatusText(
                primaryFailureRecord,
                progressRecordExists: visibleFailureRecords.Count > 0
            );
            if (hasMixedStatuses && !string.IsNullOrWhiteSpace(progressStatusText))
            {
                progressStatusText += "(混在)";
            }

            return new ThumbnailErrorRecordViewModel
            {
                MovieRecord = movie,
                MovieId = movie.Movie_Id,
                MovieName = movie.Movie_Name ?? "",
                MoviePath = movie.Movie_Path ?? "",
                FailedTabsText = string.Join(
                    ", ",
                    failedTabs.Select(GetThumbnailTabDisplayName)
                ),
                MarkerCount = failedTabs.Count,
                LastMarkerWriteTime = lastWriteTime,
                LastMarkerWriteTimeText = lastWriteTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                ProgressStatusText = progressStatusText,
                ProgressPhaseText = ResolveThumbnailErrorPhaseText(
                    primaryFailureRecord,
                    progressSnapshot
                ),
                ProgressEngineText = ResolveThumbnailErrorEngineText(
                    primaryFailureRecord,
                    progressSnapshot
                ),
                ProgressAttemptText = ResolveThumbnailErrorAttemptText(
                    primaryFailureRecord,
                    progressSnapshot
                ),
                ProgressDetailText = ResolveThumbnailErrorDetailText(
                    primaryFailureRecord,
                    progressSnapshot
                ),
                ProgressUpdatedAt = progressUpdatedAt,
                ProgressUpdatedAtText = progressUpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                ProgressSummaryKey = ResolveThumbnailErrorSummaryKey(
                    primaryFailureRecord,
                    progressRecordExists: visibleFailureRecords.Count > 0
                ),
                FailedTabIndices = failedTabs.ToArray(),
            };
        }

        private static string BuildThumbnailFailureRecordKey(string moviePathKey, int tabIndex)
        {
            return $"{moviePathKey ?? ""}|{tabIndex}";
        }

        // 進行中として見せたい状態だけを残し、reflected 済みは一覧から外す。
        private static bool ShouldDisplayThumbnailErrorFailureRecord(ThumbnailFailureRecord record)
        {
            return (record?.Status ?? "") switch
            {
                "pending_rescue" => true,
                "processing_rescue" => true,
                "rescued" => true,
                "gave_up" => true,
                "skipped" => true,
                _ => false,
            };
        }

        // 複数タブが混在していても、いま一番見せるべき状態を 1 つ選ぶ。
        private static ThumbnailFailureRecord ResolvePrimaryThumbnailErrorFailureRecord(
            IEnumerable<ThumbnailFailureRecord> records
        )
        {
            return records?
                .OrderByDescending(record => ResolveThumbnailErrorStatusPriority(record?.Status ?? ""))
                .ThenByDescending(record => record?.UpdatedAtUtc ?? DateTime.MinValue)
                .ThenByDescending(record => record?.FailureId ?? 0)
                .FirstOrDefault();
        }

        private static int ResolveThumbnailErrorStatusPriority(string status)
        {
            return status switch
            {
                "processing_rescue" => 50,
                "pending_rescue" => 40,
                "rescued" => 30,
                "gave_up" => 20,
                "skipped" => 10,
                _ => 0,
            };
        }

        private static string ResolveThumbnailErrorStatusText(
            ThumbnailFailureRecord primaryFailureRecord,
            bool progressRecordExists
        )
        {
            if (!progressRecordExists || primaryFailureRecord == null)
            {
                return "未救済";
            }

            return (primaryFailureRecord.Status ?? "") switch
            {
                "pending_rescue" => "待機中",
                "processing_rescue" => "救済中",
                "rescued" => "反映待ち",
                "gave_up" => "要確認",
                "skipped" => "対象外",
                "reflected" => "完了",
                _ => "未救済",
            };
        }

        private static string ResolveThumbnailErrorSummaryKey(
            ThumbnailFailureRecord primaryFailureRecord,
            bool progressRecordExists
        )
        {
            if (!progressRecordExists || primaryFailureRecord == null)
            {
                return "unqueued";
            }

            return (primaryFailureRecord.Status ?? "") switch
            {
                "pending_rescue" => "pending",
                "processing_rescue" => "processing",
                "rescued" => "rescued",
                "gave_up" => "attention",
                "skipped" => "attention",
                _ => "unqueued",
            };
        }

        private static string ResolveThumbnailErrorPhaseText(
            ThumbnailFailureRecord primaryFailureRecord,
            ThumbnailErrorProgressSnapshot progressSnapshot
        )
        {
            if (primaryFailureRecord == null)
            {
                return "未投入";
            }

            string phase = progressSnapshot.Phase;
            if (string.IsNullOrWhiteSpace(phase))
            {
                return (primaryFailureRecord.Status ?? "") switch
                {
                    "pending_rescue" => "worker起動待ち",
                    "processing_rescue" => "救済中",
                    "rescued" => "UI反映待ち",
                    "gave_up" => "要確認",
                    "skipped" => "対象外",
                    _ => "",
                };
            }

            return phase switch
            {
                "direct_start" => "直接救済開始",
                "direct_engine_attempt" => "直接再試行",
                "direct_engine_failed" => "直接失敗",
                "direct_engine_exception" => "直接例外",
                "direct_exhausted" => "直接失敗",
                "route_exhausted" => "経路終了",
                "repair_probe" => "修復判定",
                "repair_probe_negative" => "修復不要",
                "repair_execute" => "修復中",
                "repair_failed" => "修復失敗",
                "repair_engine_attempt" => "修復後再試行",
                "repair_engine_failed" => "修復後失敗",
                "repair_engine_exception" => "修復後例外",
                "repair_exhausted" => "修復後失敗",
                "repair_rescue" => "修復後成功",
                "direct" => "直接成功",
                "reflected" => "反映済み",
                "reflected_no_ui_match" => "反映済み",
                "requeue_output_missing" => "再救済待ち",
                _ => phase,
            };
        }

        private static string ResolveThumbnailErrorEngineText(
            ThumbnailFailureRecord primaryFailureRecord,
            ThumbnailErrorProgressSnapshot progressSnapshot
        )
        {
            string engine = progressSnapshot.Engine;
            if (string.IsNullOrWhiteSpace(engine))
            {
                engine = primaryFailureRecord?.Engine ?? "";
            }

            return string.IsNullOrWhiteSpace(engine) ? "-" : engine;
        }

        private static string ResolveThumbnailErrorAttemptText(
            ThumbnailFailureRecord primaryFailureRecord,
            ThumbnailErrorProgressSnapshot progressSnapshot
        )
        {
            int attemptNo = progressSnapshot.AttemptNo;
            if (attemptNo < 1 && !string.Equals(primaryFailureRecord?.Status, "pending_rescue"))
            {
                attemptNo = primaryFailureRecord?.AttemptNo ?? 0;
            }

            return attemptNo > 0 ? attemptNo.ToString() : "";
        }

        private static string ResolveThumbnailErrorDetailText(
            ThumbnailFailureRecord primaryFailureRecord,
            ThumbnailErrorProgressSnapshot progressSnapshot
        )
        {
            string reason = progressSnapshot.CurrentFailureReason;
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = primaryFailureRecord?.FailureReason ?? "";
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                return NormalizeThumbnailErrorDetailText(reason);
            }

            string failureKindText = ResolveThumbnailErrorFailureKindText(
                progressSnapshot.CurrentFailureKind,
                primaryFailureRecord?.FailureKind ?? ThumbnailFailureKind.Unknown
            );
            if (!string.IsNullOrWhiteSpace(failureKindText))
            {
                return failureKindText;
            }

            return "";
        }

        // 理由列は読みやすさ優先で、ノーマルレーンタイムアウト時の動画パスだけ省く。
        private static string NormalizeThumbnailErrorDetailText(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "";
            }

            const string normalLaneTimeoutPrefix = "thumbnail normal lane timeout:";
            if (!reason.StartsWith(normalLaneTimeoutPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return reason;
            }

            return Regex.Replace(reason, "movie='[^']*',\\s*", "", RegexOptions.IgnoreCase);
        }

        private static string ResolveThumbnailErrorFailureKindText(
            string currentFailureKind,
            ThumbnailFailureKind failureKind
        )
        {
            string kind = string.IsNullOrWhiteSpace(currentFailureKind)
                ? failureKind.ToString()
                : currentFailureKind;
            return kind switch
            {
                "DrmProtected" => "DRM保護",
                "UnsupportedCodec" => "未対応コーデック",
                "IndexCorruption" => "インデックス破損",
                "TransientDecodeFailure" => "一時デコード失敗",
                "NoVideoStream" => "映像ストリームなし",
                "FileLocked" => "ファイル使用中",
                "FileMissing" => "ファイル不在",
                "ZeroByteFile" => "0byteファイル",
                "HangSuspected" => "停止疑い",
                _ => "",
            };
        }

        private static DateTime? ResolveThumbnailErrorProgressUpdatedAt(
            ThumbnailFailureRecord primaryFailureRecord,
            DateTime? lastWriteTime
        )
        {
            if (
                primaryFailureRecord != null
                && primaryFailureRecord.UpdatedAtUtc > DateTime.MinValue
            )
            {
                return primaryFailureRecord.UpdatedAtUtc.ToLocalTime();
            }

            return lastWriteTime;
        }

        private static ThumbnailErrorProgressSnapshot ParseThumbnailErrorProgressSnapshot(
            string extraJson
        )
        {
            ThumbnailErrorProgressSnapshot snapshot = new();
            if (string.IsNullOrWhiteSpace(extraJson))
            {
                return snapshot;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(extraJson);
                JsonElement root = document.RootElement;
                snapshot.Phase = ReadJsonString(root, "CurrentPhase");
                if (string.IsNullOrWhiteSpace(snapshot.Phase))
                {
                    snapshot.Phase = ReadJsonString(root, "Phase");
                }

                snapshot.Engine = ReadJsonString(root, "CurrentEngine");
                if (string.IsNullOrWhiteSpace(snapshot.Engine))
                {
                    snapshot.Engine = ReadJsonString(root, "EngineForced");
                }

                snapshot.Detail = ReadJsonString(root, "Detail");
                snapshot.CurrentFailureKind = ReadJsonString(root, "CurrentFailureKind");
                snapshot.CurrentFailureReason = ReadJsonString(root, "CurrentFailureReason");
                snapshot.RepairApplied = ReadJsonBoolean(root, "RepairApplied");
                snapshot.AttemptNo = ReadJsonInt(root, "AttemptNo");
            }
            catch
            {
                // 進捗JSONの読取失敗は一覧表示を止めず、空扱いで続行する。
            }

            return snapshot;
        }

        private static string ReadJsonString(JsonElement root, string propertyName)
        {
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(propertyName, out JsonElement value)
            )
            {
                return "";
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        }

        private static bool ReadJsonBoolean(JsonElement root, string propertyName)
        {
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(propertyName, out JsonElement value)
            )
            {
                return false;
            }

            return value.ValueKind == JsonValueKind.True;
        }

        private static int ReadJsonInt(JsonElement root, string propertyName)
        {
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(propertyName, out JsonElement value)
            )
            {
                return 0;
            }

            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed)
                ? parsed
                : 0;
        }

        private sealed class ThumbnailErrorProgressSnapshot
        {
            public string Phase { get; set; } = "";
            public string Engine { get; set; } = "";
            public bool RepairApplied { get; set; }
            public int AttemptNo { get; set; }
            public string Detail { get; set; } = "";
            public string CurrentFailureKind { get; set; } = "";
            public string CurrentFailureReason { get; set; } = "";
        }

        // 旧命名が残る環境も考慮して path 名と movie 名の両方で marker を探す。
        private bool TryGetExistingThumbnailErrorMarkerPath(
            MovieRecords movie,
            int tabIndex,
            out string markerPath
        )
        {
            markerPath = null;

            if (movie == null)
            {
                return false;
            }

            TabInfo tabInfo = new(tabIndex, MainVM?.DbInfo?.DBName ?? "", MainVM?.DbInfo?.ThumbFolder ?? "");

            string primaryMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                tabInfo.OutPath,
                movie.Movie_Path
            );
            if (Path.Exists(primaryMarkerPath))
            {
                markerPath = primaryMarkerPath;
                return true;
            }

            string fallbackName = movie.Movie_Name ?? movie.Movie_Body ?? "";
            string fallbackMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                tabInfo.OutPath,
                fallbackName
            );
            if (Path.Exists(fallbackMarkerPath))
            {
                markerPath = fallbackMarkerPath;
                return true;
            }

            return false;
        }

        // UI 表示名は今のタブ表記に揃える。
        private static string GetThumbnailTabDisplayName(int tabIndex)
        {
            return tabIndex switch
            {
                0 => "Small",
                1 => "Big",
                2 => "Grid",
                3 => "List",
                4 => "5x2",
                99 => "詳細",
                _ => $"Tab{tabIndex}",
            };
        }

        // ERROR タブで選択中の行を元動画へ戻して扱う。
        private List<ThumbnailErrorRecordViewModel> GetSelectedThumbnailErrorRecords()
        {
            List<ThumbnailErrorRecordViewModel> items = [];
            DataGrid errorListDataGrid = GetThumbnailErrorDataGrid();
            if (errorListDataGrid == null)
            {
                return items;
            }

            foreach (var selectedItem in errorListDataGrid.SelectedItems)
            {
                if (selectedItem is ThumbnailErrorRecordViewModel record)
                {
                    items.Add(record);
                }
            }

            // 一覧更新直後は SelectedItems が空でも、カレント行だけ残ることがある。
            if (items.Count == 0 && errorListDataGrid.CurrentItem is ThumbnailErrorRecordViewModel current)
            {
                items.Add(current);
            }

            if (
                items.Count == 0
                && errorListDataGrid.CurrentCell.Item is ThumbnailErrorRecordViewModel currentCellItem
            )
            {
                items.Add(currentCellItem);
            }

            if (
                items.Count == 0
                && errorListDataGrid.SelectedItem is ThumbnailErrorRecordViewModel selectedRecord
            )
            {
                items.Add(selectedRecord);
            }

            return items;
        }

        // 呼び出し元ごとに、救済要求を即時実行か待機付きかで切り替える。
        private int EnqueueThumbnailErrorRecordsToRescue(
            IEnumerable<ThumbnailErrorRecordViewModel> records,
            string reason,
            bool requiresIdle = true
        )
        {
            if (records == null)
            {
                return 0;
            }

            int movieCount = 0;
            int queuedCount = 0;

            foreach (var record in records.Where(x => x != null))
            {
                movieCount++;

                foreach (int tabIndex in record.FailedTabIndices ?? [])
                {
                    QueueObj queueObj = new()
                    {
                        MovieId = record.MovieId,
                        MovieFullPath = record.MoviePath,
                        Hash = record.MovieRecord?.Hash ?? "",
                        Tabindex = tabIndex,
                    };

                    if (
                        TryEnqueueThumbnailDisplayErrorRescueJob(
                            queueObj,
                            reason: $"{reason}:{GetThumbnailTabDisplayName(tabIndex)}",
                            requiresIdle: requiresIdle
                        )
                    )
                    {
                        queuedCount++;
                    }
                }
            }

            DebugRuntimeLog.Write(
                "thumbnail-error-tab",
                $"error tab rescue enqueue end: reason={reason} movie_count={movieCount} queued={queuedCount}"
            );

            RefreshThumbnailErrorRecords();
            return queuedCount;
        }

        // ERROR タブは重い選択復元をやめ、再読込後は必要時だけ先頭へ寄せる。
        private void ReloadThumbnailErrorListButton_Click(object sender, RoutedEventArgs e)
        {
            DebugRuntimeLog.Write("thumbnail-error-tab", "error tab reload clicked");
            RefreshThumbnailErrorRecords();
            SelectFirstItem();
            Refresh();
        }

        // 表示中の ERROR マーカーと FailureDb 行をまとめて消し、一覧を空にする。
        private void ClearThumbnailErrorListButton_Click(object sender, RoutedEventArgs e)
        {
            ThumbnailErrorRecordViewModel[] visibleRecords = MainVM.ThumbnailErrorRecs.ToArray();
            if (visibleRecords.Length < 1)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                this,
                $"現在表示中の {visibleRecords.Length} 件を一覧から消します。{Environment.NewLine}ERROR マーカーと FailureDb の対象行も削除します。よろしいですか？",
                "サムネ失敗一覧クリア",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning
            );
            if (result != MessageBoxResult.OK)
            {
                return;
            }

            int deletedMarkerCount = 0;
            List<(string MoviePathKey, int TabIndex)> targets = [];

            foreach (ThumbnailErrorRecordViewModel record in visibleRecords)
            {
                string moviePath = record.MoviePath ?? record.MovieRecord?.Movie_Path ?? "";
                string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(moviePath);

                foreach (int tabIndex in record.FailedTabIndices ?? [])
                {
                    TabInfo tabInfo = new(
                        tabIndex,
                        MainVM?.DbInfo?.DBName ?? "",
                        MainVM?.DbInfo?.ThumbFolder ?? ""
                    );
                    if (TryDeleteThumbnailErrorMarker(tabInfo.OutPath, moviePath))
                    {
                        deletedMarkerCount++;
                    }

                    targets.Add((moviePathKey, tabIndex));
                }
            }

            int deletedFailureCount = 0;
            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            if (failureDbService != null)
            {
                deletedFailureCount = failureDbService.DeleteMainFailureRecords(targets);
            }

            DebugRuntimeLog.Write(
                "thumbnail-error-tab",
                $"error tab clear clicked: visible={visibleRecords.Length} deleted_markers={deletedMarkerCount} deleted_failure_rows={deletedFailureCount}"
            );

            RefreshThumbnailErrorRecords();
            SelectFirstItem();
            Refresh();
        }

        private void RescueSelectedThumbnailErrorsButton_Click(object sender, RoutedEventArgs e)
        {
            int selectedCount = GetSelectedThumbnailErrorRecords().Count;
            DebugRuntimeLog.Write(
                "thumbnail-error-tab",
                $"error tab selected rescue clicked: selected={selectedCount}"
            );
            _ = EnqueueThumbnailErrorRecordsToRescue(
                GetSelectedThumbnailErrorRecords(),
                reason: "error-tab-selected",
                requiresIdle: false
            );
            Refresh();
        }

        private void RescueAllThumbnailErrorsButton_Click(object sender, RoutedEventArgs e)
        {
            DebugRuntimeLog.Write(
                "thumbnail-error-tab",
                $"error tab all rescue clicked: visible={MainVM.ThumbnailErrorRecs.Count}"
            );
            _ = EnqueueThumbnailErrorRecordsToRescue(
                MainVM.ThumbnailErrorRecs.ToArray(),
                reason: "error-tab-all",
                requiresIdle: false
            );
            Refresh();
        }
    }
}
