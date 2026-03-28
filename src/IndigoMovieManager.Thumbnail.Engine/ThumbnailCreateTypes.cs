namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// MainWindow へ返すサムネイル生成結果の中立 DTO。
    /// </summary>
    public sealed class ThumbnailCreateResult
    {
        public string SaveThumbFileName { get; init; } = "";
        public double? DurationSec { get; init; }
        public bool IsSuccess { get; init; }
        public string ErrorMessage { get; init; } = "";
        public string ProcessEngineId { get; set; } = "";
        public ThumbnailPreviewFrame PreviewFrame { get; init; }
    }

    /// <summary>
    /// WPF 非依存でプレビュー画素を渡すための DTO。
    /// </summary>
    public sealed class ThumbnailPreviewFrame
    {
        public byte[] PixelBytes { get; init; } = [];
        public int Width { get; init; }
        public int Height { get; init; }
        public int Stride { get; init; }
        public ThumbnailPreviewPixelFormat PixelFormat { get; init; } =
            ThumbnailPreviewPixelFormat.Bgr24;

        public bool IsValid()
        {
            if (PixelBytes == null || Width < 1 || Height < 1 || Stride < 1)
            {
                return false;
            }

            long requiredLength = (long)Stride * Height;
            if (requiredLength < 1 || requiredLength > int.MaxValue)
            {
                return false;
            }

            return PixelBytes.Length >= requiredLength;
        }
    }

    public enum ThumbnailPreviewPixelFormat
    {
        Unknown = 0,
        Bgr24 = 1,
        Bgra32 = 2,
    }
}
