using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

internal static class ThumbnailCreationServiceTestFactory
{
    internal static IThumbnailCreationService CreateForTesting(
        IThumbnailGenerationEngine ffMediaToolkitEngine,
        IThumbnailGenerationEngine ffmpegOnePassEngine,
        IThumbnailGenerationEngine openCvEngine,
        IThumbnailGenerationEngine autogenEngine,
        ThumbnailCreationOptions? options = null
    )
    {
        // 本番 Factory を純化したいので、tests 側だけが internal composition を直接組み立てる。
        return new ThumbnailCreationService(
            ThumbnailCreationServiceComponentFactory.Compose(
                ThumbnailCreationServiceComponentFactory.CreateTestingOptions(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine,
                    options
                )
            )
        );
    }
}
