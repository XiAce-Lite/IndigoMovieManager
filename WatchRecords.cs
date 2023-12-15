using System.Collections.ObjectModel;
using System.ComponentModel;

namespace IndigoMovieManager
{
    public class WatchRecords : INotifyPropertyChanged
    {
        private ObservableCollection<WatchRecords> _Children = null;

        private string dir = "";
        private bool auto = true;
        private bool watch = true;
        private bool sub = true;

        public WatchRecords() { }

        public string Dir
        {
            get => dir; set { dir = value; OnPropertyChanged(nameof(Dir)); }
        }

        public bool Auto
        {
            get => auto; set { auto = value; OnPropertyChanged(nameof(Auto)); }
        }

        public bool Watch
        {
            get => watch; set { watch = value; OnPropertyChanged(nameof(Watch)); }
        }

        public bool Sub
        {
            get => sub; set { sub = value; OnPropertyChanged(nameof(Sub)); }
        }

        public ObservableCollection<WatchRecords> Children
        {
            get { return _Children; }
            set { _Children = value; OnPropertyChanged("Children"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Add(WatchRecords child)
        {
            if (null == Children) Children = [];
            Children.Add(child);
        }
    }
}
