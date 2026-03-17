using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// ThumbnailCreationService の依存組み立てを service 本体から切り離す。
    /// </summary>
    internal static class ThumbnailCreationServiceComponentFactory
    {
        public static ThumbnailCreationEngineSet CreateDefaultEngineSet()
        {
            return CreateEngineSet(
                new FfMediaToolkitThumbnailGenerationEngine(),
                new FfmpegOnePassThumbnailGenerationEngine(),
                new OpenCvThumbnailGenerationEngine(),
                new FfmpegAutoGenThumbnailGenerationEngine()
            );
        }

        public static ThumbnailCreationEngineSet CreateEngineSet(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine
        )
        {
            return new ThumbnailCreationEngineSet
            {
                FfMediaToolkitEngine =
                    ffMediaToolkitEngine
                    ?? throw new ArgumentNullException(nameof(ffMediaToolkitEngine)),
                FfmpegOnePassEngine =
                    ffmpegOnePassEngine
                    ?? throw new ArgumentNullException(nameof(ffmpegOnePassEngine)),
                OpenCvEngine = openCvEngine ?? throw new ArgumentNullException(nameof(openCvEngine)),
                AutogenEngine = autogenEngine ?? throw new ArgumentNullException(nameof(autogenEngine)),
            };
        }

        public static ThumbnailCreationOptions CreateDefaultOptions()
        {
            return CreateOptions();
        }

        public static ThumbnailCreationOptions CreateOptions(
            ThumbnailCreationEngineSet engineSet = null,
            IVideoMetadataProvider videoMetadataProvider = null,
            IThumbnailLogger logger = null,
            IThumbnailCreationHostRuntime hostRuntime = null,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
        {
            return new ThumbnailCreationOptions
            {
                EngineSet = engineSet ?? CreateDefaultEngineSet(),
                VideoMetadataProvider = videoMetadataProvider ?? NoOpVideoMetadataProvider.Instance,
                Logger = logger ?? NoOpThumbnailLogger.Instance,
                HostRuntime = hostRuntime ?? FallbackThumbnailCreationHostRuntime.Instance,
                ProcessLogWriter = processLogWriter,
            };
        }

        public static ThumbnailCreationOptions CreateTestingOptions(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            ThumbnailCreationOptions options = null
        )
        {
            return CreateOptions(
                engineSet: CreateEngineSet(
                    ffMediaToolkitEngine,
                    ffmpegOnePassEngine,
                    openCvEngine,
                    autogenEngine
                ),
                videoMetadataProvider: options?.VideoMetadataProvider,
                logger: options?.Logger,
                hostRuntime: options?.HostRuntime,
                processLogWriter: options?.ProcessLogWriter
            );
        }

        public static ThumbnailCreationServiceComposition Compose(
            ThumbnailCreationOptions options
        )
        {
            options ??= new ThumbnailCreationOptions();

            ThumbnailCreationEngineSet engineSet =
                options.EngineSet ?? throw new ArgumentNullException(nameof(options.EngineSet));
            IVideoMetadataProvider videoMetadataProvider =
                options.VideoMetadataProvider
                ?? throw new ArgumentNullException(nameof(options.VideoMetadataProvider));
            IThumbnailLogger logger =
                options.Logger ?? throw new ArgumentNullException(nameof(options.Logger));
            IThumbnailCreationHostRuntime hostRuntime =
                options.HostRuntime ?? throw new ArgumentNullException(nameof(options.HostRuntime));
            IThumbnailCreateProcessLogWriter processLogWriter =
                options.ProcessLogWriter ?? NoOpThumbnailCreateProcessLogWriter.Instance;

            ThumbnailRuntimeLog.SetLogger(logger);

            ThumbnailEngineRouter engineRouter = new([
                engineSet.FfMediaToolkitEngine,
                engineSet.FfmpegOnePassEngine,
                engineSet.OpenCvEngine,
                engineSet.AutogenEngine,
            ]);
            ThumbnailEngineExecutionPolicy engineExecutionPolicy = new(
                engineSet.FfMediaToolkitEngine,
                engineSet.FfmpegOnePassEngine,
                engineSet.OpenCvEngine,
                engineSet.AutogenEngine
            );
            ThumbnailEngineExecutionCoordinator engineExecutionCoordinator = new(
                engineExecutionPolicy
            );
            ThumbnailMovieMetaResolver movieMetaResolver = new(videoMetadataProvider);
            ThumbnailCreatePreparationResolver preparationResolver = new(movieMetaResolver);
            ThumbnailJobContextBuilder jobContextBuilder = new(movieMetaResolver);
            ThumbnailCreateResultFinalizer resultFinalizer = new(
                processLogWriter,
                movieMetaResolver
            );
            ThumbnailPrecheckCoordinator precheckCoordinator = new(
                hostRuntime,
                movieMetaResolver,
                jobContextBuilder,
                resultFinalizer
            );

            ThumbnailCreateWorkflowCoordinator workflowCoordinator = new(
                preparationResolver,
                precheckCoordinator,
                jobContextBuilder,
                engineRouter,
                engineExecutionPolicy,
                engineExecutionCoordinator,
                resultFinalizer
            );

            return new ThumbnailCreationServiceComposition
            {
                BookmarkCoordinator = new ThumbnailBookmarkCoordinator(engineRouter),
                CreateEntryCoordinator = new ThumbnailCreateEntryCoordinator(workflowCoordinator),
            };
        }
    }

    internal sealed class ThumbnailCreationEngineSet
    {
        public IThumbnailGenerationEngine FfMediaToolkitEngine { get; init; }
        public IThumbnailGenerationEngine FfmpegOnePassEngine { get; init; }
        public IThumbnailGenerationEngine OpenCvEngine { get; init; }
        public IThumbnailGenerationEngine AutogenEngine { get; init; }
    }

    internal sealed class ThumbnailCreationOptions
    {
        public ThumbnailCreationEngineSet EngineSet { get; init; }
        public IVideoMetadataProvider VideoMetadataProvider { get; init; }
        public IThumbnailLogger Logger { get; init; }
        public IThumbnailCreationHostRuntime HostRuntime { get; init; }
        public IThumbnailCreateProcessLogWriter ProcessLogWriter { get; init; }
    }

    internal sealed class ThumbnailCreationServiceComposition
    {
        public ThumbnailBookmarkCoordinator BookmarkCoordinator { get; init; }
        public ThumbnailCreateEntryCoordinator CreateEntryCoordinator { get; init; }
    }
}
