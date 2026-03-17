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

        public ThumbnailCreateEntryCoordinator(ThumbnailCreateWorkflowCoordinator workflowCoordinator)
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

        public async Task<ThumbnailCreateResult> CreateAsync(
            QueueObj queueObj,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual = false,
            CancellationToken cts = default,
            string sourceMovieFullPathOverride = null,
            string initialEngineHint = null,
            ThumbInfo thumbInfoOverride = null
        )
        {
            // legacy QueueObj は入口でだけ扱い、本流には新契約だけを流す。
            ThumbnailRequest request = queueObj?.ToThumbnailRequest() ?? new ThumbnailRequest();
            try
            {
                return await CreateAsync(
                    request,
                    dbName,
                    thumbFolder,
                    isResizeThumb,
                    isManual,
                    cts,
                    sourceMovieFullPathOverride,
                    initialEngineHint,
                    thumbInfoOverride
                );
            }
            finally
            {
                queueObj?.ApplyThumbnailRequest(request);
            }
        }

        public async Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailRequest request,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual = false,
            CancellationToken cts = default,
            string sourceMovieFullPathOverride = null,
            string initialEngineHint = null,
            ThumbInfo thumbInfoOverride = null
        )
        {
            return await executeWorkflowAsync(
                new ThumbnailCreateWorkflowRequest
                {
                    Request = request,
                    DbName = dbName,
                    ThumbFolder = thumbFolder,
                    IsResizeThumb = isResizeThumb,
                    IsManual = isManual,
                    SourceMovieFullPathOverride = sourceMovieFullPathOverride,
                    InitialEngineHint = initialEngineHint,
                    ThumbInfoOverride = thumbInfoOverride,
                },
                cts
            );
        }
    }
}
