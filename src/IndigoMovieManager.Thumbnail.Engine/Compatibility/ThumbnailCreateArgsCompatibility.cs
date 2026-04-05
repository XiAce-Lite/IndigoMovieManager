namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// legacy QueueObj 入口はここでだけ吸収し、service の public DTO は Request 本流へそろえる。
    /// </summary>
    public static class ThumbnailCreateArgsCompatibility
    {
        public static ThumbnailCreateArgs FromLegacyQueueObj(
            QueueObj queueObj,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual,
            string sourceMovieFullPathOverride = "",
            string initialEngineHint = "",
            string traceId = "",
            ThumbInfo thumbInfoOverride = null
        )
        {
            return new ThumbnailCreateArgs
            {
                Request = queueObj?.ToThumbnailRequest() ?? new ThumbnailRequest(),
                DbName = dbName ?? "",
                ThumbFolder = thumbFolder ?? "",
                IsResizeThumb = isResizeThumb,
                IsManual = isManual,
                SourceMovieFullPathOverride = sourceMovieFullPathOverride ?? "",
                InitialEngineHint = initialEngineHint ?? "",
                TraceId = traceId ?? "",
                ThumbInfoOverride = thumbInfoOverride,
            };
        }

        public static void ApplyBackToLegacyQueueObj(ThumbnailCreateArgs args, QueueObj queueObj)
        {
            if (args?.Request == null || queueObj == null)
            {
                return;
            }

            queueObj.ApplyThumbnailRequest(args.Request);
        }
    }
}
