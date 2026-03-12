using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private static readonly TimeSpan ThumbnailNormalLaneTimeout = TimeSpan.FromSeconds(10);

        // サムネイル監視タスクを再起動する。
        private void RestartThumbnailTask()
        {
            DebugRuntimeLog.TaskStart(nameof(RestartThumbnailTask));
            ClearThumbnailQueue();
            RestartThumbnailRescueTask();

            // 既存タスクのキャンセル
            _thumbCheckCts.Cancel();
            DebugRuntimeLog.Write("task", "thumbnail token canceled for restart.");

            // 新しいCancellationTokenSourceを生成
            _thumbCheckCts = new CancellationTokenSource();

            // 新しいトークンでタスクを再起動
            DebugRuntimeLog.TaskStart(nameof(CheckThumbAsync), "trigger=RestartThumbnailTask");
            _thumbCheckTask = CheckThumbAsync(_thumbCheckCts.Token);
            DebugRuntimeLog.TaskEnd(nameof(RestartThumbnailTask));
        }

        /// <summary>
        /// CheckThumbAsync サムネイル作成用に起動時にぶん投げるタスク。常時起動。終了条件はねぇ。
        /// </summary>
        private async Task CheckThumbAsync(CancellationToken cts = default)
        {
            string endStatus = "completed";
            DebugRuntimeLog.TaskStart(
                nameof(CheckThumbAsync),
                $"parallel={GetThumbnailQueueMaxParallelism()} poll_ms={ThumbnailQueuePollIntervalMs}"
            );
            try
            {
                while (true)
                {
                    cts.ThrowIfCancellationRequested();
                    try
                    {
                        await _thumbnailQueueProcessor
                            .RunAsync(
                                ResolveCurrentQueueDbService,
                                thumbnailQueueOwnerInstanceId,
                                (queueObj, token) => CreateThumbAsync(queueObj, false, token),
                                maxParallelism: GetThumbnailQueueMaxParallelism(),
                                maxParallelismResolver: GetThumbnailQueueMaxParallelism,
                                pollIntervalMs: ThumbnailQueuePollIntervalMs,
                                leaseMinutes: 5,
                                leaseBatchSize: 0,
                                preferredTabIndexResolver: ResolvePreferredThumbnailTabIndex,
                                log: message => DebugRuntimeLog.Write("queue-consumer", message),
                                progressSnapshot: (completed, total, currentParallel, configuredParallel) =>
                                {
                                    int configuredParallelForUi = GetThumbnailQueueMaxParallelism();
                                    _thumbnailProgressRuntime.UpdateSessionProgress(
                                        completed,
                                        total,
                                        currentParallel,
                                        configuredParallelForUi
                                    );
                                    RequestThumbnailProgressSnapshotRefresh();
                                },
                                onJobStarted: queueObj =>
                                {
                                    _thumbnailProgressRuntime.MarkJobStarted(queueObj);
                                    RequestThumbnailProgressSnapshotRefresh();
                                },
                                onJobCompleted: queueObj =>
                                {
                                    _thumbnailProgressRuntime.MarkJobCompleted(queueObj);
                                    RequestThumbnailProgressSnapshotRefresh();
                                },
                                progressPresenter: _thumbnailQueueProgressPresenter,
                                cts: cts
                            )
                            .ConfigureAwait(false);
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        DebugRuntimeLog.Write(
                            "queue-consumer",
                            $"consumer restart scheduled: {ex.Message}"
                        );
                        await Task.Delay(500, cts).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                endStatus = "canceled";
                throw;
            }
            catch (Exception ex)
            {
                endStatus = $"fault message='{ex.Message}'";
                throw;
            }
            finally
            {
                DebugRuntimeLog.TaskEnd(nameof(CheckThumbAsync), $"status={endStatus}");
            }
        }

        // ブックマーク用の単一フレームサムネイルを作成する。
        private async Task CreateBookmarkThumbAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos
        )
        {
            bool created = await _thumbnailCreationService.CreateBookmarkThumbAsync(
                movieFullPath,
                saveThumbPath,
                capturePos
            );
            if (!created)
            {
                return;
            }

            await Task.Delay(1000);
            BookmarkList.Items.Refresh();
        }

        /// <summary>
        /// サムネイル作成本体
        /// </summary>
        /// <param name="queueObj">取り出したQueueの中身</param>
        /// <param name="IsManual">マニュアル作成かどうか</param>
        private async Task CreateThumbAsync(
            QueueObj queueObj,
            bool IsManual = false,
            CancellationToken cts = default,
            string sourceMovieFullPathOverride = null
        )
        {
            string jobId =
                $"movie_id={queueObj?.MovieId} tab={queueObj?.Tabindex} manual={IsManual}";
            if (!string.IsNullOrWhiteSpace(sourceMovieFullPathOverride))
            {
                jobId += $" source_override='{sourceMovieFullPathOverride}'";
            }
            DebugRuntimeLog.TaskStart(nameof(CreateThumbAsync), jobId);
            try
            {
                // QueueDBリース経路ではMovieId/Hashが欠落し得るため、UI側一覧から補完する。
                long resolvedMovieId = await ResolveMovieIdByPathAsync(queueObj)
                    .ConfigureAwait(false);
                bool useNormalLaneTimeout = ShouldUseThumbnailNormalLaneTimeout(queueObj, IsManual);
                using CancellationTokenSource timeoutCts = useNormalLaneTimeout
                    ? new CancellationTokenSource(ThumbnailNormalLaneTimeout)
                    : null;
                using CancellationTokenSource linkedCts =
                    timeoutCts != null
                        ? CancellationTokenSource.CreateLinkedTokenSource(cts, timeoutCts.Token)
                        : null;
                CancellationToken effectiveCts = linkedCts?.Token ?? cts;

                ThumbnailCreateResult result;
                try
                {
                    result = await _thumbnailCreationService.CreateThumbAsync(
                        queueObj,
                        MainVM.DbInfo.DBName,
                        MainVM.DbInfo.ThumbFolder,
                        Properties.Settings.Default.IsResizeThumb,
                        IsManual,
                        effectiveCts,
                        sourceMovieFullPathOverride
                    );
                }
                catch (OperationCanceledException)
                    when (
                        useNormalLaneTimeout
                        && timeoutCts?.IsCancellationRequested == true
                        && !cts.IsCancellationRequested
                    )
                {
                    if (
                        TryPromoteThumbnailJobToRescueLane(
                            queueObj,
                            "normal-timeout"
                        )
                    )
                    {
                        DebugRuntimeLog.Write(
                            "thumbnail-timeout",
                            $"normal lane timeout handoff: movie='{queueObj?.MovieFullPath}' tab={queueObj?.Tabindex} timeout_sec={ThumbnailNormalLaneTimeout.TotalSeconds:0}"
                        );
                        return;
                    }

                    throw new TimeoutException(
                        $"thumbnail normal lane timeout: movie='{queueObj?.MovieFullPath}', tab={queueObj?.Tabindex}, timeout_sec={ThumbnailNormalLaneTimeout.TotalSeconds:0}"
                    );
                }

                // 生成失敗は例外としてキュー層へ伝播し、Failedで可視化する。
                if (!result.IsSuccess)
                {
                    if (
                        ShouldPromoteThumbnailFailureToRescueLane(queueObj, IsManual)
                        && TryPromoteThumbnailJobToRescueLane(
                            queueObj,
                            $"normal-failed:{result.ErrorMessage}"
                        )
                    )
                    {
                        DebugRuntimeLog.Write(
                            "thumbnail-recovery",
                            $"normal lane failure handoff: movie='{queueObj?.MovieFullPath}' tab={queueObj?.Tabindex} reason='{result.ErrorMessage}'"
                        );
                        return;
                    }

                    throw new ThumbnailCreateFailureException(
                        $"thumbnail create failed: movie='{queueObj?.MovieFullPath}', tab={queueObj?.Tabindex}, reason='{result.ErrorMessage}'"
                    )
                    {
                        FailureReason = result.ErrorMessage ?? "",
                    };
                }

                var saveThumbFileName = result.SaveThumbFileName;
                if (!Path.Exists(saveThumbFileName))
                {
                    throw new FileNotFoundException(
                        $"thumbnail output not found: '{saveThumbFileName}'",
                        saveThumbFileName
                    );
                }
                if (!IsManual)
                {
                    string previewCacheKey = "";
                    long previewRevision = 0;
                    bool hasMemoryPreview = TryStoreThumbnailProgressPreview(
                        queueObj,
                        result.PreviewFrame,
                        out previewCacheKey,
                        out previewRevision
                    );
                    if (!hasMemoryPreview)
                    {
                        previewCacheKey = ThumbnailProgressRuntime.CreateWorkerKey(queueObj);
                        previewRevision = DateTime.UtcNow.Ticks;
                    }

                    ThumbnailPreviewLatencyTracker.RecordSaved(
                        previewCacheKey,
                        previewRevision,
                        saveThumbFileName
                    );
                    _thumbnailProgressRuntime.MarkThumbnailSaved(
                        queueObj,
                        saveThumbFileName,
                        previewCacheKey,
                        previewRevision
                    );
                    RequestThumbnailProgressSnapshotRefresh();
                }

                // サムネイル作成完了時に保存先パスをログ出力（一時的）
                DebugRuntimeLog.Write(
                    "thumbnail-path",
                    $"Created thumbnail saved to: {saveThumbFileName}"
                );

                // 動画長はDB値とズレることがあるため、作成時の計測値で補正する。
                if (result.DurationSec.HasValue)
                {
                    bool needUpdateDb = false;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var item = MainVM
                            .MovieRecs.Where(x => IsSameMovieForQueue(x, queueObj, resolvedMovieId))
                            .FirstOrDefault();
                        if (item == null)
                        {
                            return;
                        }

                        string tSpan = new TimeSpan(
                            0,
                            0,
                            (int)(long)result.DurationSec.Value
                        ).ToString(@"hh\:mm\:ss");
                        if (item.Movie_Length != tSpan)
                        {
                            item.Movie_Length = tSpan;
                            needUpdateDb = true;
                        }
                    });

                    if (needUpdateDb)
                    {
                        if (
                            resolvedMovieId > 0
                            && !string.IsNullOrWhiteSpace(MainVM.DbInfo.DBFullPath)
                        )
                        {
                            UpdateMovieSingleColumn(
                                MainVM.DbInfo.DBFullPath,
                                resolvedMovieId,
                                "movie_length",
                                result.DurationSec.Value
                            );
                        }
                    }
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (
                        var item in MainVM.MovieRecs.Where(x =>
                            IsSameMovieForQueue(x, queueObj, resolvedMovieId)
                        )
                    )
                    {
                        switch (queueObj.Tabindex)
                        {
                            case 0:
                                item.ThumbPathSmall = saveThumbFileName;
                                break;
                            case 1:
                                item.ThumbPathBig = saveThumbFileName;
                                break;
                            case 2:
                                item.ThumbPathGrid = saveThumbFileName;
                                break;
                            case 3:
                                item.ThumbPathList = saveThumbFileName;
                                break;
                            case 4:
                                item.ThumbPathBig10 = saveThumbFileName;
                                break;
                            case 99:
                                item.ThumbDetail = saveThumbFileName;
                                break;
                            default:
                                break;
                        }
                    }
                });
            }
            finally
            {
                DebugRuntimeLog.TaskEnd(nameof(CreateThumbAsync), jobId);
            }
        }

        // 通常Queueの初回経路だけ短い時間予算を掛け、難動画が長く居座るのを防ぐ。
        internal static bool ShouldUseThumbnailNormalLaneTimeout(QueueObj queueObj, bool isManual)
        {
            if (isManual)
            {
                return false;
            }

            if (queueObj?.IsRescueRequest == true)
            {
                return false;
            }

            return queueObj != null;
        }

        // 通常レーン失敗は救済レーンへ渡し、同じ重い仕事を通常キューで再試行し続けない。
        internal static bool ShouldPromoteThumbnailFailureToRescueLane(
            QueueObj queueObj,
            bool isManual
        )
        {
            if (isManual)
            {
                return false;
            }

            if (queueObj?.IsRescueRequest == true)
            {
                return false;
            }

            return queueObj != null;
        }

        // 通常レーンから救済レーンへ仕事を引き渡し、通常jobの長時間占有を避ける。
        private bool TryPromoteThumbnailJobToRescueLane(QueueObj queueObj, string reason)
        {
            if (!ShouldPromoteThumbnailFailureToRescueLane(queueObj, isManual: false))
            {
                return false;
            }

            return TryEnqueueThumbnailRescueJob(
                queueObj,
                requiresIdle: true,
                reason: reason
            );
        }

        private sealed class ThumbnailCreateFailureException : InvalidOperationException
        {
            public ThumbnailCreateFailureException(string message)
                : base(message) { }

            public string FailureReason { get; init; } = "";
        }

        // QueueDB経由でMovieId/Hashが欠落している場合に、MoviePath一致で補完する。
        private async Task<long> ResolveMovieIdByPathAsync(QueueObj queueObj)
        {
            if (queueObj == null)
            {
                return 0;
            }

            bool needMovieId = queueObj.MovieId < 1;
            bool needHash = string.IsNullOrWhiteSpace(queueObj.Hash);
            if (!needMovieId && !needHash)
            {
                return queueObj.MovieId;
            }

            if (string.IsNullOrWhiteSpace(queueObj.MovieFullPath) && needMovieId)
            {
                return queueObj.MovieId;
            }

            long movieId = queueObj.MovieId;
            string hash = queueObj.Hash ?? "";
            await Dispatcher.InvokeAsync(() =>
            {
                MovieRecords item = null;
                if (queueObj.MovieId > 0)
                {
                    item = MainVM
                        .MovieRecs.Where(x => x.Movie_Id == queueObj.MovieId)
                        .FirstOrDefault();
                }

                if (item == null)
                {
                    item = MainVM
                        .MovieRecs.Where(x =>
                            string.Equals(
                                x.Movie_Path,
                                queueObj.MovieFullPath,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .FirstOrDefault();
                }

                if (item == null)
                {
                    return;
                }

                movieId = item.Movie_Id;
                if (string.IsNullOrWhiteSpace(hash))
                {
                    hash = item.Hash ?? "";
                }
            });

            // UI側の一覧に未展開でも、DBにはhashがあるケースがあるため補完する。
            if (
                (movieId < 1 || string.IsNullOrWhiteSpace(hash))
                && TryResolveMovieIdentityFromDb(
                    queueObj.MovieFullPath,
                    out long dbMovieId,
                    out string dbHash
                )
            )
            {
                if (movieId < 1 && dbMovieId > 0)
                {
                    movieId = dbMovieId;
                }
                if (string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(dbHash))
                {
                    hash = dbHash;
                }
            }

            if (movieId > 0)
            {
                queueObj.MovieId = movieId;
            }
            if (string.IsNullOrWhiteSpace(queueObj.Hash) && !string.IsNullOrWhiteSpace(hash))
            {
                queueObj.Hash = hash;
            }
            return movieId;
        }

        // MovieRecsに無い場合のフォールバックとして、DBからmovie_id/hashを直接引く。
        private bool TryResolveMovieIdentityFromDb(
            string movieFullPath,
            out long movieId,
            out string hash
        )
        {
            movieId = 0;
            hash = "";

            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return false;
            }

            try
            {
                string escapedMoviePath = movieFullPath.Replace("'", "''");
                var dt = GetData(
                    dbFullPath,
                    $"select movie_id, hash from movie where lower(movie_path) = lower('{escapedMoviePath}') limit 1"
                );
                if (dt == null || dt.Rows.Count < 1)
                {
                    return false;
                }

                var row = dt.Rows[0];
                _ = long.TryParse(row["movie_id"]?.ToString(), out movieId);
                hash = row["hash"]?.ToString() ?? "";
                return movieId > 0 || !string.IsNullOrWhiteSpace(hash);
            }
            catch
            {
                return false;
            }
        }

        // UI反映対象を「MovieId優先、無い場合はMoviePath一致」で判定する。
        private static bool IsSameMovieForQueue(
            MovieRecords item,
            QueueObj queueObj,
            long resolvedMovieId
        )
        {
            if (item == null || queueObj == null)
            {
                return false;
            }
            if (resolvedMovieId > 0 && item.Movie_Id == resolvedMovieId)
            {
                return true;
            }
            if (string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return false;
            }
            return string.Equals(
                item.Movie_Path,
                queueObj.MovieFullPath,
                StringComparison.OrdinalIgnoreCase
            );
        }

        // エンジンから受けた中立DTOをWPFの画像へ変換し、ミニパネル用キャッシュへ登録する。
        private static bool TryStoreThumbnailProgressPreview(
            QueueObj queueObj,
            ThumbnailPreviewFrame previewFrame,
            out string previewCacheKey,
            out long previewRevision
        )
        {
            previewCacheKey = "";
            previewRevision = 0;

            if (queueObj == null || previewFrame == null || !previewFrame.IsValid())
            {
                return false;
            }

            if (!TryCreatePreviewImageSource(previewFrame, out BitmapSource bitmapSource))
            {
                return false;
            }

            previewCacheKey = ThumbnailProgressRuntime.CreateWorkerKey(queueObj);
            if (string.IsNullOrWhiteSpace(previewCacheKey))
            {
                previewCacheKey = "";
                return false;
            }

            previewRevision = ThumbnailPreviewCache.Shared.Store(previewCacheKey, bitmapSource);
            if (previewRevision < 1)
            {
                previewCacheKey = "";
                return false;
            }

            return true;
        }

        // ピクセル配列の生データからWriteableBitmapを組み立て、UI間共有のためにFreezeする。
        private static bool TryCreatePreviewImageSource(
            ThumbnailPreviewFrame previewFrame,
            out BitmapSource bitmapSource
        )
        {
            bitmapSource = null;
            if (previewFrame == null || !previewFrame.IsValid())
            {
                return false;
            }

            if (!TryResolveWpfPixelFormat(previewFrame.PixelFormat, out PixelFormat pixelFormat))
            {
                return false;
            }

            try
            {
                WriteableBitmap bitmap = new(
                    previewFrame.Width,
                    previewFrame.Height,
                    96,
                    96,
                    pixelFormat,
                    null
                );
                bitmap.WritePixels(
                    new Int32Rect(0, 0, previewFrame.Width, previewFrame.Height),
                    previewFrame.PixelBytes,
                    previewFrame.Stride,
                    0
                );
                bitmap.Freeze();
                bitmapSource = bitmap;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveWpfPixelFormat(
            ThumbnailPreviewPixelFormat previewPixelFormat,
            out PixelFormat pixelFormat
        )
        {
            switch (previewPixelFormat)
            {
                case ThumbnailPreviewPixelFormat.Bgr24:
                    pixelFormat = PixelFormats.Bgr24;
                    return true;
                case ThumbnailPreviewPixelFormat.Bgra32:
                    pixelFormat = PixelFormats.Bgra32;
                    return true;
                default:
                    pixelFormat = default;
                    return false;
            }
        }

        /// <summary>
        /// 手動等間隔サムネイル作成
        /// </summary>
        private void CreateThumb_EqualInterval(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            // 複数選択対応: 選択中の全アイテムを取得
            List<MovieRecords> selectedItems = GetSelectedItemsByTabIndex();
            if (selectedItems == null || selectedItems.Count == 0)
            {
                return;
            }

            int targetTabIndex = Tabs.SelectedIndex;
            string currentDbName = MainVM?.DbInfo?.DBName ?? "";
            string currentThumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            TabInfo targetTabInfo = new(targetTabIndex, currentDbName, currentThumbFolder);

            foreach (var mv in selectedItems)
            {
                // 明示救済では stale な失敗固定マーカーを先に外してから1本ずつ流す。
                TryDeleteThumbnailErrorMarker(targetTabInfo.OutPath, mv.Movie_Path);

                QueueObj tempObj = new()
                {
                    MovieId = mv.Movie_Id,
                    MovieFullPath = mv.Movie_Path,
                    Hash = mv.Hash,
                    Tabindex = targetTabIndex,
                    IsRescueRequest = true,
                };
                _ = TryEnqueueThumbnailRescueJob(
                    tempObj,
                    requiresIdle: false,
                    reason: "manual-equal-interval"
                );
            }
        }
    }
}
