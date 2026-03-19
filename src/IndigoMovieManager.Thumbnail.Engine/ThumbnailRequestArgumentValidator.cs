namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// public request DTO の必須条件を 1 箇所へ集約する。
    /// </summary>
    internal static class ThumbnailRequestArgumentValidator
    {
        internal static void ValidateCreateArgs(ThumbnailCreateArgs args)
        {
            ArgumentNullException.ThrowIfNull(args);
            if (args.QueueObj is null && args.Request is null)
            {
                throw new ArgumentException(
                    "QueueObj または Request のいずれかは必須です。",
                    nameof(args)
                );
            }

            ThumbnailRequest request = args.Request ?? args.QueueObj?.ToThumbnailRequest();
            if (string.IsNullOrWhiteSpace(request?.MovieFullPath))
            {
                throw new ArgumentException("MovieFullPath は必須です。", nameof(args));
            }
        }

        internal static void ValidateBookmarkArgs(ThumbnailBookmarkArgs args)
        {
            ArgumentNullException.ThrowIfNull(args);
            if (string.IsNullOrWhiteSpace(args.MovieFullPath))
            {
                throw new ArgumentException("MovieFullPath は必須です。", nameof(args));
            }
            if (string.IsNullOrWhiteSpace(args.SaveThumbPath))
            {
                throw new ArgumentException("SaveThumbPath は必須です。", nameof(args));
            }
        }
    }
}
