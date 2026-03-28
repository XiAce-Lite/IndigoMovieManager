using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【サムネイル生成の絶対的オーケストレータ】✨
    /// 状況とルールを見極め、最適な生成エンジンを召喚してサムネイルを爆誕させるぜ！🔥
    /// </summary>
    internal sealed partial class ThumbnailCreationService : IThumbnailCreationService
    {
        private readonly Func<ThumbnailBookmarkArgs, CancellationToken, Task<bool>> createBookmarkAsync;
        private readonly Func<
            ThumbnailCreateArgs,
            CancellationToken,
            Task<ThumbnailCreateResult>
        > createThumbAsync;

        internal ThumbnailCreationService(ThumbnailCreationServiceComposition composition)
        {
            ArgumentNullException.ThrowIfNull(composition);
            createBookmarkAsync = composition.CreateBookmarkAsync;
            createThumbAsync = composition.CreateThumbAsync;
        }

        /// <summary>
        /// ブックマーク用のとっておきの一枚（単一フレーム）を生成する専用ルートだ！📸
        /// </summary>
        public Task<bool> CreateBookmarkThumbAsync(
            ThumbnailBookmarkArgs args,
            CancellationToken cts = default
        )
        {
            return createBookmarkAsync(args, cts);
        }

        /// <summary>
        /// サムネイル生成の本丸！通常・手動を問わず、すべての生成処理はここから始まる激アツなメイン・エントリーポイントだぜ！🚀
        /// </summary>
        public Task<ThumbnailCreateResult> CreateThumbAsync(
            ThumbnailCreateArgs args,
            CancellationToken cts = default
        )
        {
            return createThumbAsync(args, cts);
        }
    }
}
