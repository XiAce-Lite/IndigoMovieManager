using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【サムネイル生成の絶対的オーケストレータ】✨
    /// 状況とルールを見極め、最適な生成エンジンを召喚してサムネイルを爆誕させるぜ！🔥
    /// </summary>
    public sealed partial class ThumbnailCreationService : IThumbnailCreationService
    {
        private readonly ThumbnailBookmarkCoordinator bookmarkCoordinator;
        private readonly ThumbnailCreateEntryCoordinator createEntryCoordinator;

        internal static ThumbnailCreationService Create(
            ThumbnailCreationServiceComposition composition
        )
        {
            return new ThumbnailCreationService(composition);
        }

        private ThumbnailCreationService(ThumbnailCreationServiceComposition composition)
        {
            ArgumentNullException.ThrowIfNull(composition);
            bookmarkCoordinator = composition.BookmarkCoordinator;
            createEntryCoordinator = composition.CreateEntryCoordinator;
        }

        /// <summary>
        /// ブックマーク用のとっておきの一枚（単一フレーム）を生成する専用ルートだ！📸
        /// </summary>
        public Task<bool> CreateBookmarkThumbAsync(
            ThumbnailBookmarkArgs args,
            CancellationToken cts = default
        )
        {
            ArgumentNullException.ThrowIfNull(args);
            return bookmarkCoordinator.CreateAsync(
                args.MovieFullPath,
                args.SaveThumbPath,
                args.CapturePos,
                cts
            );
        }

        /// <summary>
        /// サムネイル生成の本丸！通常・手動を問わず、すべての生成処理はここから始まる激アツなメイン・エントリーポイントだぜ！🚀
        /// </summary>
        public Task<ThumbnailCreateResult> CreateThumbAsync(
            ThumbnailCreateArgs args,
            CancellationToken cts = default
        )
        {
            ArgumentNullException.ThrowIfNull(args);
            return createEntryCoordinator.CreateAsync(args, cts);
        }
    }
}
