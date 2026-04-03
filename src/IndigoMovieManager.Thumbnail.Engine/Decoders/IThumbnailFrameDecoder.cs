namespace IndigoMovieManager.Thumbnail.Decoders
{
    /// <summary>
    /// サムネイル用の動画デコーダー抽象。
    /// 実装ごとの差分（FFMediaToolkit/OpenCv など）を吸収する。
    /// </summary>
    internal interface IThumbnailFrameDecoder
    {
        string LibraryName { get; }

        bool TryOpen(
            string movieFullPath,
            out IThumbnailFrameSource frameSource,
            out double? durationSec,
            out string errorMessage
        );
    }
}
