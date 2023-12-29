using System.Collections.ObjectModel;
using System.ComponentModel;

namespace IndigoMovieManager
{
    public class History : INotifyPropertyChanged
    {
        private ObservableCollection<History> _Children = null;

        private long find_id = 0;
        private string find_text = "";
        private string find_date = "";

        public long Find_Id
        {
            get { return find_id; }
            set { find_id = value; OnPropertyChanged(nameof(Find_Id)); }
        }

        public string Find_Text
        {
            get { return find_text; }
            set { find_text = value; OnPropertyChanged(nameof(Find_Text)); }
        }

        public string Find_Date
        {
            get { return find_date; }
            set { find_date = value; OnPropertyChanged(nameof(Find_Date)); }
        }

        public ObservableCollection<History> Children
        {
            get { return _Children; }
            set { _Children = value; OnPropertyChanged(nameof(Children)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Add(History child)
        {
            if (null == Children) Children = [];
            Children.Add(child);
        }
    }
}
