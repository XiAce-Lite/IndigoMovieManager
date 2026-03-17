namespace IndigoMovieManager.Thumbnail
{
    // QueueObj の次段として、生成・キュー・worker が共有する中立な入力契約を定義する。
    public sealed class ThumbnailRequest
    {
        private string _hash = "";
        private ThumbnailQueuePriority _priority = ThumbnailQueuePriority.Normal;

        public int TabIndex { get; set; }
        public long MovieId { get; set; }
        public string MovieFullPath { get; set; }
        public string Hash
        {
            get { return _hash; }
            set { _hash = value ?? ""; }
        }
        public long MovieSizeBytes { get; set; }
        public int? ThumbPanelPosition { get; set; }
        public int? ThumbTimePosition { get; set; }
        public ThumbnailQueuePriority Priority
        {
            get { return _priority; }
            set { _priority = ThumbnailQueuePriorityHelper.Normalize(value); }
        }

        public ThumbnailRequest Clone()
        {
            return new ThumbnailRequest
            {
                TabIndex = TabIndex,
                MovieId = MovieId,
                MovieFullPath = MovieFullPath,
                Hash = Hash,
                MovieSizeBytes = MovieSizeBytes,
                ThumbPanelPosition = ThumbPanelPosition,
                ThumbTimePosition = ThumbTimePosition,
                Priority = Priority,
            };
        }

        public static ThumbnailRequest FromLegacyQueueObj(QueueObj queueObj)
        {
            if (queueObj == null)
            {
                return new ThumbnailRequest();
            }

            return new ThumbnailRequest
            {
                TabIndex = queueObj.Tabindex,
                MovieId = queueObj.MovieId,
                MovieFullPath = queueObj.MovieFullPath,
                Hash = queueObj.Hash,
                MovieSizeBytes = queueObj.MovieSizeBytes,
                ThumbPanelPosition = queueObj.ThumbPanelPos,
                ThumbTimePosition = queueObj.ThumbTimePos,
                Priority = queueObj.Priority,
            };
        }

        public QueueObj ToLegacyQueueObj()
        {
            return QueueObj.FromThumbnailRequest(this);
        }
    }
}
