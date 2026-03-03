using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Thumbnail.QueuePipeline
{
    // ProducerからPersisterへ渡す最小単位の要求。
    // QueueObjのうち永続化に必要な値だけを保持する。
    public sealed class QueueRequest
    {
        public string MainDbFullPath { get; set; } = "";
        public string MoviePath { get; set; } = "";
        public string MoviePathKey { get; set; } = "";
        public int TabIndex { get; set; }
        public long MovieSizeBytes { get; set; }
        public int? ThumbPanelPos { get; set; }
        public int? ThumbTimePos { get; set; }
        public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

        // QueueObjからQueueRequestへ変換する共通入口。
        public static QueueRequest FromQueueObj(string mainDbFullPath, QueueObj queueObj)
        {
            string moviePath = queueObj?.MovieFullPath ?? "";
            return new QueueRequest
            {
                MainDbFullPath = mainDbFullPath ?? "",
                MoviePath = moviePath,
                MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                TabIndex = queueObj?.Tabindex ?? 0,
                MovieSizeBytes = Math.Max(0, queueObj?.MovieSizeBytes ?? 0),
                ThumbPanelPos = queueObj?.ThumbPanelPos,
                ThumbTimePos = queueObj?.ThumbTimePos,
                RequestedAtUtc = DateTime.UtcNow
            };
        }
    }
}
