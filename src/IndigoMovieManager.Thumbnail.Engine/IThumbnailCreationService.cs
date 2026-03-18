namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 呼び出し側が触ってよい、サムネイル生成の公開面だけを表す。
    /// </summary>
    public interface IThumbnailCreationService
    {
        Task<bool> CreateBookmarkThumbAsync(
            ThumbnailBookmarkArgs args,
            CancellationToken cts = default
        );

        Task<ThumbnailCreateResult> CreateThumbAsync(
            ThumbnailCreateArgs args,
            CancellationToken cts = default
        );
    }
}
