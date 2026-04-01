namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// MainWindow 直結を避けるため、API service が必要とする入口だけを delegate で受ける。
    /// </summary>
    public sealed class WhiteBrowserSkinApiServiceDependencies
    {
        public Func<IReadOnlyList<MovieRecords>> GetVisibleMovies { get; init; } =
            static () => Array.Empty<MovieRecords>();

        public Func<int> GetCurrentTabIndex { get; init; } = static () => 2;

        public Func<string> GetCurrentDbFullPath { get; init; } = static () => "";

        public Func<string> GetCurrentDbName { get; init; } = static () => "";

        public Func<string> GetCurrentSkinName { get; init; } = static () => "";

        public Func<string> GetCurrentThumbFolder { get; init; } = static () => "";

        public Func<MovieRecords> GetCurrentSelectedMovie { get; init; } = static () => null;

        public Func<MovieRecords, Task<bool>> FocusMovieAsync { get; init; } =
            static _ => Task.FromResult(false);

        public Action<string> Trace { get; init; } = static _ => { };

        public Func<string, string> ResolveThumbUrl { get; init; } = static _ => "";
    }

    /// <summary>
    /// DTO の既定値や URI ルールを 1 か所に寄せる。
    /// </summary>
    public sealed class WhiteBrowserSkinApiServiceOptions
    {
        public int DefaultThumbnailWidth { get; init; } = 160;
        public int DefaultThumbnailHeight { get; init; } = 120;
        public int DefaultThumbnailColumns { get; init; } = 1;
        public int DefaultThumbnailRows { get; init; } = 1;
        public string ThumbnailBaseUri { get; init; } = WhiteBrowserSkinHostPaths.BuildThumbnailBaseUri();
    }
}
