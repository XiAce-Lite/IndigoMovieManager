namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// サムネイル生成エンジンの抽象。
    /// 1ジョブの生成処理を実装ごとに切り替える。
    /// </summary>
    internal interface IThumbnailGenerationEngine
    {
        string EngineId { get; }
        string EngineName { get; }

        bool CanHandle(ThumbnailJobContext context);

        Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken cts = default
        );

        Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        );
    }
}