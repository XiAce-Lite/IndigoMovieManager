using System.Collections.ObjectModel;

namespace IndigoMovieManager.ModelView
{
    public class WatchWindowViewModel
    {
        public ObservableCollection<WatchRecords> WatchRecs { get; set; }

        public WatchWindowViewModel()
        {
            WatchRecs = [];
        }
    }
}
