using System.IO;
using System.Windows;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
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
            CancellationToken cts = default
        )
        {
            string jobId =
                $"movie_id={queueObj?.MovieId} tab={queueObj?.Tabindex} manual={IsManual}";
            DebugRuntimeLog.TaskStart(nameof(CreateThumbAsync), jobId);
            try
            {
                // QueueDBリース経路ではMovieIdが空のため、まずUI側の一覧から補完する。
                long resolvedMovieId = await ResolveMovieIdByPathAsync(queueObj)
                    .ConfigureAwait(false);
                var result = await _thumbnailCreationService.CreateThumbAsync(
                    queueObj,
                    MainVM.DbInfo.DBName,
                    MainVM.DbInfo.ThumbFolder,
                    Properties.Settings.Default.IsResizeThumb,
                    IsManual,
                    cts
                );

                // 生成失敗は例外としてキュー層へ伝播し、Failedで可視化する。
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"thumbnail create failed: movie='{queueObj?.MovieFullPath}', tab={queueObj?.Tabindex}, reason='{result.ErrorMessage}'"
                    );
                }

                var saveThumbFileName = result.SaveThumbFileName;
                if (!Path.Exists(saveThumbFileName))
                {
                    throw new FileNotFoundException(
                        $"thumbnail output not found: '{saveThumbFileName}'",
                        saveThumbFileName
                    );
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

        // QueueDB経由でMovieIdが欠落している場合に、MoviePath一致で補完する。
        private async Task<long> ResolveMovieIdByPathAsync(QueueObj queueObj)
        {
            if (queueObj == null)
            {
                return 0;
            }
            if (queueObj.MovieId > 0)
            {
                return queueObj.MovieId;
            }
            if (string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return 0;
            }

            long movieId = 0;
            await Dispatcher.InvokeAsync(() =>
            {
                var item = MainVM
                    .MovieRecs.Where(x =>
                        string.Equals(
                            x.Movie_Path,
                            queueObj.MovieFullPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .FirstOrDefault();
                if (item == null)
                {
                    return;
                }
                movieId = item.Movie_Id;
            });

            if (movieId > 0)
            {
                queueObj.MovieId = movieId;
            }
            return movieId;
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

            foreach (var mv in selectedItems)
            {
                QueueObj tempObj = new()
                {
                    MovieId = mv.Movie_Id,
                    MovieFullPath = mv.Movie_Path,
                    Tabindex = Tabs.SelectedIndex,
                };
                _ = TryEnqueueThumbnailJob(tempObj);
            }
        }
    }
}
