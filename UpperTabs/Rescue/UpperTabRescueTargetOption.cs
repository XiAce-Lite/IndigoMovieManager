namespace IndigoMovieManager.UpperTabs.Rescue
{
    // 救済タブの対象切替は、固定タブIDと表示サイズをひとまとめで持つ。
    public sealed class UpperTabRescueTargetOption
    {
        public int TabIndex { get; init; }

        public string DisplayName { get; init; } = "";

        public double ThumbnailWidth { get; init; }

        public double ThumbnailHeight { get; init; }
    }
}
