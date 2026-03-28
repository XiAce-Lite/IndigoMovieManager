namespace IndigoMovieManager.UpperTabs.DuplicateVideos
{
    internal sealed class UpperTabDuplicateGroupViewModel
    {
        public string Hash { get; set; } = "";

        public string RepresentativeThumbnailPath { get; set; } = "";

        public string RepresentativeMovieName { get; set; } = "";

        public int DuplicateCount { get; set; }

        public string MaxMovieSizeText { get; set; } = "";

        public long MaxMovieSizeValue { get; set; }
    }
}
