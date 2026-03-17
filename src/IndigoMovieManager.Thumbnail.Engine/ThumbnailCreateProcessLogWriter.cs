namespace IndigoMovieManager.Thumbnail
{
    // engine 側には、process log の入れ物と書き込み契約だけを残す。
    public sealed class ThumbnailCreateProcessLogEntry
    {
        public string EngineId { get; init; } = "";
        public string MovieFullPath { get; init; } = "";
        public string Codec { get; init; } = "";
        public double? DurationSec { get; init; }
        public long FileSizeBytes { get; init; }
        public string OutputPath { get; init; } = "";
        public bool IsSuccess { get; init; }
        public string ErrorMessage { get; init; } = "";
    }

    public interface IThumbnailCreateProcessLogWriter
    {
        void Write(ThumbnailCreateProcessLogEntry entry);
    }

    // host 側が writer を渡さない場合でも、engine 本体は余計な I/O を持たずに動けるようにする。
    internal sealed class NoOpThumbnailCreateProcessLogWriter : IThumbnailCreateProcessLogWriter
    {
        internal static NoOpThumbnailCreateProcessLogWriter Instance { get; } = new();

        public void Write(ThumbnailCreateProcessLogEntry entry)
        {
            // host が writer を渡さない構成では、engine は結果だけを返して静かに終わる。
        }
    }
}
