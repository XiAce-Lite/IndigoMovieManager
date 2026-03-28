namespace IndigoMovieManager.Thumbnail
{
    // Queueへ渡す優先度は通常と優先の2段階だけに絞る。
    public enum ThumbnailQueuePriority
    {
        Normal = 0,
        Preferred = 1,
    }

    // 複数プロジェクトから同じ基準で優先度を扱えるよう、正規化を集約する。
    public static class ThumbnailQueuePriorityHelper
    {
        public static ThumbnailQueuePriority Normalize(ThumbnailQueuePriority priority)
        {
            return priority == ThumbnailQueuePriority.Preferred
                ? ThumbnailQueuePriority.Preferred
                : ThumbnailQueuePriority.Normal;
        }

        public static bool IsPreferred(ThumbnailQueuePriority priority)
        {
            return Normalize(priority) == ThumbnailQueuePriority.Preferred;
        }
    }

    // worker / queue / app が共有する最小DTOを Contracts へ寄せる。
    public class QueueObj
    {
        // 既存呼び出しは QueueObj を触り続けても、中身は新契約へ集約する。
        private readonly ThumbnailRequest _request = new();

        public int Tabindex { get { return _request.TabIndex; } set { _request.TabIndex = value; } }
        public long MovieId { get { return _request.MovieId; } set { _request.MovieId = value; } }
        public string MovieFullPath
        {
            get { return _request.MovieFullPath; }
            set { _request.MovieFullPath = value; }
        }
        public string Hash { get { return _request.Hash; } set { _request.Hash = value; } }
        public long MovieSizeBytes
        {
            get { return _request.MovieSizeBytes; }
            set { _request.MovieSizeBytes = value; }
        }
        public int? ThumbPanelPos
        {
            get { return _request.ThumbPanelPosition; }
            set { _request.ThumbPanelPosition = value; }
        }
        public int? ThumbTimePos
        {
            get { return _request.ThumbTimePosition; }
            set { _request.ThumbTimePosition = value; }
        }
        public ThumbnailQueuePriority Priority
        {
            get { return _request.Priority; }
            set { _request.Priority = value; }
        }

        public ThumbnailRequest ToThumbnailRequest()
        {
            return _request.Clone();
        }

        public void ApplyThumbnailRequest(ThumbnailRequest request)
        {
            if (request == null)
            {
                return;
            }

            _request.TabIndex = request.TabIndex;
            _request.MovieId = request.MovieId;
            _request.MovieFullPath = request.MovieFullPath;
            _request.Hash = request.Hash;
            _request.MovieSizeBytes = request.MovieSizeBytes;
            _request.ThumbPanelPosition = request.ThumbPanelPosition;
            _request.ThumbTimePosition = request.ThumbTimePosition;
            _request.Priority = request.Priority;
        }

        public static QueueObj FromThumbnailRequest(ThumbnailRequest request)
        {
            QueueObj queueObj = new();
            queueObj.ApplyThumbnailRequest(request);
            return queueObj;
        }
    }
}
