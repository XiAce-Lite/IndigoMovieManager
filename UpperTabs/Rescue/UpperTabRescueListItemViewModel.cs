namespace IndigoMovieManager.UpperTabs.Rescue
{
    // 救済タブは一覧専用の軽い表示モデルへ落とし、入れ子を減らす。
    public sealed class UpperTabRescueListItemViewModel
    {
        public MovieRecords MovieRecord { get; init; }

        public string ThumbnailPath { get; init; } = "";

        public double ThumbnailWidth { get; init; }

        public double ThumbnailHeight { get; init; }

        public string MovieName { get; init; } = "";

        public string MovieSizeText { get; init; } = "";

        public string MovieLengthText { get; init; } = "";

        public string ScoreText { get; init; } = "";

        public string FileDateText { get; init; } = "";

        public string FailedTabsText { get; init; } = "";

        public string ProgressStatusText { get; init; } = "";

        public string ProgressDetailText { get; init; } = "";

        public string MoviePath { get; init; } = "";
    }
}
