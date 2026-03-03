namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// キュー処理中の進捗表示を、UI実装から分離するためのポート。
    /// </summary>
    public interface IThumbnailQueueProgressPresenter
    {
        IThumbnailQueueProgressHandle Show(string title);
    }

    /// <summary>
    /// 表示中の進捗ハンドル。更新と解放だけを提供する。
    /// </summary>
    public interface IThumbnailQueueProgressHandle : IDisposable
    {
        void Report(double progressPercent, string message, string title, bool isIndeterminate);
    }
}
