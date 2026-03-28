namespace IndigoMovieManager.UpperTabs.Rescue
{
    // 履歴ペインは読み取り専用なので、表示用の薄い形だけをここへ集める。
    public sealed class UpperTabRescueHistoryItemViewModel
    {
        public string TimestampText { get; init; } = "";

        public string LaneText { get; init; } = "";

        public string ActionText { get; init; } = "";

        public string ResultText { get; init; } = "";

        public string AttemptText { get; init; } = "";

        public string EngineText { get; init; } = "";

        public string DetailText { get; init; } = "";
    }
}
