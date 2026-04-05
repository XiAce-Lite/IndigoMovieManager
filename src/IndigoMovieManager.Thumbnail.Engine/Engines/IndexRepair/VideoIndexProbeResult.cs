namespace IndigoMovieManager.Thumbnail.Engines.IndexRepair
{
    /// <summary>
    /// インデックス破損判定の結果DTO。
    /// </summary>
    public sealed class VideoIndexProbeResult
    {
        public string MoviePath { get; set; } = "";
        public bool IsIndexCorruptionDetected { get; set; }
        public string DetectionReason { get; set; } = "";
        public string ContainerFormat { get; set; } = "";
        public string ErrorCode { get; set; } = "";
    }
}
