namespace IndigoMovieManager.Skin
{
    /// <summary>
    /// 1 スキン分の実体を表す。
    /// HTML 本体は将来互換拡張用に保持しつつ、今は表示タブ選択に必要な情報だけ使う。
    /// </summary>
    public sealed class WhiteBrowserSkinDefinition
    {
        public WhiteBrowserSkinDefinition(
            string name,
            string folderPath,
            string htmlPath,
            WhiteBrowserSkinConfig config,
            string preferredTabStateName,
            bool isBuiltIn,
            bool isMissing = false
        )
        {
            Name = name ?? "";
            FolderPath = folderPath ?? "";
            HtmlPath = htmlPath ?? "";
            Config = config ?? WhiteBrowserSkinConfig.Empty;
            PreferredTabStateName = preferredTabStateName ?? "DefaultGrid";
            IsBuiltIn = isBuiltIn;
            IsMissing = isMissing;
        }

        public string Name { get; }
        public string FolderPath { get; }
        public string HtmlPath { get; }
        public WhiteBrowserSkinConfig Config { get; }
        public string PreferredTabStateName { get; }
        public bool IsBuiltIn { get; }
        public bool IsMissing { get; }
        public bool RequiresWebView2 => !IsBuiltIn;
        public string DisplayName =>
            IsBuiltIn
                ? $"{Name} (Built-in)"
                : (IsMissing ? $"{Name} (Missing External)" : $"{Name} (External)");
    }
}
