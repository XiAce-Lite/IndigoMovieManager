namespace IndigoMovieManager.Thumbnail
{
    public class QueueObj
    {
        private int _tabIndex;
        private long _movieId;
        private string _movieFullPath;
        private string _hash = "";
        private long _movieSizeBytes;
        private int? _thumbPanelPos = null;
        private int? _thumbTimePos = null;

        public int Tabindex { get { return _tabIndex; } set { _tabIndex = value; } }
        public long MovieId { get { return _movieId; } set { _movieId = value; } }
        public string MovieFullPath { get { return _movieFullPath; } set { _movieFullPath = value; } }
        public string Hash { get { return _hash; } set { _hash = value ?? ""; } }
        public long MovieSizeBytes { get { return _movieSizeBytes; } set { _movieSizeBytes = value; } }
        public int? ThumbPanelPos { get { return _thumbPanelPos; } set { _thumbPanelPos = value; } }
        public int? ThumbTimePos { get { return _thumbTimePos; } set { _thumbTimePos = value; } }
    }
}
