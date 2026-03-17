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

        public Task<ThumbnailCreateResult> CreateAsync(
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
            return CreateAsync(
                new ThumbnailCreateArgs
                {
                    QueueObj = queueObj,
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

        public Task<ThumbnailCreateResult> CreateAsync(
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
            return CreateAsync(
                new ThumbnailCreateArgs
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

        public Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailCreateArgs args,
            CancellationToken cts = default
        )
        {
            args ??= new ThumbnailCreateArgs();
            return CreateAsync(
                new ThumbnailCreateInvocation
                {
                    QueueObj = args.QueueObj,
                    Request = args.Request,
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

        public async Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailCreateInvocation invocation,
            CancellationToken cts = default
        )
        {
            invocation ??= new ThumbnailCreateInvocation();

            // legacy QueueObj は入口でだけ扱い、本流には新契約だけを流す。
            ThumbnailRequest request =
                invocation.Request ?? invocation.QueueObj?.ToThumbnailRequest() ?? new ThumbnailRequest();
            try
            {
                invocation.Request = request;
                return await executeWorkflowAsync(
                    new ThumbnailCreateWorkflowRequest
                    {
                        Request = request,
                        DbName = invocation.DbName,
                        ThumbFolder = invocation.ThumbFolder,
                        IsResizeThumb = invocation.IsResizeThumb,
                        IsManual = invocation.IsManual,
                        SourceMovieFullPathOverride = invocation.SourceMovieFullPathOverride,
                        InitialEngineHint = invocation.InitialEngineHint,
                        ThumbInfoOverride = invocation.ThumbInfoOverride,
                    },
                    cts
                );
            }
            finally
            {
                invocation.QueueObj?.ApplyThumbnailRequest(request);
            }
        }
    }

    internal sealed class ThumbnailCreateInvocation
    {
        public QueueObj QueueObj { get; init; }
        public ThumbnailRequest Request { get; set; }
        public string DbName { get; init; } = "";
        public string ThumbFolder { get; init; } = "";
        public bool IsResizeThumb { get; init; }
        public bool IsManual { get; init; }
        public string SourceMovieFullPathOverride { get; init; } = "";
        public string InitialEngineHint { get; init; } = "";
        public ThumbInfo ThumbInfoOverride { get; init; }
    }
}
