using System.ComponentModel;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// obsolete 化した互換入口を隔離し、本体の責務を薄く保つ。
    /// </summary>
    public sealed partial class ThumbnailCreationService
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("ThumbnailCreationServiceFactory.CreateDefault() を使用してください。service の生成入口は Factory に統一します。")]
        public ThumbnailCreationService()
            : this(ThumbnailCreationServiceFactory.CreateDefaultComposition()) { }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("ThumbnailCreationServiceFactory.Create(hostRuntime, processLogWriter) を使用してください。service の生成入口は Factory に統一します。")]
        public ThumbnailCreationService(
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
            : this(
                ThumbnailCreationServiceFactory.CreateComposition(
                    hostRuntime: hostRuntime,
                    processLogWriter: processLogWriter
                )
            ) { }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("ThumbnailCreationServiceFactory.Create(videoMetadataProvider, logger, hostRuntime, processLogWriter) を使用してください。service の生成入口は Factory に統一します。")]
        public ThumbnailCreationService(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter = null
        )
            : this(
                ThumbnailCreationServiceFactory.CreateComposition(
                    videoMetadataProvider: videoMetadataProvider,
                    logger: logger,
                    hostRuntime: hostRuntime,
                    processLogWriter: processLogWriter
                )
            ) { }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("CreateBookmarkThumbAsync(ThumbnailBookmarkArgs, CancellationToken) を使用してください。")]
        public Task<bool> CreateBookmarkThumbAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos
        )
        {
            // 既存呼び出し互換のため残す wrapper。新規呼び出しは ThumbnailBookmarkArgs を使う。
            return CreateBookmarkThumbAsync(
                new ThumbnailBookmarkArgs
                {
                    MovieFullPath = movieFullPath,
                    SaveThumbPath = saveThumbPath,
                    CapturePos = capturePos,
                }
            );
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("CreateThumbAsync(ThumbnailCreateArgs, CancellationToken) を使用してください。")]
        public Task<ThumbnailCreateResult> CreateThumbAsync(
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
            // 既存呼び出し互換のため残す wrapper。新規呼び出しは ThumbnailCreateArgs を使う。
            return CreateThumbAsync(
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

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("CreateThumbAsync(ThumbnailCreateArgs, CancellationToken) を使用してください。")]
        public Task<ThumbnailCreateResult> CreateThumbAsync(
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
            // 既存呼び出し互換のため残す wrapper。新規呼び出しは ThumbnailCreateArgs を使う。
            return CreateThumbAsync(
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
    }
}
