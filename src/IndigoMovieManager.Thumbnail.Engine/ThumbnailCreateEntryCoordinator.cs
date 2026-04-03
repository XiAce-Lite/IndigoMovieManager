namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 互換入口と workflow 入口の差を吸収し、service を薄く保つ。
    /// </summary>
    internal sealed class ThumbnailCreateEntryCoordinator
    {
        private readonly Func<
            ThumbnailCreateWorkflowRequest,
            CancellationToken,
            Task<ThumbnailCreateResult>
        > executeWorkflowAsync;

        internal ThumbnailCreateEntryCoordinator(ThumbnailCreateWorkflowCoordinator workflowCoordinator)
            : this(
                workflowCoordinator == null
                    ? throw new ArgumentNullException(nameof(workflowCoordinator))
                    : (request, cts) => workflowCoordinator.ExecuteAsync(request, cts)
            ) { }

        internal ThumbnailCreateEntryCoordinator(
            Func<
                ThumbnailCreateWorkflowRequest,
                CancellationToken,
                Task<ThumbnailCreateResult>
            > executeWorkflowAsync
        )
        {
            this.executeWorkflowAsync =
                executeWorkflowAsync ?? throw new ArgumentNullException(nameof(executeWorkflowAsync));
        }

        internal async Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailCreateArgs args,
            CancellationToken cts = default
        )
        {
            ThumbnailRequestArgumentValidator.ValidateCreateArgs(args);

            return await executeWorkflowAsync(
                new ThumbnailCreateWorkflowRequest
                {
                    Request = args.Request,
                    DbName = args.DbName,
                    ThumbFolder = args.ThumbFolder,
                    IsResizeThumb = args.IsResizeThumb,
                    IsManual = args.IsManual,
                    SourceMovieFullPathOverride = args.SourceMovieFullPathOverride,
                    InitialEngineHint = args.InitialEngineHint,
                    TraceId = args.TraceId,
                    ThumbInfoOverride = args.ThumbInfoOverride,
                },
                cts
            );
        }
    }
}
