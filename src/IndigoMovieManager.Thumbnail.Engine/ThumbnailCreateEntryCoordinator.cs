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

            // legacy QueueObj は入口でだけ扱い、本流には新契約だけを流す。
            ThumbnailRequest request =
                args.Request ?? args.QueueObj?.ToThumbnailRequest() ?? new ThumbnailRequest();
            try
            {
                return await executeWorkflowAsync(
                    new ThumbnailCreateWorkflowRequest
                    {
                        Request = request,
                        DbName = args.DbName,
                        ThumbFolder = args.ThumbFolder,
                        IsResizeThumb = args.IsResizeThumb,
                        IsManual = args.IsManual,
                        SourceMovieFullPathOverride = args.SourceMovieFullPathOverride,
                        InitialEngineHint = args.InitialEngineHint,
                        ThumbInfoOverride = args.ThumbInfoOverride,
                    },
                    cts
                );
            }
            finally
            {
                args.QueueObj?.ApplyThumbnailRequest(request);
            }
        }
    }
}
