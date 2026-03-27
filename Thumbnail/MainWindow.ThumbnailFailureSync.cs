using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using IndigoMovieManager.Converter;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager
{
    /// <summary>
    /// MainWindow の partial：RescueWorker の救済結果を UI へ同期する「橋渡し役」。
    ///
    /// 【全体の流れでの位置づけ】
    ///   RescueWorker（外部exe）が FailureDb の pending_rescue を処理
    ///     → 成功したら status を "rescued" に更新
    ///     → ★ここ★ TrySyncRescuedThumbnailRecordsAsync() が定期的にポーリング
    ///       → rescued 行の OutputThumbPath をUIスレッドで MovieRecs へ反映
    ///       → FailureDb の status を "reflected" に進める
    ///       → stale な ERROR マーカーや FailureRecord も同時に掃除
    ///
    /// キューが空いた時（OnThumbnailQueueDrainedAsync）と定期タイマーの2経路で動く。
    /// </summary>
    public partial class MainWindow
    {
        private const int ThumbnailFailureSyncBatchSize = 16;
        private static readonly int[] ThumbnailSyncCleanupTabIndexes = [0, 1, 2, 3, 4, 99];
        private static readonly TimeSpan ThumbnailFailureSyncPeriodicInterval = TimeSpan.FromSeconds(
            5
        );
        private readonly object thumbnailFailureDbServiceLock = new();
        private ThumbnailFailureDbService currentThumbnailFailureDbService;
        private string currentThumbnailFailureDbMainDbFullPath = "";
        private int thumbnailFailureSyncRunning;
        private int thumbnailFailureSyncPeriodicQueued;
        private long thumbnailFailureSyncLastScheduledUtcTicks;

        // キューが空いたら、先に rescued をUIへ戻し、その後で次の救済worker起動を判定する。
        private async Task OnThumbnailQueueDrainedAsync(CancellationToken cts)
        {
            cts.ThrowIfCancellationRequested();
            await TrySyncRescuedThumbnailRecordsAsync("queue-drained", cts).ConfigureAwait(false);
            await TryStartExternalThumbnailRescueWorkerAsync(cts).ConfigureAwait(false);
        }

        // 起動直後にも一度だけ再読込し、本exe停止中に成功した rescued を取り逃さない。
        private void TryStartInitialThumbnailFailureSync()
        {
            // 起動直後の再同期を「直近実行」とみなし、直後の periodic 二重実行を避ける。
            Interlocked.Exchange(
                ref thumbnailFailureSyncLastScheduledUtcTicks,
                DateTime.UtcNow.Ticks
            );
            CancellationToken token = _thumbCheckCts?.Token ?? CancellationToken.None;
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await TrySyncRescuedThumbnailRecordsAsync("startup", token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // 終了時キャンセルは正常系として握る。
                    }
                    catch (Exception ex)
                    {
                        DebugRuntimeLog.Write(
                            "thumbnail-sync",
                            $"startup sync failed: {ex.Message}"
                        );
                    }
                },
                token
            );
        }

        // 通常キューが流れ続けても rescued を反映できるよう、低頻度で同期だけ起こす。
        private void TryQueuePeriodicThumbnailFailureSync()
        {
            DateTime nowUtc = DateTime.UtcNow;
            DateTime lastScheduledUtc = ReadThumbnailFailureSyncLastScheduledUtc();
            if (
                !ShouldRunPeriodicThumbnailFailureSync(
                    nowUtc,
                    lastScheduledUtc,
                    ThumbnailFailureSyncPeriodicInterval,
                    isThumbnailQueueInputEnabled
                )
            )
            {
                return;
            }

            if (Interlocked.CompareExchange(ref thumbnailFailureSyncPeriodicQueued, 1, 0) == 1)
            {
                return;
            }

            nowUtc = DateTime.UtcNow;
            lastScheduledUtc = ReadThumbnailFailureSyncLastScheduledUtc();
            if (
                !ShouldRunPeriodicThumbnailFailureSync(
                    nowUtc,
                    lastScheduledUtc,
                    ThumbnailFailureSyncPeriodicInterval,
                    isThumbnailQueueInputEnabled
                )
            )
            {
                Interlocked.Exchange(ref thumbnailFailureSyncPeriodicQueued, 0);
                return;
            }

            Interlocked.Exchange(ref thumbnailFailureSyncLastScheduledUtcTicks, nowUtc.Ticks);
            CancellationToken token = _thumbCheckCts?.Token ?? CancellationToken.None;
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await TrySyncRescuedThumbnailRecordsAsync("periodic-ui-tick", token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // 終了時キャンセルは正常系として握る。
                    }
                    catch (Exception ex)
                    {
                        DebugRuntimeLog.Write(
                            "thumbnail-sync",
                            $"periodic sync failed: {ex.Message}"
                        );
                    }
                    finally
                    {
                        Interlocked.Exchange(ref thumbnailFailureSyncPeriodicQueued, 0);
                    }
                }
            );
        }

        internal static bool ShouldRunPeriodicThumbnailFailureSync(
            DateTime nowUtc,
            DateTime lastScheduledUtc,
            TimeSpan minInterval,
            bool isInputEnabled
        )
        {
            if (!isInputEnabled)
            {
                return false;
            }

            if (minInterval < TimeSpan.Zero)
            {
                minInterval = TimeSpan.Zero;
            }

            if (lastScheduledUtc == DateTime.MinValue)
            {
                return true;
            }

            return nowUtc - lastScheduledUtc >= minInterval;
        }

        private DateTime ReadThumbnailFailureSyncLastScheduledUtc()
        {
            long ticks = Interlocked.Read(ref thumbnailFailureSyncLastScheduledUtcTicks);
            if (ticks <= 0)
            {
                return DateTime.MinValue;
            }

            return new DateTime(ticks, DateTimeKind.Utc);
        }

        // rescued を一度だけ反映し、反映済みなら reflected へ進める。
        private async Task TrySyncRescuedThumbnailRecordsAsync(
            string trigger,
            CancellationToken cts
        )
        {
            if (Interlocked.CompareExchange(ref thumbnailFailureSyncRunning, 1, 0) == 1)
            {
                return;
            }

            try
            {
                if (!isThumbnailQueueInputEnabled)
                {
                    return;
                }

                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
                if (failureDbService == null)
                {
                    return;
                }

                int cleanedErrorMarkerCount = CleanupStaleErrorMarkersForCurrentDb();
                int cleanedFailureRecordCount = CleanupStaleMainFailureRecordsForCurrentDb(
                    failureDbService
                );
                int recoveredStaleCount = failureDbService.RecoverExpiredProcessingToPendingRescue(
                    DateTime.UtcNow
                );

                List<ThumbnailFailureRecord> rescuedRecords = failureDbService.GetRescuedRecordsForSync(
                    ThumbnailFailureSyncBatchSize
                );
                if (
                    rescuedRecords.Count < 1
                    && recoveredStaleCount < 1
                    && cleanedErrorMarkerCount < 1
                    && cleanedFailureRecordCount < 1
                )
                {
                    return;
                }

                int reflectedCount = 0;
                int requeuedCount = 0;

                foreach (ThumbnailFailureRecord rescuedRecord in rescuedRecords)
                {
                    cts.ThrowIfCancellationRequested();
                    if (
                        !isThumbnailQueueInputEnabled
                        || Dispatcher.HasShutdownStarted
                        || Dispatcher.HasShutdownFinished
                    )
                    {
                        return;
                    }

                    if (!CanReflectRescuedThumbnailRecord(rescuedRecord))
                    {
                        int requeued = failureDbService.ResetRescuedToPendingRescue(
                            rescuedRecord.FailureId,
                            DateTime.UtcNow,
                            failureReason: "rescued output missing during sync",
                            extraJson: BuildRescuedSyncExtraJson(
                                phase: "requeue_output_missing",
                                rescuedRecord.OutputThumbPath,
                                appliedToUi: false
                            )
                        );
                        requeuedCount += requeued;
                        continue;
                    }

                    bool appliedToUi = await ApplyRescuedThumbnailRecordToUiAsync(
                            rescuedRecord,
                            cts
                        )
                        .ConfigureAwait(false);
                    if (
                        !isThumbnailQueueInputEnabled
                        || Dispatcher.HasShutdownStarted
                        || Dispatcher.HasShutdownFinished
                    )
                    {
                        return;
                    }
                    if (
                        ShouldCountRescuedThumbnailForSession(
                            rescuedRecord,
                            _thumbnailProgressSessionStartedUtc
                        )
                    )
                    {
                        // 外部救済worker成功分も、この起動以降に完了したものだけ総作成枚数へ積む。
                        _thumbnailProgressRuntime.RecordThumbnailCreated();
                    }
                    int reflected = failureDbService.MarkRescuedAsReflected(
                        rescuedRecord.FailureId,
                        DateTime.UtcNow,
                        extraJson: BuildRescuedSyncExtraJson(
                            appliedToUi ? "reflected" : "reflected_no_ui_match",
                            rescuedRecord.OutputThumbPath,
                            appliedToUi
                        )
                    );
                    reflectedCount += reflected;
                }

                if (
                    reflectedCount > 0
                    || requeuedCount > 0
                    || recoveredStaleCount > 0
                    || cleanedErrorMarkerCount > 0
                )
                {
                    if (
                        !isThumbnailQueueInputEnabled
                        || Dispatcher.HasShutdownStarted
                        || Dispatcher.HasShutdownFinished
                    )
                    {
                        return;
                    }

                    await Dispatcher
                        .InvokeAsync(() =>
                        {
                            InvalidateThumbnailErrorRecords(refreshIfVisible: true);
                            Refresh();
                            RequestThumbnailProgressSnapshotRefresh();
                        }, DispatcherPriority.Normal, cts)
                        .Task.ConfigureAwait(false);
                    DebugRuntimeLog.Write(
                        "thumbnail-sync",
                        $"rescued sync completed: trigger={trigger} reflected={reflectedCount} requeued={requeuedCount} recovered_stale={recoveredStaleCount} cleaned_error_markers={cleanedErrorMarkerCount} cleaned_failure_records={cleanedFailureRecordCount}"
                    );
                }
            }
            finally
            {
                Interlocked.Exchange(ref thumbnailFailureSyncRunning, 0);
            }
        }

        // 既に成功jpgがあるのに残った #ERROR は、Grid が古い失敗画像を拾うため同期入口で掃除する。
        private int CleanupStaleErrorMarkersForCurrentDb()
        {
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            string thumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            if (string.IsNullOrWhiteSpace(thumbFolder))
            {
                return 0;
            }

            return CleanupStaleErrorMarkersForDb(dbName, thumbFolder);
        }

        // 正常jpgがある個体に残った stale main 行は、救済Workerカードや Error 一覧へ残像を出すので同期入口で掃除する。
        private int CleanupStaleMainFailureRecordsForCurrentDb(
            ThumbnailFailureDbService failureDbService
        )
        {
            return CleanupStaleMainFailureRecordsForDb(
                failureDbService,
                MainVM?.DbInfo?.DBName ?? "",
                MainVM?.DbInfo?.ThumbFolder ?? ""
            );
        }

        // 現在DBで使う代表タブだけを見て、成功jpgと同居する #ERROR を軽く掃除する。
        internal static int CleanupStaleErrorMarkersForDb(string dbName, string thumbFolder)
        {
            int deletedCount = 0;
            for (int i = 0; i < ThumbnailSyncCleanupTabIndexes.Length; i++)
            {
                deletedCount += CleanupStaleErrorMarkersInDirectory(
                    ResolveThumbnailOutPath(ThumbnailSyncCleanupTabIndexes[i], dbName, thumbFolder)
                );
            }

            return deletedCount;
        }

        // 成功jpgが既にある main 行だけを落とし、起動直後の stale pending_rescue 表示を残さない。
        internal static int CleanupStaleMainFailureRecordsForDb(
            ThumbnailFailureDbService failureDbService,
            string dbName,
            string thumbFolder
        )
        {
            if (failureDbService == null)
            {
                return 0;
            }

            List<(string MoviePathKey, int TabIndex)> targets = [];
            foreach (ThumbnailFailureRecord record in failureDbService.GetLatestMainFailureRecords())
            {
                if (!ShouldDeleteStaleMainFailureRecord(record, dbName, thumbFolder))
                {
                    continue;
                }

                targets.Add((record.MoviePathKey ?? "", record.TabIndex));
            }

            return failureDbService.DeleteMainFailureRecords(targets);
        }

        // 成功jpgがあるのに main 行だけ残っている個体は、再処理も進捗表示も不要なので stale 扱いにする。
        internal static bool ShouldDeleteStaleMainFailureRecord(
            ThumbnailFailureRecord record,
            string dbName,
            string thumbFolder
        )
        {
            if (
                record == null
                || string.IsNullOrWhiteSpace(record.MoviePath)
                || string.IsNullOrWhiteSpace(record.MoviePathKey)
            )
            {
                return false;
            }

            string outPath = ResolveThumbnailOutPath(record.TabIndex, dbName, thumbFolder);
            return !ShouldCreateErrorMarkerForSkippedMovie(
                outPath,
                record.MoviePath,
                out _
            );
        }

        // 同一動画の正常jpgが既にある marker だけを消し、未解決個体の ERROR は残す。
        internal static int CleanupStaleErrorMarkersInDirectory(string outPath)
        {
            if (string.IsNullOrWhiteSpace(outPath) || !Directory.Exists(outPath))
            {
                return 0;
            }

            int deletedCount = 0;
            const string errorSuffix = ".#ERROR.jpg";
            try
            {
                foreach (
                    string errorMarkerPath in Directory.EnumerateFiles(
                        outPath,
                        "*.#ERROR.jpg",
                        SearchOption.TopDirectoryOnly
                    )
                )
                {
                    string fileName = Path.GetFileName(errorMarkerPath) ?? "";
                    if (!fileName.EndsWith(errorSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string movieBody = fileName[..^errorSuffix.Length];
                    if (
                        string.IsNullOrWhiteSpace(movieBody)
                        || !ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                            outPath,
                            movieBody,
                            out _
                        )
                    )
                    {
                        continue;
                    }

                    File.Delete(errorMarkerPath);
                    deletedCount++;
                }
            }
            catch
            {
                // marker 掃除に失敗しても、本体同期は続行する。
            }

            return deletedCount;
        }

        // 反映に必要な最低条件だけを軽く見る。欠けていれば worker へ戻す。
        internal static bool CanReflectRescuedThumbnailRecord(ThumbnailFailureRecord record)
        {
            if (record == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(record.OutputThumbPath))
            {
                return false;
            }

            if (!File.Exists(record.OutputThumbPath))
            {
                return false;
            }

            return record.TabIndex is 0 or 1 or 2 or 3 or 4 or 99;
        }

        // この起動より後に rescue 成功した行だけを、総作成枚数へ1回だけ加算対象にする。
        internal static bool ShouldCountRescuedThumbnailForSession(
            ThumbnailFailureRecord record,
            DateTime sessionStartedUtc
        )
        {
            if (record == null)
            {
                return false;
            }

            if (!string.Equals(record.Status, "rescued", StringComparison.Ordinal))
            {
                return false;
            }

            if (record.UpdatedAtUtc == DateTime.MinValue)
            {
                return false;
            }

            return record.UpdatedAtUtc >= sessionStartedUtc.ToUniversalTime();
        }

        // UI側の成功反映を rescued 行にも使い回し、通常生成と同じ見え方へ揃える。
        private async Task<bool> ApplyRescuedThumbnailRecordToUiAsync(
            ThumbnailFailureRecord record,
            CancellationToken cts
        )
        {
            if (record == null || !CanReflectRescuedThumbnailRecord(record))
            {
                return false;
            }

            if (
                !isThumbnailQueueInputEnabled
                || Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished
            )
            {
                return false;
            }

            long resolvedMovieId = 0;
            _ = TryResolveMovieIdentityFromDb(record.MoviePath, out resolvedMovieId, out _);

            int appliedCount = 0;
            await Dispatcher
                .InvokeAsync(() =>
                {
                    IEnumerable<MovieRecords> targets = MainVM?.MovieRecs?.Where(x =>
                            IsSameMovieForFailureRecord(x, record, resolvedMovieId)
                        )
                        ?? Enumerable.Empty<MovieRecords>();
                    foreach (MovieRecords item in targets.ToArray())
                    {
                        if (TryApplyThumbnailPathToMovieRecord(item, record.TabIndex, record.OutputThumbPath))
                        {
                            appliedCount++;
                        }
                    }
                }, DispatcherPriority.Normal, cts)
                .Task.ConfigureAwait(false);

            return appliedCount > 0;
        }

        // 手動救済成功時だけは periodic sync を待たず、対象行へ直接サムネ反映を試す。
        private async Task<bool> TryReflectRescuedThumbnailRecordImmediatelyAsync(
            long failureId,
            string outputThumbPath,
            CancellationToken cts
        )
        {
            if (failureId < 1 || string.IsNullOrWhiteSpace(outputThumbPath))
            {
                return false;
            }

            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            if (failureDbService == null)
            {
                return false;
            }

            string normalizedOutputThumbPath = outputThumbPath.Trim();
            const int maxAttempts = 6;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                cts.ThrowIfCancellationRequested();

                ThumbnailFailureRecord record = failureDbService.GetFailureRecordById(failureId);
                if (record != null)
                {
                    record.OutputThumbPath = normalizedOutputThumbPath;
                    bool appliedToUi = await ApplyRescuedThumbnailRecordToUiAsync(record, cts)
                        .ConfigureAwait(false);
                    if (appliedToUi)
                    {
                        await Dispatcher
                            .InvokeAsync(
                                () =>
                                {
                                    TryReflectRescuedThumbnailIntoUpperTabRescueItems(
                                        record.MoviePath,
                                        record.TabIndex,
                                        normalizedOutputThumbPath
                                    );
                                    RequestThumbnailErrorSnapshotRefresh();
                                    RequestThumbnailProgressSnapshotRefresh();
                                },
                                DispatcherPriority.Normal,
                                cts
                            )
                            .Task.ConfigureAwait(false);

                        DebugRuntimeLog.Write(
                            "thumbnail-sync",
                            $"manual rescue immediate reflect: failure_id={failureId} tab={record.TabIndex} output='{normalizedOutputThumbPath}'"
                        );
                        return true;
                    }
                }

                if (attempt + 1 < maxAttempts)
                {
                    await Task.Delay(120, cts).ConfigureAwait(false);
                }
            }

            DebugRuntimeLog.Write(
                "thumbnail-sync",
                $"manual rescue immediate reflect skipped: failure_id={failureId} output='{normalizedOutputThumbPath}'"
            );
            return false;
        }

        // MainDB切替時だけ FailureDb service を差し替え、通常は同一インスタンスを使い回す。
        private ThumbnailFailureDbService ResolveCurrentThumbnailFailureDbService()
        {
            string mainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(mainDbFullPath))
            {
                return null;
            }

            lock (thumbnailFailureDbServiceLock)
            {
                if (
                    currentThumbnailFailureDbService != null
                    && string.Equals(
                        currentThumbnailFailureDbMainDbFullPath,
                        mainDbFullPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return currentThumbnailFailureDbService;
                }

                currentThumbnailFailureDbService = new ThumbnailFailureDbService(mainDbFullPath);
                currentThumbnailFailureDbMainDbFullPath = mainDbFullPath;
                return currentThumbnailFailureDbService;
            }
        }

        // rescued 行でも、通常生成と同じ MovieId優先 + MoviePath フォールバックで対象を決める。
        private static bool IsSameMovieForFailureRecord(
            MovieRecords item,
            ThumbnailFailureRecord record,
            long resolvedMovieId
        )
        {
            if (item == null || record == null)
            {
                return false;
            }

            if (resolvedMovieId > 0 && item.Movie_Id == resolvedMovieId)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(record.MoviePath))
            {
                return false;
            }

            return string.Equals(
                item.Movie_Path,
                record.MoviePath,
                StringComparison.OrdinalIgnoreCase
            );
        }

        // reflected 時の観測だけを軽く残し、後から「反映済みか」を追えるようにする。
        private static string BuildRescuedSyncExtraJson(
            string phase,
            string outputThumbPath,
            bool appliedToUi
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    WorkerRole = "main",
                    Phase = phase ?? "",
                    OutputThumbPath = outputThumbPath ?? "",
                    AppliedToUi = appliedToUi,
                }
            );
        }

        // サムネイルpath反映の分岐は通常生成と rescued sync で共通化する。
        internal static bool TryApplyThumbnailPathToMovieRecord(
            MovieRecords item,
            int tabIndex,
            string saveThumbFileName
        )
        {
            if (item == null || string.IsNullOrWhiteSpace(saveThumbFileName))
            {
                return false;
            }

            string normalizedSaveThumbFileName = saveThumbFileName.Trim();

            // 直前まで「画像なし」や古い内容を掴んでいた時でも、新規生成直後の再評価を確実に通す。
            NoLockImageConverter.InvalidateFilePath(normalizedSaveThumbFileName);

            switch (tabIndex)
            {
                case 0:
                    ApplyThumbnailPathWithForcedRebind(
                        item.ThumbPathSmall,
                        normalizedSaveThumbFileName,
                        value => item.ThumbPathSmall = value
                    );
                    return true;
                case 1:
                    ApplyThumbnailPathWithForcedRebind(
                        item.ThumbPathBig,
                        normalizedSaveThumbFileName,
                        value => item.ThumbPathBig = value
                    );
                    return true;
                case 2:
                    ApplyThumbnailPathWithForcedRebind(
                        item.ThumbPathGrid,
                        normalizedSaveThumbFileName,
                        value => item.ThumbPathGrid = value
                    );
                    return true;
                case 3:
                    ApplyThumbnailPathWithForcedRebind(
                        item.ThumbPathList,
                        normalizedSaveThumbFileName,
                        value => item.ThumbPathList = value
                    );
                    return true;
                case 4:
                    ApplyThumbnailPathWithForcedRebind(
                        item.ThumbPathBig10,
                        normalizedSaveThumbFileName,
                        value => item.ThumbPathBig10 = value
                    );
                    return true;
                case 99:
                    ApplyThumbnailPathWithForcedRebind(
                        item.ThumbDetail,
                        normalizedSaveThumbFileName,
                        value => item.ThumbDetail = value
                    );
                    return true;
                default:
                    return false;
            }
        }

        // 同じjpgパスを上書きした救済成功時でも、いったん空へ振って再バインドを強制する。
        internal static void ApplyThumbnailPathWithForcedRebind(
            string currentThumbPath,
            string nextThumbPath,
            Action<string> applyPath
        )
        {
            if (applyPath == null || string.IsNullOrWhiteSpace(nextThumbPath))
            {
                return;
            }

            if (
                string.Equals(
                    currentThumbPath?.Trim(),
                    nextThumbPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                applyPath(string.Empty);
            }

            applyPath(nextThumbPath);
        }
    }
}
