using IndigoMovieManager.Thumbnail;
using System.Windows;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // サムネイル監視タスクを再起動する。
        private void RestartThumbnailTask()
        {
            ClearThumbnailQueue();

            // 既存タスクのキャンセル
            _thumbCheckCts.Cancel();

            // 新しいCancellationTokenSourceを生成
            _thumbCheckCts = new CancellationTokenSource();

            // 新しいトークンでタスクを再起動
            _thumbCheckTask = CheckThumbAsync(_thumbCheckCts.Token);
        }

        /// <summary>
        /// CheckThumbAsync サムネイル作成用に起動時にぶん投げるタスク。常時起動。終了条件はねぇ。
        /// </summary>
        private async Task CheckThumbAsync(CancellationToken cts = default)
        {
            await _thumbnailQueueProcessor.RunAsync(
                queueThumb,
                (queueObj, token) => CreateThumbAsync(queueObj, false, token),
                GetThumbnailQueueMaxParallelism(),
                ThumbnailQueuePollIntervalMs,
                null,
                (token) => ProcessDeferredLargeCopyJobsAsync(token),
                cts).ConfigureAwait(false);
        }

        // ブックマーク用の単一フレームサムネイルを作成する。
        private async Task CreateBookmarkThumbAsync(string movieFullPath, string saveThumbPath, int capturePos)
        {
            bool created = await _thumbnailCreationService.CreateBookmarkThumbAsync(movieFullPath, saveThumbPath, capturePos);
            if (!created) { return; }

            await Task.Delay(1000);
            BookmarkList.Items.Refresh();
        }

        /// <summary>
        /// サムネイル作成本体
        /// </summary>
        /// <param name="queueObj">取り出したQueueの中身</param>
        /// <param name="IsManual">マニュアル作成かどうか</param>
        private async Task CreateThumbAsync(QueueObj queueObj, bool IsManual = false, CancellationToken cts = default, bool releaseQueueKey = true)
        {
            try
            {
                var result = await _thumbnailCreationService.CreateThumbAsync(
                    queueObj,
                    MainVM.DbInfo.DBName,
                    MainVM.DbInfo.ThumbFolder,
                    Properties.Settings.Default.IsResizeThumb,
                    IsManual,
                    cts);

                // 3GB超コピーが必要なケースは後回し登録だけ行い、通常キュー消化を優先する。
                if (result.IsDeferredByLargeCopy)
                {
                    RegisterDeferredLargeCopyJob(queueObj, result.DeferredCopySizeBytes);
                    return;
                }

                var saveThumbFileName = result.SaveThumbFileName;

                // 動画長はDB値とズレることがあるため、作成時の計測値で補正する。
                if (result.DurationSec.HasValue)
                {
                    bool needUpdateDb = false;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var item = MainVM.MovieRecs.Where(x => x.Movie_Id == queueObj.MovieId).FirstOrDefault();
                        if (item == null) { return; }

                        string tSpan = new TimeSpan(0, 0, (int)(long)result.DurationSec.Value).ToString(@"hh\:mm\:ss");
                        if (item.Movie_Length != tSpan)
                        {
                            item.Movie_Length = tSpan;
                            needUpdateDb = true;
                        }
                    });

                    if (needUpdateDb)
                    {
                        UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, queueObj.MovieId, "movie_length", result.DurationSec.Value);
                    }
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in MainVM.MovieRecs.Where(x => x.Movie_Id == queueObj.MovieId))
                    {
                        switch (queueObj.Tabindex)
                        {
                            case 0: item.ThumbPathSmall = saveThumbFileName; break;
                            case 1: item.ThumbPathBig = saveThumbFileName; break;
                            case 2: item.ThumbPathGrid = saveThumbFileName; break;
                            case 3: item.ThumbPathList = saveThumbFileName; break;
                            case 4: item.ThumbPathBig10 = saveThumbFileName; break;
                            case 99: item.ThumbDetail = saveThumbFileName; break;
                            default: break;
                        }
                    }
                });
            }
            finally
            {
                // キュー経由ジョブのみ、重複抑止キーを解放する。
                if (releaseQueueKey)
                {
                    ReleaseThumbnailJob(queueObj);
                }
            }
        }

        /// <summary>
        /// 手動等間隔サムネイル作成
        /// </summary>
        private void CreateThumb_EqualInterval(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) { return; }

            // 複数選択対応: 選択中の全アイテムを取得
            List<MovieRecords> selectedItems = GetSelectedItemsByTabIndex();
            if (selectedItems == null || selectedItems.Count == 0) { return; }

            foreach (var mv in selectedItems)
            {
                QueueObj tempObj = new()
                {
                    MovieId = mv.Movie_Id,
                    MovieFullPath = mv.Movie_Path,
                    Tabindex = Tabs.SelectedIndex
                };
                _ = TryEnqueueThumbnailJob(tempObj);
            }
        }
    }
}
