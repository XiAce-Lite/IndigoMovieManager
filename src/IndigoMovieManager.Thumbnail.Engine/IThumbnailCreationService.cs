namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル生成の「公開インターフェース」。
    ///
    /// 【全体の流れでの位置づけ】
    ///   MainWindow（UI層）
    ///     → ThumbnailCreationServiceFactory.Create() でインスタンス取得
    ///     → このインターフェース経由で生成を依頼
    ///     → 内部で Engine / Queue / RescueWorker が協調して処理
    ///
    /// 呼び出し側は concrete な実装（ThumbnailCreationService）を直接触らず、
    /// 必ずこのインターフェースだけを使うことで、Engine 側の変更に巻き込まれない設計。
    /// </summary>
    public interface IThumbnailCreationService
    {
        /// <summary>
        /// ブックマーク用サムネイル（単一フレーム）を生成する。
        /// ユーザーが手動で「この瞬間を保存！」と選んだ時に呼ばれる。
        /// </summary>
        Task<bool> CreateBookmarkThumbAsync(
            ThumbnailBookmarkArgs args,
            CancellationToken cts = default
        );

        /// <summary>
        /// サムネイル生成のメインルート。通常キュー・手動・救済、すべてここから始まる。
        /// Queue からの自動生成も、UI からの手動生成も、最終的にはこの1メソッドに集約される。
        /// </summary>
        Task<ThumbnailCreateResult> CreateThumbAsync(
            ThumbnailCreateArgs args,
            CancellationToken cts = default
        );
    }
}
