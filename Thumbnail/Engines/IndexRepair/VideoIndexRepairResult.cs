namespace IndigoMovieManager.Thumbnail.Engines.IndexRepair
{
    /// <summary>
    /// インデックス修復の結果DTO。
    /// </summary>
    public sealed class VideoIndexRepairResult
    {
        public bool IsSuccess { get; set; }
        public string InputPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public bool UsedTemporaryRemux { get; set; }
        public string ErrorMessage { get; set; } = "";
    }
}
