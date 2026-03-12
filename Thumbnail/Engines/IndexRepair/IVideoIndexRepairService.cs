namespace IndigoMovieManager.Thumbnail.Engines.IndexRepair
{
    /// <summary>
    /// インデックス破損判定と修復の抽象。
    /// </summary>
    public interface IVideoIndexRepairService
    {
        Task<VideoIndexProbeResult> ProbeAsync(
            string moviePath,
            CancellationToken cts = default
        );

        Task<VideoIndexRepairResult> RepairAsync(
            string moviePath,
            string outputPath,
            CancellationToken cts = default
        );
    }
}
