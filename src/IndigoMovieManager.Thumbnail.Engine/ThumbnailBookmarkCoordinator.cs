using System.IO;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// ブックマーク用 1 枚サムネイル生成の入口を担当する。
    /// </summary>
    internal sealed class ThumbnailBookmarkCoordinator
    {
        private readonly ThumbnailEngineRouter engineRouter;

        public ThumbnailBookmarkCoordinator(ThumbnailEngineRouter engineRouter)
        {
            this.engineRouter =
                engineRouter ?? throw new ArgumentNullException(nameof(engineRouter));
        }

        public async Task<bool> CreateAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            if (!Path.Exists(movieFullPath))
            {
                return false;
            }

            // ブックマークは位置指定の再現性を優先して専用エンジンへ流す。
            IThumbnailGenerationEngine engine = engineRouter.ResolveForBookmark();
            try
            {
                return await engine.CreateBookmarkAsync(
                    movieFullPath,
                    saveThumbPath,
                    capturePos,
                    cts
                );
            }
            catch (Exception ex)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"bookmark create failed: engine={engine.EngineId}, movie='{movieFullPath}', err='{ex.Message}'"
                );
                return false;
            }
        }
    }
}
