namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル生成リクエストの「共通データ型」。
    ///
    /// 【全体の流れでの位置づけ】
    ///   監視フォルダ検出 or UI操作
    ///     → QueueObj（旧形式）として受け取り
    ///     → ★ここ★ ThumbnailRequest に変換（FromLegacyQueueObj）
    ///     → QueueDb に永続化 → Engine が取り出して処理
    ///
    /// Queue / Engine / RescueWorker の3者が共有する「中立な入力契約」。
    /// どのレイヤーからでも同じ型でやり取りするため、プロジェクト間の依存を最小化している。
    /// </summary>
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
