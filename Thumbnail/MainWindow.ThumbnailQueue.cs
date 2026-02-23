using IndigoMovieManager.Thumbnail;
using System.Collections.Concurrent;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // (MovieId, Tabindex) 単位で重複投入を抑止するキー管理。
        private static readonly ConcurrentDictionary<string, byte> queuedThumbnailKeys = new();

        // サムネイルジョブのユニークキーを生成する。
        private static string GetThumbnailJobKey(QueueObj queueObj)
        {
            return $"{queueObj.MovieId}:{queueObj.Tabindex}";
        }

        // キューへジョブを追加する。既に同一キーがある場合は追加しない。
        private bool TryEnqueueThumbnailJob(QueueObj queueObj)
        {
            if (queueObj == null) { return false; }

            string key = GetThumbnailJobKey(queueObj);
            if (!queuedThumbnailKeys.TryAdd(key, 0))
            {
                return false;
            }

            queueThumb.Enqueue(queueObj);
            return true;
        }

        // 処理終了したジョブのキーを解放する。
        private void ReleaseThumbnailJob(QueueObj queueObj)
        {
            if (queueObj == null) { return; }
            string key = GetThumbnailJobKey(queueObj);
            queuedThumbnailKeys.TryRemove(key, out _);
        }

        // キューと重複管理キーをまとめて初期化する。
        private void ClearThumbnailQueue()
        {
            while (queueThumb.TryDequeue(out _)) { }
            queuedThumbnailKeys.Clear();
        }
    }
}
