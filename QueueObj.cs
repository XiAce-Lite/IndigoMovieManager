namespace IndigoMovieManager
{
    public class QueueObj
    {
        private int _tabIndex;
        private long _movieId;
        private string _movieFullPath;
        private int? _thumbPanelPos = null;
        private int? _thumbTimePos = null;

        public int Tabindex { get { return _tabIndex; } set { _tabIndex = value; } }
        public long MovieId { get { return _movieId; } set { _movieId = value; } }
        public string MovieFullPath { get { return _movieFullPath; } set { _movieFullPath = value; } }
        public int? ThumbPanelPos { get { return _thumbPanelPos; } set { _thumbPanelPos = value; } }
        public int? ThumbTimePos { get { return _thumbTimePos; } set { _thumbTimePos = value; } }
    }
}
