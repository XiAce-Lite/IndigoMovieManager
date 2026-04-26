using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    /// <summary>
    /// MainWindow の partial：サムネイル生成処理の「UIからの発火と結果の反映」を担当。
    ///
    /// 【全体の流れでの位置づけ】
    ///   監視フォルダ検出 or UI操作
    ///     → ThumbnailQueue（キュー投入）
    ///       → ★ここ★ CheckThumbAsync（常駐ワーカー）がキューからジョブを取り出し
    ///         → IThumbnailCreationService.CreateThumbAsync() で Engine に委譲
    ///         → 成功したらUIスレッドでサムネイル画像を反映（TryInvokeThumbnailUiReflectionAsync）
    ///         → 失敗したら ThumbnailCreateFailureException として Queue 層へ伝播
    ///
    /// 主なメソッド：
    /// - CheckThumbAsync：起動時に常駐する∞ループ。QueueProcessor と協調してジョブを消化。
    /// - CreateThumbAsync：1件のサムネイル生成→UI反映→DB更新の全手順。
    /// - CreateBookmarkThumbAsync：ブックマーク用の単一フレーム生成。
    /// </summary>
    public partial class MainWindow
    {
        private const string ThumbnailNormalLaneTimeoutSecEnvName = "IMM_THUMB_NORMAL_TIMEOUT_SEC";
        private const int DefaultThumbnailNormalLaneTimeoutSec = 40;
        private const int MinThumbnailNormalLaneTimeoutSec = 1;
        private const int MaxThumbnailNormalLaneTimeoutSec = 600;

        // サムネイル監視タスクを再起動する。
        private void RestartThumbnailTask()
        {
            DebugRuntimeLog.TaskStart(nameof(RestartThumbnailTask));
            ClearThumbnailQueue();

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
                // 起動呼び出し元のUIスレッドをすぐ返し、常駐処理の同期前半で固めない。
                await Task.Yield();

                while (true)
                {
                    cts.ThrowIfCancellationRequested();
                    if (!isThumbnailQueueInputEnabled)
                    {
                        // DB切替や起動段階ロード中は既存pendingも触らず待機し、UI優先で返す。
                        await Task.Delay(250, cts).ConfigureAwait(false);
                        continue;
                    }

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
                                preferredMoviePathKeysResolver: ResolvePreferredVisibleMoviePathKeys,
                                handoffLaneResolver: queueObj =>
                                    ResolveThumbnailRescueLaneName(queueObj?.MovieSizeBytes ?? 0),
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
                                onQueueDrainedAsync: token => OnThumbnailQueueDrainedAsync(token),
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
                new ThumbnailBookmarkArgs
                {
                    MovieFullPath = movieFullPath,
                    SaveThumbPath = saveThumbPath,
                    CapturePos = capturePos,
                }
            );
            if (!created)
            {
                return;
            }

            await Task.Delay(1000);
            RefreshBookmarkTabView();
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
            string sourceMovieFullPathOverride = null,
            string initialEngineHint = null,
            bool disableNormalLaneTimeout = false
        )
        {
            using IDisposable uiHangScope = TrackUiHangActivity(UiHangActivityKind.Thumbnail);
            string normalizedInitialEngineHint = initialEngineHint?.Trim() ?? "";
            string traceId = ThumbnailMovieTraceRuntime.TryCreateTraceId(
                queueObj?.MovieFullPath,
                sourceMovieFullPathOverride
            );
            string jobId = $"movie_id={queueObj?.MovieId} tab={queueObj?.Tabindex} manual={IsManual}";
            if (!string.IsNullOrWhiteSpace(sourceMovieFullPathOverride))
            {
                jobId += $" source_override='{sourceMovieFullPathOverride}'";
            }
            if (!string.IsNullOrWhiteSpace(normalizedInitialEngineHint))
            {
                jobId += $" initial_engine_hint='{normalizedInitialEngineHint}'";
            }
            if (!string.IsNullOrWhiteSpace(traceId))
            {
                jobId += $" trace_id={traceId}";
                ThumbnailMovieTraceLog.Write(
                    traceId,
                    source: "main",
                    phase: "main_create_dispatch",
                    moviePath: queueObj?.MovieFullPath ?? "",
                    sourceMoviePath: sourceMovieFullPathOverride ?? "",
                    tabIndex: queueObj?.Tabindex ?? -1,
                    result: "started",
                    detail:
                        $"manual={IsManual}; initial_engine_hint={normalizedInitialEngineHint}; timeout_enabled={ShouldUseThumbnailNormalLaneTimeout(queueObj, IsManual, disableNormalLaneTimeout)}"
                );
            }
            DebugRuntimeLog.TaskStart(nameof(CreateThumbAsync), jobId);
            try
            {
                // QueueDBリース経路ではMovieId/Hashが欠落し得るため、UI側一覧から補完する。
                long resolvedMovieId = await ResolveMovieIdByPathAsync(queueObj, cts)
                    .ConfigureAwait(false);
                bool useNormalLaneTimeout = ShouldUseThumbnailNormalLaneTimeout(
                    queueObj,
                    IsManual,
                    disableNormalLaneTimeout
                );
                TimeSpan normalLaneTimeout = ResolveThumbnailNormalLaneTimeout();
                using CancellationTokenSource timeoutCts = useNormalLaneTimeout
                    ? new CancellationTokenSource(normalLaneTimeout)
                    : null;
                using CancellationTokenSource linkedCts =
                    timeoutCts != null
                        ? CancellationTokenSource.CreateLinkedTokenSource(cts, timeoutCts.Token)
                        : null;
                CancellationToken effectiveCts = linkedCts?.Token ?? cts;

                ThumbnailCreateResult result;
                ThumbnailCreateArgs createArgs = ThumbnailCreateArgsCompatibility.FromLegacyQueueObj(
                    queueObj,
                    dbName: MainVM.DbInfo.DBName,
                    thumbFolder: MainVM.DbInfo.ThumbFolder,
                    isResizeThumb: Properties.Settings.Default.IsResizeThumb,
                    isManual: IsManual,
                    sourceMovieFullPathOverride: sourceMovieFullPathOverride,
                    initialEngineHint: normalizedInitialEngineHint,
                    traceId: traceId
                );
                try
                {
                    try
                    {
                        result = await _thumbnailCreationService.CreateThumbAsync(
                            createArgs,
                            effectiveCts
                        );
                    }
                    finally
                    {
                        // legacy QueueObj を使う UI 側へ、実行中に補完された hash / size を戻す。
                        ThumbnailCreateArgsCompatibility.ApplyBackToLegacyQueueObj(
                            createArgs,
                            queueObj
                        );
                    }
                }
                catch (OperationCanceledException)
                    when (
                        useNormalLaneTimeout
                        && timeoutCts?.IsCancellationRequested == true
                        && !cts.IsCancellationRequested
                    )
                {
                    throw new TimeoutException(
                        $"thumbnail normal lane timeout: movie='{queueObj?.MovieFullPath}', tab={queueObj?.Tabindex}, timeout_sec={normalLaneTimeout.TotalSeconds:0}"
                    );
                }

                // 生成失敗は例外としてキュー層へ伝播し、Failedで可視化する。
                if (!result.IsSuccess)
                {
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

                // 保存成功直後に成功jpgキャッシュへ反映し、後続判定の再走査を減らす。
                ThumbnailPathResolver.RememberSuccessThumbnailPath(saveThumbFileName);

                // 本exe側で1タブ分の保存に成功したら、起動後総作成枚数をここで1枚積む。
                _thumbnailProgressRuntime.RecordThumbnailCreated();
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
                }
                RequestThumbnailProgressSnapshotRefresh();

                // サムネイル作成完了時に保存先パスをログ出力（一時的）
                DebugRuntimeLog.Write(
                    "thumbnail-path",
                    $"Created thumbnail saved to: {saveThumbFileName}"
                );
                ThumbnailMovieTraceLog.Write(
                    traceId,
                    source: "main",
                    phase: "main_create_succeeded",
                    moviePath: queueObj?.MovieFullPath ?? "",
                    sourceMoviePath: sourceMovieFullPathOverride ?? "",
                    tabIndex: queueObj?.Tabindex ?? -1,
                    result: "success",
                    detail: $"resolved_movie_id={resolvedMovieId}",
                    outputPath: saveThumbFileName,
                    durationSec: result.DurationSec,
                    fileSizeBytes: queueObj?.MovieSizeBytes ?? 0,
                    processEngineId: result.ProcessEngineId ?? ""
                );

                // 動画長はDB値とズレることがあるため、作成時の計測値で補正する。
                if (result.DurationSec.HasValue)
                {
                    bool needUpdateDb = false;
                    bool updatedOnUi = await TryInvokeThumbnailUiReflectionAsync(
                            () =>
                            {
                                var item = MainVM
                                    .MovieRecs.Where(x =>
                                        IsSameMovieForQueue(x, queueObj, resolvedMovieId)
                                    )
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
                            },
                            cts
                        )
                        .ConfigureAwait(false);

                    if (updatedOnUi && needUpdateDb)
                    {
                        if (
                            resolvedMovieId > 0
                            && !string.IsNullOrWhiteSpace(MainVM.DbInfo.DBFullPath)
                        )
                        {
                            _mainDbMovieMutationFacade.UpdateMovieLength(
                                MainVM.DbInfo.DBFullPath,
                                resolvedMovieId,
                                result.DurationSec.Value
                            );
                        }
                    }
                }

                _ = await TryInvokeThumbnailUiReflectionAsync(
                        () =>
                        {
                            string reflectedMoviePath =
                                sourceMovieFullPathOverride ?? queueObj?.MovieFullPath ?? "";
                            MovieRecords updatedMovie = null;
                            foreach (
                                var item in MainVM.MovieRecs.Where(x =>
                                    IsSameMovieForQueue(x, queueObj, resolvedMovieId)
                                )
                            )
                            {
                                if (
                                    TryApplyThumbnailPathToMovieRecord(
                                        item,
                                        queueObj.Tabindex,
                                        saveThumbFileName
                                    )
                                )
                                {
                                    updatedMovie ??= item;
                                }
                            }

                            // 特殊タブはコピー済みViewModelを持つため、成功jpgを個別に差し替える。
                            TryReflectRescuedThumbnailIntoUpperTabRescueItems(
                                reflectedMoviePath,
                                queueObj?.Tabindex ?? -1,
                                saveThumbFileName
                            );
                            TryReflectCreatedThumbnailIntoUpperTabDuplicateItems(
                                reflectedMoviePath,
                                queueObj?.Tabindex ?? -1,
                                saveThumbFileName
                            );

                            // ユーザーが明示要求した高優先度作成だけは、その場で main tab の見た目も取り直す。
                            if (ShouldRefreshVisibleThumbnailUiAfterCreate(queueObj))
                            {
                                RefreshVisibleThumbnailUiAfterImmediateThumbnailSuccess(
                                    "preferred-create-success"
                                );
                                RequestMainTabFullReloadAfterThumbnailSuccess(
                                    "preferred-create-success"
                                );
                            }

                            if (updatedMovie != null)
                            {
                                TryQueueExternalSkinThumbnailUpdated(
                                    updatedMovie,
                                    queueObj.Tabindex,
                                    "thumbnail-create-success"
                                );
                            }
                        },
                        cts
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ThumbnailMovieTraceLog.Write(
                    traceId,
                    source: "main",
                    phase: "main_create_failed",
                    moviePath: queueObj?.MovieFullPath ?? "",
                    sourceMoviePath: sourceMovieFullPathOverride ?? "",
                    tabIndex: queueObj?.Tabindex ?? -1,
                    result: "failed",
                    detail: ex.Message,
                    fileSizeBytes: queueObj?.MovieSizeBytes ?? 0
                );
                throw;
            }
            finally
            {
                DebugRuntimeLog.TaskEnd(nameof(CreateThumbAsync), jobId);
            }
        }

        // 終了やDB切替では、バックグラウンド側がUI反映待ちでぶら下がらないようにする。
        internal static bool ShouldSkipThumbnailUiReflection(
            bool isInputEnabled,
            bool dispatcherHasShutdownStarted,
            bool dispatcherHasShutdownFinished,
            bool isCancellationRequested
        )
        {
            return !isInputEnabled
                || dispatcherHasShutdownStarted
                || dispatcherHasShutdownFinished
                || isCancellationRequested;
        }

        // ユーザーが前に出した preferred job だけは、成功時に visible UI の再読込まで行う。
        internal static bool ShouldRefreshVisibleThumbnailUiAfterCreate(QueueObj queueObj)
        {
            return queueObj != null && ThumbnailQueuePriorityHelper.IsPreferred(queueObj.Priority);
        }

        private bool ShouldSkipThumbnailUiReflection(CancellationToken cts)
        {
            return ShouldSkipThumbnailUiReflection(
                isThumbnailQueueInputEnabled,
                Dispatcher.HasShutdownStarted,
                Dispatcher.HasShutdownFinished,
                cts.IsCancellationRequested
            );
        }

        // 通常時だけUIスレッドへ戻し、終了・切替中は待たずに処理完了を優先する。
        private async Task<bool> TryInvokeThumbnailUiReflectionAsync(
            Action action,
            CancellationToken cts = default,
            DispatcherPriority priority = DispatcherPriority.Normal
        )
        {
            if (action == null)
            {
                return false;
            }

            if (ShouldSkipThumbnailUiReflection(cts))
            {
                return false;
            }

            if (Dispatcher.CheckAccess())
            {
                action();
                return true;
            }

            try
            {
                await Dispatcher.InvokeAsync(action, priority, cts).Task.ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (ShouldSkipThumbnailUiReflection(cts))
            {
                return false;
            }
            catch (InvalidOperationException) when (ShouldSkipThumbnailUiReflection(cts))
            {
                return false;
            }
        }

        // 通常Queueの初回経路だけ短い時間予算を掛け、難動画が長く居座るのを防ぐ。
        internal static bool ShouldUseThumbnailNormalLaneTimeout(
            QueueObj queueObj,
            bool isManual,
            bool disableNormalLaneTimeout = false
        )
        {
            if (isManual)
            {
                return false;
            }

            if (disableNormalLaneTimeout)
            {
                return false;
            }

            return queueObj != null;
        }

        // live確認で秒数を差し替えやすいよう、通常レーンtimeoutは環境変数で上書き可能にする。
        internal static TimeSpan ResolveThumbnailNormalLaneTimeout()
        {
            string raw = Environment.GetEnvironmentVariable(ThumbnailNormalLaneTimeoutSecEnvName)
                ?.Trim() ?? "";
            if (int.TryParse(raw, out int parsed))
            {
                int clamped = Math.Clamp(
                    parsed,
                    MinThumbnailNormalLaneTimeoutSec,
                    MaxThumbnailNormalLaneTimeoutSec
                );
                return TimeSpan.FromSeconds(clamped);
            }

            return TimeSpan.FromSeconds(DefaultThumbnailNormalLaneTimeoutSec);
        }

        private sealed class ThumbnailCreateFailureException : InvalidOperationException
        {
            public ThumbnailCreateFailureException(string message)
                : base(message) { }

            public string FailureReason { get; init; } = "";
        }

        // QueueDB経由でMovieId/Hashが欠落している場合に、MoviePath一致で補完する。
        private async Task<long> ResolveMovieIdByPathAsync(
            QueueObj queueObj,
            CancellationToken cts = default
        )
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

            if (ShouldSkipThumbnailUiReflection(cts))
            {
                return queueObj.MovieId;
            }

            long movieId = queueObj.MovieId;
            string hash = queueObj.Hash ?? "";
            bool resolvedOnUi = await TryInvokeThumbnailUiReflectionAsync(
                    () =>
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
                    },
                    cts
                )
                .ConfigureAwait(false);
            if (!resolvedOnUi && ShouldSkipThumbnailUiReflection(cts))
            {
                return movieId;
            }

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
                ShowThumbnailUserActionPopup(
                    "等間隔サムネイル作成",
                    "対象タブを選択してから実行してください。",
                    MessageBoxImage.Warning
                );
                return;
            }

            // 複数選択対応: 発火元一覧に応じて対象を取り、下部エラータブ右クリックも誤参照しない。
            List<MovieRecords> selectedItems = ResolveSelectedMovieRecordsForThumbnailUserAction(sender);
            if (selectedItems == null || selectedItems.Count == 0)
            {
                ShowThumbnailUserActionPopup(
                    "等間隔サムネイル作成",
                    "対象動画が選択されていません。",
                    MessageBoxImage.Warning
                );
                return;
            }

            int targetTabIndex = GetCurrentThumbnailActionTabIndex();
            ThumbnailRescueUserActionDispatchResult dispatchResult =
                DispatchThumbnailRescueUserAction(
                    selectedItems,
                    new ThumbnailRescueUserActionRequest(
                        TargetTabIndex: targetTabIndex,
                        Priority: ThumbnailQueuePriority.Preferred,
                        Reason: "manual-equal-interval",
                        UseDedicatedManualWorkerSlot: false,
                        // ユーザー要請なら既存成功jpgがあっても止めず、別タイミングの1枚を作り直せるようにする。
                        SkipWhenSuccessExists: false,
                        RescueMode: "",
                        DeleteErrorMarkerFirst: true
                    )
                );

            ShowThumbnailUserActionPopup(
                "等間隔サムネイル作成",
                BuildThumbnailRescueUserActionPopupMessage(
                    "等間隔サムネイル作成",
                    dispatchResult.SelectedCount,
                    dispatchResult.AcceptedCount,
                    dispatchResult.DuplicateRequestCount,
                    dispatchResult.ExistingSuccessCount
                ),
                ResolveThumbnailRescueUserActionPopupImage(
                    dispatchResult.AcceptedCount,
                    dispatchResult.DuplicateRequestCount,
                    dispatchResult.ExistingSuccessCount
                )
            );
        }
    }
}
