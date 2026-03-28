namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 進捗表示を行わない既定実装。UI注入なしでも処理継続できるようにする。
    /// </summary>
    public sealed class NoOpThumbnailQueueProgressPresenter : IThumbnailQueueProgressPresenter
    {
        public static readonly NoOpThumbnailQueueProgressPresenter Instance = new();

        private NoOpThumbnailQueueProgressPresenter() { }

        public IThumbnailQueueProgressHandle Show(string title)
        {
            return NoOpThumbnailQueueProgressHandle.Instance;
        }
    }

    /// <summary>
    /// 何もしない進捗ハンドル。
    /// </summary>
    public sealed class NoOpThumbnailQueueProgressHandle : IThumbnailQueueProgressHandle
    {
        public static readonly NoOpThumbnailQueueProgressHandle Instance = new();

        private NoOpThumbnailQueueProgressHandle() { }

        public void Report(
            double progressPercent,
            string message,
            string title,
            bool isIndeterminate
        ) { }

        public void Dispose() { }
    }
}
