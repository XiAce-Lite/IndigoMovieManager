using System.IO;
using System.Text.Json;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int ThumbnailFailureSyncBatchSize = 16;
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

                ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
                if (failureDbService == null)
                {
                    return;
                }

                List<ThumbnailFailureRecord> rescuedRecords = failureDbService.GetRescuedRecordsForSync(
                    ThumbnailFailureSyncBatchSize
                );
                if (rescuedRecords.Count < 1)
                {
                    return;
                }

                int reflectedCount = 0;
                int requeuedCount = 0;

                foreach (ThumbnailFailureRecord rescuedRecord in rescuedRecords)
                {
                    cts.ThrowIfCancellationRequested();

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

                    bool appliedToUi = await ApplyRescuedThumbnailRecordToUiAsync(rescuedRecord)
                        .ConfigureAwait(false);
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

                if (reflectedCount > 0 || requeuedCount > 0)
                {
                    await Dispatcher
                        .InvokeAsync(() =>
                        {
                            RefreshThumbnailErrorRecords();
                            Refresh();
                            RequestThumbnailProgressSnapshotRefresh();
                        })
                        .Task.ConfigureAwait(false);
                    DebugRuntimeLog.Write(
                        "thumbnail-sync",
                        $"rescued sync completed: trigger={trigger} reflected={reflectedCount} requeued={requeuedCount}"
                    );
                }
            }
            finally
            {
                Interlocked.Exchange(ref thumbnailFailureSyncRunning, 0);
            }
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

        // UI側の成功反映を rescued 行にも使い回し、通常生成と同じ見え方へ揃える。
        private async Task<bool> ApplyRescuedThumbnailRecordToUiAsync(ThumbnailFailureRecord record)
        {
            if (record == null || !CanReflectRescuedThumbnailRecord(record))
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
                })
                .Task.ConfigureAwait(false);

            return appliedCount > 0;
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

            switch (tabIndex)
            {
                case 0:
                    item.ThumbPathSmall = saveThumbFileName;
                    return true;
                case 1:
                    item.ThumbPathBig = saveThumbFileName;
                    return true;
                case 2:
                    item.ThumbPathGrid = saveThumbFileName;
                    return true;
                case 3:
                    item.ThumbPathList = saveThumbFileName;
                    return true;
                case 4:
                    item.ThumbPathBig10 = saveThumbFileName;
                    return true;
                case 99:
                    item.ThumbDetail = saveThumbFileName;
                    return true;
                default:
                    return false;
            }
        }
    }
}
