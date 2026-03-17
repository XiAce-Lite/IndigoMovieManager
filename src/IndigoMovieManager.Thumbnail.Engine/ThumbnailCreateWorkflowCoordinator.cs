using System.Collections.Concurrent;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// service に残った通常生成の本流を、1 本の workflow としてまとめる。
    /// </summary>
    internal sealed class ThumbnailCreateWorkflowCoordinator
    {
        // 同一出力先へ複数ジョブが同時書き込みしないよう、workflow 単位で直列化する。
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> OutputFileLocks = new(
            StringComparer.OrdinalIgnoreCase
        );

        private readonly ThumbnailCreatePreparationResolver preparationResolver;
        private readonly ThumbnailPrecheckCoordinator precheckCoordinator;
        private readonly ThumbnailJobContextBuilder jobContextBuilder;
        private readonly ThumbnailEngineRouter engineRouter;
        private readonly ThumbnailEngineExecutionPolicy engineExecutionPolicy;
        private readonly ThumbnailEngineExecutionCoordinator engineExecutionCoordinator;
        private readonly ThumbnailCreateResultFinalizer resultFinalizer;

        public ThumbnailCreateWorkflowCoordinator(
            ThumbnailCreatePreparationResolver preparationResolver,
            ThumbnailPrecheckCoordinator precheckCoordinator,
            ThumbnailJobContextBuilder jobContextBuilder,
            ThumbnailEngineRouter engineRouter,
            ThumbnailEngineExecutionPolicy engineExecutionPolicy,
            ThumbnailEngineExecutionCoordinator engineExecutionCoordinator,
            ThumbnailCreateResultFinalizer resultFinalizer
        )
        {
            this.preparationResolver =
                preparationResolver ?? throw new ArgumentNullException(nameof(preparationResolver));
            this.precheckCoordinator =
                precheckCoordinator ?? throw new ArgumentNullException(nameof(precheckCoordinator));
            this.jobContextBuilder =
                jobContextBuilder ?? throw new ArgumentNullException(nameof(jobContextBuilder));
            this.engineRouter =
                engineRouter ?? throw new ArgumentNullException(nameof(engineRouter));
            this.engineExecutionPolicy =
                engineExecutionPolicy
                ?? throw new ArgumentNullException(nameof(engineExecutionPolicy));
            this.engineExecutionCoordinator =
                engineExecutionCoordinator
                ?? throw new ArgumentNullException(nameof(engineExecutionCoordinator));
            this.resultFinalizer =
                resultFinalizer ?? throw new ArgumentNullException(nameof(resultFinalizer));
        }

        public async Task<ThumbnailCreateResult> ExecuteAsync(
            ThumbnailCreateWorkflowRequest request,
            CancellationToken cts = default
        )
        {
            request ??= new ThumbnailCreateWorkflowRequest();
            request.Request ??= new ThumbnailRequest();

            ThumbnailCreatePreparation preparation = preparationResolver.Prepare(
                new ThumbnailCreatePreparationRequest
                {
                    Request = request.Request,
                    DbName = request.DbName,
                    ThumbFolder = request.ThumbFolder,
                    SourceMovieFullPathOverride = request.SourceMovieFullPathOverride,
                    InitialEngineHint = request.InitialEngineHint,
                }
            );
            SemaphoreSlim outputLock = OutputFileLocks.GetOrAdd(
                preparation.SaveThumbFileName,
                _ => new SemaphoreSlim(1, 1)
            );
            await outputLock.WaitAsync(cts);

            try
            {
                ThumbnailPrecheckOutcome precheckOutcome = precheckCoordinator.Run(
                    new ThumbnailPrecheckRequest
                    {
                        Request = request.Request,
                        LayoutProfile = preparation.LayoutProfile,
                        ThumbnailOutPath = preparation.ThumbnailOutPath,
                        MovieFullPath = preparation.MovieFullPath,
                        SourceMovieFullPath = preparation.SourceMovieFullPath,
                        SaveThumbFileName = preparation.SaveThumbFileName,
                        IsResizeThumb = request.IsResizeThumb,
                        IsManual = request.IsManual,
                        KnownDurationSec = preparation.DurationSec,
                        CacheMeta = preparation.CacheMeta,
                    }
                );
                if (precheckOutcome.HasImmediateResult)
                {
                    return precheckOutcome.ImmediateResult;
                }

                double? durationSec = preparationResolver.ResolveDurationIfMissing(preparation);
                ThumbnailJobContextBuildOutcome contextOutcome = jobContextBuilder.Build(
                    new ThumbnailJobContextBuildRequest
                    {
                        Request = request.Request,
                        LayoutProfile = preparation.LayoutProfile,
                        ThumbnailOutPath = preparation.ThumbnailOutPath,
                        MovieFullPath = preparation.MovieFullPath,
                        SourceMovieFullPath = preparation.SourceMovieFullPath,
                        SaveThumbFileName = preparation.SaveThumbFileName,
                        IsResizeThumb = request.IsResizeThumb,
                        IsManual = request.IsManual,
                        DurationSec = durationSec,
                        FileSizeBytes = precheckOutcome.FileSizeBytes,
                        InitialEngineHint = preparation.InitialEngineHint,
                        ThumbInfoOverride = request.ThumbInfoOverride,
                    }
                );
                if (!contextOutcome.IsSuccess)
                {
                    return resultFinalizer.FinalizeImmediate(
                        new ThumbnailImmediateFinalizationRequest
                        {
                            Result = ThumbnailCreationService.CreateFailedResult(
                                preparation.SaveThumbFileName,
                                durationSec,
                                contextOutcome.ErrorMessage
                            ),
                            EngineId = "precheck",
                            MovieFullPath = preparation.MovieFullPath,
                            KnownDurationSec = durationSec,
                            OutputPath = preparation.SaveThumbFileName,
                        }
                    );
                }

                ThumbnailJobContext context = contextOutcome.Context;
                IThumbnailGenerationEngine selectedEngine = engineRouter.ResolveForThumbnail(
                    context
                );
                List<IThumbnailGenerationEngine> engineOrder =
                    engineExecutionPolicy.BuildThumbnailEngineOrder(selectedEngine, context);
                ThumbnailEngineExecutionOutcome executionOutcome =
                    await engineExecutionCoordinator.ExecuteAsync(
                        selectedEngine,
                        engineOrder,
                        context,
                        preparation.MovieFullPath,
                        cts
                    );
                return resultFinalizer.FinalizeExecution(
                    new ThumbnailExecutionFinalizationRequest
                    {
                        Result = executionOutcome.Result,
                        ProcessEngineId = executionOutcome.ProcessEngineId,
                        Context = context,
                        EngineErrorMessages = executionOutcome.EngineErrorMessages,
                        MovieFullPath = preparation.MovieFullPath,
                        KnownDurationSec = durationSec,
                        CacheKey = preparation.CacheKey,
                        CacheMeta = preparation.CacheMeta,
                    }
                );
            }
            finally
            {
                outputLock.Release();
            }
        }
    }

    internal sealed class ThumbnailCreateWorkflowRequest
    {
        public ThumbnailRequest Request { get; set; } = new();
        public string DbName { get; init; } = "";
        public string ThumbFolder { get; init; } = "";
        public bool IsResizeThumb { get; init; }
        public bool IsManual { get; init; }
        public string SourceMovieFullPathOverride { get; init; } = "";
        public string InitialEngineHint { get; init; } = "";
        public ThumbInfo ThumbInfoOverride { get; init; }
    }
}
