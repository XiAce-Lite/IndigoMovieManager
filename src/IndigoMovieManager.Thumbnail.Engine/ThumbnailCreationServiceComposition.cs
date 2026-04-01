using System.IO;
using System.Text;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// ThumbnailCreationService の依存組み立てを service 本体から切り離す。
    /// </summary>
    internal static class ThumbnailCreationServiceComponentFactory
    {
        internal static ThumbnailCreationEngineSet CreateDefaultEngineSet()
        {
            return CreateEngineSet(
                new FfMediaToolkitThumbnailGenerationEngine(),
                new FfmpegOnePassThumbnailGenerationEngine(),
                new OpenCvThumbnailGenerationEngine(),
                new FfmpegAutoGenThumbnailGenerationEngine()
            );
        }

        internal static ThumbnailCreationEngineSet CreateEngineSet(
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

        internal static ThumbnailCreationOptions CreateDefaultOptions()
        {
            return CreateOptions();
        }

        internal static ThumbnailCreationOptions CreateOptions(
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

        internal static ThumbnailCreationOptions CreateTestingOptions(
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

        internal static ThumbnailCreationServiceComposition Compose(
            ThumbnailCreationOptions options
        )
        {
            options ??= new ThumbnailCreationOptions();

            // 既存互換で必要なコードページを、service ではなく composition 境界で有効化する。
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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
            string movieTraceLogPath = hostRuntime.ResolveProcessLogPath(ThumbnailMovieTraceLog.FileName);
            ThumbnailMovieTraceRuntime.ConfigureLogDirectoryFromHost(
                Path.GetDirectoryName(movieTraceLogPath) ?? ""
            );

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
            ThumbnailSourceImageImportCoordinator sourceImageImportCoordinator = new();
            ThumbnailPrecheckCoordinator precheckCoordinator = new(
                hostRuntime,
                movieMetaResolver,
                jobContextBuilder,
                resultFinalizer,
                sourceImageImportCoordinator
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
                CreateBookmarkAsync = new ThumbnailBookmarkCoordinator(engineRouter).CreateAsync,
                CreateThumbAsync = new ThumbnailCreateEntryCoordinator(workflowCoordinator).CreateAsync,
            };
        }
    }

    internal sealed class ThumbnailCreationEngineSet
    {
        internal IThumbnailGenerationEngine FfMediaToolkitEngine { get; init; }
        internal IThumbnailGenerationEngine FfmpegOnePassEngine { get; init; }
        internal IThumbnailGenerationEngine OpenCvEngine { get; init; }
        internal IThumbnailGenerationEngine AutogenEngine { get; init; }
    }

    internal sealed class ThumbnailCreationOptions
    {
        internal ThumbnailCreationEngineSet EngineSet { get; init; }
        internal IVideoMetadataProvider VideoMetadataProvider { get; init; }
        internal IThumbnailLogger Logger { get; init; }
        internal IThumbnailCreationHostRuntime HostRuntime { get; init; }
        internal IThumbnailCreateProcessLogWriter ProcessLogWriter { get; init; }
    }

    internal sealed class ThumbnailCreationServiceComposition
    {
        internal Func<ThumbnailBookmarkArgs, CancellationToken, Task<bool>> CreateBookmarkAsync { get; init; }
        internal Func<ThumbnailCreateArgs, CancellationToken, Task<ThumbnailCreateResult>> CreateThumbAsync { get; init; }
    }
}
