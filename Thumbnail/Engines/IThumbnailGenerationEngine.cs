using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// サムネイル生成エンジンの共通インターフェース。
    /// 各エンジン（FFMediaToolkit / ffmpeg 1pass / OpenCV）が実装する。
    /// </summary>
    internal interface IThumbnailGenerationEngine
    {
        /// <summary>
        /// エンジンを識別する文字列（ログ出力・環境変数指定に使用）。
        /// </summary>
        string EngineId { get; }

        /// <summary>
        /// ブックマーク用の単一フレームサムネイルを生成する。
        /// </summary>
        Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePosSec,
            CancellationToken ct
        );

        /// <summary>
        /// 通常・手動サムネイルを生成するメインメソッド。
        /// </summary>
        Task<ThumbnailCreateResult> CreateAsync(ThumbnailJobContext context, CancellationToken ct);
    }
}
